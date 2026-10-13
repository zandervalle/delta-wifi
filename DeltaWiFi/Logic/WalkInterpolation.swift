//
//  WalkInterpolation.swift
//  DeltaWiFi
//
//  Ordnet Lauf-Samples über ihre Zeit einer Position zwischen zwei Taps zu.
//

import Foundation

nonisolated enum WalkInterpolation {
    /// Position zum Zeitpunkt `t` (ms), linear zwischen den umgebenden Taps interpoliert.
    /// `taps` müssen aufsteigend nach Zeit sortiert sein. Außerhalb des ersten/letzten Taps `nil`.
    static func position(at t: Int64, sortedTaps taps: [Tap]) -> (x: Double, y: Double)? {
        guard let first = taps.first, let last = taps.last, t >= first.t, t <= last.t else { return nil }

        // Erster Tap mit Zeit >= t
        var low = 0
        var high = taps.count - 1
        while low < high {
            let mid = (low + high) / 2
            if taps[mid].t < t { low = mid + 1 } else { high = mid }
        }
        let after = taps[low]
        guard low > 0, after.t > t else { return (after.x, after.y) }

        let before = taps[low - 1]
        let fraction = Double(t - before.t) / Double(after.t - before.t)
        return (before.x + (after.x - before.x) * fraction,
                before.y + (after.y - before.y) * fraction)
    }

    /// Verortet alle Samples, die zeitlich zwischen erstem und letztem Tap liegen. Der Rest fällt weg.
    static func locate(_ samples: [RadioSample], taps: [Tap]) -> [SurveySample] {
        let sortedTaps = taps.sorted { $0.t < $1.t }
        return samples.compactMap { sample in
            guard let position = position(at: sample.t, sortedTaps: sortedTaps) else { return nil }
            return SurveySample(x: position.x, y: position.y,
                                rssi: sample.rssi, snr: sample.snr, rate: sample.rate, band: sample.band,
                                bssid: sample.bssid)
        }
    }
}

/// Ein Funk-Sample mit Zeitstempel, noch ohne Position.
nonisolated struct RadioSample: Equatable, Sendable {
    /// Zeitpunkt in ms seit Epoch.
    var t: Int64
    var rssi: Double?
    var snr: Double?
    var rate: Double?
    var band: String?
    var bssid: String?
}
