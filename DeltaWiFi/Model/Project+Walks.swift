//
//  Project+Walks.swift
//  DeltaWiFi
//
//  Läufe (Heatmap-Begehungen) ablegen und entfernen.
//

import Foundation

nonisolated extension Project {
    /// Legt einen Lauf an bzw. aktualisiert ihn und verortet seine Samples neu
    /// (Zeitinterpolation zwischen den Taps; Samples außerhalb fallen weg).
    mutating func upsertWalk(_ walk: Walk, samples: [RadioSample]) {
        if let index = walks.firstIndex(where: { $0.id == walk.id }) {
            walks[index] = walk
        } else {
            walks.append(walk)
        }
        survey[walk.id] = WalkInterpolation.locate(samples, taps: walk.taps)
    }

    mutating func removeWalk(id: String) {
        guard walks.contains(where: { $0.id == id }) || survey[id] != nil else { return }
        walks.removeAll { $0.id == id }
        survey[id] = nil
    }
}
