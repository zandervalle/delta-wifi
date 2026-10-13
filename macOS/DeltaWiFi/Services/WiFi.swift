//
//  WiFi.swift
//  DeltaWiFi
//
//  Funkwerte über CoreWLAN sowie Gateway und IP-Adresse des WLAN-Interface.
//

import CoreWLAN
import Foundation

enum WiFi {
    private static var interface: CWInterface? { CWWiFiClient.shared().interface() }

    /// BSD-Name des WLAN-Interface, z. B. `en0`.
    static var interfaceName: String? { interface?.interfaceName }

    /// Aktuelle Funkwerte; `nil`, wenn WLAN aus oder nicht verbunden ist.
    static func snapshot() -> RadioSnapshot? {
        guard let interface, interface.powerOn() else { return nil }
        let rssi = interface.rssiValue()
        // CoreWLAN meldet 0, solange keine Verbindung besteht.
        guard rssi != 0 else { return nil }
        let noise = interface.noiseMeasurement()
        let rate = interface.transmitRate()
        let channel = interface.wlanChannel()
        return RadioSnapshot(rssi: rssi,
                             noise: NoiseFloor.isPlausible(Double(noise)) ? noise : nil,
                             txRate: rate > 0 ? rate : nil,
                             band: channel.flatMap { band($0.channelBand) },
                             channel: channel?.channelNumber,
                             width: channel.flatMap { width($0.channelWidth) },
                             phy: phy(interface.activePHYMode()),
                             bssid: interface.bssid(),
                             ssid: interface.ssid())
    }

    private static var didRequestLocalNetworkAccess = false

    /// Löst die macOS-Abfrage „Geräte im lokalen Netzwerk finden“ aus, indem einmal die Box angepingt wird.
    /// Ohne diese Freigabe scheitern Ping zur Box und iperf3.
    static func requestLocalNetworkAccess() async {
        guard !didRequestLocalNetworkAccess, let interface = interfaceName else { return }
        didRequestLocalNetworkAccess = true
        let gateway = await NetworkInfo.gateway(interface: interface)
        _ = try? await ProcessRunner.run("/sbin/ping", ["-b", interface, "-c", "1", "-t", "2", gateway])
    }

    private static func band(_ band: CWChannelBand) -> String? {
        switch band {
        case .band2GHz: "2.4"
        case .band5GHz: "5"
        case .band6GHz: "6"
        default: nil
        }
    }

    private static func width(_ width: CWChannelWidth) -> Int? {
        switch width {
        case .width20MHz: 20
        case .width40MHz: 40
        case .width80MHz: 80
        case .width160MHz: 160
        default: nil
        }
    }

    private static func phy(_ mode: CWPHYMode) -> String? {
        switch mode {
        case .mode11a: "11a"
        case .mode11b: "11b"
        case .mode11g: "11g"
        case .mode11n: "11n"
        case .mode11ac: "11ac"
        case .mode11ax: "11ax"
        case .mode11be: "11be"
        case .modeNone: nil
        @unknown default: "PHY \(mode.rawValue)"
        }
    }
}

nonisolated enum NetworkInfo {
    static let fallbackGateway = "192.168.178.1"
    static let internetTarget = "1.1.1.1"

    /// Gateway aus `route -n get default`, bevorzugt für das WLAN-Interface (falls zusätzlich ein Kabel steckt).
    @concurrent
    static func gateway(interface: String) async -> String {
        for arguments in [["-n", "get", "-ifscope", interface, "default"], ["-n", "get", "default"]] {
            if let output = try? await ProcessRunner.run("/sbin/route", arguments),
               let gateway = MeasurementParsing.gateway(fromRouteOutput: output.text) {
                return gateway
            }
        }
        return fallbackGateway
    }

    /// IPv4-Adresse eines Interface, z. B. für `iperf3 -B`.
    static func ipv4Address(of interface: String) -> String? {
        var list: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&list) == 0 else { return nil }
        defer { freeifaddrs(list) }

        var cursor = list
        while let entry = cursor {
            cursor = entry.pointee.ifa_next
            guard let address = entry.pointee.ifa_addr, address.pointee.sa_family == UInt8(AF_INET),
                  String(cString: entry.pointee.ifa_name) == interface else { continue }
            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            if getnameinfo(address, socklen_t(address.pointee.sa_len), &host, socklen_t(host.count), nil, 0, NI_NUMERICHOST) == 0 {
                return String(cString: host)
            }
        }
        return nil
    }

    /// Pfad zu `iperf3` aus Homebrew, falls installiert.
    static var iperfPath: String? {
        ["/opt/homebrew/bin/iperf3", "/usr/local/bin/iperf3"].first { FileManager.default.isExecutableFile(atPath: $0) }
    }
}
