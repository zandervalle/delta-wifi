//
//  MeasurePanel.cs
//  DeltaWiFi
//
//  Geführte Messung an den festen Punkten: Phase wählen, Punkt für Punkt
//  messen, Ergebnis ansehen.
//

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DeltaWiFi.Logic;
using DeltaWiFi.Model;
using DeltaWiFi.Services;

namespace DeltaWiFi.Views;

public sealed class MeasurePanel : StackPanel
{
    private readonly MainWindow host;

    private readonly ComboBox phasePicker = new();
    private readonly TextBlock openText = Ui.Text("", secondary: true);
    private readonly StackPanel pointArea = new();
    private readonly Button nextButton = new() { Content = "Nächster offener Punkt  →", Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel wifiArea = new();
    private readonly CheckBox speedtestBox = new() { Content = "Internet-Speedtest" };
    private readonly CheckBox iperfBox = new() { Content = "Intern messen (iperf3)" };
    private readonly TextBlock iperfHint = Ui.Text("iperf3.exe nicht gefunden. Von iperf.fr laden und in den PATH oder neben die App legen.", secondary: true);
    private readonly StackPanel serverArea;
    private readonly TextBox serverBox = new();
    private readonly TextBox noteBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 70 };
    private readonly Button startButton = new() { Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock serverMissing = Ui.Text("Für die interne Messung fehlt die Adresse des iperf3-Servers.", secondary: true);
    private readonly StackPanel progressArea;
    private readonly TextBlock progressTarget = Ui.Text("", bold: true);
    private readonly TextBlock progressTitle = Ui.Text("");
    private readonly ProgressBar progress = new() { Height = 6, Margin = new Thickness(0, 4, 0, 4), Maximum = 1 };
    private readonly StackPanel messageArea = new();
    private readonly Border detailsSection;
    private readonly TextBlock detailsHeader;
    private readonly StackPanel detailsArea;

    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private RadioSnapshot? live;
    private bool iperfAvailable = NetworkInfo.IperfPath != null;
    private bool updating;

    private Project Project => host.Project;
    private Settings Settings => host.Settings;
    private MeasurementRunner Runner => host.Runner;
    private MeasurePoint? Point => Project.Point(host.Selection);
    private string TrimmedServer => serverBox.Text.Trim();
    private bool ServerMissing => iperfAvailable && Settings.IperfEnabled && TrimmedServer.Length == 0;

    public MeasurePanel(MainWindow host)
    {
        this.host = host;

        Children.Add(Ui.Section("Phase", out var phaseContent));
        phaseContent.Children.Add(Labeled("Messen in", phasePicker));
        phaseContent.Children.Add(openText);

        Children.Add(Ui.Section("Messpunkt", out var pointContent));
        pointContent.Children.Add(pointArea);
        pointContent.Children.Add(nextButton);

        Children.Add(Ui.Section("WLAN", out var wifiContent));
        wifiContent.Children.Add(wifiArea);

        Children.Add(Ui.Section("Messung", out var runContent));
        serverArea = Ui.Stack(Ui.Text("iperf3-Server (IP-Adresse, z. B. 192.168.178.20)", secondary: true, size: 11), serverBox);
        serverArea.Margin = new Thickness(0, 6, 0, 0);
        var noteArea = Ui.Stack(Ui.Text("Notiz (optional)", secondary: true, size: 11), noteBox);
        noteArea.Margin = new Thickness(0, 8, 0, 0);
        var cancelButton = new Button { Content = "Abbrechen", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        progressArea = Ui.Stack(progressTarget, progressTitle, progress, Ui.Text("Bitte am Messpunkt stehen bleiben.", secondary: true), cancelButton);
        progressArea.Margin = new Thickness(0, 10, 0, 0);
        iperfBox.Margin = new Thickness(0, 4, 0, 0);
        foreach (var element in new UIElement[] { speedtestBox, iperfBox, iperfHint, serverArea, noteArea, progressArea, startButton, serverMissing, messageArea })
        {
            runContent.Children.Add(element);
        }

        detailsSection = Ui.Section("", out detailsArea, out detailsHeader);
        Children.Add(detailsSection);

        phasePicker.SelectionChanged += (_, _) => { if (!updating && Ui.SelectedId(phasePicker) is { } id) host.SetPhase(id); };
        nextButton.Click += (_, _) => host.Select(Project.NextOpenPoint(host.Phase, host.Selection)?.Name);
        RoutedEventHandler speedtestToggled = (_, _) => { if (!updating) { Settings.SpeedtestEnabled = speedtestBox.IsChecked == true; Update(); } };
        RoutedEventHandler iperfToggled = (_, _) => { if (!updating) { Settings.IperfEnabled = iperfBox.IsChecked == true; Update(); } };
        speedtestBox.Checked += speedtestToggled;
        speedtestBox.Unchecked += speedtestToggled;
        iperfBox.Checked += iperfToggled;
        iperfBox.Unchecked += iperfToggled;
        serverBox.TextChanged += (_, _) => { if (!updating) { Settings.IperfServer = serverBox.Text; Update(); } };
        startButton.Click += (_, _) => Start();
        cancelButton.Click += (_, _) => Runner.Cancel();

        liveTimer.Tick += async (_, _) => await RefreshLive();
        progressTimer.Tick += (_, _) => UpdateProgress();
        // Laufende Anzeige der Funkwerte, solange das Panel zu sehen ist.
        IsVisibleChanged += async (_, _) =>
        {
            liveTimer.IsEnabled = IsVisible;
            if (IsVisible) await RefreshLive();
        };
    }

    private static FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var text = Ui.Text(label, secondary: true);
        text.Margin = new Thickness(0, 0, 10, 0);
        panel.Children.Add(text);
        panel.Children.Add(control);
        return panel;
    }

    private async Task RefreshLive()
    {
        live = await Task.Run(WiFi.Snapshot);
        iperfAvailable = NetworkInfo.IperfPath != null;
        Update();
    }

    // MARK: - Anzeige

    public void Update()
    {
        updating = true;
        try
        {
            var running = Runner.IsRunning;
            var point = Point;
            var phase = host.Phase;

            Ui.SetItems(phasePicker, Project.Phases.Select(entry => (entry.Id, entry.Name)).ToList(), phase);
            phasePicker.IsEnabled = !running;
            var open = Project.OpenPoints(phase).Count;
            openText.Text = open == 0 ? $"Alle {Project.Points.Count} Punkte gemessen." : $"{open} von {Project.Points.Count} Punkten noch offen.";

            pointArea.Children.Clear();
            if (point != null)
            {
                pointArea.Children.Add(Ui.Row(point.Name, Project.FloorName(point.Floor)));
                if (!point.IsPlaced) pointArea.Children.Add(Ui.Note("Position auf dem Plan noch nicht gesetzt. Messen geht trotzdem."));
            }
            else
            {
                pointArea.Children.Add(Ui.Text("Kein Punkt ausgewählt.", secondary: true));
            }
            nextButton.IsEnabled = !running && Project.NextOpenPoint(phase, host.Selection) != null;

            UpdateWifi(running ? Runner.Samples.LastOrDefault() ?? live : live);

            speedtestBox.IsChecked = Settings.SpeedtestEnabled;
            iperfBox.IsChecked = Settings.IperfEnabled && iperfAvailable;
            speedtestBox.IsEnabled = !running;
            iperfBox.IsEnabled = !running && iperfAvailable;
            iperfHint.Visibility = iperfAvailable ? Visibility.Collapsed : Visibility.Visible;
            serverArea.Visibility = iperfAvailable && Settings.IperfEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (serverBox.Text != Settings.IperfServer && !serverBox.IsKeyboardFocused) serverBox.Text = Settings.IperfServer;
            serverBox.IsEnabled = noteBox.IsEnabled = !running;

            progressArea.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            startButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            serverMissing.Visibility = !running && ServerMissing ? Visibility.Visible : Visibility.Collapsed;
            messageArea.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            progressTimer.IsEnabled = running;
            if (running)
            {
                if (Runner.CurrentTarget is { } target) progressTarget.Text = $"Messe „{target.Point}“ ({Project.PhaseName(target.Phase)})";
                UpdateProgress();
            }
            else
            {
                var repeats = point != null && Project.Measurement(point.Name, phase) != null;
                startButton.Content = repeats ? "▶  Messung wiederholen" : "▶  Messung starten";
                startButton.IsEnabled = point != null && live != null && !ServerMissing;
                messageArea.Children.Clear();
                foreach (var message in Runner.Messages) messageArea.Children.Add(Ui.Note(message));
            }

            detailsArea.Children.Clear();
            if (point != null && Project.Measurement(point.Name, phase) is { } measurement)
            {
                detailsHeader.Text = $"{Project.PhaseName(phase)}: gültige Messung";
                MeasurementDetails.Fill(detailsArea, measurement, Project);
                detailsSection.Visibility = Visibility.Visible;
            }
            else
            {
                detailsSection.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            updating = false;
        }
    }

    private void UpdateWifi(RadioSnapshot? snapshot)
    {
        wifiArea.Children.Clear();
        if (snapshot != null)
        {
            wifiArea.Children.Add(Ui.Row("Netz", snapshot.Ssid ?? "–"));
            wifiArea.Children.Add(Ui.Row("Knoten (BSSID)", snapshot.Bssid ?? "–"));
            wifiArea.Children.Add(Ui.Row("Signal", $"{snapshot.Rssi} dBm" + (snapshot.TxRate is { } rate ? $" · {MetricInfo.Number(rate)} Mbit/s" : "")));
            wifiArea.Children.Add(Ui.Row("Kanal", ChannelText(snapshot.Band, snapshot.Channel, snapshot.Width, null)));
        }
        else
        {
            wifiArea.Children.Add(Ui.Note("Nicht mit einem WLAN verbunden.", "⚠"));
        }
        if (!WiFi.IsAccessDenied) return;

        wifiArea.Children.Add(Ui.Note("Ohne Ortungsfreigabe liefert Windows weder Netzname noch BSSID. Die Messung läuft, aber es bleibt offen, " +
                                      "mit welchem Knoten der Rechner verbunden war.", "⚠"));
        var open = new Button { Content = "Ortungseinstellungen öffnen", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true });
        wifiArea.Children.Add(open);
    }

    public static string ChannelText(string? band, int? channel, int? width, string? phy) =>
        string.Join(" · ", new[]
        {
            band == null ? null : $"{band.Replace('.', ',')} GHz", channel == null ? null : $"Kanal {channel}",
            width == null ? null : $"{width} MHz", phy,
        }.OfType<string>());

    private void UpdateProgress()
    {
        if (Runner.CurrentStage is not { } stage) return;
        progressTitle.Text = stage.Title;
        progress.IsIndeterminate = stage.Duration == null;
        if (stage.Duration is { } duration) progress.Value = Math.Clamp((DateTime.Now - stage.Start).TotalSeconds / duration, 0, 1);
    }

    private void Start()
    {
        if (Point is not { } point) return;
        var target = new MeasurementRunner.Target(point.Name, host.Phase);
        var options = new MeasurementRunner.Options(Settings.SpeedtestEnabled, iperfAvailable && Settings.IperfEnabled ? TrimmedServer : null);
        Settings.Save();
        Runner.Start(target, options, noteBox.Text, measurement =>
        {
            host.Edit(project => project.Record(measurement, target.Point, target.Phase));
            noteBox.Text = "";
        });
        // Fortschritt und „Abbrechen“ sollen zu sehen sein, auch wenn das Panel höher ist als das Fenster.
        Dispatcher.BeginInvoke(progressArea.BringIntoView, DispatcherPriority.Loaded);
    }
}

/// Alle Werte einer Messung als Liste; die sieben bewerteten Messgrößen mit Statuspunkt.
public static class MeasurementDetails
{
    public static void Fill(StackPanel panel, Measurement measurement, Project project)
    {
        void Row(string title, string? value)
        {
            if (!string.IsNullOrEmpty(value)) panel.Children.Add(Ui.Row(title, value));
        }
        void MetricRow(Metric metric)
        {
            if (metric.Value(measurement) is not { } value) return;
            var scale = project.Scale(metric);
            panel.Children.Add(Ui.Row(metric.Title(), scale.Describe(value), HeatColor.Scale(scale.Score(value))));
        }
        static string? Number(double? value, string unit) => value is { } number ? $"{MetricInfo.Number(number)} {unit}" : null;

        if (measurement.Zeit is { } zeit)
        {
            Row("Zeit", zeit + (measurement.Wiederholung is { } repetition ? $" · Wiederholung {repetition}" : ""));
        }
        MetricRow(Metric.Rssi);
        Row("Rauschen", Number(measurement.NoiseDBm, "dBm"));
        MetricRow(Metric.Snr);
        MetricRow(Metric.LinkRate);
        Row("Kanal", MeasurePanel.ChannelText(measurement.BandGHz, measurement.Kanal, measurement.BreiteMHz, measurement.Phy));
        Row("Knoten (BSSID)", measurement.Bssid);
        MetricRow(Metric.GatewayLoss);
        Row("Ping zur Box (Mittel)", Number(measurement.PingGwAvgMs, "ms"));
        MetricRow(Metric.GatewayMaxPing);
        Row("Paketverlust Internet", Number(measurement.PingInetVerlustPct, "%"));
        Row("Max. Ping Internet", Number(measurement.PingInetMaxMs, "ms"));
        MetricRow(Metric.InternetDown);
        Row("Internet Upload", Number(measurement.InetUpMbit, "Mbit/s") is { } upload
            ? upload + (MetricScale.ShareText(measurement.InetUpMbit, project.LineUpMbit) ?? "") : null);
        MetricRow(Metric.LanDown);
        Row("Intern Upload (iperf3)", Number(measurement.LanUpMbit, "Mbit/s"));
        Row("Notiz", measurement.Notiz);
    }
}
