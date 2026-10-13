//
//  Metric.swift
//  DeltaWiFi
//
//  Messgrößen mit ihren Grenzwerten und die Farbskala der Heatmap.
//

import Foundation

nonisolated enum Rating: Equatable, Sendable {
    case good, okay, critical
}

nonisolated enum Metric: String, CaseIterable, Identifiable, Sendable {
    case rssi = "rssi_dbm"
    case snr = "snr_db"
    case linkRate = "txrate_mbit"
    case gatewayLoss = "ping_gw_verlust_pct"
    case gatewayMaxPing = "ping_gw_max_ms"
    case internetDown = "inet_down_mbit"
    case lanDown = "lan_down_mbit"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .rssi: "Signalstärke"
        case .snr: "Signal-Rausch-Abstand"
        case .linkRate: "Link-Rate"
        case .gatewayLoss: "Paketverlust zur Box"
        case .gatewayMaxPing: "Max. Ping zur Box"
        case .internetDown: "Internet Download"
        case .lanDown: "Intern Download (iperf3)"
        }
    }

    var unit: String {
        switch self {
        case .rssi: "dBm"
        case .snr: "dB"
        case .linkRate, .internetDown, .lanDown: "Mbit/s"
        case .gatewayLoss: "%"
        case .gatewayMaxPing: "ms"
        }
    }

    var higherIsBetter: Bool {
        switch self {
        case .gatewayLoss, .gatewayMaxPing: false
        default: true
        }
    }

    /// Grenzwert, ab dem ein Wert als gut gilt.
    var good: Double {
        switch self {
        case .rssi: -60
        case .snr: 30
        case .linkRate: 400
        case .gatewayLoss: 0
        case .gatewayMaxPing: 30
        case .internetDown: 120
        case .lanDown: 300
        }
    }

    /// Grenzwert, ab dem ein Wert noch okay ist; jenseits davon kritisch.
    var okay: Double {
        switch self {
        case .rssi: -70
        case .snr: 20
        case .linkRate: 150
        case .gatewayLoss: 1
        case .gatewayMaxPing: 100
        case .internetDown: 60
        case .lanDown: 100
        }
    }

    /// Läufe liefern nur Funkwerte, keinen Ping und keinen Durchsatz.
    var isAvailableFromWalks: Bool {
        switch self {
        case .rssi, .snr, .linkRate: true
        default: false
        }
    }

    func value(in measurement: Measurement) -> Double? {
        switch self {
        case .rssi: measurement.rssiDBm
        case .snr: NoiseFloor.plausibleSNR(measurement.snrDB, rssi: measurement.rssiDBm)
        case .linkRate: measurement.txrateMbit
        case .gatewayLoss: measurement.pingGwVerlustPct
        case .gatewayMaxPing: measurement.pingGwMaxMs
        case .internetDown: measurement.inetDownMbit
        case .lanDown: measurement.lanDownMbit
        }
    }

    func value(in sample: SurveySample) -> Double? {
        switch self {
        case .rssi: sample.rssi
        case .snr: NoiseFloor.plausibleSNR(sample.snr, rssi: sample.rssi)
        case .linkRate: sample.rate
        default: nil
        }
    }

    /// Skala mit den festen Standardgrenzwerten. Projektbezogene Grenzen liefert `Project.scale(for:)`.
    var scale: MetricScale { MetricScale(metric: self, good: good, okay: okay) }

    func rating(of value: Double) -> Rating { scale.rating(of: value) }
    func score(of value: Double) -> Double { scale.score(of: value) }
    func change(from before: Double, to after: Double) -> Double { scale.change(from: before, to: after) }

    func format(_ value: Double, withUnit: Bool = true) -> String {
        let number = value.formatted(.number.precision(.fractionLength(0...1)).locale(Locale(identifier: "de_DE")))
        return withUnit ? "\(number) \(unit)" : number
    }

    /// Grenzwert als Text, z. B. „≥ -60 dBm“ oder „≤ 30 ms“.
    func thresholdText(_ threshold: Double) -> String {
        if !higherIsBetter && threshold == 0 { return format(threshold) }
        return "\(higherIsBetter ? "≥" : "≤") \(format(threshold))"
    }
}

/// Eine Messgröße mit den Grenzwerten, die für ein Projekt gelten. Für den Internet-Download
/// richten sie sich nach der Anschlussrate, sofern der Nutzer sie eingetragen hat.
nonisolated struct MetricScale: Equatable, Sendable {
    let metric: Metric
    /// Grenzwert, ab dem ein Wert als gut gilt.
    var good: Double
    /// Grenzwert, ab dem ein Wert noch okay ist.
    var okay: Double
    /// Bezugswert für Prozentangaben (Anschlussrate); `nil` = keine Prozentangabe.
    var reference: Double?

    func rating(of value: Double) -> Rating {
        if metric.higherIsBetter {
            value >= good ? .good : value >= okay ? .okay : .critical
        } else {
            value <= good ? .good : value <= okay ? .okay : .critical
        }
    }

    /// Stetige Bewertung für die Farbskala: 1 = gut-Grenze (grün), 0,5 = okay-Grenze (gelb),
    /// 0 = noch einmal derselbe Abstand jenseits der okay-Grenze (rot).
    func score(of value: Double) -> Double {
        min(max(0.5 + 0.5 * (value - okay) / (good - okay), 0), 1)
    }

    /// Veränderung von −1 (deutlich schlechter) bis +1 (deutlich besser).
    /// Volle Sättigung bei 1,5 × |gut − okay|.
    func change(from before: Double, to after: Double) -> Double {
        min(max((after - before) / (1.5 * (good - okay)), -1), 1)
    }

    /// Wert mit Einheit, bei bekannter Anschlussrate mit Anteil: „212 Mbit/s (85 %)“.
    func describe(_ value: Double) -> String {
        metric.format(value) + (Self.shareText(value, of: reference) ?? "")
    }

    /// Anteil am Bezugswert als Zusatz „ (85 %)“; `nil` ohne gültigen Bezugswert.
    static func shareText(_ value: Double?, of reference: Double?) -> String? {
        guard let value, let reference, reference > 0 else { return nil }
        return " (\(Int((value / reference * 100).rounded())) %)"
    }
}

/// Farben der Heatmap als sRGB-Anteile 0…1.
nonisolated struct HeatColor: Equatable, Sendable {
    var red: Double
    var green: Double
    var blue: Double

    static let critical = HeatColor(red: 0.84, green: 0.19, blue: 0.17)
    static let okay = HeatColor(red: 0.95, green: 0.77, blue: 0.10)
    static let good = HeatColor(red: 0.16, green: 0.66, blue: 0.31)
    static let unchanged = HeatColor(red: 0.55, green: 0.55, blue: 0.55)

    func mixed(with other: HeatColor, fraction: Double) -> HeatColor {
        // In dieser Form sind die Endpunkte exakt (fraction 0 bzw. 1).
        let rest = 1 - fraction
        return HeatColor(red: red * rest + other.red * fraction,
                         green: green * rest + other.green * fraction,
                         blue: blue * rest + other.blue * fraction)
    }

    /// Rot → Gelb → Grün für eine Bewertung 0…1 (siehe `Metric.score`).
    static func scale(_ score: Double) -> HeatColor {
        let score = min(max(score, 0), 1)
        return score < 0.5 ? critical.mixed(with: okay, fraction: score * 2)
                           : okay.mixed(with: good, fraction: score * 2 - 1)
    }

    /// Grau = unverändert, Grün = besser, Rot = schlechter für eine Veränderung −1…1.
    static func difference(_ change: Double) -> HeatColor {
        let change = min(max(change, -1), 1)
        return unchanged.mixed(with: change >= 0 ? good : critical, fraction: abs(change))
    }
}
