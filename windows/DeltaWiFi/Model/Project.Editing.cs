//
//  Project.Editing.cs
//  DeltaWiFi
//
//  Messpunkte, Messungen und Läufe ändern: ergänzen, verschieben, entfernen,
//  Messungen ablegen und den geführten Ablauf steuern.
//

using DeltaWiFi.Logic;

namespace DeltaWiFi.Model;

public sealed partial class Project
{
    // MARK: Punkte

    /// Setzt die Position eines Punkts, begrenzt auf 0…1.
    public void SetPosition(string name, double x, double y)
    {
        var index = Points.FindIndex(point => point.Name == name);
        if (index < 0) return;
        Points[index] = Points[index] with { X = Math.Clamp(x, 0, 1), Y = Math.Clamp(y, 0, 1) };
    }

    public void ClearPosition(string name)
    {
        var index = Points.FindIndex(point => point.Name == name);
        if (index < 0) return;
        Points[index] = Points[index] with { X = null, Y = null };
    }

    /// Verlegt einen Punkt auf eine andere Etage. Die Position gilt dort nicht mehr und wird gelöscht.
    public void SetFloor(string floor, string name)
    {
        var index = Points.FindIndex(point => point.Name == name);
        if (index < 0 || Points[index].Floor == floor) return;
        Points[index] = Points[index] with { Floor = floor, X = null, Y = null };
    }

    /// Legt einen neuen Punkt an (gilt für alle Phasen). `false` bei leerem oder schon vorhandenem Namen.
    public bool AddPoint(string rawName, string floor)
    {
        var name = rawName.NormalizedPointName();
        if (name.Length == 0 || Point(name) != null) return false;
        Points.Add(new MeasurePoint(name, floor));
        return true;
    }

    public bool HasMeasurements(string name) =>
        Phases.Any(phase => Measurement(name, phase.Id) != null || HistoryAt(name, phase.Id).Count > 0);

    /// Entfernt einen Punkt, aber nur, wenn an ihm noch nichts gemessen wurde.
    public bool RemovePoint(string name)
    {
        if (HasMeasurements(name)) return false;
        var index = Points.FindIndex(point => point.Name == name);
        if (index < 0) return false;
        Points.RemoveAt(index);
        return true;
    }

    // MARK: Messen

    /// Legt eine neue Messung ab: Sie wird zur gültigen Messung des Punkts, frühere bleiben in der Historie.
    /// Liefert die vergebene Wiederholungsnummer.
    public int Record(Measurement measurement, string name, string phase)
    {
        var all = HistoryAt(name, phase).ToList();
        // Ältere Dateien kennen nur die aktuelle Messung: in die Historie retten, bevor sie ersetzt wird.
        if (all.Count == 0 && Measurement(name, phase) is { } current) all.Add(current);

        var repetition = Math.Max(all.Max(entry => entry.Wiederholung) ?? 0, all.Count) + 1;
        measurement = measurement with { Wiederholung = repetition };
        all.Add(measurement);
        if (!History.ContainsKey(phase)) History[phase] = [];
        if (!Measurements.ContainsKey(phase)) Measurements[phase] = [];
        History[phase][name] = all;
        Measurements[phase][name] = measurement;
        return repetition;
    }

    /// Punkte, die in der Phase noch keine Messung haben, in Listenreihenfolge.
    public List<MeasurePoint> OpenPoints(string phase) => Points.Where(point => Measurement(point.Name, phase) == null).ToList();

    /// Nächster offener Punkt hinter `name` (mit Umbruch am Listenende); ohne `name` der erste.
    public MeasurePoint? NextOpenPoint(string phase, string? name)
    {
        var start = Points.FindIndex(point => point.Name == name) + 1;
        return Points.Skip(start).Concat(Points.Take(start))
            .FirstOrDefault(point => Measurement(point.Name, phase) == null && point.Name != name);
    }

    // MARK: Läufe

    /// Legt einen Lauf an bzw. aktualisiert ihn und verortet seine Samples neu
    /// (Zeitinterpolation zwischen den Taps; Samples außerhalb fallen weg).
    public void UpsertWalk(Walk walk, IReadOnlyList<RadioSample> samples)
    {
        var index = Walks.FindIndex(existing => existing.Id == walk.Id);
        if (index >= 0) Walks[index] = walk; else Walks.Add(walk);
        Survey[walk.Id] = WalkInterpolation.Locate(samples, walk.Taps);
    }

    public void RemoveWalk(string id)
    {
        Walks.RemoveAll(walk => walk.Id == id);
        Survey.Remove(id);
    }
}
