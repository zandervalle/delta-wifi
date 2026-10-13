//
//  ContentView.swift
//  DeltaWiFi
//
//  Created by Alexander on 11.10.26.
//

import SwiftUI
import UniformTypeIdentifiers

struct ContentView: View {
    @Binding var document: DeltaWiFiDocument

    // Gewählte Etage und Phasen als IDs. Verschwindet eine davon aus dem Projekt, greifen die
    // berechneten Eigenschaften unten auf einen vorhandenen Eintrag zurück.
    @State private var floorSelection = "eg"
    @State private var phaseSelection = "VORHER"
    @State private var isComparison = false
    @State private var compareFromSelection: String?
    @State private var compareToSelection: String?
    @State private var showStructureEditor = false
    @State private var showSetup = false
    @State private var display = Display.map

    private enum Display {
        case map, analysis
    }
    @State private var selection: String?
    @State private var metric = Metric.rssi
    @State private var heatmapImage: CGImage?
    @State private var showMeasurePanel = false
    @State private var runner = MeasurementRunner()
    @State private var recorder = WalkRecorder()
    @State private var panelTab = PanelTab.points
    @AppStorage("showWalkPaths") private var showWalkPaths = true

    private enum PanelTab {
        case points, walk
    }
    /// Dekodierte Grundrisse je Etage, damit das Base64 nicht bei jedem Neuzeichnen gelesen wird.
    @State private var planImages: [String: CGImage] = [:]
    // Der Öffnen-Dialog hängt an der Hauptansicht: An Toolbar-Elementen erschien er verzögert und mehrfach.
    @State private var showPlanImporter = false
    @State private var notice: Notice?

    private struct Notice {
        var title: String
        var messages: [String]
    }

    private var project: Project { document.project }
    private var floor: String { project.resolvedFloorID(floorSelection) }
    /// Angezeigte Phase; in sie wird auch gemessen und gelaufen.
    private var phase: String { project.resolvedPhaseID(phaseSelection) }
    /// Vergleich: von der ersten zur letzten Phase, solange nichts anderes gewählt ist.
    private var compareFrom: String { project.resolvedPhaseID(compareFromSelection) }
    private var compareTo: String { project.resolvedPhaseID(compareToSelection, fallback: project.phases.last?.id) }
    private var plan: Plan? { project.plans[floor] }
    private var floorPoints: [MeasurePoint] { project.points.filter { $0.floor == floor } }

    /// Ausgewählter Punkt dieser Etage ohne Position: Der nächste Klick auf den Plan setzt ihn.
    private var pendingPoint: MeasurePoint? {
        floorPoints.first { $0.name == selection && !$0.isPlaced }
    }

    var body: some View {
        NavigationSplitView {
            PointSidebar(project: $document.project, floor: floor, selection: $selection)
                .disabled(recorder.isActive || project.floors.isEmpty)
                .navigationSplitViewColumnWidth(min: 220, ideal: 270)
        } detail: {
            // Das Messpanel ist bewusst eine einfache Spalte: Als `.inspector` geriet das Fenster-Layout
            // in eine Schleife (Absturz beim Verlassen des Vollbilds).
            HStack(spacing: 0) {
                VStack(spacing: 0) {
                    controls
                    Divider()
                    if display == .analysis {
                        AnalysisView(project: project, metric: metric, compareFrom: compareFrom, compareTo: compareTo,
                                     selection: $selection)
                    } else {
                        PlanView(floorName: project.floorName(floor),
                                 image: planImages[floor],
                                 heatmap: heatmapImage,
                                 aspectRatio: PlanImage.aspectRatio(of: plan),
                                 points: floorPoints,
                                 markers: markers,
                                 walkPaths: walkPaths,
                                 isWalking: recorder.isActive,
                                 pendingPointName: pendingPoint?.name,
                                 selection: $selection,
                                 onSetPosition: setPosition,
                                 onWalkTap: addWalkTap)
                        Divider()
                        footer
                    }
                }
                .dropDestination(for: URL.self) { urls, _ in
                    guard display == .map, let url = urls.first else { return false }
                    return loadPlan(from: url)
                }
                .frame(minWidth: 520, minHeight: 520)
                if showMeasurePanel {
                    Divider()
                    sidePanel
                        .frame(width: 340)
                }
            }
        }
        .overlay {
            // Ohne Bereiche gibt es nichts zu zeigen: Die Einrichtung verdeckt Karte und Auswertung.
            if project.floors.isEmpty {
                ContentUnavailableView {
                    Label("Projekt einrichten", systemImage: "square.3.layers.3d")
                } description: {
                    Text("Lege zuerst die Etagen oder Bereiche und ihre Messpunkte an.")
                } actions: {
                    Button("Einrichten …") { showSetup = true }
                        .buttonStyle(.borderedProminent)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(.background)
            }
        }
        .task {
            // Beide Systemabfragen gleich beim Start, nicht erst mitten in der ersten Messung.
            LocationAccess.shared.request()
            await WiFi.requestLocalNetworkAccess()
        }
        .onDisappear {
            runner.cancel()
            recorder.stopSampling()
        }
        .task(id: heatmapInput) {
            heatmapImage = await Heatmap.image(for: heatmapInput)
        }
        .toolbar { toolbar }
        .onChange(of: recorder.isActive) {
            // Während eines Laufs zeigt die Karte die Phase, in der gelaufen wird.
            if recorder.isActive {
                isComparison = false
                display = .map
            }
        }
        .sheet(isPresented: $showStructureEditor) {
            StructureEditor(project: $document.project)
        }
        .sheet(isPresented: $showSetup) {
            ProjectSetupSheet(project: $document.project)
        }
        .onAppear {
            // Ein neues Projekt fragt zuerst nach Bereichen und Messpunkten.
            if project.floors.isEmpty { showSetup = true }
        }
        .onChange(of: selection) {
            if let point = selection.flatMap(project.point(named:)), point.floor != floor { floorSelection = point.floor }
        }
        .onChange(of: floor) {
            if let point = selection.flatMap(project.point(named:)), point.floor != floor { selection = nil }
        }
        .onChange(of: project.plans, initial: true) { old, new in
            for (key, plan) in new where old[key] != plan || planImages[key] == nil {
                planImages[key] = PlanImage.cgImage(of: plan)
            }
            for key in planImages.keys where new[key] == nil { planImages[key] = nil }
        }
        .fileImporter(isPresented: $showPlanImporter, allowedContentTypes: [.image]) { result in
            switch result {
            case .success(let url): loadPlan(from: url)
            case .failure(let error): notice = Notice(title: "Grundriss", messages: [error.localizedDescription])
            }
        }
        .alert(notice?.title ?? "", isPresented: Binding(get: { notice != nil }, set: { if !$0 { notice = nil } }),
               presenting: notice) { _ in
            Button("OK") {}
        } message: { notice in
            Text(notice.messages.joined(separator: "\n\n"))
        }
    }

    @ToolbarContentBuilder
    private var toolbar: some ToolbarContent {
        ToolbarItem(placement: .principal) {
            Picker("Etage", systemImage: "square.3.layers.3d",
                   selection: Binding(get: { floor }, set: { floorSelection = $0 })) {
                ForEach(project.floors) { Text($0.name).tag($0.id) }
            }
            .pickerStyle(.segmented)
            .disabled(recorder.isActive)
        }
        ToolbarItem(placement: .primaryAction) {
            Picker("Darstellung", systemImage: "rectangle.split.2x1", selection: $display) {
                Label("Karte", systemImage: "map").tag(Display.map)
                Label("Auswertung", systemImage: "tablecells").tag(Display.analysis)
            }
            .pickerStyle(.segmented)
            .help("Karte mit Heatmap oder Auswertung mit Übersicht, Tabelle und Details")
            .disabled(recorder.isActive)
        }
        ToolbarItem(placement: .primaryAction) {
            Menu("Grundriss", systemImage: "photo") {
                Button(plan == nil ? "Bild laden …" : "Bild ersetzen …") { showPlanImporter = true }
                Button("Bild entfernen", role: .destructive) { document.project.plans[floor] = nil }
                    .disabled(plan == nil)
            }
            .help("Grundriss für \(project.floorName(floor)) laden (JPG, PNG, HEIC) oder Bild auf den Plan ziehen")
        }
        ToolbarItem(placement: .primaryAction) {
            Button("Projekt", systemImage: "slider.horizontal.3") { showStructureEditor = true }
                .help("Etagen und Phasen verwalten, Rate des Internetanschlusses eintragen")
                .disabled(runner.isRunning || recorder.isActive)
        }
        ToolbarItem(placement: .primaryAction) {
            Toggle("Messen", systemImage: "wifi", isOn: $showMeasurePanel)
                .help("Geführte Messung an den festen Punkten")
        }
    }

    // MARK: - Heatmap

    private var heatmapInput: HeatmapInput {
        project.heatmapInput(metric: metric, floor: floor,
                             view: isComparison ? .comparison(from: compareFrom, to: compareTo) : .phase(phase))
    }

    /// Phase für Anzeige und Messung; eine Auswahl führt aus dem Vergleich zurück zur einzelnen Phase.
    private var phaseBinding: Binding<String> {
        Binding(get: { phase }, set: {
            phaseSelection = $0
            isComparison = false
        })
    }

    private func phasePicker(_ title: String, selection: Binding<String>) -> some View {
        Picker(title, selection: selection) {
            ForEach(project.phases) { Text($0.name).tag($0.id) }
        }
        .labelsHidden()
        .fixedSize()
        .help(title)
    }

    private var controls: some View {
        // Passt nicht alles in eine Zeile, rückt die Legende in eine zweite, statt umzubrechen.
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 12) {
                pickers
                Spacer(minLength: 12)
                legend
                    .fixedSize()
            }
            VStack(alignment: .leading, spacing: 6) {
                pickers
                legend
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private var pickers: some View {
        HStack(spacing: 8) {
            // Die Auswertung zeigt immer alle Phasen; dort wird nur das Vergleichspaar gewählt.
            if display == .map {
                Picker("Ansicht", selection: $isComparison) {
                    Text("Phase").tag(false)
                    Text("Vergleich").tag(true)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
                .disabled(recorder.isActive)
            } else {
                Text("Vergleich")
                    .foregroundStyle(.secondary)
            }
            Group {
                if isComparison || display == .analysis {
                    phasePicker("Vergleich von", selection: Binding(get: { compareFrom }, set: { compareFromSelection = $0 }))
                    Image(systemName: "arrow.right")
                        .foregroundStyle(.secondary)
                        .accessibilityLabel("nach")
                    phasePicker("Vergleich nach", selection: Binding(get: { compareTo }, set: { compareToSelection = $0 }))
                } else {
                    phasePicker("Phase", selection: Binding(get: { phase }, set: { phaseSelection = $0 }))
                }
            }
            .disabled(recorder.isActive)
            Picker("Messgröße", selection: $metric) {
                ForEach(Metric.allCases) { Text($0.title).tag($0) }
            }
            .labelsHidden()
            .fixedSize()
        }
    }

    @ViewBuilder
    private var legend: some View {
        if display == .map, let missingDataNote {
            Label(missingDataNote, systemImage: "info.circle")
                .font(.callout)
                .foregroundStyle(.secondary)
                .lineLimit(2)
        } else {
            HeatmapLegend(scale: project.scale(for: metric), isComparison: isComparison && display == .map)
        }
    }

    /// Erklärt, warum keine Heatmap zu sehen ist.
    private var missingDataNote: String? {
        switch heatmapInput.content {
        case .single(let source):
            return source.values.isEmpty ? "Keine verorteten Werte für \(project.phaseName(phase)) auf dieser Etage." : nil
        case .comparison(let from, let to):
            let missing = [(compareFrom, from), (compareTo, to)].filter { $0.1.values.isEmpty }.map { project.phaseName($0.0) }
            return missing.isEmpty ? nil : "Für den Vergleich fehlen verortete Werte: \(Set(missing).sorted().joined(separator: " und "))."
        }
    }

    // MARK: - Messpanel und Lauf

    private var sidePanel: some View {
        VStack(spacing: 0) {
            Picker("Art der Messung", selection: $panelTab) {
                Text("Feste Punkte").tag(PanelTab.points)
                Text("Lauf").tag(PanelTab.walk)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .padding(10)
            // Während gemessen oder gelaufen wird, bleibt die Art fest.
            .disabled(runner.isRunning || recorder.isActive)
            Divider()
            switch panelTab {
            case .points:
                MeasurePanel(project: $document.project, selection: $selection, phase: phaseBinding, runner: runner)
            case .walk:
                WalkPanel(project: $document.project, floor: floor, phase: phaseBinding, recorder: recorder,
                          showPaths: $showWalkPaths)
            }
        }
    }

    /// Wege der Läufe dieser Etage in der angezeigten Phase (im Vergleich beide Phasen).
    private var walkPaths: [PlanWalkPath] {
        let shownPhases: Set<String> = isComparison ? [compareFrom, compareTo] : [phase]
        return project.walks.compactMap { walk in
            let isActive = walk.id == recorder.walk?.id
            guard walk.floor == floor, isActive || showWalkPaths,
                  isActive || shownPhases.contains(walk.phase) else { return nil }
            return PlanWalkPath(id: walk.id, taps: walk.taps.map { CGPoint(x: $0.x, y: $0.y) }, isActive: isActive)
        }
    }

    private func addWalkTap(x: Double, y: Double) {
        recorder.addTap(x: x, y: y)
        recorder.commit(into: &document.project)
    }

    /// Farbe und Wert der gewählten Messgröße je Punkt dieser Etage.
    private var markers: [String: PlanMarker] {
        var result: [String: PlanMarker] = [:]
        let scale = project.scale(for: metric)
        for point in floorPoints {
            func value(_ phase: String) -> Double? {
                project.measurement(at: point.name, in: phase).flatMap(metric.value(in:))
            }
            if isComparison {
                let before = value(compareFrom)
                let after = value(compareTo)
                guard before != nil || after != nil else { continue }
                let color = if let before, let after {
                    Color(HeatColor.difference(scale.change(from: before, to: after)))
                } else {
                    Color(white: 0.45)
                }
                let texts = [before, after].map { $0.map { metric.format($0, withUnit: false) } ?? "–" }
                result[point.name] = PlanMarker(color: color, text: "\(texts[0]) → \(texts[1]) \(metric.unit)")
            } else {
                guard let value = value(phase) else { continue }
                result[point.name] = PlanMarker(color: Color(HeatColor.scale(scale.score(of: value))),
                                                text: scale.describe(value))
            }
        }
        return result
    }

    private var footer: some View {
        HStack {
            Text(hint)
            Spacer()
            let placed = floorPoints.filter(\.isPlaced).count
            Text("\(placed) von \(floorPoints.count) Punkten gesetzt")
        }
        .font(.callout)
        .foregroundStyle(.secondary)
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
    }

    private var hint: String {
        if recorder.isActive {
            return "Lauf aktiv: die eigene Position auf dem Plan anklicken."
        }
        if let pendingPoint {
            return "Auf den Plan klicken, um „\(pendingPoint.name)“ zu setzen."
        }
        if floorPoints.contains(where: { !$0.isPlaced }) {
            return "Punkt ohne Position in der Liste auswählen und auf den Plan klicken. Ziehen verschiebt einen Punkt."
        }
        return floorPoints.isEmpty ? "Auf dieser Etage gibt es noch keine Messpunkte." : "Ziehen verschiebt einen Punkt."
    }

    // MARK: - Punkte

    private func setPosition(of name: String, x: Double, y: Double) {
        let wasPending = pendingPoint?.name == name
        document.project.setPosition(ofPointNamed: name, x: x, y: y)
        // Nach dem Setzen gleich den nächsten offenen Punkt der Etage anbieten.
        if wasPending, let next = floorPoints.first(where: { !$0.isPlaced }) {
            selection = next.name
        }
    }

    // MARK: - Grundriss

    @discardableResult
    private func loadPlan(from url: URL) -> Bool {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        guard let data = try? Data(contentsOf: url), let plan = PlanImage.makePlan(from: data) else {
            notice = Notice(title: "Grundriss", messages: ["„\(url.lastPathComponent)“ ließ sich nicht als Bild lesen. Unterstützt werden JPG, PNG und HEIC."])
            return false
        }
        document.project.plans[floor] = plan
        return true
    }

}

#Preview {
    ContentView(document: .constant(DeltaWiFiDocument()))
}
