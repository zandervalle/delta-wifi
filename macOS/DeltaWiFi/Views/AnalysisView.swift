//
//  AnalysisView.swift
//  DeltaWiFi
//
//  Auswertung: Übersicht je Phase, Tabelle aller Messpunkte und Detailansicht
//  des ausgewählten Punkts mit allen Werten je Phase.
//

import SwiftUI

struct AnalysisView: View {
    let project: Project
    let metric: Metric
    /// IDs der beiden verglichenen Phasen.
    let compareFrom: String
    let compareTo: String
    @Binding var selection: String?

    /// Messgröße mit den Grenzwerten des Projekts.
    private var scale: MetricScale { project.scale(for: metric) }

    var body: some View {
        VStack(spacing: 0) {
            ScrollView(.horizontal) {
                HStack(alignment: .top, spacing: 12) {
                    ForEach(project.phases) { phase in
                        phaseCard(phase)
                    }
                    changeCard
                }
                .padding(12)
            }
            .scrollIndicators(.never)
            Divider()
            table
            Divider()
            detail
                .frame(height: 250)
        }
    }

    // MARK: - Übersicht

    private func phaseCard(_ phase: Phase) -> some View {
        let summary = project.phaseSummary(metric: metric, phase: phase.id)
        return card(title: phase.name) {
            if let median = summary.median {
                HStack(spacing: 6) {
                    Text("Median \(scale.describe(median))")
                        .font(.title3.weight(.semibold))
                    StatusDot(color: Color(HeatColor.scale(scale.score(of: median))))
                }
                HStack(spacing: 10) {
                    count(summary.good, "gut", .good)
                    count(summary.okay, "okay", .okay)
                    count(summary.critical, "kritisch", .critical)
                }
                Text("\(summary.count) von \(project.points.count) Punkten gemessen")
                    .foregroundStyle(.secondary)
                // Liegt schon der Median am Anschlussmaximum, unterscheidet der Speedtest gute Punkte nicht mehr.
                if let line = scale.reference, median >= 0.9 * line {
                    Text("Nahe am Maximum des Anschlusses. Der interne Durchsatz sagt hier mehr über das WLAN.")
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: 230, alignment: .leading)
                        .fixedSize(horizontal: false, vertical: true)
                }
            } else {
                Text("Keine Werte")
                    .font(.title3)
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var changeCard: some View {
        let summary = project.changeSummary(metric: metric, from: compareFrom, to: compareTo)
        return card(title: "\(project.phaseName(compareFrom)) → \(project.phaseName(compareTo))") {
            if summary.compared > 0 {
                HStack(spacing: 10) {
                    count(summary.better, "besser", .good)
                    count(summary.worse, "schlechter", .critical)
                }
                .font(.title3.weight(.semibold))
                Text("\(summary.unchanged) unverändert")
                Text("\(summary.compared) Punkte in beiden Phasen gemessen")
                    .foregroundStyle(.secondary)
            } else {
                Text("Kein Punkt in beiden Phasen gemessen")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private func card(title: String, @ViewBuilder content: () -> some View) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title)
                .font(.headline)
            content()
        }
        .font(.callout)
        .padding(10)
        .frame(minWidth: 190, alignment: .leading)
        .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 8))
        .accessibilityElement(children: .combine)
    }

    private func count(_ number: Int, _ label: String, _ color: HeatColor) -> some View {
        HStack(spacing: 4) {
            StatusDot(color: Color(color))
            Text("\(number) \(label)")
        }
    }

    // MARK: - Tabelle

    private struct Row: Identifiable {
        let id: String
        let floorName: String
        /// Wert der Messgröße je Phasen-ID.
        let values: [String: Double]
        let before: Double?
        let after: Double?
    }

    private var rows: [Row] {
        project.points.map { point in
            var values: [String: Double] = [:]
            for phase in project.phases {
                values[phase.id] = project.value(of: metric, at: point.name, in: phase.id)
            }
            return Row(id: point.name, floorName: project.floorName(point.floor), values: values,
                       before: values[compareFrom], after: values[compareTo])
        }
    }

    private var table: some View {
        Table(rows, selection: $selection) {
            TableColumn("Messpunkt") { Text($0.id) }
                .width(min: 150, ideal: 220)
            TableColumn("Etage") { Text($0.floorName) }
                .width(min: 80, ideal: 110)
            TableColumnForEach(project.phases) { phase in
                TableColumn(phase.name) { row in
                    valueCell(row.values[phase.id])
                }
                .width(min: 90, ideal: 110)
            }
            TableColumn("Veränderung") { row in
                changeCell(row)
            }
            .width(min: 110, ideal: 130)
        }
    }

    @ViewBuilder
    private func valueCell(_ value: Double?) -> some View {
        if let value {
            HStack(spacing: 6) {
                StatusDot(color: Color(HeatColor.scale(scale.score(of: value))))
                Text(scale.describe(value))
                    .monospacedDigit()
            }
        } else {
            Text("–")
                .foregroundStyle(.secondary)
        }
    }

    @ViewBuilder
    private func changeCell(_ row: Row) -> some View {
        if let before = row.before, let after = row.after {
            let trend = scale.trend(from: before, to: after)
            HStack(spacing: 6) {
                StatusDot(color: Color(trend == .better ? HeatColor.good : trend == .worse ? .critical : .unchanged))
                Text(metric.formatDifference(after - before))
                    .monospacedDigit()
                Text(trend == .better ? "besser" : trend == .worse ? "schlechter" : "unverändert")
                    .foregroundStyle(.secondary)
            }
        } else {
            Text("–")
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Detail

    @ViewBuilder
    private var detail: some View {
        if let point = selection.flatMap(project.point(named:)) {
            ScrollView([.horizontal, .vertical]) {
                PointDetailGrid(project: project, point: point)
                    .padding(12)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
        } else {
            Text("Punkt in der Tabelle auswählen, um alle Werte je Phase und die Notizen zu sehen.")
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

/// Kleiner Statuspunkt in einer der Signalfarben.
struct StatusDot: View {
    let color: Color

    var body: some View {
        Circle()
            .fill(color)
            .frame(width: 9, height: 9)
            .accessibilityHidden(true)
    }
}

/// Alle Werte eines Punkts, die Phasen nebeneinander.
struct PointDetailGrid: View {
    let project: Project
    let point: MeasurePoint

    private struct Field: Identifiable {
        let id: String
        var metric: Metric?
        let text: (Measurement) -> String?

        init(_ title: String, _ text: @escaping (Measurement) -> String?) {
            id = title
            self.text = text
        }

        init(_ metric: Metric) {
            id = metric.title
            self.metric = metric
            text = { measurement in metric.value(in: measurement).map { metric.format($0) } }
        }
    }

    private static func number(_ value: Double?, _ unit: String) -> String? {
        value.map { "\($0.formatted(.number.precision(.fractionLength(0...1)).locale(Locale(identifier: "de_DE")))) \(unit)" }
    }

    private static let uploadTitle = "Internet Upload"

    private static let fields: [Field] = [
        Field("Zeit") { $0.zeit },
        Field("Wiederholung") { $0.wiederholung.map(String.init) },
        Field(.rssi),
        Field("Rauschen") { number($0.noiseDBm, "dBm") },
        Field(.snr),
        Field(.linkRate),
        Field("Band") { $0.bandGHz.map { "\($0) GHz" } },
        Field("Kanal") { m in m.kanal.map { "\($0)" + (m.breiteMHz.map { " (\($0) MHz)" } ?? "") } },
        Field("PHY") { $0.phy },
        Field("Netz") { $0.ssid },
        Field("Knoten (BSSID)") { $0.bssid },
        Field(.gatewayLoss),
        Field("Ping zur Box (Mittel)") { number($0.pingGwAvgMs, "ms") },
        Field(.gatewayMaxPing),
        Field("Paketverlust Internet") { number($0.pingInetVerlustPct, "%") },
        Field("Ping Internet (Mittel)") { number($0.pingInetAvgMs, "ms") },
        Field("Max. Ping Internet") { number($0.pingInetMaxMs, "ms") },
        Field(.internetDown),
        Field(uploadTitle) { number($0.inetUpMbit, "Mbit/s") },
        Field(.lanDown),
        Field("Intern Upload (iperf3)") { number($0.lanUpMbit, "Mbit/s") },
        Field("Notiz") { $0.notiz },
    ]

    var body: some View {
        let measurements = project.phases.map { project.measurement(at: point.name, in: $0.id) }
        VStack(alignment: .leading, spacing: 8) {
            Text("\(point.name) · \(project.floorName(point.floor))")
                .font(.headline)
            if measurements.allSatisfy({ $0 == nil }) {
                Text("An diesem Punkt wurde noch nicht gemessen.")
                    .foregroundStyle(.secondary)
            } else {
                Grid(alignment: .topLeading, horizontalSpacing: 20, verticalSpacing: 4) {
                    GridRow {
                        Text("")
                        ForEach(project.phases) { Text($0.name).fontWeight(.semibold) }
                    }
                    Divider()
                    // Zeilen, die in keiner Phase einen Wert haben, entfallen.
                    ForEach(Self.fields.filter { field in measurements.contains { $0.flatMap(field.text) != nil } }) { field in
                        GridRow {
                            Text(field.id)
                                .foregroundStyle(.secondary)
                            ForEach(Array(measurements.enumerated()), id: \.offset) { _, measurement in
                                cell(field, measurement)
                            }
                        }
                    }
                }
                .font(.callout)
                .textSelection(.enabled)
            }
        }
    }

    /// Anteil an der Anschlussrate für die Internet-Werte, sofern sie eingetragen ist.
    private func share(_ field: Field, _ measurement: Measurement) -> String? {
        if field.metric == .internetDown { return MetricScale.shareText(measurement.inetDownMbit, of: project.lineDownMbit) }
        if field.id == Self.uploadTitle { return MetricScale.shareText(measurement.inetUpMbit, of: project.lineUpMbit) }
        return nil
    }

    @ViewBuilder
    private func cell(_ field: Field, _ measurement: Measurement?) -> some View {
        if let measurement, let text = field.text(measurement) {
            HStack(spacing: 6) {
                if let metric = field.metric, let value = metric.value(in: measurement) {
                    StatusDot(color: Color(HeatColor.scale(project.scale(for: metric).score(of: value))))
                }
                Text(text + (share(field, measurement) ?? ""))
                    .monospacedDigit()
                    .frame(maxWidth: 260, alignment: .leading)
            }
        } else {
            Text("–")
                .foregroundStyle(.secondary)
        }
    }
}
