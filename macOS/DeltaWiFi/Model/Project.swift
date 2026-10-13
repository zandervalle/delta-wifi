//
//  Project.swift
//  DeltaWiFi
//
//  Datenmodell des Dokuments. Das JSON-Format folgt `projekt.json` der
//  bisherigen Web-Messkarte (siehe CLAUDE.md, „Datenformat“).
//

import Foundation

// MARK: - Etage und Phase

/// Eine Etage bzw. Ebene des Projekts. Punkte, Grundrisse und Läufe verweisen über die `id` darauf;
/// der Name ist frei wählbar.
nonisolated struct Floor: Codable, Hashable, Identifiable, Sendable {
    var id: String
    var name: String

    static let defaults = [
        Floor(id: "keller", name: "Keller"),
        Floor(id: "eg", name: "Erdgeschoss"),
        Floor(id: "og", name: "Obergeschoss"),
        Floor(id: "aussen", name: "Außen"),
    ]
}

/// Eine Messphase (z. B. vor und nach dem Umbau). Messungen und Läufe verweisen über die `id` darauf.
nonisolated struct Phase: Codable, Hashable, Identifiable, Sendable {
    var id: String
    var name: String

    static let defaults = [
        Phase(id: "VORHER", name: "Vorher"),
        Phase(id: "NACHHER", name: "Nachher"),
    ]
}

// MARK: - Grundriss, Punkt, Messung

nonisolated struct Plan: Codable, Equatable, Sendable {
    /// Bild als Data-URL (`data:image/jpeg;base64,…`).
    var src: String
    var w: Int
    var h: Int

    enum CodingKeys: String, CodingKey {
        case src, w, h
    }

    init(src: String, w: Int, h: Int) {
        self.src = src
        self.w = w
        self.h = h
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        src = try c.decode(String.self, forKey: .src)
        w = c.flexInt(.w) ?? 0
        h = c.flexInt(.h) ?? 0
    }
}

nonisolated struct MeasurePoint: Codable, Equatable, Identifiable, Sendable {
    var name: String
    /// ID der Etage (`Floor.id`).
    var floor: String
    /// Position relativ 0…1, `nil` = noch nicht gesetzt.
    var x: Double?
    var y: Double?

    var id: String { name }
    var isPlaced: Bool { x != nil && y != nil }

    enum CodingKeys: String, CodingKey {
        case name, floor, x, y
    }

    init(name: String, floor: String, x: Double? = nil, y: Double? = nil) {
        self.name = name
        self.floor = floor
        self.x = x
        self.y = y
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = try c.decode(String.self, forKey: .name).normalizedPointName
        // Fehlt die Etage, ordnet das Projekt den Punkt beim Laden der ersten Etage zu.
        floor = c.flexString(.floor) ?? ""
        x = c.flexDouble(.x)
        y = c.flexDouble(.y)
    }

    // Nicht gesetzte Positionen werden ausdrücklich als `null` geschrieben.
    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(name, forKey: .name)
        try c.encode(floor, forKey: .floor)
        try c.encode(x, forKey: .x)
        try c.encode(y, forKey: .y)
    }
}

/// Eine Messung an einem festen Punkt. Alle Werte sind optional, weil alte
/// CSV-Zeilen lückenhaft sein können (kein iperf3, Speedtest abgeschaltet …).
nonisolated struct Measurement: Codable, Equatable, Sendable {
    var zeit: String?
    var wiederholung: Int?
    var ssid: String?
    var bssid: String?
    var bandGHz: String?
    var kanal: Int?
    var breiteMHz: Int?
    var phy: String?
    var nss: Int?
    var rssiDBm: Double?
    var noiseDBm: Double?
    var snrDB: Double?
    var txrateMbit: Double?
    var pingGwVerlustPct: Double?
    var pingGwAvgMs: Double?
    var pingGwMaxMs: Double?
    var pingInetVerlustPct: Double?
    var pingInetAvgMs: Double?
    var pingInetMaxMs: Double?
    var inetDownMbit: Double?
    var inetUpMbit: Double?
    var lanDownMbit: Double?
    var lanUpMbit: Double?
    var notiz: String?

    enum CodingKeys: String, CodingKey {
        case zeit, wiederholung, ssid, bssid
        case bandGHz = "band_ghz"
        case kanal
        case breiteMHz = "breite_mhz"
        case phy, nss
        case rssiDBm = "rssi_dbm"
        case noiseDBm = "noise_dbm"
        case snrDB = "snr_db"
        case txrateMbit = "txrate_mbit"
        case pingGwVerlustPct = "ping_gw_verlust_pct"
        case pingGwAvgMs = "ping_gw_avg_ms"
        case pingGwMaxMs = "ping_gw_max_ms"
        case pingInetVerlustPct = "ping_inet_verlust_pct"
        case pingInetAvgMs = "ping_inet_avg_ms"
        case pingInetMaxMs = "ping_inet_max_ms"
        case inetDownMbit = "inet_down_mbit"
        case inetUpMbit = "inet_up_mbit"
        case lanDownMbit = "lan_down_mbit"
        case lanUpMbit = "lan_up_mbit"
        case notiz
    }

    init() {}

    /// Gemeinsamer Aufbau für JSON und CSV: beide liefern Text bzw. Zahl je Feldname.
    init(text: (CodingKeys) -> String?, number: (CodingKeys) -> Double?) {
        func string(_ key: CodingKeys) -> String? {
            guard let value = text(key)?.trimmingCharacters(in: .whitespacesAndNewlines), !value.isEmpty else { return nil }
            return value
        }
        func int(_ key: CodingKeys) -> Int? { number(key).flatMap { Int(exactly: $0.rounded()) } }

        zeit = string(.zeit)
        wiederholung = int(.wiederholung)
        ssid = string(.ssid)
        bssid = string(.bssid)
        bandGHz = number(.bandGHz).map(formatNumber) ?? string(.bandGHz)
        kanal = int(.kanal)
        breiteMHz = int(.breiteMHz)
        phy = string(.phy)
        nss = int(.nss)
        rssiDBm = number(.rssiDBm)
        noiseDBm = number(.noiseDBm)
        snrDB = number(.snrDB)
        txrateMbit = number(.txrateMbit)
        pingGwVerlustPct = number(.pingGwVerlustPct)
        pingGwAvgMs = number(.pingGwAvgMs)
        pingGwMaxMs = number(.pingGwMaxMs)
        pingInetVerlustPct = number(.pingInetVerlustPct)
        pingInetAvgMs = number(.pingInetAvgMs)
        pingInetMaxMs = number(.pingInetMaxMs)
        inetDownMbit = number(.inetDownMbit)
        inetUpMbit = number(.inetUpMbit)
        lanDownMbit = number(.lanDownMbit)
        lanUpMbit = number(.lanUpMbit)
        notiz = string(.notiz)
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.init(text: { c.flexString($0) }, number: { c.flexDouble($0) })
    }
}

// MARK: - Läufe

nonisolated struct Tap: Codable, Equatable, Sendable {
    /// Zeitpunkt in ms seit Epoch.
    var t: Int64
    var x: Double
    var y: Double

    enum CodingKeys: String, CodingKey {
        case t, x, y
    }

    init(t: Int64, x: Double, y: Double) {
        self.t = t
        self.x = x
        self.y = y
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        guard let t = c.flexInt64(.t), let x = c.flexDouble(.x), let y = c.flexDouble(.y) else {
            throw DecodingError.dataCorrupted(.init(codingPath: decoder.codingPath,
                                                    debugDescription: "Tap braucht t, x und y"))
        }
        self.init(t: t, x: x, y: y)
    }
}

nonisolated struct Walk: Codable, Equatable, Identifiable, Sendable {
    var id: String
    /// ID der Phase (`Phase.id`).
    var phase: String
    /// ID der Etage (`Floor.id`).
    var floor: String
    /// Startzeit in ms seit Epoch.
    var started: Int64
    var taps: [Tap]

    enum CodingKeys: String, CodingKey {
        case id, phase, floor, started, taps
    }

    init(id: String, phase: String, floor: String, started: Int64, taps: [Tap] = []) {
        self.id = id
        self.phase = phase
        self.floor = floor
        self.started = started
        self.taps = taps
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        phase = c.flexString(.phase) ?? ""
        floor = c.flexString(.floor) ?? ""
        taps = try c.decodeIfPresent([Tap].self, forKey: .taps) ?? []
        started = c.flexInt64(.started) ?? taps.first?.t ?? 0
        id = c.flexString(.id) ?? "w\(started)"
    }
}

/// Ein verortetes Lauf-Sample, im JSON als Array `[x, y, rssi, snr, rate, band, bssid]`.
/// Die BSSID ist eine Erweiterung gegenüber der Web-Messkarte und fehlt, wenn sie unbekannt ist.
nonisolated struct SurveySample: Codable, Equatable, Sendable {
    var x: Double
    var y: Double
    var rssi: Double?
    var snr: Double?
    var rate: Double?
    var band: String?
    var bssid: String?

    init(x: Double, y: Double, rssi: Double? = nil, snr: Double? = nil, rate: Double? = nil, band: String? = nil,
         bssid: String? = nil) {
        self.x = x
        self.y = y
        self.rssi = rssi
        self.snr = snr
        self.rate = rate
        self.band = band
        self.bssid = bssid
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.singleValueContainer().decode([FlexValue].self)
        func value(_ index: Int) -> FlexValue { index < values.count ? values[index] : .none }
        guard let x = value(0).double, let y = value(1).double else {
            throw DecodingError.dataCorrupted(.init(codingPath: decoder.codingPath,
                                                    debugDescription: "Sample braucht x und y"))
        }
        self.init(x: x, y: y, rssi: value(2).double, snr: value(3).double, rate: value(4).double, band: value(5).string,
                  bssid: value(6).string.flatMap { $0.isEmpty ? nil : $0 })
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.unkeyedContainer()
        try c.encode(x)
        try c.encode(y)
        for number in [rssi, snr, rate] {
            if let number { try c.encode(number) } else { try c.encodeNil() }
        }
        if let band { try c.encode(band) } else { try c.encodeNil() }
        // Ohne BSSID bleibt es bei den sechs Elementen der Web-Messkarte.
        if let bssid { try c.encode(bssid) }
    }
}

// MARK: - Projekt

nonisolated struct Project: Codable, Equatable, Sendable {
    var version = 1
    /// Etagen bzw. Bereiche in Anzeigereihenfolge; vom Nutzer verwaltet. Ein neues Projekt hat noch keine.
    var floors: [Floor] = []
    /// Phasen in Anzeigereihenfolge; vom Nutzer verwaltet, nie leer.
    var phases: [Phase] = Phase.defaults
    /// Synchronisierte Rate des Internetanschlusses in Mbit/s; optional, für die Bewertung der Speedtests.
    var lineDownMbit: Double?
    var lineUpMbit: Double?
    /// Grundriss je Etage (Schlüssel = `Floor.id`).
    var plans: [String: Plan] = [:]
    var points: [MeasurePoint] = []
    /// Phase → Punktname → aktuell gültige (letzte) Messung.
    var measurements: [String: [String: Measurement]] = Project.emptyPhases(Phase.defaults)
    /// Phase → Punktname → alle Wiederholungen, aufsteigend.
    var history: [String: [String: [Measurement]]] = Project.emptyPhases(Phase.defaults)
    var walks: [Walk] = []
    /// Lauf-ID → verortete Samples.
    var survey: [String: [SurveySample]] = [:]

    enum CodingKeys: String, CodingKey {
        case version, floors, phases, plans, points, measurements, history, walks, survey
        case lineDownMbit = "line_down_mbit"
        case lineUpMbit = "line_up_mbit"
    }

    private static func emptyPhases<T>(_ phases: [Phase]) -> [String: [String: T]] {
        Dictionary(phases.map { ($0.id, [:]) }, uniquingKeysWith: { first, _ in first })
    }

    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        version = c.flexInt(.version) ?? 1
        // Ältere Dateien kennen keine Listen: Dann gelten die vier früheren Standardetagen und die zwei Standardphasen.
        floors = try c.decodeIfPresent([Floor].self, forKey: .floors) ?? Floor.defaults
        phases = try c.decodeIfPresent([Phase].self, forKey: .phases) ?? Phase.defaults
        lineDownMbit = c.flexDouble(.lineDownMbit).flatMap { $0 > 0 ? $0 : nil }
        lineUpMbit = c.flexDouble(.lineUpMbit).flatMap { $0 > 0 ? $0 : nil }
        plans = try c.decodeIfPresent([String: Plan].self, forKey: .plans) ?? [:]
        points = try c.decodeIfPresent([MeasurePoint].self, forKey: .points) ?? []
        measurements = Self.normalizedPointNames(try c.decodeIfPresent([String: [String: Measurement]].self, forKey: .measurements))
        history = Self.normalizedPointNames(try c.decodeIfPresent([String: [String: [Measurement]]].self, forKey: .history))
        walks = try c.decodeIfPresent([Walk].self, forKey: .walks) ?? []
        survey = try c.decodeIfPresent([String: [SurveySample]].self, forKey: .survey) ?? [:]
        normalize()
    }

    private static func normalizedPointNames<T>(_ raw: [String: [String: T]]?) -> [String: [String: T]] {
        (raw ?? [:]).mapValues { byPoint in
            Dictionary(byPoint.map { ($0.key.normalizedPointName, $0.value) }, uniquingKeysWith: { first, _ in first })
        }
    }

    /// Stellt die Grundannahmen her: Etagen und Phasen sind eindeutig, Phasen nie leer, und alles,
    /// worauf Punkte, Grundrisse, Messungen und Läufe verweisen, existiert auch.
    mutating func normalize() {
        var seen = Set<String>()
        floors = floors.filter { !$0.id.isEmpty && seen.insert($0.id).inserted }
        seen = []
        phases = phases.filter { !$0.id.isEmpty && seen.insert($0.id).inserted }

        // Verwaiste Verweise bekommen einen Eintrag, damit keine Daten unsichtbar werden.
        var floorIDs = Set(floors.map(\.id))
        let usedFloors = points.map(\.floor) + walks.map(\.floor) + plans.keys.sorted()
        for id in usedFloors where !id.isEmpty && floorIDs.insert(id).inserted {
            floors.append(Floor.defaults.first { $0.id == id } ?? Floor(id: id, name: id))
        }

        var phaseIDs = Set(phases.map(\.id))
        let usedPhases = measurements.keys.sorted() + history.keys.sorted() + walks.map(\.phase)
        for id in usedPhases where !id.isEmpty && phaseIDs.insert(id).inserted {
            phases.append(Phase.defaults.first { $0.id == id } ?? Phase(id: id, name: id))
        }
        if phases.isEmpty { phases = Phase.defaults }

        // Punkte und Läufe ohne Angabe landen auf der ersten Etage bzw. in der ersten Phase.
        let firstFloor = floors.first?.id ?? ""
        for index in points.indices where points[index].floor.isEmpty { points[index].floor = firstFloor }
        for index in walks.indices {
            if walks[index].floor.isEmpty { walks[index].floor = firstFloor }
            if walks[index].phase.isEmpty { walks[index].phase = phases[0].id }
        }
        for phase in phases {
            if measurements[phase.id] == nil { measurements[phase.id] = [:] }
            if history[phase.id] == nil { history[phase.id] = [:] }
        }
        ensurePointsForMeasurements()
    }

    // MARK: Zugriff

    func floorName(_ id: String) -> String {
        floors.first { $0.id == id }?.name ?? id
    }

    func phaseName(_ id: String) -> String {
        phases.first { $0.id == id }?.name ?? id
    }

    /// Die gewünschte Etage, falls es sie (noch) gibt, sonst die erste.
    func resolvedFloorID(_ id: String?) -> String {
        floors.first { $0.id == id }?.id ?? floors.first?.id ?? ""
    }

    /// Die gewünschte Phase, falls es sie (noch) gibt, sonst `fallback` bzw. die erste.
    func resolvedPhaseID(_ id: String?, fallback: String? = nil) -> String {
        phases.first { $0.id == id }?.id ?? fallback ?? phases.first?.id ?? ""
    }

    func measurement(at point: String, in phase: String) -> Measurement? {
        measurements[phase]?[point]
    }

    func history(at point: String, in phase: String) -> [Measurement] {
        history[phase]?[point] ?? []
    }

    func point(named name: String) -> MeasurePoint? {
        points.first { $0.name == name }
    }

    /// Legt für Messungen ohne Punkt einen Punkt auf der ersten Etage an („nicht gesetzt“).
    @discardableResult
    mutating func ensurePointsForMeasurements() -> [String] {
        var known = Set(points.map(\.name))
        var added: [String] = []
        let names = phases.flatMap { phase in (measurements[phase.id] ?? [:]).keys.sorted() }
        for name in names where known.insert(name).inserted {
            points.append(MeasurePoint(name: name, floor: floors.first?.id ?? ""))
            added.append(name)
        }
        return added
    }

    // MARK: Datei

    static func decode(_ data: Data) throws -> Project {
        try JSONDecoder().decode(Project.self, from: data.droppingUTF8BOM)
    }

    func encoded() throws -> Data {
        let encoder = JSONEncoder()
        // Ohne maskierte Schrägstriche, sonst blähen sich die Base64-Grundrisse auf.
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(self)
    }
}
