//
//  WalkRecorder.swift
//  DeltaWiFi
//
//  Lauf-Modus: sampelt jede Sekunde die Funkwerte und merkt sich die
//  angeklickten Positionen (Taps) mit Zeitstempel.
//

import Foundation
import Observation

@Observable
final class WalkRecorder {
    /// Der laufende Lauf mit seinen bisherigen Taps; `nil`, wenn keiner läuft.
    private(set) var walk: Walk?
    /// Alle Funk-Samples seit dem Start, noch ohne Position.
    private(set) var samples: [RadioSample] = []
    /// Letzte Momentaufnahme für die Anzeige; `nil`, wenn die Verbindung weg ist.
    private(set) var lastSnapshot: RadioSnapshot?

    private var task: Task<Void, Never>?

    var isActive: Bool { walk != nil }

    private static var now: Int64 { Int64((Date.now.timeIntervalSince1970 * 1000).rounded()) }

    func start(phase: String, floor: String) {
        guard !isActive else { return }
        let started = Self.now
        walk = Walk(id: "w\(started)", phase: phase, floor: floor, started: started)
        samples = []
        task = Task { [weak self] in
            let clock = ContinuousClock()
            var next = clock.now
            while !Task.isCancelled {
                self?.sample()
                next += .seconds(1)
                try? await clock.sleep(until: next)
            }
        }
    }

    private func sample() {
        lastSnapshot = WiFi.snapshot()
        guard let snapshot = lastSnapshot else { return }
        samples.append(RadioSample(t: Self.now, rssi: Double(snapshot.rssi), snr: snapshot.snr.map(Double.init),
                                   rate: snapshot.txRate, band: snapshot.band, bssid: snapshot.bssid))
    }

    /// Merkt die aktuelle Position (relativ 0…1) mit Zeitstempel.
    func addTap(x: Double, y: Double) {
        walk?.taps.append(Tap(t: Self.now, x: min(max(x, 0), 1), y: min(max(y, 0), 1)))
    }

    func removeLastTap() {
        guard walk?.taps.isEmpty == false else { return }
        walk?.taps.removeLast()
    }

    /// Schreibt den Zwischenstand ins Projekt, damit Weg und Heatmap mitwachsen
    /// und bei einem Abbruch nichts verloren geht.
    func commit(into project: inout Project) {
        guard let walk else { return }
        if walk.taps.isEmpty {
            project.removeWalk(id: walk.id)
        } else {
            project.upsertWalk(walk, samples: samples)
        }
    }

    /// Beendet den Lauf und liefert eine Meldung für die Oberfläche.
    func finish(into project: inout Project) -> String? {
        guard let walk else { return nil }
        stopSampling()
        guard walk.taps.count >= 2 else {
            project.removeWalk(id: walk.id)
            return "Lauf verworfen: Für die Zuordnung sind mindestens zwei Positionen nötig."
        }
        project.upsertWalk(walk, samples: samples)
        let located = project.survey[walk.id]?.count ?? 0
        return "Lauf gespeichert: \(located) von \(samples.count) Samples zwischen \(walk.taps.count) Positionen verortet."
    }

    func discard(from project: inout Project) {
        guard let walk else { return }
        stopSampling()
        project.removeWalk(id: walk.id)
    }

    /// Stoppt das Sampling, ohne das Projekt anzufassen (z. B. wenn das Fenster schließt).
    func stopSampling() {
        task?.cancel()
        task = nil
        walk = nil
    }
}
