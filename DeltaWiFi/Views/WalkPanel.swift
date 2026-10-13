//
//  WalkPanel.swift
//  DeltaWiFi
//
//  Lauf-Modus: Lauf starten, Positionen anklicken, beenden; dazu die Liste
//  der gespeicherten Läufe.
//

import SwiftUI

struct WalkPanel: View {
    @Binding var project: Project
    /// ID der Etage, die gerade angezeigt wird; dort findet ein neuer Lauf statt.
    let floor: String
    /// ID der Phase, in die gelaufen wird.
    @Binding var phase: String
    let recorder: WalkRecorder
    @Binding var showPaths: Bool

    @State private var live: RadioSnapshot?
    @State private var message: String?
    @State private var walkToDelete: Walk?

    var body: some View {
        Form {
            if let walk = recorder.walk {
                activeSection(walk)
            } else {
                startSection
            }
            listSection
        }
        .formStyle(.grouped)
        .task {
            while !Task.isCancelled {
                if !recorder.isActive { live = WiFi.snapshot() }
                try? await Task.sleep(for: .seconds(2))
            }
        }
        .confirmationDialog("Lauf löschen?", isPresented: Binding(get: { walkToDelete != nil }, set: { if !$0 { walkToDelete = nil } }),
                            presenting: walkToDelete) { walk in
            Button("Löschen", role: .destructive) { project.removeWalk(id: walk.id) }
            Button("Abbrechen", role: .cancel) {}
        } message: { walk in
            Text("\(title(of: walk)) mit \(project.survey[walk.id]?.count ?? 0) Samples wird entfernt.")
        }
    }

    // MARK: - Abschnitte

    private var startSection: some View {
        Section("Neuer Lauf") {
            Picker("Phase", selection: $phase) {
                ForEach(project.phases) { Text($0.name).tag($0.id) }
            }
            LabeledContent("Etage", value: project.floorName(floor))
            Text("Nach dem Start mit dem MacBook durchs Haus gehen und alle paar Meter die eigene Position auf dem Plan anklicken. Die Funkwerte werden jede Sekunde aufgezeichnet.")
                .foregroundStyle(.secondary)
            if let live {
                LabeledContent("Signal", value: signalText(live))
            } else {
                Label("Nicht mit einem WLAN verbunden.", systemImage: "wifi.slash")
            }
            Button("Lauf starten", systemImage: "figure.walk") {
                message = nil
                recorder.start(phase: phase, floor: floor)
            }
            .buttonStyle(.borderedProminent)
            .disabled(live == nil)
            if let message {
                Label(message, systemImage: "info.circle")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private func activeSection(_ walk: Walk) -> some View {
        Section("Lauf läuft: \(project.phaseName(walk.phase)), \(project.floorName(walk.floor))") {
            LabeledContent("Dauer") {
                Text(date(walk.started), style: .timer)
                    .monospacedDigit()
            }
            LabeledContent("Samples", value: "\(recorder.samples.count)")
            LabeledContent("Positionen", value: "\(walk.taps.count)")
            if let snapshot = recorder.lastSnapshot {
                LabeledContent("Signal", value: signalText(snapshot))
            } else {
                Label("WLAN-Verbindung weg, es wird nichts aufgezeichnet.", systemImage: "wifi.slash")
            }
            Text(walk.taps.isEmpty ? "Jetzt die Startposition auf dem Plan anklicken."
                                   : "Weitergehen und alle paar Meter die aktuelle Position anklicken. Gezählt werden nur Samples zwischen erster und letzter Position.")
                .foregroundStyle(.secondary)
            Button("Letzte Position löschen", systemImage: "arrow.uturn.backward") {
                recorder.removeLastTap()
                recorder.commit(into: &project)
            }
            .disabled(walk.taps.isEmpty)
            Button("Lauf beenden", systemImage: "stop.fill") {
                message = recorder.finish(into: &project)
            }
            .buttonStyle(.borderedProminent)
            Button("Lauf verwerfen", role: .destructive) {
                recorder.discard(from: &project)
                message = "Lauf verworfen."
            }
        }
    }

    private var listSection: some View {
        Section("Gespeicherte Läufe") {
            Toggle("Laufwege auf dem Plan zeigen", isOn: $showPaths)
            let walks = project.walks.filter { $0.id != recorder.walk?.id }
            if walks.isEmpty {
                Text("Noch keine Läufe.")
                    .foregroundStyle(.secondary)
            }
            ForEach(walks) { walk in
                HStack {
                    VStack(alignment: .leading, spacing: 1) {
                        Text(title(of: walk))
                        Text(details(of: walk))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("Lauf löschen", systemImage: "trash") { walkToDelete = walk }
                        .labelStyle(.iconOnly)
                        .buttonStyle(.borderless)
                        .disabled(recorder.isActive)
                }
            }
        }
    }

    // MARK: - Hilfen

    private func details(of walk: Walk) -> String {
        let samples = project.survey[walk.id] ?? []
        var parts = [date(walk.started).formatted(date: .numeric, time: .shortened),
                     "\(walk.taps.count) Positionen", "\(samples.count) Samples"]
        // Anzahl verschiedener WLAN-Knoten, sofern die BSSID aufgezeichnet wurde.
        let nodes = Set(samples.compactMap(\.bssid)).count
        if nodes > 0 { parts.append(nodes == 1 ? "1 Knoten" : "\(nodes) Knoten") }
        return parts.joined(separator: " · ")
    }

    private func title(of walk: Walk) -> String { "\(project.phaseName(walk.phase)) · \(project.floorName(walk.floor))" }

    private func date(_ milliseconds: Int64) -> Date { Date(timeIntervalSince1970: Double(milliseconds) / 1000) }

    private func signalText(_ snapshot: RadioSnapshot) -> String {
        ["\(snapshot.rssi) dBm", snapshot.snr.map { "SNR \($0) dB" }, snapshot.band.map { "\($0) GHz" }]
            .compactMap { $0 }.joined(separator: " · ")
    }
}
