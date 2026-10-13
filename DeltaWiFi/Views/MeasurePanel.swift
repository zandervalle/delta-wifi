//
//  MeasurePanel.swift
//  DeltaWiFi
//
//  Geführte Messung an den festen Punkten: Phase wählen, Punkt für Punkt
//  messen, Ergebnis ansehen.
//

import CoreLocation
import SwiftUI

struct MeasurePanel: View {
    @Binding var project: Project
    @Binding var selection: String?
    /// ID der Phase, in die gemessen wird.
    @Binding var phase: String
    let runner: MeasurementRunner

    @State private var note = ""
    @State private var live: RadioSnapshot?
    @AppStorage("speedtestEnabled") private var speedtestEnabled = true
    @AppStorage("iperfEnabled") private var iperfEnabled = false
    @AppStorage("iperfServer") private var iperfServer = ""

    private var location: LocationAccess { .shared }
    private var point: MeasurePoint? { selection.flatMap(project.point(named:)) }
    private var iperfAvailable: Bool { NetworkInfo.iperfPath != nil }
    private var trimmedServer: String { iperfServer.trimmingCharacters(in: .whitespaces) }

    var body: some View {
        Form {
            Section("Phase") {
                Picker("Messen in", selection: $phase) {
                    ForEach(project.phases) { Text($0.name).tag($0.id) }
                }
                .disabled(runner.isRunning)
                let open = project.openPoints(in: phase).count
                Text(open == 0 ? "Alle \(project.points.count) Punkte gemessen." : "\(open) von \(project.points.count) Punkten noch offen.")
                    .foregroundStyle(.secondary)
            }

            Section("Messpunkt") {
                if let point {
                    LabeledContent(point.name, value: project.floorName(point.floor))
                    if !point.isPlaced {
                        Label("Position auf dem Plan noch nicht gesetzt. Messen geht trotzdem.", systemImage: "mappin.slash")
                            .foregroundStyle(.secondary)
                    }
                } else {
                    Text("Kein Punkt ausgewählt.")
                        .foregroundStyle(.secondary)
                }
                Button("Nächster offener Punkt", systemImage: "arrow.forward.circle") {
                    selection = project.nextOpenPoint(in: phase, after: selection)?.name
                }
                .disabled(project.nextOpenPoint(in: phase, after: selection) == nil)
            }

            Section("WLAN") {
                wifiStatus
            }

            Section("Messung") {
                Toggle("Internet-Speedtest", isOn: $speedtestEnabled)
                Toggle("Intern messen (iperf3)", isOn: $iperfEnabled)
                    .disabled(!iperfAvailable)
                if !iperfAvailable {
                    Text("iperf3 nicht gefunden. Installieren mit „brew install iperf3“.")
                        .foregroundStyle(.secondary)
                } else if iperfEnabled {
                    TextField("iperf3-Server", text: $iperfServer, prompt: Text("IP-Adresse, z. B. 192.168.178.20"))
                }
                TextField("Notiz", text: $note, prompt: Text("optional"), axis: .vertical)
                    .lineLimit(1...3)
                runControls
            }

            if let point, let measurement = project.measurement(at: point.name, in: phase) {
                Section("\(project.phaseName(phase)): gültige Messung") {
                    MeasurementDetails(measurement: measurement, project: project)
                }
            }
        }
        .formStyle(.grouped)
        .task {
            // Laufende Anzeige der Funkwerte, solange das Panel offen ist.
            while !Task.isCancelled {
                live = WiFi.snapshot()
                try? await Task.sleep(for: .seconds(2))
            }
        }
    }

    // MARK: - Abschnitte

    @ViewBuilder
    private var wifiStatus: some View {
        if let live = runner.isRunning ? (runner.samples.last ?? live) : live {
            LabeledContent("Netz", value: live.ssid ?? "–")
            LabeledContent("Knoten (BSSID)", value: live.bssid ?? "–")
            LabeledContent("Signal", value: "\(live.rssi) dBm" + (live.snr.map { " · SNR \($0) dB" } ?? ""))
            LabeledContent("Kanal", value: [live.band.map { "\($0) GHz" }, live.channel.map { "Kanal \($0)" },
                                           live.width.map { "\($0) MHz" }].compactMap { $0 }.joined(separator: " · "))
        } else {
            Label("Nicht mit einem WLAN verbunden.", systemImage: "wifi.slash")
        }
        if !location.isAuthorized {
            VStack(alignment: .leading, spacing: 6) {
                Label("Ohne Ortungsfreigabe liefert macOS keine BSSID. Die Messung läuft, aber es bleibt offen, mit welchem Knoten der Mac verbunden war.",
                      systemImage: "exclamationmark.triangle")
                if location.status == .notDetermined {
                    Button("Freigabe anfragen") { location.request() }
                } else if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_LocationServices") {
                    Link("Ortungsdienste in den Systemeinstellungen öffnen", destination: url)
                }
            }
        }
    }

    @ViewBuilder
    private var runControls: some View {
        if let stage = runner.stage {
            VStack(alignment: .leading, spacing: 6) {
                if let target = runner.target {
                    Text("Messe „\(target.point)“ (\(project.phaseName(target.phase)))")
                        .fontWeight(.medium)
                }
                if let duration = stage.duration {
                    ProgressView(timerInterval: stage.start...stage.start.addingTimeInterval(duration), countsDown: false) {
                        Text(stage.title)
                    }
                } else {
                    ProgressView { Text(stage.title) }
                        .progressViewStyle(.linear)
                }
                Text("Bitte am Messpunkt stehen bleiben.")
                    .foregroundStyle(.secondary)
                Button("Abbrechen", role: .cancel) { runner.cancel() }
            }
        } else {
            let repeats = point.map { project.measurement(at: $0.name, in: phase) != nil } ?? false
            Button(repeats ? "Messung wiederholen" : "Messung starten", systemImage: "play.fill", action: start)
                .buttonStyle(.borderedProminent)
                .disabled(point == nil || live == nil || (iperfAvailable && iperfEnabled && trimmedServer.isEmpty))
            if iperfAvailable && iperfEnabled && trimmedServer.isEmpty {
                Text("Für die interne Messung fehlt die Adresse des iperf3-Servers.")
                    .foregroundStyle(.secondary)
            }
            ForEach(runner.messages, id: \.self) { message in
                Label(message, systemImage: "info.circle")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private func start() {
        guard let point else { return }
        let target = MeasurementRunner.Target(point: point.name, phase: phase)
        let options = MeasurementRunner.Options(speedtest: speedtestEnabled,
                                                iperfServer: iperfAvailable && iperfEnabled ? trimmedServer : nil)
        runner.start(target: target, options: options, note: note) { measurement in
            project.record(measurement, at: target.point, phase: target.phase)
            note = ""
        }
    }
}

/// Alle Werte einer Messung als Liste; die sieben bewerteten Messgrößen mit Statuspunkt.
struct MeasurementDetails: View {
    let measurement: Measurement
    /// Liefert die Grenzwerte und die Anschlussrate für Prozentangaben.
    let project: Project

    var body: some View {
        if let zeit = measurement.zeit {
            row("Zeit", zeit + (measurement.wiederholung.map { " · Wiederholung \($0)" } ?? ""))
        }
        metricRow(.rssi)
        row("Rauschen", number(measurement.noiseDBm, "dBm"))
        metricRow(.snr)
        metricRow(.linkRate)
        row("Kanal", [measurement.bandGHz.map { "\($0) GHz" }, measurement.kanal.map { "Kanal \($0)" },
                      measurement.breiteMHz.map { "\($0) MHz" }, measurement.phy].compactMap { $0 }.joined(separator: " · "))
        row("Knoten (BSSID)", measurement.bssid)
        metricRow(.gatewayLoss)
        row("Ping zur Box (Mittel)", number(measurement.pingGwAvgMs, "ms"))
        metricRow(.gatewayMaxPing)
        row("Paketverlust Internet", number(measurement.pingInetVerlustPct, "%"))
        row("Max. Ping Internet", number(measurement.pingInetMaxMs, "ms"))
        metricRow(.internetDown)
        row("Internet Upload", number(measurement.inetUpMbit, "Mbit/s").map {
            $0 + (MetricScale.shareText(measurement.inetUpMbit, of: project.lineUpMbit) ?? "")
        })
        metricRow(.lanDown)
        row("Intern Upload (iperf3)", number(measurement.lanUpMbit, "Mbit/s"))
        row("Notiz", measurement.notiz)
    }

    private func number(_ value: Double?, _ unit: String) -> String? {
        value.map { "\($0.formatted(.number.precision(.fractionLength(0...1)).locale(Locale(identifier: "de_DE")))) \(unit)" }
    }

    @ViewBuilder
    private func row(_ title: String, _ value: String?) -> some View {
        if let value, !value.isEmpty {
            LabeledContent(title, value: value)
        }
    }

    @ViewBuilder
    private func metricRow(_ metric: Metric) -> some View {
        if let value = metric.value(in: measurement) {
            let scale = project.scale(for: metric)
            LabeledContent(metric.title) {
                HStack(spacing: 6) {
                    Text(scale.describe(value))
                    Circle()
                        .fill(Color(HeatColor.scale(scale.score(of: value))))
                        .frame(width: 9, height: 9)
                        .accessibilityLabel(ratingText(scale.rating(of: value)))
                }
            }
        }
    }

    private func ratingText(_ rating: Rating) -> String {
        switch rating {
        case .good: "gut"
        case .okay: "okay"
        case .critical: "kritisch"
        }
    }
}
