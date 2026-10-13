//
//  Project.cs
//  DeltaWiFi
//
//  Datenmodell des Dokuments. Das JSON-Format ist dasselbe wie in der
//  macOS-App (siehe CLAUDE.md, „Datenformat“); Lesen und Schreiben stehen in ProjectJson.cs.
//

namespace DeltaWiFi.Model;

// MARK: - Etage und Phase

/// Eine Etage bzw. Ebene des Projekts. Punkte, Grundrisse und Läufe verweisen über die `Id` darauf;
/// der Name ist frei wählbar.
public sealed record Floor(string Id, string Name)
{
    public static readonly IReadOnlyList<Floor> Defaults =
    [
        new("keller", "Keller"),
        new("eg", "Erdgeschoss"),
        new("og", "Obergeschoss"),
        new("aussen", "Außen"),
    ];
}

/// Eine Messphase (z. B. vor und nach dem Umbau). Messungen und Läufe verweisen über die `Id` darauf.
public sealed record Phase(string Id, string Name)
{
    public static readonly IReadOnlyList<Phase> Defaults =
    [
        new("VORHER", "Vorher"),
        new("NACHHER", "Nachher"),
    ];
}

// MARK: - Grundriss, Punkt, Messung

/// Grundriss einer Etage; `Src` ist das Bild als Data-URL (`data:image/jpeg;base64,…`).
public sealed record Plan(string Src, int W, int H);

/// Ein fester Messpunkt. `Floor` ist die ID der Etage, die Position relativ 0…1 (`null` = noch nicht gesetzt).
public sealed record MeasurePoint(string Name, string Floor, double? X = null, double? Y = null)
{
    public bool IsPlaced => X != null && Y != null;
}

/// Eine Messung an einem festen Punkt. Alle Werte sind optional, weil Messungen
/// lückenhaft sein können (kein iperf3, Speedtest abgeschaltet …).
public sealed record Measurement
{
    public string? Zeit { get; init; }
    public int? Wiederholung { get; init; }
    public string? Ssid { get; init; }
    public string? Bssid { get; init; }
    public string? BandGHz { get; init; }
    public int? Kanal { get; init; }
    public int? BreiteMHz { get; init; }
    public string? Phy { get; init; }
    public int? Nss { get; init; }
    public double? RssiDBm { get; init; }
    public double? NoiseDBm { get; init; }
    public double? SnrDB { get; init; }
    public double? TxrateMbit { get; init; }
    public double? PingGwVerlustPct { get; init; }
    public double? PingGwAvgMs { get; init; }
    public double? PingGwMaxMs { get; init; }
    public double? PingInetVerlustPct { get; init; }
    public double? PingInetAvgMs { get; init; }
    public double? PingInetMaxMs { get; init; }
    public double? InetDownMbit { get; init; }
    public double? InetUpMbit { get; init; }
    public double? LanDownMbit { get; init; }
    public double? LanUpMbit { get; init; }
    public string? Notiz { get; init; }
}

// MARK: - Läufe

/// Angeklickte Position eines Laufs; `T` in ms seit Epoch.
public sealed record Tap(long T, double X, double Y);

/// Ein Lauf (Heatmap-Begehung). `Phase` und `Floor` sind IDs, `Started` in ms seit Epoch.
public sealed record Walk(string Id, string Phase, string Floor, long Started, IReadOnlyList<Tap> Taps)
{
    public Walk(string id, string phase, string floor, long started) : this(id, phase, floor, started, []) { }
}

/// Ein verortetes Lauf-Sample, im JSON als Array `[x, y, rssi, snr, rate, band, bssid]`.
/// Die BSSID fehlt, wenn sie unbekannt ist.
public sealed record SurveySample(double X, double Y, double? Rssi = null, double? Snr = null, double? Rate = null,
                                  string? Band = null, string? Bssid = null);

// MARK: - Projekt

public sealed partial class Project
{
    public int Version { get; set; } = 1;
    /// Etagen bzw. Bereiche in Anzeigereihenfolge; vom Nutzer verwaltet. Ein neues Projekt hat noch keine.
    public List<Floor> Floors { get; set; } = [];
    /// Phasen in Anzeigereihenfolge; vom Nutzer verwaltet, nie leer.
    public List<Phase> Phases { get; set; } = [.. Phase.Defaults];
    /// Synchronisierte Rate des Internetanschlusses in Mbit/s; optional, für die Bewertung der Speedtests.
    public double? LineDownMbit { get; set; }
    public double? LineUpMbit { get; set; }
    /// Grundriss je Etage (Schlüssel = `Floor.Id`).
    public Dictionary<string, Plan> Plans { get; set; } = [];
    public List<MeasurePoint> Points { get; set; } = [];
    /// Phase → Punktname → aktuell gültige (letzte) Messung.
    public Dictionary<string, Dictionary<string, Measurement>> Measurements { get; set; } =
        Phase.Defaults.ToDictionary(phase => phase.Id, _ => new Dictionary<string, Measurement>());
    /// Phase → Punktname → alle Wiederholungen, aufsteigend.
    public Dictionary<string, Dictionary<string, List<Measurement>>> History { get; set; } =
        Phase.Defaults.ToDictionary(phase => phase.Id, _ => new Dictionary<string, List<Measurement>>());
    public List<Walk> Walks { get; set; } = [];
    /// Lauf-ID → verortete Samples.
    public Dictionary<string, IReadOnlyList<SurveySample>> Survey { get; set; } = [];

    /// Unabhängige Kopie, z. B. als Stand für „Rückgängig“. Die Einträge selbst sind unveränderlich und werden geteilt.
    public Project Clone() => new()
    {
        Version = Version,
        Floors = [.. Floors],
        Phases = [.. Phases],
        LineDownMbit = LineDownMbit,
        LineUpMbit = LineUpMbit,
        Plans = new(Plans),
        Points = [.. Points],
        Measurements = Measurements.ToDictionary(phase => phase.Key, phase => new Dictionary<string, Measurement>(phase.Value)),
        History = History.ToDictionary(phase => phase.Key,
                                       phase => phase.Value.ToDictionary(point => point.Key, point => new List<Measurement>(point.Value))),
        Walks = [.. Walks],
        Survey = new(Survey),
    };

    /// Stellt die Grundannahmen her: Etagen und Phasen sind eindeutig, Phasen nie leer, und alles,
    /// worauf Punkte, Grundrisse, Messungen und Läufe verweisen, existiert auch.
    public void Normalize()
    {
        var seen = new HashSet<string>();
        Floors = Floors.Where(floor => floor.Id.Length > 0 && seen.Add(floor.Id)).ToList();
        seen = [];
        Phases = Phases.Where(phase => phase.Id.Length > 0 && seen.Add(phase.Id)).ToList();

        // Verwaiste Verweise bekommen einen Eintrag, damit keine Daten unsichtbar werden.
        var floorIDs = Floors.Select(floor => floor.Id).ToHashSet();
        var usedFloors = Points.Select(point => point.Floor).Concat(Walks.Select(walk => walk.Floor)).Concat(Sorted(Plans.Keys));
        foreach (var id in usedFloors.ToList())
        {
            if (id.Length == 0 || !floorIDs.Add(id)) continue;
            Floors.Add(Floor.Defaults.FirstOrDefault(floor => floor.Id == id) ?? new Floor(id, id));
        }

        var phaseIDs = Phases.Select(phase => phase.Id).ToHashSet();
        var usedPhases = Sorted(Measurements.Keys).Concat(Sorted(History.Keys)).Concat(Walks.Select(walk => walk.Phase));
        foreach (var id in usedPhases.ToList())
        {
            if (id.Length == 0 || !phaseIDs.Add(id)) continue;
            Phases.Add(Phase.Defaults.FirstOrDefault(phase => phase.Id == id) ?? new Phase(id, id));
        }
        if (Phases.Count == 0) Phases = [.. Phase.Defaults];

        // Punkte und Läufe ohne Angabe landen auf der ersten Etage bzw. in der ersten Phase.
        var firstFloor = Floors.FirstOrDefault()?.Id ?? "";
        for (var index = 0; index < Points.Count; index++)
        {
            if (Points[index].Floor.Length == 0) Points[index] = Points[index] with { Floor = firstFloor };
        }
        for (var index = 0; index < Walks.Count; index++)
        {
            if (Walks[index].Floor.Length == 0) Walks[index] = Walks[index] with { Floor = firstFloor };
            if (Walks[index].Phase.Length == 0) Walks[index] = Walks[index] with { Phase = Phases[0].Id };
        }
        foreach (var phase in Phases)
        {
            Measurements.TryAdd(phase.Id, []);
            History.TryAdd(phase.Id, []);
        }
        EnsurePointsForMeasurements();
    }

    internal static IEnumerable<string> Sorted(IEnumerable<string> keys) => keys.Order(StringComparer.Ordinal);

    // MARK: Zugriff

    public string FloorName(string id) => Floors.FirstOrDefault(floor => floor.Id == id)?.Name ?? id;

    public string PhaseName(string id) => Phases.FirstOrDefault(phase => phase.Id == id)?.Name ?? id;

    /// Die gewünschte Etage, falls es sie (noch) gibt, sonst die erste.
    public string ResolvedFloorID(string? id) =>
        Floors.FirstOrDefault(floor => floor.Id == id)?.Id ?? Floors.FirstOrDefault()?.Id ?? "";

    /// Die gewünschte Phase, falls es sie (noch) gibt, sonst `fallback` bzw. die erste.
    public string ResolvedPhaseID(string? id, string? fallback = null) =>
        Phases.FirstOrDefault(phase => phase.Id == id)?.Id ?? fallback ?? Phases.FirstOrDefault()?.Id ?? "";

    public Measurement? Measurement(string point, string phase) =>
        Measurements.TryGetValue(phase, out var byPoint) && byPoint.TryGetValue(point, out var measurement) ? measurement : null;

    public IReadOnlyList<Measurement> HistoryAt(string point, string phase) =>
        History.TryGetValue(phase, out var byPoint) && byPoint.TryGetValue(point, out var all) ? all : [];

    public MeasurePoint? Point(string? name) => Points.FirstOrDefault(point => point.Name == name);

    /// Legt für Messungen ohne Punkt einen Punkt auf der ersten Etage an („nicht gesetzt“).
    public List<string> EnsurePointsForMeasurements()
    {
        var known = Points.Select(point => point.Name).ToHashSet();
        var added = new List<string>();
        var names = Phases.SelectMany(phase => Measurements.TryGetValue(phase.Id, out var byPoint) ? Sorted(byPoint.Keys) : []);
        foreach (var name in names.ToList())
        {
            if (!known.Add(name)) continue;
            Points.Add(new MeasurePoint(name, Floors.FirstOrDefault()?.Id ?? ""));
            added.Add(name);
        }
        return added;
    }
}
