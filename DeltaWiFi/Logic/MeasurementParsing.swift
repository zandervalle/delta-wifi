//
//  MeasurementParsing.swift
//  DeltaWiFi
//
//  Auswertung der Messwerkzeuge: Stichproben der Funkwerte, ping,
//  networkQuality, iperf3 und route.
//

import Foundation

/// Momentaufnahme der Funkwerte des WLAN-Interface.
nonisolated struct RadioSnapshot: Equatable, Sendable {
    var rssi: Int
    var noise: Int?
    var txRate: Double?
    var band: String?
    var channel: Int?
    var width: Int?
    var phy: String?
    /// Nur mit Ortungsfreigabe vorhanden.
    var bssid: String?
    var ssid: String?

    var snr: Int? { noise.map { rssi - $0 } }
}

/// Rauschpegel, die ein WLAN-Empfänger realistisch misst. macOS meldet zeitweise Werte wie −17 dBm,
/// die über dem Signal liegen; sie zählen als „nicht gemessen“.
nonisolated enum NoiseFloor {
    static let plausible: ClosedRange<Double> = -110 ... -75

    static func isPlausible(_ noise: Double) -> Bool { plausible.contains(noise) }

    /// SNR, sofern der zugehörige Rauschpegel (`rssi − snr`) plausibel ist. Ohne Signalwert bleibt der SNR ungeprüft.
    static func plausibleSNR(_ snr: Double?, rssi: Double?) -> Double? {
        guard let snr, let rssi else { return snr }
        return isPlausible(rssi - snr) ? snr : nil
    }
}

nonisolated struct PingResult: Equatable, Sendable {
    var lossPct: Double
    /// Fehlen bei 100 % Verlust.
    var avgMs: Double?
    var maxMs: Double?
}

nonisolated enum MeasurementParsing {

    // MARK: Stichproben

    static func median(_ values: [Double]) -> Double? {
        guard !values.isEmpty else { return nil }
        let sorted = values.sorted()
        let middle = sorted.count / 2
        return sorted.count.isMultiple(of: 2) ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle]
    }

    /// Häufigster Wert; bei Gleichstand der zuletzt gesehene.
    static func mostCommon<T: Hashable>(_ values: [T]) -> T? {
        var counts: [T: Int] = [:]
        var best: T?
        for value in values {
            counts[value, default: 0] += 1
            if let current = best, counts[value]! < counts[current]! { continue }
            best = value
        }
        return best
    }

    /// Fasst die Stichproben zusammen: Median (auf ganze Zahl gerundet) für die Pegel,
    /// häufigster Wert für Band, Kanal, BSSID usw.
    static func summarize(_ samples: [RadioSnapshot]) -> Measurement {
        func roundedMedian(_ values: [Double]) -> Double? { median(values)?.rounded() }
        var result = Measurement()
        result.rssiDBm = roundedMedian(samples.map { Double($0.rssi) })
        result.noiseDBm = roundedMedian(samples.compactMap { $0.noise.map(Double.init) }.filter(NoiseFloor.isPlausible))
        if let rssi = result.rssiDBm, let noise = result.noiseDBm { result.snrDB = rssi - noise }
        result.txrateMbit = roundedMedian(samples.compactMap(\.txRate))
        result.bandGHz = mostCommon(samples.compactMap(\.band))
        result.kanal = mostCommon(samples.compactMap(\.channel))
        result.breiteMHz = mostCommon(samples.compactMap(\.width))
        result.phy = mostCommon(samples.compactMap(\.phy))
        result.bssid = mostCommon(samples.compactMap(\.bssid))
        result.ssid = mostCommon(samples.compactMap(\.ssid))
        return result
    }

    /// Zeitstempel einer Messung in Ortszeit, im Format der bisherigen CSV: `2026-10-11 14:05:00`.
    static func timestamp(_ date: Date, timeZone: TimeZone = .current) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = timeZone
        formatter.dateFormat = "yyyy-MM-dd HH:mm:ss"
        return formatter.string(from: date)
    }

    // MARK: Werkzeuge

    /// Wertet die Zeilen `packet loss` und `round-trip min/avg/max/stddev` von `/sbin/ping` aus.
    static func ping(_ output: String) -> PingResult? {
        guard let loss = firstMatch(#"([0-9.]+)% packet loss"#, in: output)?.first.flatMap(Double.init) else { return nil }
        let times = firstMatch(#"min/avg/max/[a-z]+ = ([0-9.]+)/([0-9.]+)/([0-9.]+)/"#, in: output)?.compactMap(Double.init)
        return PingResult(lossPct: loss, avgMs: times.flatMap { $0.count == 3 ? $0[1] : nil },
                          maxMs: times.flatMap { $0.count == 3 ? $0[2] : nil })
    }

    /// JSON von `networkQuality -c`: `dl_throughput` / `ul_throughput` in bit/s → Mbit/s.
    static func networkQuality(_ data: Data) -> (downMbit: Double?, upMbit: Double?)? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        let down = (json["dl_throughput"] as? NSNumber).map { megabit($0.doubleValue) }
        let up = (json["ul_throughput"] as? NSNumber).map { megabit($0.doubleValue) }
        return down == nil && up == nil ? nil : (down, up)
    }

    /// JSON von `iperf3 -J`: `end.sum_received.bits_per_second` → Mbit/s.
    static func iperf(_ data: Data) -> Double? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let end = json["end"] as? [String: Any],
              let received = end["sum_received"] as? [String: Any],
              let bits = received["bits_per_second"] as? NSNumber else { return nil }
        return megabit(bits.doubleValue)
    }

    /// Zeile `gateway:` aus `route -n get default`; nur IPv4-Adressen.
    static func gateway(fromRouteOutput output: String) -> String? {
        firstMatch(#"gateway:\s*([0-9]{1,3}(?:\.[0-9]{1,3}){3})\s*(?:\n|$)"#, in: output)?.first
    }

    private static func megabit(_ bitsPerSecond: Double) -> Double {
        (bitsPerSecond / 1_000_000 * 10).rounded() / 10
    }

    /// Die Fundstellen der Klammergruppen des ersten Treffers.
    private static func firstMatch(_ pattern: String, in text: String) -> [String]? {
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) else { return nil }
        return (1..<match.numberOfRanges).compactMap { Range(match.range(at: $0), in: text).map { String(text[$0]) } }
    }
}
