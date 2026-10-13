//
//  PlanImage.swift
//  DeltaWiFi
//
//  Grundriss-Bilder einlesen (JPG/PNG/HEIC), verkleinern und als Data-URL ablegen.
//

import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

nonisolated enum PlanImage {
    /// Längste Kante eines gespeicherten Grundrisses in Pixeln.
    static let maxDimension = 1600
    /// Seitenverhältnis des Platzhalters, wenn kein Bild geladen ist.
    static let placeholderSize = CGSize(width: 1600, height: 1120)

    /// Bilddatei → Grundriss: Ausrichtung anwenden, auf `maxDimension` verkleinern, als JPEG speichern.
    static func makePlan(from data: Data, maxDimension: Int = maxDimension, quality: Double = 0.8) -> Plan? {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: maxDimension,
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary),
              let colorSpace = CGColorSpace(name: CGColorSpace.sRGB),
              // Auf Weiß zeichnen: JPEG kennt keine Transparenz, sonst würde ein PNG-Hintergrund schwarz.
              let context = CGContext(data: nil, width: image.width, height: image.height, bitsPerComponent: 8,
                                      bytesPerRow: 0, space: colorSpace,
                                      bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)
        else { return nil }

        let rect = CGRect(x: 0, y: 0, width: image.width, height: image.height)
        context.setFillColor(CGColor(gray: 1, alpha: 1))
        context.fill(rect)
        context.draw(image, in: rect)

        let output = NSMutableData()
        guard let flattened = context.makeImage(),
              let destination = CGImageDestinationCreateWithData(output, UTType.jpeg.identifier as CFString, 1, nil)
        else { return nil }
        CGImageDestinationAddImage(destination, flattened,
                                   [kCGImageDestinationLossyCompressionQuality: quality] as CFDictionary)
        guard CGImageDestinationFinalize(destination) else { return nil }

        return Plan(src: "data:image/jpeg;base64," + (output as Data).base64EncodedString(),
                    w: flattened.width, h: flattened.height)
    }

    /// Bilddaten aus der Data-URL eines Grundrisses.
    static func imageData(of plan: Plan) -> Data? {
        guard let marker = plan.src.range(of: "base64,") else { return nil }
        return Data(base64Encoded: String(plan.src[marker.upperBound...]), options: .ignoreUnknownCharacters)
    }

    static func cgImage(of plan: Plan) -> CGImage? {
        guard let data = imageData(of: plan), let source = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
        return CGImageSourceCreateImageAtIndex(source, 0, nil)
    }

    /// Breite/Höhe des Plans; ohne (gültiges) Bild das Verhältnis des Platzhalters.
    static func aspectRatio(of plan: Plan?) -> CGFloat {
        guard let plan, plan.w > 0, plan.h > 0 else { return placeholderSize.width / placeholderSize.height }
        return CGFloat(plan.w) / CGFloat(plan.h)
    }
}
