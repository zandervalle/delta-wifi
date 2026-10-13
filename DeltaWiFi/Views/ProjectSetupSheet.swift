//
//  ProjectSetupSheet.swift
//  DeltaWiFi
//
//  Einrichtung eines neuen Projekts: fragt nach den Etagen bzw. Bereichen
//  und den Messpunkten darin.
//

import SwiftUI

struct ProjectSetupSheet: View {
    @Binding var project: Project
    @Environment(\.dismiss) private var dismiss

    @State private var areas = [Area()]
    @FocusState private var focus: UUID?

    private struct Area: Identifiable {
        let id = UUID()
        var name = ""
        var points = [Point()]
    }

    private struct Point: Identifiable {
        let id = UUID()
        var name = ""
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Projekt einrichten")
                    .font(.title2.weight(.semibold))
                Text("Welche Etagen oder Bereiche sollen ausgemessen werden, und an welchen Stellen darin? Jeder Bereich bekommt später seinen eigenen Grundriss. Alles lässt sich nachträglich ändern und ergänzen.")
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding()
            Divider()
            Form {
                ForEach($areas) { $area in
                    Section {
                        TextField("Name", text: $area.name, prompt: Text("z. B. Erdgeschoss"))
                            .focused($focus, equals: area.id)
                            .onSubmit { focus = area.points.first?.id }
                        ForEach($area.points) { $point in
                            HStack {
                                Image(systemName: "mappin")
                                    .foregroundStyle(.secondary)
                                TextField("Messpunkt", text: $point.name, prompt: Text("Messpunkt, z. B. Wohnzimmer Sofa"))
                                    .labelsHidden()
                                    .focused($focus, equals: point.id)
                                    // Eingabetaste springt zum nächsten Punkt bzw. legt einen weiteren an.
                                    .onSubmit { advance(from: point.id, in: area.id) }
                                Button("Messpunkt entfernen", systemImage: "minus.circle") {
                                    area.points.removeAll { $0.id == point.id }
                                }
                                .labelStyle(.iconOnly)
                                .buttonStyle(.borderless)
                                .disabled(area.points.count <= 1)
                            }
                        }
                        Button("Messpunkt hinzufügen", systemImage: "plus") { addPoint(to: area.id) }
                            .buttonStyle(.borderless)
                    } header: {
                        HStack {
                            Text("Etage / Bereich")
                            Spacer()
                            if areas.count > 1 {
                                Button("Bereich entfernen") { areas.removeAll { $0.id == area.id } }
                                    .buttonStyle(.borderless)
                                    .font(.callout)
                            }
                        }
                    }
                }
                Section {
                    Button("Weiteren Bereich hinzufügen", systemImage: "plus") {
                        let area = Area()
                        areas.append(area)
                        focus = area.id
                    }
                    .buttonStyle(.borderless)
                }
                LineRateSection(project: $project)
            }
            .formStyle(.grouped)
            Divider()
            HStack {
                if let problem {
                    Label(problem, systemImage: "exclamationmark.triangle")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Button("Später") { dismiss() }
                Button("Projekt anlegen") {
                    // In einem Schritt übernehmen, damit Widerrufen alles zusammen zurücknimmt.
                    var updated = project
                    updated.addAreas(areas.map { area in
                        Project.SetupArea(name: area.name, points: area.points.map(\.name))
                    })
                    project = updated
                    dismiss()
                }
                .buttonStyle(.borderedProminent)
                .disabled(problem != nil)
            }
            .padding()
        }
        .frame(width: 520, height: 620)
        .onAppear { focus = areas.first?.id }
    }

    // MARK: - Eingabe

    private func addPoint(to areaID: UUID) {
        guard let index = areas.firstIndex(where: { $0.id == areaID }) else { return }
        let point = Point()
        areas[index].points.append(point)
        focus = point.id
    }

    private func advance(from pointID: UUID, in areaID: UUID) {
        guard let area = areas.first(where: { $0.id == areaID }),
              let index = area.points.firstIndex(where: { $0.id == pointID }) else { return }
        if index + 1 < area.points.count {
            focus = area.points[index + 1].id
        } else if !area.points[index].name.normalizedPointName.isEmpty {
            addPoint(to: areaID)
        }
    }

    /// Was dem Anlegen noch im Weg steht; `nil`, wenn alles passt.
    private var problem: String? {
        let named = areas.filter { !$0.name.trimmingCharacters(in: .whitespaces).isEmpty }
        if named.isEmpty { return "Mindestens ein Bereich braucht einen Namen." }
        if areas.contains(where: { area in
            area.name.trimmingCharacters(in: .whitespaces).isEmpty && area.points.contains { !$0.name.normalizedPointName.isEmpty }
        }) {
            return "Ein Bereich mit Messpunkten hat noch keinen Namen."
        }
        let areaNames = named.map { $0.name.trimmingCharacters(in: .whitespaces) }
        if Set(areaNames).count != areaNames.count { return "Bereichsnamen müssen eindeutig sein." }

        var seen = Set(project.points.map(\.name))
        for name in areas.flatMap(\.points).map(\.name.normalizedPointName) where !name.isEmpty {
            if !seen.insert(name).inserted { return "Der Messpunkt „\(name)“ kommt mehrfach vor. Namen müssen eindeutig sein." }
        }
        return nil
    }
}
