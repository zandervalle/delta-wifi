using System.Text;
using DeltaWiFi.Logic;
using DeltaWiFi.Model;

namespace DeltaWiFi.Tests;

internal static class Fixtures
{
    /// Ausgangslage der meisten Tests: vier Etagen mit zehn Messpunkten.
    public static readonly MeasurePoint[] StandardPoints =
    [
        new("Flur UG (Referenz nahe Router)", "keller"),
        new("Büro Keller", "keller"),
        new("Wohnzimmer Sofa", "eg"),
        new("Küche Esstisch", "eg"),
        new("Büro OG", "og"),
        new("Schlafzimmer", "og"),
        new("Terrasse (außen)", "aussen"),
        new("Terrasse unten (außen)", "aussen"),
        new("Gewächshaus (außen)", "aussen"),
        new("Einfahrt E-Ladeplatz (außen)", "aussen"),
    ];

    public static Project StandardProject() => new() { Floors = [.. Floor.Defaults], Points = [.. StandardPoints] };

    public static Project Decode(string json) => Project.Decode(Encoding.UTF8.GetBytes(json));
}

public class NumberParsingTests
{
    [Fact]
    public void ParsesNumbersLeniently()
    {
        Assert.Equal(1.7, Flex.ParseNumber(" 1,7 "));
        Assert.Equal(-79, Flex.ParseNumber("-79"));
        Assert.Null(Flex.ParseNumber(""));
        Assert.Null(Flex.ParseNumber("n/a"));
        Assert.Null(Flex.ParseNumber("nan"));
        Assert.Equal("5", Flex.FormatNumber(5));
        Assert.Equal("2.4", Flex.FormatNumber(2.4));
    }
}

public class ProjectFormatTests
{
    // Datei im alten Format ohne Etagen- und Phasenliste, Zahlen teils als Text: Das Lesen bleibt nachsichtig.
    private const string WebJSON = """
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
        """;

    [Fact]
    public void ReadsOlderProjectFile()
    {
        var project = Project.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(WebJSON)]);

        Assert.Equal(new Plan("data:image/jpeg;base64,AAAA/BBB", 1600, 1120), project.Plans["eg"]);
        Assert.Equal(["Büro Keller", "Küche Esstisch", "Carport"], project.Points.Select(point => point.Name));
        Assert.Equal(0.28, project.Points[0].X);
        Assert.False(project.Points[1].IsPlaced);
        // Ohne Listen in der Datei gelten die Standardetagen und -phasen; Punkte ohne Etage landen auf der ersten.
        Assert.Equal(Floor.Defaults, project.Floors);
        Assert.Equal(Phase.Defaults, project.Phases);
        Assert.Equal("keller", project.Point("Carport")?.Floor);

        var measurement = project.Measurement("Büro Keller", "VORHER");
        Assert.NotNull(measurement);
        Assert.Equal(-79, measurement.RssiDBm);
        Assert.Equal(6, measurement.TxrateMbit);
        Assert.Equal("2.4", measurement.BandGHz);
        Assert.Equal(310, measurement.PingGwMaxMs);
        Assert.Null(measurement.LanDownMbit);
        Assert.Null(measurement.Bssid);
        Assert.Equal(1, measurement.Wiederholung);
        Assert.Empty(project.Measurements["NACHHER"]);

        var walk = Assert.Single(project.Walks);
        Assert.Equal(("w1", "VORHER", "eg", 1_791_745_000_000), (walk.Id, walk.Phase, walk.Floor, walk.Started));
        Assert.Equal([new Tap(1_791_745_003_000, 0.5, 0.56)], walk.Taps);
        Assert.Equal([new SurveySample(0.5, 0.56, -52, 40, 744, "5"), new SurveySample(0.6, 0.5, -60, null, 300, "2.4")],
                     project.Survey["w1"]);
    }

    [Fact]
    public void RoundTripKeepsCompatibleFieldNames()
    {
        var project = Fixtures.Decode(WebJSON);
        var json = project.ToJson();
        Assert.Equal(json, Fixtures.Decode(json).ToJson());

        Assert.Contains("""
            "survey":{"w1":[[0.5,0.56,-52,40,744,"5"],[0.6,0.5,-60,null,300,"2.4"]]}
            """.Trim(), json);
        Assert.Contains("""{"floor":"eg","name":"Küche Esstisch","x":null,"y":null}""", json);
        Assert.Contains("\"started\":1791745000000", json);
        Assert.Contains("\"rssi_dbm\":-79", json);
        Assert.Contains("data:image/jpeg;base64,AAAA/BBB", json);
        Assert.Contains("\"NACHHER\":{}", json);
    }

    [Fact]
    public void NewProjectStartsEmptyWithStandardPhases()
    {
        var project = new Project();
        Assert.Empty(project.Floors);
        Assert.Empty(project.Points);
        Assert.Equal(Phase.Defaults, project.Phases);
        Assert.Equal(["NACHHER", "VORHER"], project.Measurements.Keys.Order());
        // Auch der leere Zustand übersteht das Speichern; es kommen keine Standardetagen dazu.
        var reloaded = Project.Decode(project.Encoded());
        Assert.Equal(project.ToJson(), reloaded.ToJson());
        Assert.Empty(reloaded.Floors);
        Assert.Equal("", project.ResolvedFloorID("eg"));
    }

    [Fact]
    public void SetupCreatesAreasWithPoints()
    {
        var project = new Project();
        project.AddAreas([
            new(" Erdgeschoss ", ["Wohnzimmer Sofa", "", "Küche"]),
            new("", ["verwaist"]),
            new("Garten", ["Terrasse", "Küche"]),
        ]);
        Assert.Equal([new Floor("etage-1", "Erdgeschoss"), new Floor("etage-2", "Garten")], project.Floors);
        // Leere und doppelte Punktnamen sowie Bereiche ohne Namen werden übergangen.
        Assert.Equal([new MeasurePoint("Wohnzimmer Sofa", "etage-1"), new MeasurePoint("Küche", "etage-1"), new MeasurePoint("Terrasse", "etage-2")],
                     project.Points);
        Assert.Equal("Wohnzimmer Sofa", project.NextOpenPoint("VORHER", null)?.Name);
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var project = Fixtures.StandardProject();
        project.Record(new Measurement { RssiDBm = -60 }, "Büro OG", "VORHER");
        var copy = project.Clone();
        Assert.Equal(project.ToJson(), copy.ToJson());

        project.Record(new Measurement { RssiDBm = -70 }, "Büro OG", "VORHER");
        project.SetPosition("Büro OG", 0.5, 0.5);
        project.RemoveFloor("aussen");
        Assert.Equal(-60, copy.Measurement("Büro OG", "VORHER")?.RssiDBm);
        Assert.Single(copy.HistoryAt("Büro OG", "VORHER"));
        Assert.False(copy.Point("Büro OG")!.IsPlaced);
        Assert.Equal(4, copy.Floors.Count);
    }
}

public class PointEditingTests
{
    [Fact]
    public void PositionsAreClampedAndClearedOnFloorChange()
    {
        var project = Fixtures.StandardProject();
        project.SetPosition("Büro OG", 1.4, -0.2);
        Assert.Equal(1, project.Point("Büro OG")?.X);
        Assert.Equal(0, project.Point("Büro OG")?.Y);

        project.SetFloor("og", "Büro OG");
        Assert.True(project.Point("Büro OG")?.IsPlaced);
        project.SetFloor("eg", "Büro OG");
        var moved = project.Point("Büro OG")!;
        Assert.Equal("eg", moved.Floor);
        Assert.False(moved.IsPlaced);
    }

    [Fact]
    public void AddsAndRemovesPoints()
    {
        var project = Fixtures.StandardProject();
        Assert.True(project.AddPoint("  Gästezimmer ", "og"));
        Assert.Equal("og", project.Point("Gästezimmer")?.Floor);
        Assert.False(project.AddPoint("Gästezimmer", "eg"));
        Assert.False(project.AddPoint("   ", "eg"));

        project.Measurements["NACHHER"]["Gästezimmer"] = new Measurement();
        Assert.False(project.RemovePoint("Gästezimmer"));
        project.Measurements["NACHHER"].Remove("Gästezimmer");
        Assert.True(project.RemovePoint("Gästezimmer"));
        Assert.Equal(Fixtures.StandardPoints, project.Points);
    }

    [Fact]
    public void RecordsRepetitionsAndKeepsHistory()
    {
        var project = Fixtures.StandardProject();
        // Stand wie aus einer alten Datei: nur die aktuelle Messung, keine Historie.
        project.Measurements["VORHER"]["Büro Keller"] = new Measurement { RssiDBm = -79, Wiederholung = 2 };

        var fresh = new Measurement { RssiDBm = -70 };
        Assert.Equal(3, project.Record(fresh, "Büro Keller", "VORHER"));
        Assert.Equal(-70, project.Measurement("Büro Keller", "VORHER")?.RssiDBm);
        Assert.Equal([2, 3], project.HistoryAt("Büro Keller", "VORHER").Select(entry => entry.Wiederholung));

        Assert.Equal(1, project.Record(fresh, "Büro OG", "NACHHER"));
        Assert.Equal(2, project.Record(fresh, "Büro OG", "NACHHER"));
        Assert.Null(project.Measurement("Büro OG", "VORHER"));
    }

    [Fact]
    public void GuidesThroughOpenPoints()
    {
        var project = Fixtures.StandardProject();
        var names = project.Points.Select(point => point.Name).ToList();
        Assert.Equal(names[0], project.NextOpenPoint("VORHER", null)?.Name);
        Assert.Equal(names[1], project.NextOpenPoint("VORHER", names[0])?.Name);

        project.Record(new Measurement(), names[1], "VORHER");
        Assert.Equal(names[2], project.NextOpenPoint("VORHER", names[0])?.Name);
        Assert.Equal(names[1], project.NextOpenPoint("NACHHER", names[0])?.Name);
        // Am Listenende wieder von vorn.
        Assert.Equal(names[0], project.NextOpenPoint("VORHER", names[9])?.Name);

        foreach (var name in names.Where(name => name != names[4])) project.Record(new Measurement(), name, "VORHER");
        Assert.Equal([names[4]], project.OpenPoints("VORHER").Select(point => point.Name));
        Assert.Null(project.NextOpenPoint("VORHER", names[4]));
        Assert.Equal(names[4], project.NextOpenPoint("VORHER", null)?.Name);
    }
}

public class WalkStorageTests
{
    [Fact]
    public void UpsertLocatesSamplesAndReplacesEarlierState()
    {
        var project = Fixtures.StandardProject();
        var samples = Enumerable.Range(0, 11).Select(index => new RadioSample(index * 1000L, -50 - index, 40, 500, "5")).ToList();
        var walk = new Walk("w1", "VORHER", "eg", 0, [new Tap(2000, 0, 0)]);

        // Ein einzelner Tap verortet nur das Sample genau zu diesem Zeitpunkt.
        project.UpsertWalk(walk, samples);
        Assert.Same(walk, Assert.Single(project.Walks));
        Assert.Equal([new SurveySample(0, 0, -52, 40, 500, "5")], project.Survey["w1"]);

        walk = walk with { Taps = [.. walk.Taps, new Tap(6000, 1, 0.5)] };
        project.UpsertWalk(walk, samples);
        Assert.Single(project.Walks);
        Assert.Equal([0, 0.25, 0.5, 0.75, 1], project.Survey["w1"].Select(sample => sample.X));
        Assert.Equal([-52, -53, -54, -55, -56], project.Survey["w1"].Select(sample => sample.Rssi));

        // Letzte Position gelöscht: Die Samples dahinter fallen wieder weg.
        project.UpsertWalk(walk with { Taps = [walk.Taps[0]] }, samples);
        Assert.Single(project.Survey["w1"]);

        var source = project.FieldSource(Metric.Rssi, "eg", "VORHER");
        Assert.True(source.HasWalkData);
        Assert.Single(source.Values);
    }

    [Fact]
    public void BssidIsStoredAsOptionalSeventhElement()
    {
        var project = Fixtures.StandardProject();
        var walk = new Walk("w1", "NACHHER", "og", 0, [new Tap(0, 0, 0), new Tap(1000, 1, 1)]);
        project.UpsertWalk(walk, [new RadioSample(0, -50, 44, 800, "6", "dc:15:c8:aa:00:02"), new RadioSample(1000, -61, 33, 400, "5")]);
        var json = project.ToJson();
        Assert.Contains("""[[0,0,-50,44,800,"6","dc:15:c8:aa:00:02"],[1,1,-61,33,400,"5"]]""", json);
        Assert.Equal(json, Fixtures.Decode(json).ToJson());
    }

    [Fact]
    public void RemoveWalkDropsItsSamples()
    {
        var project = Fixtures.StandardProject();
        project.UpsertWalk(new Walk("w1", "VORHER", "eg", 0, [new Tap(0, 0, 0)]), [new RadioSample(0, -50)]);
        project.UpsertWalk(new Walk("w2", "NACHHER", "og", 0), []);
        project.RemoveWalk("w1");
        Assert.Equal(["w2"], project.Walks.Select(walk => walk.Id));
        Assert.Equal(["w2"], project.Survey.Keys);

        var before = project.ToJson();
        project.RemoveWalk("gibt-es-nicht");
        Assert.Equal(before, project.ToJson());
    }
}

public class StructureTests
{
    [Fact]
    public void AddsRenamesAndMovesFloorsAndPhases()
    {
        var project = Fixtures.StandardProject();
        var attic = project.AddFloor("Dachboden");
        var garage = project.AddFloor("Garage");
        Assert.Equal(("etage-1", "etage-2"), (attic, garage));
        project.RenameFloor("og", "1. Stock");
        project.MoveFloor(garage, -10);
        project.MoveFloor("keller", 1);
        Assert.Equal([garage, "eg", "keller", "og", "aussen", attic], project.Floors.Select(floor => floor.Id));
        Assert.Equal("1. Stock", project.FloorName("og"));
        Assert.Equal("gibt-es-nicht", project.FloorName("gibt-es-nicht"));

        var interim = project.AddPhase("Zwischenstand");
        Assert.Equal("phase-1", interim);
        Assert.Empty(project.Measurements[interim]);
        project.MovePhase(interim, -1);
        Assert.Equal(["Vorher", "Zwischenstand", "Nachher"], project.Phases.Select(phase => phase.Name));
        project.Record(new Measurement(), "Büro OG", interim);
        Assert.Equal(9, project.OpenPoints(interim).Count);

        // Listen, Namen und Reihenfolge überstehen das Speichern.
        Assert.Equal(project.ToJson(), Project.Decode(project.Encoded()).ToJson());

        Assert.Equal("og", project.ResolvedFloorID("og"));
        Assert.Equal(garage, project.ResolvedFloorID("weg"));
        Assert.Equal("NACHHER", project.ResolvedPhaseID(null, "NACHHER"));
        Assert.Equal("VORHER", project.ResolvedPhaseID("weg"));
    }

    [Fact]
    public void RemovingFloorTakesItsContentAlong()
    {
        var project = Fixtures.StandardProject();
        project.Plans["og"] = new Plan("data:image/jpeg;base64,AAAA", 10, 10);
        project.Record(new Measurement(), "Büro OG", "VORHER");
        project.Record(new Measurement(), "Büro Keller", "VORHER");
        project.UpsertWalk(new Walk("w1", "VORHER", "og", 0, [new Tap(0, 0, 0)]), [new RadioSample(0, -50)]);
        Assert.Equal(new FloorUsage(true, 2, 1, 1), project.FloorUsage("og"));
        Assert.True(project.FloorUsage(project.AddFloor("Leer")).IsEmpty);

        Assert.True(project.RemoveFloor("og"));
        Assert.DoesNotContain(project.Floors, floor => floor.Id == "og");
        Assert.Equal(8, project.Points.Count);
        Assert.Null(project.Point("Büro OG"));
        Assert.Equal(["Büro Keller"], project.Measurements["VORHER"].Keys);
        Assert.Equal(["Büro Keller"], project.History["VORHER"].Keys);
        Assert.Empty(project.Walks);
        Assert.Empty(project.Survey);
        Assert.Empty(project.Plans);

        // Die letzte Etage bleibt.
        foreach (var floor in project.Floors.Skip(1).ToList()) project.RemoveFloor(floor.Id);
        Assert.False(project.RemoveFloor(project.Floors[0].Id));
        Assert.Single(project.Floors);
    }

    [Fact]
    public void RemovingPhaseTakesItsMeasurementsAndWalksAlong()
    {
        var project = Fixtures.StandardProject();
        project.Record(new Measurement(), "Büro OG", "VORHER");
        project.Record(new Measurement(), "Büro OG", "NACHHER");
        project.UpsertWalk(new Walk("w1", "VORHER", "og", 0, [new Tap(0, 0, 0)]), [new RadioSample(0, -50)]);
        project.UpsertWalk(new Walk("w2", "NACHHER", "og", 0, [new Tap(0, 0, 0)]), [new RadioSample(0, -50)]);
        Assert.Equal(new PhaseUsage(1, 1), project.PhaseUsage("VORHER"));

        Assert.True(project.RemovePhase("VORHER"));
        Assert.Equal([new Phase("NACHHER", "Nachher")], project.Phases);
        Assert.Equal(["NACHHER"], project.Measurements.Keys);
        Assert.Equal(["NACHHER"], project.History.Keys);
        Assert.Equal(["w2"], project.Walks.Select(walk => walk.Id));
        Assert.Equal(["w2"], project.Survey.Keys);
        Assert.Equal(10, project.Points.Count);

        Assert.False(project.RemovePhase("NACHHER"));
        Assert.Single(project.Phases);
    }

    [Fact]
    public void LoadingRepairsDanglingReferences()
    {
        var project = Fixtures.Decode("""
            { "floors": [ { "id": "a", "name": "Halle" }, { "id": "a", "name": "doppelt" } ],
              "phases": [],
              "points": [ { "name": "Tor", "floor": "b", "x": 0.5, "y": 0.5 }, { "name": "Büro" } ],
              "plans": { "og": { "src": "data:image/jpeg;base64,AAAA", "w": 4, "h": 3 } },
              "measurements": { "Winter": { "Tor": { "rssi_dbm": -60 }, "Lager": { "rssi_dbm": -70 } } },
              "walks": [ { "id": "w1", "phase": "Sommer", "taps": [] } ] }
            """);

        // Verweise auf unbekannte Etagen und Phasen bekommen einen Eintrag, bekannte Standard-IDs ihren Namen.
        Assert.Equal([new Floor("a", "Halle"), new Floor("b", "b"), new Floor("og", "Obergeschoss")], project.Floors);
        Assert.Equal([new Phase("Winter", "Winter"), new Phase("Sommer", "Sommer")], project.Phases);
        Assert.Equal("a", project.Point("Büro")?.Floor);
        Assert.Equal("a", project.Point("Lager")?.Floor);
        Assert.Equal("a", project.Walks[0].Floor);
        Assert.Equal(["Sommer", "Winter"], project.Measurements.Keys.Order());
        Assert.Equal(-60, project.Measurement("Tor", "Winter")?.RssiDBm);
    }
}

public class LineRateTests
{
    [Fact]
    public void InternetDownloadIsRatedAgainstLineRate()
    {
        var project = new Project();
        // Ohne Eingabe gelten die festen Grenzen.
        Assert.Equal(Metric.InternetDown.Scale(), project.Scale(Metric.InternetDown));
        Assert.Equal(Rating.Okay, project.Scale(Metric.InternetDown).Rating(90));
        Assert.Equal("90 Mbit/s", project.Scale(Metric.InternetDown).Describe(90));

        project.LineDownMbit = 100;
        var slow = project.Scale(Metric.InternetDown);
        Assert.Equal((80, 50, 100), (slow.Good, slow.Okay, slow.Reference));
        Assert.Equal(Rating.Good, slow.Rating(90));
        Assert.Equal(Rating.Okay, slow.Rating(50));
        Assert.Equal(Rating.Critical, slow.Rating(49));
        Assert.Equal((1, 0.5, 0), (slow.Score(80), slow.Score(50), slow.Score(20)));
        Assert.Equal("85,4 Mbit/s (85 %)", slow.Describe(85.4));

        // Dieselben 90 Mbit/s sind an einem Gigabit-Anschluss kritisch.
        project.LineDownMbit = 1000;
        Assert.Equal(Rating.Critical, project.Scale(Metric.InternetDown).Rating(90));
        Assert.Equal("1.000 Mbit/s (100 %)", project.Scale(Metric.InternetDown).Describe(1000));
        // Andere Messgrößen bleiben unberührt.
        Assert.Equal(Metric.Rssi.Scale(), project.Scale(Metric.Rssi));
        Assert.Equal(Metric.LanDown.Scale(), project.Scale(Metric.LanDown));
    }

    [Fact]
    public void SummaryAndTrendUseLineRate()
    {
        var project = new Project();
        project.AddAreas([new("EG", ["A", "B"])]);
        foreach (var (name, values) in new[] { ("A", new[] { 70.0, 95.0 }), ("B", new[] { 40.0, 44.0 }) })
        {
            project.Record(new Measurement { InetDownMbit = values[0] }, name, "VORHER");
            project.Record(new Measurement { InetDownMbit = values[1] }, name, "NACHHER");
        }
        // Feste Grenzen: 70 ist okay, 40 kritisch; +4 Mbit/s liegt unter der Toleranz von 6.
        Assert.Equal(new PhaseSummary(55, 0, 1, 1), project.PhaseSummary(Metric.InternetDown, "VORHER"));
        Assert.Equal(new ChangeSummary(1, 0, 1), project.ChangeSummary(Metric.InternetDown, "VORHER", "NACHHER"));

        // 50-Mbit-Anschluss: gut ab 40, okay ab 25; die Toleranz schrumpft auf 1,5 Mbit/s.
        project.LineDownMbit = 50;
        Assert.Equal(new PhaseSummary(55, 2, 0, 0), project.PhaseSummary(Metric.InternetDown, "VORHER"));
        Assert.Equal(new ChangeSummary(2, 0, 0), project.ChangeSummary(Metric.InternetDown, "VORHER", "NACHHER"));
        Assert.Equal(40, project.HeatmapInput(Metric.InternetDown, "etage-1", new HeatmapView("VORHER")).Scale.Good);
    }

    [Fact]
    public void LineRateIsStoredAndSanitized()
    {
        var project = new Project { LineDownMbit = 250, LineUpMbit = 40.5 };
        var json = project.ToJson();
        Assert.Contains("\"line_down_mbit\":250", json);
        Assert.Contains("\"line_up_mbit\":40.5", json);
        Assert.Equal(json, Fixtures.Decode(json).ToJson());

        // Ohne Eingabe stehen die Schlüssel nicht in der Datei; unsinnige Werte werden verworfen.
        Assert.DoesNotContain("line_", new Project().ToJson());
        var odd = Fixtures.Decode("""{"floors": [], "line_down_mbit": 0, "line_up_mbit": "-5"}""");
        Assert.Null(odd.LineDownMbit);
        Assert.Null(odd.LineUpMbit);
        Assert.Equal(" (95 %)", MetricScale.ShareText(38, 40));
        Assert.Null(MetricScale.ShareText(38, null));
        Assert.Null(MetricScale.ShareText(null, 40));
    }
}
