//
//  Project+Measuring.swift
//  DeltaWiFi
//
//  Messungen an festen Punkten ablegen und den geführten Ablauf steuern.
//

import Foundation

nonisolated extension Project {
    /// Legt eine neue Messung ab: Sie wird zur gültigen Messung des Punkts, frühere bleiben in der Historie.
    /// Liefert die vergebene Wiederholungsnummer.
    @discardableResult
    mutating func record(_ measurement: Measurement, at name: String, phase: String) -> Int {
        var all = history(at: name, in: phase)
        // Ältere Dateien kennen nur die aktuelle Messung: in die Historie retten, bevor sie ersetzt wird.
        if all.isEmpty, let current = self.measurement(at: name, in: phase) { all.append(current) }

        var measurement = measurement
        let repetition = max(all.compactMap(\.wiederholung).max() ?? 0, all.count) + 1
        measurement.wiederholung = repetition
        all.append(measurement)
        history[phase, default: [:]][name] = all
        measurements[phase, default: [:]][name] = measurement
        return repetition
    }

    /// Punkte, die in der Phase noch keine Messung haben, in Listenreihenfolge.
    func openPoints(in phase: String) -> [MeasurePoint] {
        points.filter { measurement(at: $0.name, in: phase) == nil }
    }

    /// Nächster offener Punkt hinter `name` (mit Umbruch am Listenende); ohne `name` der erste.
    func nextOpenPoint(in phase: String, after name: String?) -> MeasurePoint? {
        let start = name.flatMap { name in points.firstIndex { $0.name == name } }.map { $0 + 1 } ?? 0
        let ordered = points[start...] + points[..<start]
        return ordered.first { measurement(at: $0.name, in: phase) == nil && $0.name != name }
    }
}
