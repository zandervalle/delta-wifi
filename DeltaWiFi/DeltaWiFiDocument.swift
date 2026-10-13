//
//  DeltaWiFiDocument.swift
//  DeltaWiFi
//

import SwiftUI
import UniformTypeIdentifiers

nonisolated extension UTType {
    /// Eigenes Dokumentformat `.wlanprojekt` (JSON), in der Info.plist deklariert.
    static let wlanProjekt = UTType(exportedAs: "de.mhs131.netmap.wlanprojekt")
}

nonisolated struct DeltaWiFiDocument: FileDocument {
    static let readableContentTypes: [UTType] = [.wlanProjekt]

    var project: Project

    init(project: Project = Project()) {
        self.project = project
    }

    init(configuration: ReadConfiguration) throws {
        guard let data = configuration.file.regularFileContents else {
            throw CocoaError(.fileReadCorruptFile)
        }
        project = try Project.decode(data)
    }

    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        FileWrapper(regularFileWithContents: try project.encoded())
    }
}
