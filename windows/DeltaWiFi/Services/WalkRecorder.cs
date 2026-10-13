//
//  WalkRecorder.cs
//  DeltaWiFi
//
//  Lauf-Modus: sampelt jede Sekunde die Funkwerte und merkt sich die
//  angeklickten Positionen (Taps) mit Zeitstempel.
//

using DeltaWiFi.Logic;
using DeltaWiFi.Model;

namespace DeltaWiFi.Services;

public sealed class WalkRecorder
{
    /// Der laufende Lauf mit seinen bisherigen Taps; `null`, wenn keiner läuft.
    public Walk? Walk { get; private set; }
    /// Alle Funk-Samples seit dem Start, noch ohne Position.
    public List<RadioSample> Samples { get; private set; } = [];
    /// Letzte Momentaufnahme für die Anzeige; `null`, wenn die Verbindung weg ist.
    public RadioSnapshot? LastSnapshot { get; private set; }

    /// Meldet jede Änderung des Zustands, immer auf dem UI-Thread.
    public event Action? Changed;

    private CancellationTokenSource? cancellation;

    public bool IsActive => Walk != null;

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// Startet den Lauf; muss vom UI-Thread aus aufgerufen werden.
    public void Start(string phase, string floor)
    {
        if (IsActive) return;
        var started = Now;
        Walk = new Walk($"w{started}", phase, floor, started);
        Samples = [];
        cancellation = new CancellationTokenSource();
        _ = SampleLoop(cancellation.Token);
        Changed?.Invoke();
    }

    private async Task SampleLoop(CancellationToken cancel)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                var snapshot = await Task.Run(WiFi.Snapshot, cancel);
                if (cancel.IsCancellationRequested) return;
                LastSnapshot = snapshot;
                if (snapshot != null)
                {
                    Samples.Add(new RadioSample(Now, snapshot.Rssi, snapshot.Snr, snapshot.TxRate, snapshot.Band, snapshot.Bssid));
                }
                Changed?.Invoke();
            }
            while (await timer.WaitForNextTickAsync(cancel));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// Merkt die aktuelle Position (relativ 0…1) mit Zeitstempel.
    public void AddTap(double x, double y)
    {
        if (Walk == null) return;
        Walk = Walk with { Taps = [.. Walk.Taps, new Tap(Now, Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1))] };
    }

    public void RemoveLastTap()
    {
        if (Walk == null || Walk.Taps.Count == 0) return;
        Walk = Walk with { Taps = [.. Walk.Taps.Take(Walk.Taps.Count - 1)] };
    }

    /// Schreibt den Zwischenstand ins Projekt, damit Weg und Heatmap mitwachsen
    /// und bei einem Abbruch nichts verloren geht.
    public void Commit(Project project)
    {
        if (Walk == null) return;
        if (Walk.Taps.Count == 0) project.RemoveWalk(Walk.Id); else project.UpsertWalk(Walk, Samples);
    }

    /// Beendet den Lauf und liefert eine Meldung für die Oberfläche.
    public string? Finish(Project project)
    {
        if (Walk is not { } walk) return null;
        var samples = Samples;
        StopSampling();
        if (walk.Taps.Count < 2)
        {
            project.RemoveWalk(walk.Id);
            return "Lauf verworfen: Für die Zuordnung sind mindestens zwei Positionen nötig.";
        }
        project.UpsertWalk(walk, samples);
        var located = project.Survey.GetValueOrDefault(walk.Id)?.Count ?? 0;
        return $"Lauf gespeichert: {located} von {samples.Count} Samples zwischen {walk.Taps.Count} Positionen verortet.";
    }

    public void Discard(Project project)
    {
        if (Walk is not { } walk) return;
        StopSampling();
        project.RemoveWalk(walk.Id);
    }

    /// Stoppt das Sampling, ohne das Projekt anzufassen (z. B. wenn das Fenster schließt).
    public void StopSampling()
    {
        cancellation?.Cancel();
        cancellation = null;
        Walk = null;
        Changed?.Invoke();
    }
}
