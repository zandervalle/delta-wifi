//
//  Summary.swift
//  DeltaWiFi
//
//  Auswertung der festen Messpunkte: Übersicht je Phase und Veränderung
//  zwischen zwei Phasen.
//

import Foundation

nonisolated enum Trend: Equatable, Sendable {
    case better, worse, unchanged
}

/// Kennzahlen einer Phase für eine Messgröße, über alle Punkte mit Wert.
nonisolated struct PhaseSummary: Equatable, Sendable {
    var median: Double?
    var good = 0
    var okay = 0
    var critical = 0

    var count: Int { good + okay + critical }
}

/// Wie viele Punkte sich zwischen zwei Phasen verbessert oder verschlechtert haben.
nonisolated struct ChangeSummary: Equatable, Sendable {
    var better = 0
    var worse = 0
    var unchanged = 0

    /// Punkte, die in beiden Phasen einen Wert haben.
    var compared: Int { better + worse + unchanged }
}

nonisolated extension Metric {
    /// Anteil des Abstands gut–okay, unter dem eine Veränderung als Messrauschen gilt.
    static let unchangedTolerance = 0.1

    func trend(from before: Double, to after: Double) -> Trend { scale.trend(from: before, to: after) }

    /// Einheit einer Differenz; der Unterschied zweier Pegel in dBm ist ein Wert in dB.
    var differenceUnit: String { self == .rssi ? "dB" : unit }

    /// Differenz mit Vorzeichen, z. B. „+18 dB“ oder „−40 ms“.
    func formatDifference(_ difference: Double) -> String {
        let number = difference.formatted(.number.precision(.fractionLength(0...1))
            .sign(strategy: .always(includingZero: false)).locale(Locale(identifier: "de_DE")))
        return "\(number) \(differenceUnit)"
    }
}

nonisolated extension MetricScale {
    /// Richtung einer Veränderung. Unterschiede unter 10 % des Abstands gut–okay
    /// (z. B. unter 1 dB Signal oder 7 ms Ping) zählen als unverändert.
    func trend(from before: Double, to after: Double) -> Trend {
        let steps = (after - before) / (good - okay)
        if abs(steps) < Metric.unchangedTolerance { return .unchanged }
        return steps > 0 ? .better : .worse
    }
}

nonisolated extension Project {
    /// Anteil der Anschlussrate, ab dem der Internet-Download als gut bzw. okay gilt.
    static let lineGoodShare = 0.8
    static let lineOkayShare = 0.5

    /// Grenzwerte einer Messgröße für dieses Projekt. Ist die Anschlussrate eingetragen,
    /// wird der Internet-Download an ihr gemessen statt an festen Mbit/s.
    func scale(for metric: Metric) -> MetricScale {
        guard metric == .internetDown, let line = lineDownMbit, line > 0 else { return metric.scale }
        return MetricScale(metric: metric, good: Self.lineGoodShare * line, okay: Self.lineOkayShare * line, reference: line)
    }

    func value(of metric: Metric, at point: String, in phase: String) -> Double? {
        measurement(at: point, in: phase).flatMap(metric.value(in:))
    }

    func phaseSummary(metric: Metric, phase: String) -> PhaseSummary {
        let values = points.compactMap { value(of: metric, at: $0.name, in: phase) }
        var summary = PhaseSummary(median: MeasurementParsing.median(values))
        let scale = scale(for: metric)
        for value in values {
            switch scale.rating(of: value) {
            case .good: summary.good += 1
            case .okay: summary.okay += 1
            case .critical: summary.critical += 1
            }
        }
        return summary
    }

    func changeSummary(metric: Metric, from: String, to: String) -> ChangeSummary {
        var summary = ChangeSummary()
        let scale = scale(for: metric)
        for point in points {
            guard let before = value(of: metric, at: point.name, in: from),
                  let after = value(of: metric, at: point.name, in: to) else { continue }
            switch scale.trend(from: before, to: after) {
            case .better: summary.better += 1
            case .worse: summary.worse += 1
            case .unchanged: summary.unchanged += 1
            }
        }
        return summary
    }
}
