//
//  MeasurementRunner.swift
//  DeltaWiFi
//
//  Führt eine Messung an einem festen Punkt aus: Signal-Stichproben und Ping
//  parallel, danach Speedtest und optional iperf3.
//

import Foundation
import Observation

@Observable
final class MeasurementRunner {
    struct Options {
        var speedtest = true
        /// Adresse des iperf3-Servers; `nil` = keine interne Messung.
        var iperfServer: String?
    }

    struct Stage {
        var title: String
        var start = Date.now
        /// Erwartete Dauer; `nil`, wenn sie nicht absehbar ist.
        var duration: TimeInterval?
    }

    struct Target: Equatable {
        var point: String
        /// ID der Phase.
        var phase: String
    }

    static let sampleCount = 10
    static let pingCount = 60
    /// Parallele TCP-Ströme bei iperf3. Ein einzelner Strom schwankte über WLAN stark (177–354 Mbit/s am
    /// selben Platz) und lag deutlich unter dem, was die Funkstrecke hergibt; vier Ströme messen stabil.
    static let iperfStreams = 4

    /// Aktueller Abschnitt; `nil`, wenn keine Messung läuft.
    private(set) var stage: Stage?
    private(set) var target: Target?
    /// Stichproben der laufenden bzw. letzten Messung.
    private(set) var samples: [RadioSnapshot] = []
    /// Hinweise und Fehler der letzten Messung.
    private(set) var messages: [String] = []

    private var task: Task<Void, Never>?

    var isRunning: Bool { stage != nil }

    func start(target: Target, options: Options, note: String, onFinish: @escaping (Measurement) -> Void) {
        guard !isRunning else { return }
        self.target = target
        samples = []
        messages = []
        stage = Stage(title: "Messung wird vorbereitet")
        task = Task {
            do {
                var measurement = try await measure(options: options)
                measurement.notiz = note.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : note
                onFinish(measurement)
            } catch is CancellationError {
                messages = ["Messung abgebrochen, nichts gespeichert."]
            } catch {
                messages = [error.localizedDescription]
            }
            stage = nil
        }
    }

    func cancel() {
        task?.cancel()
    }

    // MARK: - Ablauf

    private enum Failure: LocalizedError {
        case notConnected

        var errorDescription: String? {
            "Der Mac ist mit keinem WLAN verbunden. Bitte WLAN einschalten und verbinden, dann erneut messen."
        }
    }

    private func measure(options: Options) async throws -> Measurement {
        guard let interface = WiFi.interfaceName, WiFi.snapshot() != nil else { throw Failure.notConnected }
        let gateway = await NetworkInfo.gateway(interface: interface)

        // Signal und beide Pings laufen gleichzeitig.
        stage = Stage(title: "Signal und Ping (\(Self.pingCount) s)", duration: Double(Self.pingCount) + 1)
        async let gatewayPing = Self.ping(gateway, interface: interface)
        async let internetPing = Self.ping(NetworkInfo.internetTarget, interface: interface)
        for index in 0..<Self.sampleCount {
            if index > 0 { try await Task.sleep(for: .seconds(1)) }
            if let snapshot = WiFi.snapshot() { samples.append(snapshot) }
        }
        let (toGateway, toInternet) = await (gatewayPing, internetPing)
        try Task.checkCancellation()

        var measurement = MeasurementParsing.summarize(samples)
        measurement.zeit = MeasurementParsing.timestamp(.now)
        if samples.count < Self.sampleCount {
            messages.append("Nur \(samples.count) von \(Self.sampleCount) Signal-Stichproben: Die WLAN-Verbindung war zeitweise weg.")
        }
        if let toGateway {
            measurement.pingGwVerlustPct = toGateway.lossPct
            measurement.pingGwAvgMs = toGateway.avgMs
            measurement.pingGwMaxMs = toGateway.maxMs
        } else {
            messages.append("Ping zur Box (\(gateway)) lieferte kein Ergebnis.")
        }
        if let toInternet {
            measurement.pingInetVerlustPct = toInternet.lossPct
            measurement.pingInetAvgMs = toInternet.avgMs
            measurement.pingInetMaxMs = toInternet.maxMs
        } else {
            messages.append("Ping ins Internet (\(NetworkInfo.internetTarget)) lieferte kein Ergebnis.")
        }

        if options.speedtest {
            stage = Stage(title: "Internet-Speedtest")
            let output = try? await ProcessRunner.run("/usr/bin/networkQuality", ["-I", interface, "-s", "-c"])
            try Task.checkCancellation()
            if let result = output.flatMap({ MeasurementParsing.networkQuality($0.stdout) }) {
                measurement.inetDownMbit = result.downMbit
                measurement.inetUpMbit = result.upMbit
            } else {
                messages.append("Der Internet-Speedtest (networkQuality) lieferte kein Ergebnis.")
            }
        }

        if let server = options.iperfServer {
            if let iperf = NetworkInfo.iperfPath, let address = NetworkInfo.ipv4Address(of: interface) {
                // -R: Der Server sendet, gemessen wird also der Download.
                for (title, extra) in [("Intern Download (iperf3, 10 s)", ["-R"]), ("Intern Upload (iperf3, 10 s)", [])] {
                    stage = Stage(title: title, duration: 11)
                    let output = try? await ProcessRunner.run(iperf, ["-c", server, "-B", address, "-t", "10", "-P", String(Self.iperfStreams)] + extra + ["-J"])
                    try Task.checkCancellation()
                    let value = output.flatMap { MeasurementParsing.iperf($0.stdout) }
                    if extra.isEmpty { measurement.lanUpMbit = value } else { measurement.lanDownMbit = value }
                    if value == nil {
                        messages.append("iperf3 lieferte kein Ergebnis (\(title)). Läuft auf \(server) „iperf3 -s“?")
                        break
                    }
                }
            } else {
                messages.append("Interne Messung übersprungen: iperf3 oder die WLAN-IP-Adresse wurde nicht gefunden.")
            }
        }
        return measurement
    }

    /// `/sbin/ping` über das WLAN-Interface, eine Anfrage je Sekunde.
    private static func ping(_ target: String, interface: String) async -> PingResult? {
        let output = try? await ProcessRunner.run("/sbin/ping", ["-b", interface, "-c", String(pingCount), "-i", "1", target])
        return output.flatMap { MeasurementParsing.ping($0.text) }
    }
}
