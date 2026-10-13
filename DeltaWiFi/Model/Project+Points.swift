//
//  Project+Points.swift
//  DeltaWiFi
//
//  Messpunkte ergänzen, verschieben und entfernen.
//

import Foundation

nonisolated extension Project {
    /// Setzt die Position eines Punkts, begrenzt auf 0…1.
    mutating func setPosition(ofPointNamed name: String, x: Double, y: Double) {
        guard let index = points.firstIndex(where: { $0.name == name }) else { return }
        points[index].x = min(max(x, 0), 1)
        points[index].y = min(max(y, 0), 1)
    }

    mutating func clearPosition(ofPointNamed name: String) {
        guard let index = points.firstIndex(where: { $0.name == name }) else { return }
        points[index].x = nil
        points[index].y = nil
    }

    /// Verlegt einen Punkt auf eine andere Etage. Die Position gilt dort nicht mehr und wird gelöscht.
    mutating func setFloor(_ floor: String, forPointNamed name: String) {
        guard let index = points.firstIndex(where: { $0.name == name }), points[index].floor != floor else { return }
        points[index].floor = floor
        points[index].x = nil
        points[index].y = nil
    }

    /// Legt einen neuen Punkt an (gilt für beide Phasen). `false` bei leerem oder schon vorhandenem Namen.
    @discardableResult
    mutating func addPoint(named rawName: String, floor: String) -> Bool {
        let name = rawName.normalizedPointName
        guard !name.isEmpty, point(named: name) == nil else { return false }
        points.append(MeasurePoint(name: name, floor: floor))
        return true
    }

    func hasMeasurements(at name: String) -> Bool {
        phases.contains { phase in
            measurement(at: name, in: phase.id) != nil || !history(at: name, in: phase.id).isEmpty
        }
    }

    /// Entfernt einen Punkt, aber nur, wenn an ihm noch nichts gemessen wurde.
    @discardableResult
    mutating func removePoint(named name: String) -> Bool {
        guard !hasMeasurements(at: name), let index = points.firstIndex(where: { $0.name == name }) else { return false }
        points.remove(at: index)
        return true
    }
}
