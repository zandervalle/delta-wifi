//
//  DeltaWiFiTests.swift
//  DeltaWiFiTests
//
//  Created by Alexander on 11.10.26.
//

import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers
import Testing
@testable import DeltaWiFi

/// Ausgangslage der meisten Tests: vier Etagen mit zehn Messpunkten.
private let standardPoints: [MeasurePoint] = [
    .init(name: "Flur UG (Referenz nahe Router)", floor: "keller"),
    .init(name: "Büro Keller", floor: "keller"),
    .init(name: "Wohnzimmer Sofa", floor: "eg"),
    .init(name: "Küche Esstisch", floor: "eg"),
    .init(name: "Büro OG", floor: "og"),
    .init(name: "Schlafzimmer", floor: "og"),
    .init(name: "Terrasse (außen)", floor: "aussen"),
    .init(name: "Terrasse unten (außen)", floor: "aussen"),
    .init(name: "Gewächshaus (außen)", floor: "aussen"),
    .init(name: "Einfahrt E-Ladeplatz (außen)", floor: "aussen"),
]

private func standardProject() -> Project {
    var project = Project()
    project.floors = Floor.defaults
    project.points = standardPoints
    return project
}

struct NumberParsingTests {

    @Test func parsesNumbersLeniently() {
        #expect(parseNumber(" 1,7 ") == 1.7)
        #expect(parseNumber("-79") == -79)
        #expect(parseNumber("") == nil)
        #expect(parseNumber("n/a") == nil)
        #expect(parseNumber("nan") == nil)
        #expect(formatNumber(5) == "5")
        #expect(formatNumber(2.4) == "2.4")
    }
}

struct WalkTests {
    private let taps = [Tap(t: 1_000, x: 0, y: 0), Tap(t: 3_000, x: 1, y: 0.5), Tap(t: 4_000, x: 1, y: 1)]

    @Test func interpolatesBetweenSurroundingTaps() throws {
        let middle = try #require(WalkInterpolation.position(at: 2_000, sortedTaps: taps))
        #expect(middle.x == 0.5 && middle.y == 0.25)
        let later = try #require(WalkInterpolation.position(at: 3_500, sortedTaps: taps))
        #expect(later.x == 1 && later.y == 0.75)
        let onTap = try #require(WalkInterpolation.position(at: 3_000, sortedTaps: taps))
        #expect(onTap.x == 1 && onTap.y == 0.5)
        let first = try #require(WalkInterpolation.position(at: 1_000, sortedTaps: taps))
        #expect(first.x == 0 && first.y == 0)
    }

    @Test func dropsSamplesOutsideTaps() {
        #expect(WalkInterpolation.position(at: 999, sortedTaps: taps) == nil)
        #expect(WalkInterpolation.position(at: 4_001, sortedTaps: taps) == nil)
        #expect(WalkInterpolation.position(at: 1_000, sortedTaps: []) == nil)
    }

}

struct ProjectFormatTests {
    // Datei im alten Format ohne Etagen- und Phasenliste, Zahlen teils als Text: Das Lesen bleibt nachsichtig.
    private let webJSON = """
    {
      "version": 1,
      "plans": { "eg": { "src": "data:image/jpeg;base64,AAAA/BBB", "w": 1600, "h": 1120 } },
      "points": [ { "name": "Büro Keller", "floor": "keller", "x": 0.28, "y": 0.38 },
                  { "name": "Küche Esstisch", "floor": "eg", "x": null, "y": null } ],
      "measurements": {
        "VORHER": { "Büro Keller": { "rssi_dbm": -79, "noise_dbm": -92, "snr_db": 13, "txrate_mbit": "6", "band_ghz": 2.4,
                 "ping_gw_verlust_pct": 1.7, "ping_gw_avg_ms": 18, "ping_gw_max_ms": 310,
                 "ping_inet_verlust_pct": 3.3, "ping_inet_max_ms": 290, "inet_down_mbit": 22, "inet_up_mbit": 8,
                 "lan_down_mbit": "", "bssid": "", "notiz": "", "wiederholung": 1 },
                 "Carport": { "rssi_dbm": -88 } }
      },
      "walks": [ { "id": "w1", "phase": "VORHER", "floor": "eg", "started": 1791745000000,
                   "taps": [ { "t": 1791745003000, "x": 0.5, "y": 0.56 } ] } ],
      "survey": { "w1": [ [0.5, 0.56, -52, 40, 744, "5"], [0.6, 0.5, -60, null, 300, 2.4] ] }
    }
    """

    @Test func readsWebstandardProject() throws {
        let project = try Project.decode(Data([0xEF, 0xBB, 0xBF]) + Data(webJSON.utf8))

        #expect(project.plans["eg"] == Plan(src: "data:image/jpeg;base64,AAAA/BBB", w: 1600, h: 1120))
        #expect(project.points.map(\.name) == ["Büro Keller", "Küche Esstisch", "Carport"])
        #expect(project.points[0].x == 0.28)
        #expect(!project.points[1].isPlaced)
        // Ohne Listen in der Datei gelten die Standardetagen und -phasen; Punkte ohne Etage landen auf der ersten.
        #expect(project.floors == Floor.defaults)
        #expect(project.phases == Phase.defaults)
        #expect(project.point(named: "Carport")?.floor == "keller")

        let measurement = try #require(project.measurement(at: "Büro Keller", in: "VORHER"))
        #expect(measurement.rssiDBm == -79)
        #expect(measurement.txrateMbit == 6)
        #expect(measurement.bandGHz == "2.4")
        #expect(measurement.pingGwMaxMs == 310)
        #expect(measurement.lanDownMbit == nil)
        #expect(measurement.wiederholung == 1)
        #expect(project.measurements["NACHHER"] == [:])

        #expect(project.walks == [Walk(id: "w1", phase: "VORHER", floor: "eg", started: 1_791_745_000_000,
                                       taps: [Tap(t: 1_791_745_003_000, x: 0.5, y: 0.56)])])
        #expect(project.survey["w1"] == [
            SurveySample(x: 0.5, y: 0.56, rssi: -52, snr: 40, rate: 744, band: "5"),
            SurveySample(x: 0.6, y: 0.5, rssi: -60, snr: nil, rate: 300, band: "2.4"),
        ])
    }

    @Test func roundTripKeepsCompatibleFieldNames() throws {
        let project = try Project.decode(Data(webJSON.utf8))
        let data = try project.encoded()
        #expect(try Project.decode(data) == project)

        let json = try #require(String(data: data, encoding: .utf8))
        #expect(json.contains(#""survey":{"w1":[[0.5,0.56,-52,40,744,"5"],[0.6,0.5,-60,null,300,"2.4"]]}"#))
        #expect(json.contains(#"{"floor":"eg","name":"Küche Esstisch","x":null,"y":null}"#))
        #expect(json.contains(#""started":1791745000000"#))
        #expect(json.contains(#""rssi_dbm":-79"#))
        #expect(json.contains("data:image/jpeg;base64,AAAA/BBB"))
        #expect(json.contains(#""NACHHER":{}"#))
    }

    @Test func newProjectStartsEmptyWithStandardPhases() throws {
        let project = Project()
        #expect(project.floors.isEmpty && project.points.isEmpty)
        #expect(project.phases == Phase.defaults)
        #expect(Set(project.measurements.keys) == ["VORHER", "NACHHER"])
        // Auch der leere Zustand übersteht das Speichern; es kommen keine Standardetagen dazu.
        let reloaded = try Project.decode(project.encoded())
        #expect(reloaded == project && reloaded.floors.isEmpty)
        #expect(project.resolvedFloorID("eg") == "")
    }

    @Test func setupCreatesAreasWithPoints() {
        var project = Project()
        project.addAreas([
            .init(name: " Erdgeschoss ", points: ["Wohnzimmer Sofa", "", "Küche"]),
            .init(name: "", points: ["verwaist"]),
            .init(name: "Garten", points: ["Terrasse", "Küche"]),
        ])
        #expect(project.floors == [Floor(id: "etage-1", name: "Erdgeschoss"), Floor(id: "etage-2", name: "Garten")])
        // Leere und doppelte Punktnamen sowie Bereiche ohne Namen werden übergangen.
        #expect(project.points == [MeasurePoint(name: "Wohnzimmer Sofa", floor: "etage-1"), MeasurePoint(name: "Küche", floor: "etage-1"),
                                   MeasurePoint(name: "Terrasse", floor: "etage-2")])
        #expect(project.nextOpenPoint(in: "VORHER", after: nil)?.name == "Wohnzimmer Sofa")
    }

}

struct PointEditingTests {

    @Test func positionsAreClampedAndClearedOnFloorChange() throws {
        var project = standardProject()
        project.setPosition(ofPointNamed: "Büro OG", x: 1.4, y: -0.2)
        #expect(project.point(named: "Büro OG")?.x == 1)
        #expect(project.point(named: "Büro OG")?.y == 0)

        project.setFloor("og", forPointNamed: "Büro OG")
        #expect(project.point(named: "Büro OG")?.isPlaced == true)
        project.setFloor("eg", forPointNamed: "Büro OG")
        let moved = try #require(project.point(named: "Büro OG"))
        #expect(moved.floor == "eg" && !moved.isPlaced)
    }

    @Test func addsAndRemovesPoints() {
        var project = standardProject()
        let added = project.addPoint(named: "  Gästezimmer ", floor: "og")
        #expect(added)
        #expect(project.point(named: "Gästezimmer")?.floor == "og")
        let duplicate = project.addPoint(named: "Gästezimmer", floor: "eg")
        let empty = project.addPoint(named: "   ", floor: "eg")
        #expect(!duplicate && !empty)

        project.measurements["NACHHER"]?["Gästezimmer"] = Measurement()
        let removedWithData = project.removePoint(named: "Gästezimmer")
        #expect(!removedWithData)
        project.measurements["NACHHER"]?["Gästezimmer"] = nil
        let removed = project.removePoint(named: "Gästezimmer")
        #expect(removed)
        #expect(project.points == standardPoints)
    }
}

struct PlanImageTests {
    /// Teiltransparentes PNG in der gewünschten Größe.
    private func png(width: Int, height: Int) throws -> Data {
        let context = try #require(CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                             space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                             bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red: 0, green: 0, blue: 1, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: width / 2, height: height))
        let image = try #require(context.makeImage())
        let data = NSMutableData()
        let destination = try #require(CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil))
        CGImageDestinationAddImage(destination, image, nil)
        #expect(CGImageDestinationFinalize(destination))
        return data as Data
    }

    @Test func scalesLargeImagesDownToMaxDimension() throws {
        let plan = try #require(PlanImage.makePlan(from: try png(width: 3200, height: 1600)))
        #expect(plan.w == 1600 && plan.h == 800)
        #expect(plan.src.hasPrefix("data:image/jpeg;base64,"))
        #expect(PlanImage.aspectRatio(of: plan) == 2)

        let image = try #require(PlanImage.cgImage(of: plan))
        #expect(image.width == 1600 && image.height == 800)
    }

    @Test func keepsSmallImagesAndRejectsNonImages() throws {
        let plan = try #require(PlanImage.makePlan(from: try png(width: 100, height: 50)))
        #expect(plan.w == 100 && plan.h == 50)
        #expect(PlanImage.makePlan(from: Data("kein Bild".utf8)) == nil)
        #expect(PlanImage.cgImage(of: Plan(src: "data:image/jpeg;base64,AAAA", w: 1, h: 1)) == nil)
        #expect(abs(PlanImage.aspectRatio(of: nil) - 1600.0 / 1120.0) < 1e-9)
    }

    @Test func transparentAreasBecomeWhite() throws {
        let plan = try #require(PlanImage.makePlan(from: try png(width: 200, height: 100)))
        let image = try #require(PlanImage.cgImage(of: plan))
        let context = try #require(CGContext(data: nil, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                                             space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                             bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue))
        // Pixel aus der rechten, im PNG transparenten Hälfte.
        context.draw(image, in: CGRect(x: -150, y: -50, width: 200, height: 100))
        let pixel = try #require(context.data).assumingMemoryBound(to: UInt8.self)
        #expect(pixel[0] > 240 && pixel[1] > 240 && pixel[2] > 240)
    }
}

struct MetricTests {

    @Test func scoreFollowsThresholds() {
        #expect(Metric.rssi.score(of: -60) == 1)
        #expect(Metric.rssi.score(of: -65) == 0.75)
        #expect(Metric.rssi.score(of: -70) == 0.5)
        #expect(Metric.rssi.score(of: -80) == 0)
        #expect(Metric.rssi.score(of: -95) == 0)
        #expect(Metric.rssi.score(of: -40) == 1)
        // Niedriger ist besser
        #expect(Metric.gatewayMaxPing.score(of: 30) == 1)
        #expect(Metric.gatewayMaxPing.score(of: 100) == 0.5)
        #expect(Metric.gatewayMaxPing.score(of: 170) == 0)
        #expect(Metric.gatewayLoss.score(of: 0) == 1)
        #expect(Metric.gatewayLoss.score(of: 2) == 0)
    }

    @Test func ratingUsesGoodAndOkayLimits() {
        #expect(Metric.rssi.rating(of: -60) == .good)
        #expect(Metric.rssi.rating(of: -70) == .okay)
        #expect(Metric.rssi.rating(of: -70.5) == .critical)
        #expect(Metric.gatewayLoss.rating(of: 0) == .good)
        #expect(Metric.gatewayLoss.rating(of: 1) == .okay)
        #expect(Metric.gatewayLoss.rating(of: 1.7) == .critical)
        #expect(Metric.linkRate.rating(of: 400) == .good)
        #expect(Metric.lanDown.rating(of: 99) == .critical)
    }

    @Test func changeSaturatesAtOneAndAHalfSteps() {
        #expect(Metric.rssi.change(from: -70, to: -55) == 1)
        #expect(Metric.rssi.change(from: -55, to: -70) == -1)
        #expect(Metric.rssi.change(from: -70, to: -70) == 0)
        #expect(abs(Metric.rssi.change(from: -70, to: -62.5) - 0.5) < 1e-9)
        // Weniger Verlust ist besser.
        #expect(Metric.gatewayLoss.change(from: 2, to: 0.5) == 1)
        #expect(Metric.gatewayMaxPing.change(from: 30, to: 300) == -1)
    }

    @Test func colorsAndTexts() {
        #expect(HeatColor.scale(0) == .critical)
        #expect(HeatColor.scale(0.5) == .okay)
        #expect(HeatColor.scale(1) == .good)
        #expect(HeatColor.difference(0) == .unchanged)
        #expect(HeatColor.difference(1) == .good)
        #expect(HeatColor.difference(-2) == .critical)
        #expect(Metric.rssi.format(-79) == "-79 dBm")
        #expect(Metric.gatewayLoss.format(1.7) == "1,7 %")
        #expect(Metric.rssi.thresholdText(-60) == "≥ -60 dBm")
        #expect(Metric.gatewayMaxPing.thresholdText(30) == "≤ 30 ms")
        #expect(Metric.gatewayLoss.thresholdText(0) == "0 %")
    }
}

struct HeatmapTests {

    @Test func weightMatchesFormula() {
        for squared in [0.0, 0.0001, 0.01, 0.25, 2.0] {
            let expected = 1 / pow(squared + 0.0004, 1.25)
            #expect(abs(Heatmap.weight(distanceSquared: squared) - expected) / expected < 1e-12)
        }
    }

    @Test func interpolatesByInverseDistance() throws {
        #expect(Heatmap.interpolate([], aspect: 1, width: 4, height: 4) == nil)

        let single = try #require(Heatmap.interpolate([FieldValue(x: 0.5, y: 0.5, value: -61)], aspect: 1, width: 8, height: 8))
        #expect(single.values.allSatisfy { abs($0 + 61) < 1e-9 })

        let values = [FieldValue(x: 0.05, y: 0.5, value: -50), FieldValue(x: 0.95, y: 0.5, value: -90)]
        let field = try #require(Heatmap.interpolate(values, aspect: 1, width: 10, height: 1))
        #expect(abs(field.values[0] + 50) < 0.01)
        #expect(abs(field.values[9] + 90) < 0.01)
        #expect(abs((field.values[4] + field.values[5]) / 2 + 70) < 1e-9)
        #expect(field.values == field.values.sorted(by: >))
    }

    @Test func distancesAreAspectCorrected() throws {
        // Plan doppelt so breit wie hoch: Der rechte Rand ist zwei Planhöhen vom linken entfernt.
        let field = try #require(Heatmap.interpolate([FieldValue(x: 0, y: 0.5, value: 1)], aspect: 2, width: 100, height: 1))
        #expect(abs(field.nearest[99] - 1.99) < 1e-9)
        #expect(abs(field.nearest[0] - 0.01) < 1e-9)

        let size = Heatmap.gridSize(aspect: 2)
        #expect(size.width == 160 && size.height == 80)
        let tall = Heatmap.gridSize(aspect: 0.5)
        #expect(tall.width == 80 && tall.height == 160)
    }

    @Test func opacityFadesWithDistance() {
        #expect(Heatmap.opacity(nearest: 0, hasWalkData: false) == 0.58)
        #expect(abs(Heatmap.opacity(nearest: 0.32, hasWalkData: false) - 0.18) < 1e-9)
        #expect(abs(Heatmap.opacity(nearest: 0.9, hasWalkData: false) - 0.18) < 1e-9)
        #expect(abs(Heatmap.opacity(nearest: 0.18, hasWalkData: true) - 0.18) < 1e-9)
        #expect(Heatmap.opacity(nearest: 0.18, hasWalkData: false) > 0.4)
    }

    @Test func rendersPhaseAndComparison() throws {
        let before = FieldSource(values: [FieldValue(x: 0.5, y: 0.5, value: -80)])
        let after = FieldSource(values: [FieldValue(x: 0.5, y: 0.5, value: -55)])
        var input = HeatmapInput(metric: .rssi, aspect: 1, content: .single(before))

        // Mittleres Pixel liegt auf dem Messpunkt: volle Deckkraft, kritisch = rot.
        var pixels = try #require(Heatmap.rgba(for: input, width: 3, height: 3))
        #expect(pixels.count == 36)
        #expect(pixels[4 * 4 + 3] == UInt8((0.58 * 255).rounded()))
        #expect(pixels[4 * 4] == UInt8((HeatColor.critical.red * 0.58 * 255).rounded()))
        #expect(pixels[3] < pixels[4 * 4 + 3])

        input.content = .comparison(from: before, to: after)
        pixels = try #require(Heatmap.rgba(for: input, width: 3, height: 3))
        #expect(pixels[4 * 4 + 1] == UInt8((HeatColor.good.green * 0.58 * 255).rounded()))
        // Umgekehrte Richtung: derselbe Unterschied gilt als Verschlechterung.
        input.content = .comparison(from: after, to: before)
        pixels = try #require(Heatmap.rgba(for: input, width: 3, height: 3))
        #expect(pixels[4 * 4] == UInt8((HeatColor.critical.red * 0.58 * 255).rounded()))

        // Fehlt eine Phase, wird im Vergleich nichts gezeichnet.
        input.content = .comparison(from: before, to: FieldSource())
        #expect(Heatmap.rgba(for: input, width: 3, height: 3) == nil)
        input.content = .single(FieldSource())
        #expect(Heatmap.rgba(for: input, width: 3, height: 3) == nil)
    }

    @Test func collectsFixedPointsAndWalkSamples() {
        var project = standardProject()
        var measurement = Measurement()
        measurement.rssiDBm = -79
        measurement.pingGwMaxMs = 310
        project.measurements["VORHER"]?["Büro Keller"] = measurement
        project.measurements["VORHER"]?["Büro OG"] = measurement
        project.measurements["VORHER"]?["Flur UG (Referenz nahe Router)"] = measurement
        project.setPosition(ofPointNamed: "Büro Keller", x: 0.2, y: 0.3)
        project.setPosition(ofPointNamed: "Büro OG", x: 0.6, y: 0.6)
        // „Flur UG“ ist im Keller, aber nicht gesetzt.
        project.walks = [Walk(id: "w1", phase: "VORHER", floor: "keller", started: 0),
                         Walk(id: "w2", phase: "NACHHER", floor: "keller", started: 0)]
        project.survey = ["w1": [SurveySample(x: 0.5, y: 0.5, rssi: -60, snr: nil, rate: 300)],
                          "w2": [SurveySample(x: 0.1, y: 0.1, rssi: -40)]]

        let rssi = project.fieldSource(metric: .rssi, floor: "keller", phase: "VORHER")
        #expect(rssi.values == [FieldValue(x: 0.2, y: 0.3, value: -79), FieldValue(x: 0.5, y: 0.5, value: -60)])
        #expect(rssi.hasWalkData)

        // Läufe liefern keinen Ping; Samples ohne Wert zählen nicht.
        let ping = project.fieldSource(metric: .gatewayMaxPing, floor: "keller", phase: "VORHER")
        #expect(ping == FieldSource(values: [FieldValue(x: 0.2, y: 0.3, value: 310)], hasWalkData: false))
        #expect(project.fieldSource(metric: .snr, floor: "keller", phase: "VORHER") == FieldSource())

        let input = project.heatmapInput(metric: .rssi, floor: "keller", view: .comparison(from: "VORHER", to: "NACHHER"))
        #expect(input.content == .comparison(from: rssi, to: FieldSource(values: [FieldValue(x: 0.1, y: 0.1, value: -40)], hasWalkData: true)))
        #expect(project.heatmapInput(metric: .rssi, floor: "keller", view: .phase("VORHER")).content == .single(rssi))
        #expect(abs(input.aspect - 1600.0 / 1120.0) < 1e-9)
    }
}

struct MeasurementParsingTests {

    @Test func parsesPingStatistics() {
        let output = """
        PING 192.168.178.1 (192.168.178.1): 56 data bytes
        64 bytes from 192.168.178.1: icmp_seq=0 ttl=64 time=3.492 ms
        Request timeout for icmp_seq 1

        --- 192.168.178.1 ping statistics ---
        60 packets transmitted, 59 packets received, 1.7% packet loss
        round-trip min/avg/max/stddev = 1.927/4.512/310.201/12.215 ms
        """
        #expect(MeasurementParsing.ping(output) == PingResult(lossPct: 1.7, avgMs: 4.512, maxMs: 310.201))

        let lost = "--- 1.1.1.1 ping statistics ---\n60 packets transmitted, 0 packets received, 100.0% packet loss\n"
        #expect(MeasurementParsing.ping(lost) == PingResult(lossPct: 100, avgMs: nil, maxMs: nil))
        #expect(MeasurementParsing.ping("ping: bind: Can't assign requested address") == nil)
    }

    @Test func parsesNetworkQualityAndIperf() throws {
        let quality = Data(#"{"base_rtt": 12.3, "dl_throughput": 241530000, "ul_throughput": 48260000, "interface_name": "en0"}"#.utf8)
        let result = try #require(MeasurementParsing.networkQuality(quality))
        #expect(result.downMbit == 241.5 && result.upMbit == 48.3)
        #expect(MeasurementParsing.networkQuality(Data("Fehler".utf8)) == nil)
        #expect(MeasurementParsing.networkQuality(Data("{}".utf8)) == nil)

        let iperf = Data(#"{"start": {}, "end": {"sum_sent": {"bits_per_second": 9.1e8}, "sum_received": {"bits_per_second": 873456789.5}}}"#.utf8)
        #expect(MeasurementParsing.iperf(iperf) == 873.5)
        #expect(MeasurementParsing.iperf(Data(#"{"error": "unable to connect to server"}"#.utf8)) == nil)
    }

    @Test func parsesGatewayFromRoute() {
        let output = """
           route to: default
        destination: default
               mask: default
            gateway: 192.168.178.1
          interface: en0
        """
        #expect(MeasurementParsing.gateway(fromRouteOutput: output) == "192.168.178.1")
        #expect(MeasurementParsing.gateway(fromRouteOutput: "    gateway: fe80::1%en0\n") == nil)
        #expect(MeasurementParsing.gateway(fromRouteOutput: "route: writing to routing socket: not in table") == nil)
    }

    @Test func summarizesSamplesWithMedian() {
        #expect(MeasurementParsing.median([]) == nil)
        #expect(MeasurementParsing.median([3, 1, 2]) == 2)
        #expect(MeasurementParsing.median([4, 1, 2, 3]) == 2.5)
        #expect(MeasurementParsing.mostCommon(["a", "b", "b", "a"]) == "a")
        #expect(MeasurementParsing.mostCommon([String]()) == nil)

        func sample(_ rssi: Int, _ noise: Int?, _ rate: Double, _ bssid: String?) -> RadioSnapshot {
            RadioSnapshot(rssi: rssi, noise: noise, txRate: rate, band: "5", channel: 44, width: 80, phy: "11ax", bssid: bssid, ssid: "Zuhause")
        }
        // Ein Ausreißer und ein kurzer Wechsel des Knotens verfälschen das Ergebnis nicht.
        let measurement = MeasurementParsing.summarize([
            sample(-61, -94, 720, "aa"), sample(-62, -94, 720, "aa"), sample(-88, nil, 6, "bb"), sample(-60, -95, 864, "aa"),
        ])
        #expect(measurement.rssiDBm == -62)   // Median −61,5, gerundet
        #expect(measurement.noiseDBm == -94)
        #expect(measurement.snrDB == 32)
        #expect(measurement.txrateMbit == 720)
        #expect(measurement.bssid == "aa")
        #expect(measurement.bandGHz == "5" && measurement.kanal == 44 && measurement.breiteMHz == 80 && measurement.phy == "11ax")
        #expect(MeasurementParsing.summarize([]) == Measurement())

        // Unplausible Rauschwerte von macOS fallen aus dem Median; überwiegen sie, gibt es keinen SNR.
        let mixed = MeasurementParsing.summarize([sample(-35, -17, 866, "aa"), sample(-35, -92, 866, "aa"), sample(-35, -90, 866, "aa")])
        #expect(mixed.noiseDBm == -91 && mixed.snrDB == 56)
        let bogus = MeasurementParsing.summarize([sample(-35, -17, 866, "aa"), sample(-36, -14, 866, "aa")])
        #expect(bogus.rssiDBm == -36 && bogus.noiseDBm == nil && bogus.snrDB == nil)
    }

    @Test func ignoresImplausibleNoiseInStoredData() {
        #expect(NoiseFloor.isPlausible(-92) && NoiseFloor.isPlausible(-75))
        #expect(!NoiseFloor.isPlausible(-74) && !NoiseFloor.isPlausible(-17) && !NoiseFloor.isPlausible(0))

        var measurement = Measurement()
        measurement.rssiDBm = -35
        measurement.noiseDBm = -17
        measurement.snrDB = -18
        #expect(Metric.snr.value(in: measurement) == nil)
        #expect(Metric.rssi.value(in: measurement) == -35)
        measurement.noiseDBm = -85
        measurement.snrDB = 50
        #expect(Metric.snr.value(in: measurement) == 50)

        #expect(Metric.snr.value(in: SurveySample(x: 0, y: 0, rssi: -69, snr: -13)) == nil)
        #expect(Metric.snr.value(in: SurveySample(x: 0, y: 0, rssi: -69, snr: 20)) == 20)
        #expect(Metric.snr.value(in: SurveySample(x: 0, y: 0, rssi: nil, snr: 20)) == 20)
    }

    @Test func formatsTimestampInLocalTime() throws {
        let berlin = try #require(TimeZone(identifier: "Europe/Berlin"))
        #expect(MeasurementParsing.timestamp(Date(timeIntervalSince1970: 1_790_414_990), timeZone: berlin) == "2026-09-26 11:29:50")
    }

    @Test func recordsRepetitionsAndKeepsHistory() {
        var project = standardProject()
        var imported = Measurement()
        imported.rssiDBm = -79
        imported.wiederholung = 2
        // Stand wie aus einer alten Datei: nur die aktuelle Messung, keine Historie.
        project.measurements["VORHER"]?["Büro Keller"] = imported

        var fresh = Measurement()
        fresh.rssiDBm = -70
        let repetition = project.record(fresh, at: "Büro Keller", phase: "VORHER")
        #expect(repetition == 3)
        #expect(project.measurement(at: "Büro Keller", in: "VORHER")?.rssiDBm == -70)
        #expect(project.history(at: "Büro Keller", in: "VORHER").map(\.wiederholung) == [2, 3])

        let first = project.record(fresh, at: "Büro OG", phase: "NACHHER")
        let second = project.record(fresh, at: "Büro OG", phase: "NACHHER")
        #expect(first == 1 && second == 2)
        #expect(project.measurement(at: "Büro OG", in: "VORHER") == nil)
    }

    @Test func guidesThroughOpenPoints() {
        var project = standardProject()
        let names = project.points.map(\.name)
        #expect(project.nextOpenPoint(in: "VORHER", after: nil)?.name == names[0])
        #expect(project.nextOpenPoint(in: "VORHER", after: names[0])?.name == names[1])

        project.record(Measurement(), at: names[1], phase: "VORHER")
        #expect(project.nextOpenPoint(in: "VORHER", after: names[0])?.name == names[2])
        #expect(project.nextOpenPoint(in: "NACHHER", after: names[0])?.name == names[1])
        // Am Listenende wieder von vorn.
        #expect(project.nextOpenPoint(in: "VORHER", after: names[9])?.name == names[0])

        for name in names where name != names[4] { project.record(Measurement(), at: name, phase: "VORHER") }
        #expect(project.openPoints(in: "VORHER").map(\.name) == [names[4]])
        #expect(project.nextOpenPoint(in: "VORHER", after: names[4]) == nil)
        #expect(project.nextOpenPoint(in: "VORHER", after: nil)?.name == names[4])
    }
}

struct WalkStorageTests {

    @Test func upsertLocatesSamplesAndReplacesEarlierState() {
        var project = standardProject()
        let samples = (0...10).map { RadioSample(t: Int64($0) * 1000, rssi: -50 - Double($0), snr: 40, rate: 500, band: "5") }
        var walk = Walk(id: "w1", phase: "VORHER", floor: "eg", started: 0, taps: [Tap(t: 2000, x: 0, y: 0)])

        // Ein einzelner Tap verortet nur das Sample genau zu diesem Zeitpunkt.
        project.upsertWalk(walk, samples: samples)
        #expect(project.walks == [walk])
        #expect(project.survey["w1"] == [SurveySample(x: 0, y: 0, rssi: -52, snr: 40, rate: 500, band: "5")])

        walk.taps.append(Tap(t: 6000, x: 1, y: 0.5))
        project.upsertWalk(walk, samples: samples)
        #expect(project.walks.count == 1)
        #expect(project.survey["w1"]?.map(\.x) == [0, 0.25, 0.5, 0.75, 1])
        #expect(project.survey["w1"]?.map(\.rssi) == [-52, -53, -54, -55, -56])

        // Letzte Position gelöscht: Die Samples dahinter fallen wieder weg.
        walk.taps.removeLast()
        project.upsertWalk(walk, samples: samples)
        #expect(project.survey["w1"]?.count == 1)

        let source = project.fieldSource(metric: .rssi, floor: "eg", phase: "VORHER")
        #expect(source.hasWalkData && source.values.count == 1)
    }

    @Test func bssidIsStoredAsOptionalSeventhElement() throws {
        var project = standardProject()
        let walk = Walk(id: "w1", phase: "NACHHER", floor: "og", started: 0, taps: [Tap(t: 0, x: 0, y: 0), Tap(t: 1000, x: 1, y: 1)])
        project.upsertWalk(walk, samples: [RadioSample(t: 0, rssi: -50, snr: 44, rate: 800, band: "6", bssid: "dc:15:c8:aa:00:02"),
                                           RadioSample(t: 1000, rssi: -61, snr: 33, rate: 400, band: "5")])
        let data = try project.encoded()
        let json = try #require(String(data: data, encoding: .utf8))
        #expect(json.contains(#"[[0,0,-50,44,800,"6","dc:15:c8:aa:00:02"],[1,1,-61,33,400,"5"]]"#))
        #expect(try Project.decode(data) == project)
    }

    @Test func removeWalkDropsItsSamples() {
        var project = standardProject()
        project.upsertWalk(Walk(id: "w1", phase: "VORHER", floor: "eg", started: 0, taps: [Tap(t: 0, x: 0, y: 0)]),
                           samples: [RadioSample(t: 0, rssi: -50)])
        project.upsertWalk(Walk(id: "w2", phase: "NACHHER", floor: "og", started: 0), samples: [])
        project.removeWalk(id: "w1")
        #expect(project.walks.map(\.id) == ["w2"])
        #expect(project.survey.keys.sorted() == ["w2"])

        let before = project
        project.removeWalk(id: "gibt-es-nicht")
        #expect(project == before)
    }
}

struct StructureTests {

    @Test func addsRenamesAndMovesFloorsAndPhases() throws {
        var project = standardProject()
        let attic = project.addFloor(named: "Dachboden")
        let garage = project.addFloor(named: "Garage")
        #expect(attic == "etage-1" && garage == "etage-2")
        project.renameFloor(id: "og", to: "1. Stock")
        project.moveFloor(id: garage, by: -10)
        project.moveFloor(id: "keller", by: 1)
        #expect(project.floors.map(\.id) == [garage, "eg", "keller", "og", "aussen", attic])
        #expect(project.floorName("og") == "1. Stock")
        #expect(project.floorName("gibt-es-nicht") == "gibt-es-nicht")

        let interim = project.addPhase(named: "Zwischenstand")
        #expect(interim == "phase-1")
        #expect(project.measurements[interim] == [:])
        project.movePhase(id: interim, by: -1)
        #expect(project.phases.map(\.name) == ["Vorher", "Zwischenstand", "Nachher"])
        project.record(Measurement(), at: "Büro OG", phase: interim)
        #expect(project.openPoints(in: interim).count == 9)

        // Listen, Namen und Reihenfolge überstehen das Speichern.
        #expect(try Project.decode(project.encoded()) == project)

        #expect(project.resolvedFloorID("og") == "og")
        #expect(project.resolvedFloorID("weg") == garage)
        #expect(project.resolvedPhaseID(nil, fallback: "NACHHER") == "NACHHER")
        #expect(project.resolvedPhaseID("weg") == "VORHER")
    }

    @Test func removingFloorTakesItsContentAlong() {
        var project = standardProject()
        project.plans["og"] = Plan(src: "data:image/jpeg;base64,AAAA", w: 10, h: 10)
        project.record(Measurement(), at: "Büro OG", phase: "VORHER")
        project.record(Measurement(), at: "Büro Keller", phase: "VORHER")
        project.upsertWalk(Walk(id: "w1", phase: "VORHER", floor: "og", started: 0, taps: [Tap(t: 0, x: 0, y: 0)]),
                           samples: [RadioSample(t: 0, rssi: -50)])
        #expect(project.floorUsage(id: "og") == .init(hasPlan: true, points: 2, measuredPoints: 1, walks: 1))
        #expect(project.floorUsage(id: project.addFloor(named: "Leer")).isEmpty)

        let removed = project.removeFloor(id: "og")
        #expect(removed)
        #expect(!project.floors.contains { $0.id == "og" })
        #expect(project.points.count == 8 && project.point(named: "Büro OG") == nil)
        #expect(project.measurements["VORHER"]?.keys.sorted() == ["Büro Keller"])
        #expect(project.history["VORHER"]?.keys.sorted() == ["Büro Keller"])
        #expect(project.walks.isEmpty && project.survey.isEmpty && project.plans.isEmpty)

        // Die letzte Etage bleibt.
        for floor in project.floors.dropFirst() { project.removeFloor(id: floor.id) }
        let last = project.removeFloor(id: project.floors[0].id)
        #expect(!last && project.floors.count == 1)
    }

    @Test func removingPhaseTakesItsMeasurementsAndWalksAlong() {
        var project = standardProject()
        project.record(Measurement(), at: "Büro OG", phase: "VORHER")
        project.record(Measurement(), at: "Büro OG", phase: "NACHHER")
        project.upsertWalk(Walk(id: "w1", phase: "VORHER", floor: "og", started: 0, taps: [Tap(t: 0, x: 0, y: 0)]),
                           samples: [RadioSample(t: 0, rssi: -50)])
        project.upsertWalk(Walk(id: "w2", phase: "NACHHER", floor: "og", started: 0, taps: [Tap(t: 0, x: 0, y: 0)]),
                           samples: [RadioSample(t: 0, rssi: -50)])
        #expect(project.phaseUsage(id: "VORHER") == .init(measuredPoints: 1, walks: 1))

        let removed = project.removePhase(id: "VORHER")
        #expect(removed)
        #expect(project.phases == [Phase(id: "NACHHER", name: "Nachher")])
        #expect(project.measurements.keys.sorted() == ["NACHHER"] && project.history.keys.sorted() == ["NACHHER"])
        #expect(project.walks.map(\.id) == ["w2"] && project.survey.keys.sorted() == ["w2"])
        #expect(project.points.count == 10)

        let last = project.removePhase(id: "NACHHER")
        #expect(!last && project.phases.count == 1)
    }

    @Test func loadingRepairsDanglingReferences() throws {
        let json = """
        { "floors": [ { "id": "a", "name": "Halle" }, { "id": "a", "name": "doppelt" } ],
          "phases": [],
          "points": [ { "name": "Tor", "floor": "b", "x": 0.5, "y": 0.5 }, { "name": "Büro" } ],
          "plans": { "og": { "src": "data:image/jpeg;base64,AAAA", "w": 4, "h": 3 } },
          "measurements": { "Winter": { "Tor": { "rssi_dbm": -60 }, "Lager": { "rssi_dbm": -70 } } },
          "walks": [ { "id": "w1", "phase": "Sommer", "taps": [] } ] }
        """
        let project = try Project.decode(Data(json.utf8))

        // Verweise auf unbekannte Etagen und Phasen bekommen einen Eintrag, bekannte Standard-IDs ihren Namen.
        #expect(project.floors == [Floor(id: "a", name: "Halle"), Floor(id: "b", name: "b"), Floor(id: "og", name: "Obergeschoss")])
        #expect(project.phases == [Phase(id: "Winter", name: "Winter"), Phase(id: "Sommer", name: "Sommer")])
        #expect(project.point(named: "Büro")?.floor == "a")
        #expect(project.point(named: "Lager")?.floor == "a")
        #expect(project.walks[0].floor == "a")
        #expect(project.measurements.keys.sorted() == ["Sommer", "Winter"])
        #expect(project.measurement(at: "Tor", in: "Winter")?.rssiDBm == -60)
    }
}

struct SummaryTests {
    private func project(_ values: [String: [Double?]]) -> Project {
        // Je Punktname: Signal in VORHER und NACHHER.
        var project = standardProject()
        for (name, pair) in values {
            for (phase, value) in zip(["VORHER", "NACHHER"], pair) {
                guard let value else { continue }
                var measurement = Measurement()
                measurement.rssiDBm = value
                project.record(measurement, at: name, phase: phase)
            }
        }
        return project
    }

    @Test func summarizesPhase() {
        let project = project(["Büro OG": [-58, -50], "Büro Keller": [-79, -65], "Schlafzimmer": [-70, nil], "Küche Esstisch": [-64, -64.5]])
        #expect(project.phaseSummary(metric: .rssi, phase: "VORHER") == PhaseSummary(median: -67, good: 1, okay: 2, critical: 1))
        #expect(project.phaseSummary(metric: .rssi, phase: "NACHHER") == PhaseSummary(median: -64.5, good: 1, okay: 2, critical: 0))
        // Messgröße ohne Werte und unbekannte Phase
        #expect(project.phaseSummary(metric: .lanDown, phase: "VORHER") == PhaseSummary())
        #expect(project.phaseSummary(metric: .rssi, phase: "gibt-es-nicht").count == 0)
    }

    @Test func countsBetterAndWorse() {
        let project = project(["Büro OG": [-58, -50], "Büro Keller": [-79, -65], "Schlafzimmer": [-70, nil],
                               "Küche Esstisch": [-64, -64.5], "Wohnzimmer Sofa": [-60, -66]])
        let forward = project.changeSummary(metric: .rssi, from: "VORHER", to: "NACHHER")
        #expect(forward == ChangeSummary(better: 2, worse: 1, unchanged: 1))
        #expect(forward.compared == 4)
        // In Gegenrichtung tauschen besser und schlechter.
        #expect(project.changeSummary(metric: .rssi, from: "NACHHER", to: "VORHER") == ChangeSummary(better: 1, worse: 2, unchanged: 1))
        #expect(project.changeSummary(metric: .rssi, from: "VORHER", to: "VORHER") == ChangeSummary(better: 0, worse: 0, unchanged: 5))
    }

    @Test func trendHasToleranceAndRespectsDirection() {
        #expect(Metric.rssi.trend(from: -70, to: -69.5) == .unchanged)
        #expect(Metric.rssi.trend(from: -70, to: -69) == .better)
        #expect(Metric.rssi.trend(from: -70, to: -72) == .worse)
        // Niedriger ist besser
        #expect(Metric.gatewayMaxPing.trend(from: 100, to: 60) == .better)
        #expect(Metric.gatewayMaxPing.trend(from: 30, to: 35) == .unchanged)
        #expect(Metric.gatewayLoss.trend(from: 0, to: 1.7) == .worse)

        #expect(Metric.rssi.formatDifference(18) == "+18 dB")
        #expect(Metric.gatewayMaxPing.formatDifference(-40.25) == "-40,2 ms" || Metric.gatewayMaxPing.formatDifference(-40.25) == "-40,3 ms")
        #expect(Metric.gatewayLoss.formatDifference(0) == "0 %")
    }
}

struct LineRateTests {

    @Test func internetDownloadIsRatedAgainstLineRate() {
        var project = Project()
        // Ohne Eingabe gelten die festen Grenzen.
        #expect(project.scale(for: .internetDown) == Metric.internetDown.scale)
        #expect(project.scale(for: .internetDown).rating(of: 90) == .okay)
        #expect(project.scale(for: .internetDown).describe(90) == "90 Mbit/s")

        project.lineDownMbit = 100
        let slow = project.scale(for: .internetDown)
        #expect(slow.good == 80 && slow.okay == 50 && slow.reference == 100)
        #expect(slow.rating(of: 90) == .good)
        #expect(slow.rating(of: 50) == .okay)
        #expect(slow.rating(of: 49) == .critical)
        #expect(slow.score(of: 80) == 1 && slow.score(of: 50) == 0.5 && slow.score(of: 20) == 0)
        #expect(slow.describe(85.4) == "85,4 Mbit/s (85 %)")

        // Dieselben 90 Mbit/s sind an einem Gigabit-Anschluss kritisch.
        project.lineDownMbit = 1000
        #expect(project.scale(for: .internetDown).rating(of: 90) == .critical)
        // Andere Messgrößen bleiben unberührt.
        #expect(project.scale(for: .rssi) == Metric.rssi.scale)
        #expect(project.scale(for: .lanDown) == Metric.lanDown.scale)
    }

    @Test func summaryAndTrendUseLineRate() {
        var project = Project()
        project.addAreas([.init(name: "EG", points: ["A", "B"])])
        for (name, values) in ["A": [70.0, 95.0], "B": [40.0, 44.0]] {
            for (phase, value) in zip(["VORHER", "NACHHER"], values) {
                var measurement = Measurement()
                measurement.inetDownMbit = value
                project.record(measurement, at: name, phase: phase)
            }
        }
        // Feste Grenzen: 70 ist okay, 40 kritisch; +4 Mbit/s liegt unter der Toleranz von 6.
        #expect(project.phaseSummary(metric: .internetDown, phase: "VORHER") == PhaseSummary(median: 55, good: 0, okay: 1, critical: 1))
        #expect(project.changeSummary(metric: .internetDown, from: "VORHER", to: "NACHHER") == ChangeSummary(better: 1, worse: 0, unchanged: 1))

        // 50-Mbit-Anschluss: gut ab 40, okay ab 25; die Toleranz schrumpft auf 1,5 Mbit/s.
        project.lineDownMbit = 50
        #expect(project.phaseSummary(metric: .internetDown, phase: "VORHER") == PhaseSummary(median: 55, good: 2, okay: 0, critical: 0))
        #expect(project.changeSummary(metric: .internetDown, from: "VORHER", to: "NACHHER") == ChangeSummary(better: 2, worse: 0, unchanged: 0))
        #expect(project.heatmapInput(metric: .internetDown, floor: "etage-1", view: .phase("VORHER")).scale.good == 40)
    }

    @Test func lineRateIsStoredAndSanitized() throws {
        var project = Project()
        project.lineDownMbit = 250
        project.lineUpMbit = 40.5
        let data = try project.encoded()
        let json = try #require(String(data: data, encoding: .utf8))
        #expect(json.contains(#""line_down_mbit":250"#) && json.contains(#""line_up_mbit":40.5"#))
        #expect(try Project.decode(data) == project)

        // Ohne Eingabe stehen die Schlüssel nicht in der Datei; unsinnige Werte werden verworfen.
        #expect(try !#require(String(data: Project().encoded(), encoding: .utf8)).contains("line_"))
        let odd = try Project.decode(Data(#"{"floors": [], "line_down_mbit": 0, "line_up_mbit": "-5"}"#.utf8))
        #expect(odd.lineDownMbit == nil && odd.lineUpMbit == nil)
        #expect(MetricScale.shareText(38, of: 40) == " (95 %)")
        #expect(MetricScale.shareText(38, of: nil) == nil && MetricScale.shareText(nil, of: 40) == nil)
    }
}
