//
//  DeltaWiFiApp.swift
//  DeltaWiFi
//
//  Created by Alexander on 11.10.26.
//

import SwiftUI

@main
struct DeltaWiFiApp: App {
    var body: some Scene {
        DocumentGroup(newDocument: DeltaWiFiDocument()) { file in
            ContentView(document: file.$document)
        }
    }
}
