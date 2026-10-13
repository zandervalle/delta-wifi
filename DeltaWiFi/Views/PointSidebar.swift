//
//  PointSidebar.swift
//  DeltaWiFi
//
//  Liste aller Messpunkte, nach Etage gruppiert.
//

import SwiftUI

struct PointSidebar: View {
    @Binding var project: Project
    /// ID der aktuell angezeigten Etage; neue Punkte werden dort angelegt.
    let floor: String
    @Binding var selection: String?

    @State private var showAddPoint = false
    @State private var newPointName = ""

    var body: some View {
        List(selection: $selection) {
            ForEach(project.floors) { floor in
                let points = project.points.filter { $0.floor == floor.id }
                if !points.isEmpty {
                    Section(floor.name) {
                        ForEach(points) { point in
                            row(for: point)
                                .tag(point.name)
                                .contextMenu { menu(for: point) }
                        }
                    }
                }
            }
        }
        .safeAreaInset(edge: .bottom, spacing: 0) {
            VStack(spacing: 0) {
                Divider()
                Button("Punkt hinzufügen", systemImage: "plus") {
                    newPointName = ""
                    showAddPoint = true
                }
                .buttonStyle(.borderless)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(8)
            }
            .background(.bar)
        }
        .alert("Neuer Messpunkt", isPresented: $showAddPoint) {
            TextField("Name", text: $newPointName)
            Button("Hinzufügen") { addPoint() }
                .disabled(newPointName.normalizedPointName.isEmpty)
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text("Der Punkt wird auf der Etage „\(project.floorName(floor))“ angelegt und gilt für alle Phasen.")
        }
    }

    private func row(for point: MeasurePoint) -> some View {
        HStack(spacing: 8) {
            Image(systemName: point.isPlaced ? "mappin.circle.fill" : "mappin.slash")
                .foregroundStyle(.secondary)
                .frame(width: 18)
                .help(point.isPlaced ? "Position gesetzt" : "Position noch nicht gesetzt")
            VStack(alignment: .leading, spacing: 1) {
                Text(point.name)
                if let summary = measurementSummary(for: point) {
                    Text(summary)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
            }
        }
    }

    private func measurementSummary(for point: MeasurePoint) -> String? {
        let parts = project.phases.compactMap { phase -> String? in
            guard let measurement = project.measurement(at: point.name, in: phase.id) else { return nil }
            let value = measurement.rssiDBm.map { "\(formatNumber($0)) dBm" } ?? "gemessen"
            return "\(phase.name) \(value)"
        }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }

    @ViewBuilder
    private func menu(for point: MeasurePoint) -> some View {
        Picker("Etage", selection: Binding(
            get: { point.floor },
            set: { project.setFloor($0, forPointNamed: point.name) }
        )) {
            ForEach(project.floors) { Text($0.name).tag($0.id) }
        }
        Button("Position entfernen") { project.clearPosition(ofPointNamed: point.name) }
            .disabled(!point.isPlaced)
        Divider()
        // Punkte mit Messungen bleiben, damit keine Messdaten verwaisen.
        Button("Punkt löschen", role: .destructive) {
            if selection == point.name { selection = nil }
            project.removePoint(named: point.name)
        }
        .disabled(project.hasMeasurements(at: point.name))
    }

    private func addPoint() {
        let name = newPointName.normalizedPointName
        project.addPoint(named: name, floor: floor)
        // Gibt es den Namen schon, wird der vorhandene Punkt ausgewählt.
        selection = name
    }
}
