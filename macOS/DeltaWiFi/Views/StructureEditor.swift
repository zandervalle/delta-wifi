//
//  StructureEditor.swift
//  DeltaWiFi
//
//  Projekt verwalten: Etagen, Phasen und die Rate des Internetanschlusses.
//

import SwiftUI

struct StructureEditor: View {
    @Binding var project: Project
    @Environment(\.dismiss) private var dismiss

    @State private var pendingRemoval: Removal?

    private enum Removal {
        case floor(Floor, Project.FloorUsage)
        case phase(Phase, Project.PhaseUsage)

        var name: String {
            switch self {
            case .floor(let floor, _): floor.name
            case .phase(let phase, _): phase.name
            }
        }
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    ForEach(Array(project.floors.enumerated()), id: \.element.id) { index, floor in
                        row(name: Binding(get: { floor.name }, set: { project.renameFloor(id: floor.id, to: $0) }),
                            index: index, count: project.floors.count,
                            move: { project.moveFloor(id: floor.id, by: $0) },
                            remove: { requestRemoval(of: floor) })
                    }
                    Button("Etage hinzufügen", systemImage: "plus") { project.addFloor(named: "Neue Etage") }
                } header: {
                    Text("Etagen")
                } footer: {
                    Text("Jede Etage hat ihren eigenen Grundriss, ihre Messpunkte und Läufe.")
                }

                Section {
                    ForEach(Array(project.phases.enumerated()), id: \.element.id) { index, phase in
                        row(name: Binding(get: { phase.name }, set: { project.renamePhase(id: phase.id, to: $0) }),
                            index: index, count: project.phases.count,
                            move: { project.movePhase(id: phase.id, by: $0) },
                            remove: { requestRemoval(of: phase) })
                    }
                    Button("Phase hinzufügen", systemImage: "plus") { project.addPhase(named: "Neue Phase") }
                } header: {
                    Text("Phasen")
                } footer: {
                    Text("Jede Phase ist ein eigener Messdurchgang, etwa vor und nach einem Umbau. Verglichen werden können zwei beliebige Phasen.")
                }

                LineRateSection(project: $project)
            }
            .formStyle(.grouped)
            Divider()
            HStack {
                Text("Änderungen lassen sich widerrufen (⌘Z).")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Fertig") { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding()
        }
        .frame(width: 480, height: 680)
        .confirmationDialog("„\(pendingRemoval?.name ?? "")“ löschen?",
                            isPresented: Binding(get: { pendingRemoval != nil }, set: { if !$0 { pendingRemoval = nil } }),
                            presenting: pendingRemoval) { removal in
            Button("Löschen", role: .destructive) { perform(removal) }
            Button("Abbrechen", role: .cancel) {}
        } message: { removal in
            Text(warning(for: removal))
        }
    }

    private func row(name: Binding<String>, index: Int, count: Int,
                     move: @escaping (Int) -> Void, remove: @escaping () -> Void) -> some View {
        HStack(spacing: 6) {
            TextField("Name", text: name)
                .labelsHidden()
            Group {
                Button("Nach oben", systemImage: "chevron.up") { move(-1) }
                    .disabled(index == 0)
                Button("Nach unten", systemImage: "chevron.down") { move(1) }
                    .disabled(index == count - 1)
                // Mindestens eine Etage und eine Phase müssen bleiben.
                Button("Löschen", systemImage: "trash") { remove() }
                    .disabled(count <= 1)
            }
            .labelStyle(.iconOnly)
            .buttonStyle(.borderless)
        }
    }

    // MARK: - Löschen

    private func requestRemoval(of floor: Floor) {
        let usage = project.floorUsage(id: floor.id)
        // Leere Einträge verschwinden ohne Rückfrage.
        if usage.isEmpty { project.removeFloor(id: floor.id) } else { pendingRemoval = .floor(floor, usage) }
    }

    private func requestRemoval(of phase: Phase) {
        let usage = project.phaseUsage(id: phase.id)
        if usage.isEmpty { project.removePhase(id: phase.id) } else { pendingRemoval = .phase(phase, usage) }
    }

    private func perform(_ removal: Removal) {
        switch removal {
        case .floor(let floor, _): project.removeFloor(id: floor.id)
        case .phase(let phase, _): project.removePhase(id: phase.id)
        }
    }

    private func warning(for removal: Removal) -> String {
        var parts: [String] = []
        switch removal {
        case .floor(_, let usage):
            if usage.hasPlan { parts.append("der Grundriss") }
            if usage.points > 0 {
                let measured = usage.measuredPoints > 0 ? " (\(usage.measuredPoints) davon mit Messungen in allen Phasen)" : ""
                parts.append("\(count(usage.points, "Messpunkt", "Messpunkte"))\(measured)")
            }
            if usage.walks > 0 { parts.append(count(usage.walks, "Lauf", "Läufe")) }
        case .phase(_, let usage):
            if usage.measuredPoints > 0 { parts.append("die Messungen an \(count(usage.measuredPoints, "Punkt", "Punkten"))") }
            if usage.walks > 0 { parts.append(count(usage.walks, "Lauf", "Läufe")) }
        }
        return "Mit entfernt werden: \(parts.joined(separator: ", "))."
    }

    private func count(_ number: Int, _ singular: String, _ plural: String) -> String {
        "\(number) \(number == 1 ? singular : plural)"
    }
}

/// Optionale Eingabe der Anschlussrate; daran wird der Internet-Speedtest gemessen.
struct LineRateSection: View {
    @Binding var project: Project

    var body: some View {
        Section {
            field("Download", value: $project.lineDownMbit)
            field("Upload", value: $project.lineUpMbit)
        } header: {
            Text("Internetanschluss (optional)")
        } footer: {
            Text("Synchronisierte Rate in Mbit/s, zu finden in der Oberfläche des Routers. Ist der Download eingetragen, gilt ein Speedtest ab 80 % davon als gut und ab 50 % als okay, und die Werte erscheinen zusätzlich als Anteil am Anschluss. Ohne Eingabe gelten feste Grenzen (120 und 60 Mbit/s).")
        }
    }

    private func field(_ title: String, value: Binding<Double?>) -> some View {
        // Null und negative Eingaben gelten als „nicht eingetragen“.
        let sanitized = Binding<Double?>(get: { value.wrappedValue },
                                         set: { value.wrappedValue = $0.flatMap { $0 > 0 ? $0 : nil } })
        return TextField(title, value: sanitized, format: .number.precision(.fractionLength(0...1)), prompt: Text("Mbit/s"))
    }
}
