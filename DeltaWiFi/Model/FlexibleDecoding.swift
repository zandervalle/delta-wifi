//
//  FlexibleDecoding.swift
//  DeltaWiFi
//
//  Nachsichtiges Lesen von Zahlen und Texten: Die Web-Messkarte und die
//  CSV-Dateien liefern Zahlen teils als Text ("-79", "1,7") oder leer.
//

import Foundation

/// Zahl aus Text lesen. Akzeptiert Dezimalkomma, liefert `nil` für leere oder unlesbare Werte.
nonisolated func parseNumber(_ text: String?) -> Double? {
    guard let text else { return nil }
    let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: ".")
    guard !trimmed.isEmpty, let value = Double(trimmed), value.isFinite else { return nil }
    return value
}

/// Ganze Zahlen ohne Nachkommastelle ("5"), sonst wie üblich ("2.4").
nonisolated func formatNumber(_ value: Double) -> String {
    if let whole = Int(exactly: value) { return String(whole) }
    return String(value)
}

nonisolated enum FlexValue: Decodable, Sendable {
    case number(Double)
    case text(String)
    case none

    init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if c.decodeNil() {
            self = .none
        } else if let number = try? c.decode(Double.self) {
            self = .number(number)
        } else if let text = try? c.decode(String.self) {
            self = .text(text)
        } else {
            self = .none
        }
    }

    var double: Double? {
        switch self {
        case .number(let number): number.isFinite ? number : nil
        case .text(let text): parseNumber(text)
        case .none: nil
        }
    }

    var string: String? {
        switch self {
        case .number(let number): formatNumber(number)
        case .text(let text): text
        case .none: nil
        }
    }
}

nonisolated extension KeyedDecodingContainer {
    private func flex(_ key: Key) -> FlexValue {
        ((try? decodeIfPresent(FlexValue.self, forKey: key)) ?? nil) ?? .none
    }

    func flexDouble(_ key: Key) -> Double? { flex(key).double }
    func flexString(_ key: Key) -> String? { flex(key).string }
    func flexInt(_ key: Key) -> Int? { flex(key).double.flatMap { Int(exactly: $0.rounded()) } }
    func flexInt64(_ key: Key) -> Int64? { flex(key).double.flatMap { Int64(exactly: $0.rounded()) } }
}

nonisolated extension String {
    /// Punktnamen vergleichbar machen: getrimmt und Unicode-normalisiert (ä als ein Zeichen).
    var normalizedPointName: String {
        trimmingCharacters(in: .whitespacesAndNewlines).precomposedStringWithCanonicalMapping
    }
}

nonisolated extension Data {
    var droppingUTF8BOM: Data {
        starts(with: [0xEF, 0xBB, 0xBF]) ? dropFirst(3) : self
    }
}
