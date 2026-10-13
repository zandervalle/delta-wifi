//
//  WalkPanel.cs
//  DeltaWiFi
//
//  Lauf-Modus: Lauf starten, Positionen anklicken, beenden; dazu die Liste
//  der gespeicherten Läufe.
//

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DeltaWiFi.Logic;
using DeltaWiFi.Model;
using DeltaWiFi.Services;

namespace DeltaWiFi.Views;

public sealed class WalkPanel : StackPanel
{
    private readonly MainWindow host;

    private readonly Border startSection;
    private readonly ComboBox phasePicker = new();
    private readonly StackPanel startArea = new();
    private readonly Button startButton = new() { Content = "▶  Lauf starten", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border activeSection;
    private readonly TextBlock activeHeader;
    private readonly StackPanel activeArea = new();
    private readonly Button undoButton = new() { Content = "↶  Letzte Position löschen", Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox showPathsBox = new() { Content = "Laufwege auf dem Plan zeigen", Margin = new Thickness(0, 0, 0, 6) };
    private readonly StackPanel listArea = new();

    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private RadioSnapshot? live;
    private string? message;
    private bool updating;

    private Project Project => host.Project;
    private WalkRecorder Recorder => host.Recorder;

    public WalkPanel(MainWindow host)
    {
        this.host = host;

        startSection = Ui.Section("Neuer Lauf", out var startContent);
        var phaseRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var phaseLabel = Ui.Text("Phase", secondary: true);
        phaseLabel.Margin = new Thickness(0, 0, 10, 0);
        phaseRow.Children.Add(phaseLabel);
        phaseRow.Children.Add(phasePicker);
        startContent.Children.Add(phaseRow);
        startContent.Children.Add(startArea);
        startContent.Children.Add(startButton);
        Children.Add(startSection);

        activeSection = Ui.Section("", out var activeContent, out activeHeader);
        var finishButton = new Button { Content = "■  Lauf beenden", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 6, 0, 0) };
        var discardButton = new Button { Content = "Lauf verwerfen", Margin = new Thickness(0, 6, 0, 0) };
        foreach (var element in new UIElement[] { activeArea, undoButton, finishButton, discardButton }) activeContent.Children.Add(element);
        Children.Add(activeSection);

        Children.Add(Ui.Section("Gespeicherte Läufe", out var listContent));
        listContent.Children.Add(showPathsBox);
        listContent.Children.Add(listArea);

        phasePicker.SelectionChanged += (_, _) => { if (!updating && Ui.SelectedId(phasePicker) is { } id) host.SetPhase(id); };
        startButton.Click += (_, _) =>
        {
            message = null;
            Recorder.Start(host.Phase, host.Floor);
        };
        undoButton.Click += (_, _) =>
        {
            Recorder.RemoveLastTap();
            host.Edit(Recorder.Commit);
        };
        finishButton.Click += (_, _) => host.Edit(project => message = Recorder.Finish(project));
        discardButton.Click += (_, _) =>
        {
            host.Edit(Recorder.Discard);
            message = "Lauf verworfen.";
            Update();
        };
        RoutedEventHandler pathsToggled = (_, _) =>
        {
            if (updating) return;
            host.Settings.ShowWalkPaths = showPathsBox.IsChecked == true;
            host.Refresh();
        };
        showPathsBox.Checked += pathsToggled;
        showPathsBox.Unchecked += pathsToggled;

        liveTimer.Tick += async (_, _) => await RefreshLive();
        IsVisibleChanged += async (_, _) =>
        {
            liveTimer.IsEnabled = IsVisible;
            if (IsVisible) await RefreshLive();
        };
    }

    private async Task RefreshLive()
    {
        // Während eines Laufs liefert der Rekorder die Werte.
        if (Recorder.IsActive) return;
        live = await Task.Run(WiFi.Snapshot);
        Update();
    }

    public void Update()
    {
        updating = true;
        try
        {
            var walk = Recorder.Walk;
            startSection.Visibility = walk == null ? Visibility.Visible : Visibility.Collapsed;
            activeSection.Visibility = walk == null ? Visibility.Collapsed : Visibility.Visible;
            if (walk == null) UpdateStart(); else UpdateActive(walk);

            showPathsBox.IsChecked = host.Settings.ShowWalkPaths;
            listArea.Children.Clear();
            var walks = Project.Walks.Where(entry => entry.Id != walk?.Id).ToList();
            if (walks.Count == 0) listArea.Children.Add(Ui.Text("Noch keine Läufe.", secondary: true));
            foreach (var entry in walks) listArea.Children.Add(WalkRow(entry));
        }
        finally
        {
            updating = false;
        }
    }

    private void UpdateStart()
    {
        Ui.SetItems(phasePicker, Project.Phases.Select(phase => (phase.Id, phase.Name)).ToList(), host.Phase);
        startArea.Children.Clear();
        startArea.Children.Add(Ui.Row("Etage", Project.FloorName(host.Floor)));
        startArea.Children.Add(Ui.Note("Nach dem Start mit dem Notebook durchs Haus gehen und alle paar Meter die eigene Position auf dem Plan " +
                                       "anklicken. Die Funkwerte werden jede Sekunde aufgezeichnet.", ""));
        startArea.Children.Add(live != null ? Ui.Row("Signal", SignalText(live)) : Ui.Note("Nicht mit einem WLAN verbunden.", "⚠"));
        if (message != null) startArea.Children.Add(Ui.Note(message));
        startButton.IsEnabled = live != null;
    }

    private void UpdateActive(Walk walk)
    {
        activeHeader.Text = $"Lauf läuft: {Project.PhaseName(walk.Phase)}, {Project.FloorName(walk.Floor)}";
        var duration = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(walk.Started);
        activeArea.Children.Clear();
        activeArea.Children.Add(Ui.Row("Dauer", $"{(int)duration.TotalMinutes}:{duration.Seconds:00}"));
        activeArea.Children.Add(Ui.Row("Samples", Recorder.Samples.Count.ToString()));
        activeArea.Children.Add(Ui.Row("Positionen", walk.Taps.Count.ToString()));
        activeArea.Children.Add(Recorder.LastSnapshot is { } snapshot
            ? Ui.Row("Signal", SignalText(snapshot))
            : Ui.Note("WLAN-Verbindung weg, es wird nichts aufgezeichnet.", "⚠"));
        activeArea.Children.Add(Ui.Note(walk.Taps.Count == 0
            ? "Jetzt die Startposition auf dem Plan anklicken."
            : "Weitergehen und alle paar Meter die aktuelle Position anklicken. Gezählt werden nur Samples zwischen erster und letzter Position.", ""));
        undoButton.IsEnabled = walk.Taps.Count > 0;
    }

    private FrameworkElement WalkRow(Walk walk)
    {
        var samples = Project.Survey.GetValueOrDefault(walk.Id) ?? [];
        var started = DateTimeOffset.FromUnixTimeMilliseconds(walk.Started).LocalDateTime;
        var parts = new List<string> { started.ToString("dd.MM.yyyy HH:mm"), $"{walk.Taps.Count} Positionen", $"{samples.Count} Samples" };
        // Anzahl verschiedener WLAN-Knoten, sofern die BSSID aufgezeichnet wurde.
        var nodes = samples.Select(sample => sample.Bssid).OfType<string>().Distinct().Count();
        if (nodes > 0) parts.Add(nodes == 1 ? "1 Knoten" : $"{nodes} Knoten");

        var title = $"{Project.PhaseName(walk.Phase)} · {Project.FloorName(walk.Floor)}";
        var delete = new Button
        {
            Content = "Löschen", Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !Recorder.IsActive, ToolTip = "Lauf löschen",
        };
        delete.Click += (_, _) =>
        {
            var question = $"{title} mit {samples.Count} Samples wird entfernt.";
            if (MessageBox.Show(host, question, "Lauf löschen?", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            {
                host.Edit(project => project.RemoveWalk(walk.Id));
            }
        };
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        DockPanel.SetDock(delete, Dock.Right);
        row.Children.Add(delete);
        row.Children.Add(Ui.Stack(Ui.Text(title), Ui.Text(string.Join(" · ", parts), secondary: true, size: 11)));
        return row;
    }

    private static string SignalText(RadioSnapshot snapshot) =>
        string.Join(" · ", new[]
        {
            $"{snapshot.Rssi} dBm", snapshot.Snr is { } snr ? $"SNR {snr} dB" : null,
            snapshot.Band == null ? null : $"{snapshot.Band.Replace('.', ',')} GHz",
        }.OfType<string>());
}
