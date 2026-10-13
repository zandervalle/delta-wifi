//
//  Project+Structure.swift
//  DeltaWiFi
//
//  Etagen und Phasen verwalten: anlegen, umbenennen, umsortieren, löschen.
//

import Foundation

nonisolated extension Project {
    /// Was an einer Etage hängt und beim Löschen mit entfernt wird.
    struct FloorUsage: Equatable, Sendable {
        var hasPlan = false
        var points = 0
        var measuredPoints = 0
        var walks = 0

        var isEmpty: Bool { !hasPlan && points == 0 && walks == 0 }
    }

    /// Was an einer Phase hängt und beim Löschen mit entfernt wird.
    struct PhaseUsage: Equatable, Sendable {
        var measuredPoints = 0
        var walks = 0

        var isEmpty: Bool { measuredPoints == 0 && walks == 0 }
    }

    // MARK: Einrichtung

    /// Ein Bereich mit seinen Messpunkten, wie er beim Einrichten eines Projekts abgefragt wird.
    struct SetupArea: Equatable, Sendable {
        var name: String
        var points: [String]
    }

    /// Legt Bereiche samt Messpunkten an. Bereiche ohne Namen, leere und doppelte Punktnamen werden übergangen.
    mutating func addAreas(_ areas: [SetupArea]) {
        for area in areas {
            let name = area.name.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !name.isEmpty else { continue }
            let floor = addFloor(named: name)
            for point in area.points { addPoint(named: point, floor: floor) }
        }
    }

    // MARK: Etagen

    @discardableResult
    mutating func addFloor(named name: String) -> String {
        let id = Self.unusedID(prefix: "etage", taken: floors.map(\.id))
        floors.append(Floor(id: id, name: name))
        // Punkte, die es schon vor der ersten Etage gab, gehören ab jetzt zu ihr.
        for index in points.indices where points[index].floor.isEmpty { points[index].floor = id }
        return id
    }

    mutating func renameFloor(id: String, to name: String) {
        guard let index = floors.firstIndex(where: { $0.id == id }) else { return }
        floors[index].name = name
    }

    /// Verschiebt eine Etage um `offset` Plätze in der Anzeigereihenfolge.
    mutating func moveFloor(id: String, by offset: Int) {
        Self.move(&floors, id: id, by: offset)
    }

    func floorUsage(id: String) -> FloorUsage {
        let onFloor = points.filter { $0.floor == id }
        return FloorUsage(hasPlan: plans[id] != nil, points: onFloor.count,
                          measuredPoints: onFloor.filter { hasMeasurements(at: $0.name) }.count,
                          walks: walks.filter { $0.floor == id }.count)
    }

    /// Löscht eine Etage samt Grundriss, Punkten (mit ihren Messungen) und Läufen. Die letzte Etage bleibt.
    @discardableResult
    mutating func removeFloor(id: String) -> Bool {
        guard floors.count > 1, floors.contains(where: { $0.id == id }) else { return false }
        for point in points where point.floor == id {
            for phase in phases {
                measurements[phase.id]?[point.name] = nil
                history[phase.id]?[point.name] = nil
            }
        }
        points.removeAll { $0.floor == id }
        for walk in walks where walk.floor == id { survey[walk.id] = nil }
        walks.removeAll { $0.floor == id }
        plans[id] = nil
        floors.removeAll { $0.id == id }
        return true
    }

    // MARK: Phasen

    @discardableResult
    mutating func addPhase(named name: String) -> String {
        let id = Self.unusedID(prefix: "phase", taken: phases.map(\.id))
        phases.append(Phase(id: id, name: name))
        measurements[id] = [:]
        history[id] = [:]
        return id
    }

    mutating func renamePhase(id: String, to name: String) {
        guard let index = phases.firstIndex(where: { $0.id == id }) else { return }
        phases[index].name = name
    }

    mutating func movePhase(id: String, by offset: Int) {
        Self.move(&phases, id: id, by: offset)
    }

    func phaseUsage(id: String) -> PhaseUsage {
        PhaseUsage(measuredPoints: Set((measurements[id] ?? [:]).keys).union((history[id] ?? [:]).filter { !$0.value.isEmpty }.keys).count,
                   walks: walks.filter { $0.phase == id }.count)
    }

    /// Löscht eine Phase samt ihren Messungen und Läufen. Die letzte Phase bleibt.
    @discardableResult
    mutating func removePhase(id: String) -> Bool {
        guard phases.count > 1, phases.contains(where: { $0.id == id }) else { return false }
        measurements[id] = nil
        history[id] = nil
        for walk in walks where walk.phase == id { survey[walk.id] = nil }
        walks.removeAll { $0.phase == id }
        phases.removeAll { $0.id == id }
        return true
    }

    // MARK: Hilfen

    /// Kleinste freie ID der Form `prefix-N`.
    private static func unusedID(prefix: String, taken: [String]) -> String {
        let taken = Set(taken)
        var number = 1
        while taken.contains("\(prefix)-\(number)") { number += 1 }
        return "\(prefix)-\(number)"
    }

    private static func move<Item: Identifiable>(_ items: inout [Item], id: Item.ID, by offset: Int) {
        guard let index = items.firstIndex(where: { $0.id == id }) else { return }
        let target = min(max(index + offset, 0), items.count - 1)
        guard target != index else { return }
        items.insert(items.remove(at: index), at: target)
    }
}
