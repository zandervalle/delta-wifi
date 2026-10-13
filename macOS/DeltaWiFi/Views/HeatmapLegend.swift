//
//  HeatmapLegend.swift
//  DeltaWiFi
//
//  Farbskala zur Heatmap mit den Grenzwerten der gewählten Messgröße.
//

import SwiftUI

extension Color {
    init(_ color: HeatColor) {
        self.init(red: color.red, green: color.green, blue: color.blue)
    }
}

struct HeatmapLegend: View {
    /// Messgröße mit den Grenzwerten des Projekts.
    let scale: MetricScale
    let isComparison: Bool

    var body: some View {
        // Bei wenig Platz entfällt zuerst die Erläuterung der Grenzwerte; umgebrochen wird nie.
        ViewThatFits(in: .horizontal) {
            scale(withExplanation: true)
            scale(withExplanation: false)
        }
        .font(.callout)
        .lineLimit(1)
        .accessibilityElement(children: .combine)
        .help(explanation)
    }

    private func scale(withExplanation: Bool) -> some View {
        HStack(spacing: 8) {
            Text(isComparison ? "schlechter" : "kritisch")
            LinearGradient(colors: colors, startPoint: .leading, endPoint: .trailing)
                .frame(width: 120, height: 10)
                .clipShape(Capsule())
            Text(isComparison ? "besser" : "gut")
            if withExplanation {
                Text(explanation)
                    .foregroundStyle(.secondary)
            }
        }
        .fixedSize()
    }

    private var colors: [Color] {
        let steps = stride(from: 0.0, through: 1.0, by: 0.125)
        return isComparison ? steps.map { Color(HeatColor.difference($0 * 2 - 1)) }
                                  : steps.map { Color(HeatColor.scale($0)) }
    }

    private var explanation: String {
        if isComparison {
            let full = scale.metric.format(1.5 * abs(scale.good - scale.okay), withUnit: false)
            return "volle Farbe ab ± \(full) \(scale.metric.differenceUnit)"
        }
        let note = scale.reference == nil ? "" : " (50 % / 80 % des Anschlusses)"
        return "okay \(scale.metric.thresholdText(scale.okay)) · gut \(scale.metric.thresholdText(scale.good))\(note)"
    }
}
