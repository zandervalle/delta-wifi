//
//  LocationAccess.swift
//  DeltaWiFi
//
//  macOS liefert BSSID und SSID nur mit Ortungsfreigabe.
//

import CoreLocation
import Observation

@Observable
final class LocationAccess: NSObject, CLLocationManagerDelegate {
    static let shared = LocationAccess()

    private let manager = CLLocationManager()
    private(set) var status = CLAuthorizationStatus.notDetermined

    private override init() {
        super.init()
        manager.delegate = self
        status = manager.authorizationStatus
    }

    var isAuthorized: Bool { status == .authorizedAlways }

    /// Fragt beim ersten Start nach der Freigabe; später entscheidet die Systemeinstellung.
    func request() {
        if manager.authorizationStatus == .notDetermined { manager.requestWhenInUseAuthorization() }
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        status = manager.authorizationStatus
    }
}
