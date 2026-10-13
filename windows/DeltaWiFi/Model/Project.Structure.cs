//
//  Project.Structure.cs
//  DeltaWiFi
//
//  Etagen und Phasen verwalten: anlegen, umbenennen, umsortieren, löschen.
//

namespace DeltaWiFi.Model;

/// Was an einer Etage hängt und beim Löschen mit entfernt wird.
public sealed record FloorUsage(bool HasPlan = false, int Points = 0, int MeasuredPoints = 0, int Walks = 0)
{
    public bool IsEmpty => !HasPlan && Points == 0 && Walks == 0;
}

/// Was an einer Phase hängt und beim Löschen mit entfernt wird.
public sealed record PhaseUsage(int MeasuredPoints = 0, int Walks = 0)
{
    public bool IsEmpty => MeasuredPoints == 0 && Walks == 0;
}

/// Ein Bereich mit seinen Messpunkten, wie er beim Einrichten eines Projekts abgefragt wird.
public sealed record SetupArea(string Name, IReadOnlyList<string> Points);

public sealed partial class Project
{
    // MARK: Einrichtung

    /// Legt Bereiche samt Messpunkten an. Bereiche ohne Namen, leere und doppelte Punktnamen werden übergangen.
    public void AddAreas(IEnumerable<SetupArea> areas)
    {
        foreach (var area in areas)
        {
            var name = area.Name.Trim();
            if (name.Length == 0) continue;
            var floor = AddFloor(name);
            foreach (var point in area.Points) AddPoint(point, floor);
        }
    }

    // MARK: Etagen

    public string AddFloor(string name)
    {
        var id = UnusedID("etage", Floors.Select(floor => floor.Id));
        Floors.Add(new Floor(id, name));
        // Punkte, die es schon vor der ersten Etage gab, gehören ab jetzt zu ihr.
        for (var index = 0; index < Points.Count; index++)
        {
            if (Points[index].Floor.Length == 0) Points[index] = Points[index] with { Floor = id };
        }
        return id;
    }

    public void RenameFloor(string id, string name)
    {
        var index = Floors.FindIndex(floor => floor.Id == id);
        if (index >= 0) Floors[index] = Floors[index] with { Name = name };
    }

    /// Verschiebt eine Etage um `offset` Plätze in der Anzeigereihenfolge.
    public void MoveFloor(string id, int offset) => Move(Floors, Floors.FindIndex(floor => floor.Id == id), offset);

    public FloorUsage FloorUsage(string id)
    {
        var onFloor = Points.Where(point => point.Floor == id).ToList();
        return new FloorUsage(Plans.ContainsKey(id), onFloor.Count, onFloor.Count(point => HasMeasurements(point.Name)),
                              Walks.Count(walk => walk.Floor == id));
    }

    /// Löscht eine Etage samt Grundriss, Punkten (mit ihren Messungen) und Läufen. Die letzte Etage bleibt.
    public bool RemoveFloor(string id)
    {
        if (Floors.Count <= 1 || Floors.All(floor => floor.Id != id)) return false;
        foreach (var point in Points.Where(point => point.Floor == id))
        {
            foreach (var phase in Phases)
            {
                if (Measurements.TryGetValue(phase.Id, out var measurements)) measurements.Remove(point.Name);
                if (History.TryGetValue(phase.Id, out var history)) history.Remove(point.Name);
            }
        }
        Points.RemoveAll(point => point.Floor == id);
        foreach (var walk in Walks.Where(walk => walk.Floor == id)) Survey.Remove(walk.Id);
        Walks.RemoveAll(walk => walk.Floor == id);
        Plans.Remove(id);
        Floors.RemoveAll(floor => floor.Id == id);
        return true;
    }

    // MARK: Phasen

    public string AddPhase(string name)
    {
        var id = UnusedID("phase", Phases.Select(phase => phase.Id));
        Phases.Add(new Phase(id, name));
        Measurements[id] = [];
        History[id] = [];
        return id;
    }

    public void RenamePhase(string id, string name)
    {
        var index = Phases.FindIndex(phase => phase.Id == id);
        if (index >= 0) Phases[index] = Phases[index] with { Name = name };
    }

    public void MovePhase(string id, int offset) => Move(Phases, Phases.FindIndex(phase => phase.Id == id), offset);

    public PhaseUsage PhaseUsage(string id)
    {
        var measured = new HashSet<string>(Measurements.GetValueOrDefault(id)?.Keys ?? Enumerable.Empty<string>());
        foreach (var (name, all) in History.GetValueOrDefault(id) ?? [])
        {
            if (all.Count > 0) measured.Add(name);
        }
        return new PhaseUsage(measured.Count, Walks.Count(walk => walk.Phase == id));
    }

    /// Löscht eine Phase samt ihren Messungen und Läufen. Die letzte Phase bleibt.
    public bool RemovePhase(string id)
    {
        if (Phases.Count <= 1 || Phases.All(phase => phase.Id != id)) return false;
        Measurements.Remove(id);
        History.Remove(id);
        foreach (var walk in Walks.Where(walk => walk.Phase == id)) Survey.Remove(walk.Id);
        Walks.RemoveAll(walk => walk.Phase == id);
        Phases.RemoveAll(phase => phase.Id == id);
        return true;
    }

    // MARK: Hilfen

    /// Kleinste freie ID der Form `prefix-N`.
    private static string UnusedID(string prefix, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet();
        var number = 1;
        while (used.Contains($"{prefix}-{number}")) number++;
        return $"{prefix}-{number}";
    }

    private static void Move<T>(List<T> items, int index, int offset)
    {
        if (index < 0) return;
        var target = Math.Clamp(index + offset, 0, items.Count - 1);
        if (target == index) return;
        var item = items[index];
        items.RemoveAt(index);
        items.Insert(target, item);
    }
}
