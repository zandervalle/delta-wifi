//
//  PlanView.swift
//  DeltaWiFi
//
//  Grundriss einer Etage mit Heatmap und den festen Messpunkten. Klick setzt den
//  ausgewählten, noch nicht gesetzten Punkt; Ziehen verschiebt einen Punkt.
//

import SwiftUI

/// Darstellung eines Messpunkts: Statusfarbe und Wert der gewählten Messgröße.
struct PlanMarker {
    var color: Color
    var text: String
}

/// Weg eines Laufs: die angeklickten Positionen relativ 0…1.
struct PlanWalkPath: Identifiable {
    var id: String
    var taps: [CGPoint]
    /// Der laufende Lauf wird durchgezogen gezeichnet, abgeschlossene gestrichelt.
    var isActive: Bool
}

struct PlanView: View {
    let floorName: String
    let image: CGImage?
    let heatmap: CGImage?
    /// Breite / Höhe des Plans.
    let aspectRatio: CGFloat
    /// Punkte dieser Etage.
    let points: [MeasurePoint]
    /// Farbe und Wert je Punktname; Punkte ohne Eintrag haben keinen Messwert.
    let markers: [String: PlanMarker]
    let walkPaths: [PlanWalkPath]
    /// Im Lauf-Modus setzt jeder Klick auf den Plan eine Position des Laufs.
    let isWalking: Bool
    /// Ausgewählter Punkt dieser Etage, der noch keine Position hat.
    let pendingPointName: String?
    @Binding var selection: String?
    var onSetPosition: (_ name: String, _ x: Double, _ y: Double) -> Void
    var onWalkTap: (_ x: Double, _ y: Double) -> Void

    @State private var drag: (name: String, location: CGPoint)?

    private static let planSpace = "plan"

    var body: some View {
        GeometryReader { geometry in
            let size = fittedSize(in: geometry.size)
            ZStack(alignment: .topLeading) {
                background
                    .frame(width: size.width, height: size.height)
                if let heatmap {
                    Image(decorative: heatmap, scale: 1)
                        .resizable()
                        .interpolation(.high)
                        .frame(width: size.width, height: size.height)
                        .allowsHitTesting(false)
                }
                walkLayer
                    .frame(width: size.width, height: size.height)
                    .allowsHitTesting(false)
                ForEach(points.filter(\.isPlaced)) { point in
                    marker(for: point, in: size)
                        .allowsHitTesting(!isWalking)
                }
            }
            .frame(width: size.width, height: size.height)
            .coordinateSpace(.named(Self.planSpace))
            .contentShape(Rectangle())
            .onTapGesture(coordinateSpace: .local) { location in
                if isWalking {
                    onWalkTap(location.x / size.width, location.y / size.height)
                } else if let pendingPointName {
                    onSetPosition(pendingPointName, location.x / size.width, location.y / size.height)
                } else {
                    selection = nil
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    private func fittedSize(in available: CGSize) -> CGSize {
        let width = max(available.width - 32, 1)
        let height = min(max(available.height - 32, 1), width / aspectRatio)
        return CGSize(width: height * aspectRatio, height: height)
    }

    @ViewBuilder
    private var background: some View {
        if let image {
            Image(decorative: image, scale: 1)
                .resizable()
                .interpolation(.high)
                .shadow(radius: 2)
        } else {
            ZStack {
                RoundedRectangle(cornerRadius: 8)
                    .fill(.quaternary.opacity(0.5))
                RoundedRectangle(cornerRadius: 8)
                    .strokeBorder(.secondary, style: StrokeStyle(lineWidth: 1, dash: [6, 4]))
                VStack(spacing: 6) {
                    Image(systemName: "square.dashed")
                        .font(.largeTitle)
                    Text("Kein Grundriss für \(floorName)")
                        .font(.headline)
                    Text("Bild hierher ziehen oder über „Grundriss“ laden.\nPunkte lassen sich trotzdem setzen.")
                        .font(.callout)
                        .multilineTextAlignment(.center)
                }
                .foregroundStyle(.secondary)
                .padding()
            }
        }
    }

    /// Wege und Positionen der Läufe: heller Rand unter dunkler Linie, damit sie auf jeder Heatmap lesbar bleiben.
    private var walkLayer: some View {
        Canvas { context, size in
            for path in walkPaths {
                let points = path.taps.map { CGPoint(x: $0.x * size.width, y: $0.y * size.height) }
                var line = Path()
                line.addLines(points)
                let dash: [CGFloat] = path.isActive ? [] : [6, 5]
                context.stroke(line, with: .color(.white.opacity(0.9)),
                               style: StrokeStyle(lineWidth: 4.5, lineCap: .round, lineJoin: .round, dash: dash))
                context.stroke(line, with: .color(.black.opacity(0.75)),
                               style: StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round, dash: dash))
                for (index, point) in points.enumerated() {
                    // Die zuletzt gesetzte Position des laufenden Laufs ist größer.
                    let radius: CGFloat = path.isActive && index == points.count - 1 ? 6 : 3.5
                    let dot = Path(ellipseIn: CGRect(x: point.x - radius, y: point.y - radius, width: radius * 2, height: radius * 2))
                    context.fill(dot, with: .color(.black.opacity(0.8)))
                    context.stroke(dot, with: .color(.white), lineWidth: 1.5)
                }
            }
        }
    }

    private func marker(for point: MeasurePoint, in size: CGSize) -> some View {
        let isSelected = selection == point.name
        let info = markers[point.name]
        let position: CGPoint = if let drag, drag.name == point.name {
            CGPoint(x: min(max(drag.location.x, 0), size.width), y: min(max(drag.location.y, 0), size.height))
        } else {
            CGPoint(x: (point.x ?? 0) * size.width, y: (point.y ?? 0) * size.height)
        }

        return Circle()
            .fill(info?.color ?? Color(white: 0.45))
            .stroke(.white, lineWidth: 2)
            .frame(width: 14, height: 14)
            .shadow(radius: 1)
            .frame(width: 28, height: 28)
            .overlay {
                if isSelected {
                    Circle().stroke(Color.accentColor, lineWidth: 2.5)
                        .frame(width: 22, height: 22)
                }
            }
            .contentShape(Circle())
            .overlay(alignment: .top) {
                VStack(spacing: 0) {
                    Text(point.name)
                    if let info {
                        Text(info.text).fontWeight(.semibold)
                    }
                }
                .font(.caption2)
                .padding(.horizontal, 5)
                .padding(.vertical, 1)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 5))
                .fixedSize()
                .offset(y: 26)
                .allowsHitTesting(false)
            }
            .position(position)
            .zIndex(isSelected ? 1 : 0)
            .onTapGesture { selection = point.name }
            .gesture(
                DragGesture(minimumDistance: 3, coordinateSpace: .named(Self.planSpace))
                    .onChanged { value in
                        selection = point.name
                        drag = (point.name, value.location)
                    }
                    .onEnded { value in
                        drag = nil
                        onSetPosition(point.name, value.location.x / size.width, value.location.y / size.height)
                    }
            )
            .help(point.name)
    }
}
