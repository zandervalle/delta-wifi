//
//  Heatmap.swift
//  DeltaWiFi
//
//  Interpolation der Messwerte über den Plan (inverse Distanzgewichtung)
//  und Umsetzung in ein Bild.
//

import CoreGraphics
import Foundation

/// Ein Messwert mit Position relativ 0…1 auf dem Plan.
nonisolated struct FieldValue: Equatable, Sendable {
    var x: Double
    var y: Double
    var value: Double
}

/// Alle Werte einer Etage und Phase für eine Messgröße.
nonisolated struct FieldSource: Equatable, Sendable {
    var values: [FieldValue] = []
    /// Mit Lauf-Daten liegen die Werte dicht, die Unsicherheit wächst dann schneller mit dem Abstand.
    var hasWalkData = false
}

nonisolated struct ScalarField: Equatable, Sendable {
    var width: Int
    var height: Int
    /// Interpolierter Wert je Pixel, zeilenweise von oben links.
    var values: [Double]
    /// Abstand zum nächsten Messwert je Pixel, in Planhöhen.
    var nearest: [Double]
}

/// Was die Karte zeigt: eine Phase oder den Unterschied zwischen zwei frei gewählten Phasen.
nonisolated enum HeatmapView: Equatable, Sendable {
    case phase(String)
    case comparison(from: String, to: String)
}

nonisolated struct HeatmapInput: Equatable, Sendable {
    enum Content: Equatable, Sendable {
        case single(FieldSource)
        case comparison(from: FieldSource, to: FieldSource)
    }

    /// Messgröße mit den Grenzwerten des Projekts.
    var scale: MetricScale
    /// Breite / Höhe des Plans.
    var aspect: Double
    var content: Content

    var metric: Metric { scale.metric }

    init(scale: MetricScale, aspect: Double, content: Content) {
        self.scale = scale
        self.aspect = aspect
        self.content = content
    }

    /// Mit den festen Standardgrenzwerten der Messgröße.
    init(metric: Metric, aspect: Double, content: Content) {
        self.init(scale: metric.scale, aspect: aspect, content: content)
    }
}

nonisolated enum Heatmap {
    static let maxOpacity = 0.58
    static let minOpacity = 0.18
    /// Abstand (in Planhöhen), ab dem die Fläche nur noch mit Mindestdeckkraft gezeichnet wird.
    static let fadeDistance = 0.32
    static let fadeDistanceWithWalkData = 0.18
    /// Längere Kante des berechneten Rasters; das Bild wird beim Anzeigen weich hochskaliert.
    static let gridLongSide = 160

    /// `1 / (d² + 0.0004)^1.25`: Potenz 2,5, an den Messpunkten geglättet.
    static func weight(distanceSquared: Double) -> Double {
        let base = distanceSquared + 0.0004
        return 1 / (base * base.squareRoot().squareRoot())
    }

    static func gridSize(aspect: Double, longSide: Int = gridLongSide) -> (width: Int, height: Int) {
        let aspect = aspect.isFinite && aspect > 0 ? aspect : 1
        return aspect >= 1 ? (longSide, max(1, Int((Double(longSide) / aspect).rounded())))
                           : (max(1, Int((Double(longSide) * aspect).rounded())), longSide)
    }

    /// Inverse Distanzgewichtung über alle Werte. Distanzen seitenverhältnis-korrigiert:
    /// x wird mit Breite/Höhe multipliziert, y bleibt 0…1.
    static func interpolate(_ source: [FieldValue], aspect: Double, width: Int, height: Int) -> ScalarField? {
        guard !source.isEmpty, width > 0, height > 0 else { return nil }
        let xs = source.map { $0.x * aspect }
        let ys = source.map(\.y)
        let vs = source.map(\.value)
        var values = [Double](repeating: 0, count: width * height)
        var nearest = [Double](repeating: 0, count: width * height)

        for row in 0..<height {
            let py = (Double(row) + 0.5) / Double(height)
            for column in 0..<width {
                let px = (Double(column) + 0.5) / Double(width) * aspect
                var weighted = 0.0
                var total = 0.0
                var minimum = Double.infinity
                for index in vs.indices {
                    let dx = xs[index] - px
                    let dy = ys[index] - py
                    let squared = dx * dx + dy * dy
                    let w = weight(distanceSquared: squared)
                    weighted += w * vs[index]
                    total += w
                    if squared < minimum { minimum = squared }
                }
                values[row * width + column] = weighted / total
                nearest[row * width + column] = minimum.squareRoot()
            }
        }
        return ScalarField(width: width, height: height, values: values, nearest: nearest)
    }

    /// Deckkraft nach Abstand zum nächsten Messwert: nah kräftig, weit weg blass.
    static func opacity(nearest distance: Double, hasWalkData: Bool) -> Double {
        maxOpacity - (maxOpacity - minOpacity) * fade(distance, hasWalkData: hasWalkData)
    }

    private static func fade(_ distance: Double, hasWalkData: Bool) -> Double {
        let fraction = min(max(distance / (hasWalkData ? fadeDistanceWithWalkData : fadeDistance), 0), 1)
        return fraction * fraction
    }

    /// Pixel der Heatmap als vormultipliziertes RGBA, zeilenweise von oben links.
    /// `nil`, wenn es nichts zu zeichnen gibt (im Vergleich: sobald eine der beiden Phasen fehlt).
    static func rgba(for input: HeatmapInput, width: Int, height: Int) -> [UInt8]? {
        var pixels = [UInt8](repeating: 0, count: width * height * 4)
        func set(_ index: Int, _ color: HeatColor, _ alpha: Double) {
            pixels[index * 4] = UInt8((color.red * alpha * 255).rounded())
            pixels[index * 4 + 1] = UInt8((color.green * alpha * 255).rounded())
            pixels[index * 4 + 2] = UInt8((color.blue * alpha * 255).rounded())
            pixels[index * 4 + 3] = UInt8((alpha * 255).rounded())
        }

        switch input.content {
        case .single(let source):
            guard let field = interpolate(source.values, aspect: input.aspect, width: width, height: height) else { return nil }
            for index in field.values.indices {
                set(index, .scale(input.scale.score(of: field.values[index])),
                    opacity(nearest: field.nearest[index], hasWalkData: source.hasWalkData))
            }
        case .comparison(let from, let to):
            guard let before = interpolate(from.values, aspect: input.aspect, width: width, height: height),
                  let after = interpolate(to.values, aspect: input.aspect, width: width, height: height)
            else { return nil }
            for index in before.values.indices {
                // Die unsicherere der beiden Phasen bestimmt die Deckkraft.
                let fading = max(fade(before.nearest[index], hasWalkData: from.hasWalkData),
                                 fade(after.nearest[index], hasWalkData: to.hasWalkData))
                set(index, .difference(input.scale.change(from: before.values[index], to: after.values[index])),
                    maxOpacity - (maxOpacity - minOpacity) * fading)
            }
        }
        return pixels
    }

    /// Berechnet das Heatmap-Bild abseits des Main-Threads.
    @concurrent
    static func image(for input: HeatmapInput) async -> CGImage? {
        let size = gridSize(aspect: input.aspect)
        guard let pixels = rgba(for: input, width: size.width, height: size.height),
              let provider = CGDataProvider(data: Data(pixels) as CFData),
              let colorSpace = CGColorSpace(name: CGColorSpace.sRGB)
        else { return nil }
        return CGImage(width: size.width, height: size.height, bitsPerComponent: 8, bitsPerPixel: 32,
                       bytesPerRow: size.width * 4, space: colorSpace,
                       bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
                       provider: provider, decode: nil, shouldInterpolate: true, intent: .defaultIntent)
    }
}

nonisolated extension Project {
    /// Alle verorteten Werte einer Etage und Phase: feste Punkte plus Lauf-Samples.
    func fieldSource(metric: Metric, floor: String, phase: String) -> FieldSource {
        var source = FieldSource()
        for point in points where point.floor == floor {
            guard let x = point.x, let y = point.y,
                  let value = measurement(at: point.name, in: phase).flatMap(metric.value(in:)) else { continue }
            source.values.append(FieldValue(x: x, y: y, value: value))
        }
        guard metric.isAvailableFromWalks else { return source }
        for walk in walks where walk.floor == floor && walk.phase == phase {
            for sample in survey[walk.id] ?? [] {
                guard let value = metric.value(in: sample) else { continue }
                source.values.append(FieldValue(x: sample.x, y: sample.y, value: value))
                source.hasWalkData = true
            }
        }
        return source
    }

    func heatmapInput(metric: Metric, floor: String, view: HeatmapView) -> HeatmapInput {
        let content: HeatmapInput.Content = switch view {
        case .phase(let phase):
            .single(fieldSource(metric: metric, floor: floor, phase: phase))
        case .comparison(let from, let to):
            .comparison(from: fieldSource(metric: metric, floor: floor, phase: from),
                        to: fieldSource(metric: metric, floor: floor, phase: to))
        }
        return HeatmapInput(scale: scale(for: metric), aspect: Double(PlanImage.aspectRatio(of: plans[floor])), content: content)
    }
}
