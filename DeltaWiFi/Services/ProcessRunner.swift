//
//  ProcessRunner.swift
//  DeltaWiFi
//
//  Startet Kommandozeilenwerkzeuge (ping, networkQuality, route, iperf3) und
//  liefert ihre Ausgabe. Beim Abbruch des Tasks wird der Prozess beendet.
//

import Foundation
import os

nonisolated enum ProcessRunner {
    struct Output: Sendable {
        var status: Int32
        var stdout: Data

        var text: String { String(decoding: stdout, as: UTF8.self) }
    }

    @concurrent
    static func run(_ path: String, _ arguments: [String]) async throws -> Output {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: path)
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice

        let cancelled = OSAllocatedUnfairLock(initialState: false)

        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Output, Error>) in
                DispatchQueue.global(qos: .userInitiated).async {
                    do {
                        let started = try cancelled.withLock { cancelled -> Bool in
                            guard !cancelled else { return false }
                            try process.run()
                            return true
                        }
                        guard started else { throw CancellationError() }
                        let data = pipe.fileHandleForReading.readDataToEndOfFile()
                        process.waitUntilExit()
                        continuation.resume(returning: Output(status: process.terminationStatus, stdout: data))
                    } catch {
                        continuation.resume(throwing: error)
                    }
                }
            }
        } onCancel: {
            cancelled.withLock { cancelled in
                cancelled = true
                if process.isRunning { process.terminate() }
            }
        }
    }
}
