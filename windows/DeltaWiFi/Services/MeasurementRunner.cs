//
//  MeasurementRunner.cs
//  DeltaWiFi
//
//  Führt eine Messung an einem festen Punkt aus: Signal-Stichproben und Ping
//  parallel, danach Speedtest und optional iperf3.
//

using DeltaWiFi.Logic;
using DeltaWiFi.Model;

namespace DeltaWiFi.Services;

public sealed class MeasurementRunner
{
    /// `IperfServer` ist die Adresse des iperf3-Servers; `null` = keine interne Messung.
    public sealed record Options(bool Speedtest = true, string? IperfServer = null);

    /// Ein Abschnitt der Messung. `Duration` ist die erwartete Dauer in Sekunden; `null`, wenn sie nicht absehbar ist.
    public sealed record Stage(string Title, double? Duration = null)
    {
        public DateTime Start { get; } = DateTime.Now;
    }

    public sealed record Target(string Point, string Phase);

    public const int SampleCount = 10;
    public const int PingCount = 60;
    /// Parallele TCP-Ströme bei iperf3. Ein einzelner Strom schwankte über WLAN stark (177–354 Mbit/s am
    /// selben Platz) und lag deutlich unter dem, was die Funkstrecke hergibt; vier Ströme messen stabil.
    public const int IperfStreams = 4;

    /// Aktueller Abschnitt; `null`, wenn keine Messung läuft.
    public Stage? CurrentStage { get; private set; }
    public Target? CurrentTarget { get; private set; }
    /// Stichproben der laufenden bzw. letzten Messung.
    public List<RadioSnapshot> Samples { get; private set; } = [];
    /// Hinweise und Fehler der letzten Messung.
    public List<string> Messages { get; private set; } = [];

    /// Meldet jede Änderung des Zustands, immer auf dem UI-Thread.
    public event Action? Changed;

    private CancellationTokenSource? cancellation;

    public bool IsRunning => CurrentStage != null;

    /// Startet die Messung; muss vom UI-Thread aus aufgerufen werden. `onFinish` bekommt das Ergebnis.
    public async void Start(Target target, Options options, string note, Action<Measurement> onFinish)
    {
        if (IsRunning) return;
        CurrentTarget = target;
        Samples = [];
        Messages = [];
        SetStage(new Stage("Messung wird vorbereitet"));
        cancellation = new CancellationTokenSource();
        try
        {
            var measurement = await MeasureAsync(options, cancellation.Token);
            onFinish(measurement with { Notiz = note.Trim().Length == 0 ? null : note });
        }
        catch (OperationCanceledException)
        {
            Messages = ["Messung abgebrochen, nichts gespeichert."];
        }
        catch (Exception error)
        {
            Messages = [error.Message];
        }
        cancellation.Dispose();
        cancellation = null;
        SetStage(null);
    }

    public void Cancel() => cancellation?.Cancel();

    private void SetStage(Stage? stage)
    {
        CurrentStage = stage;
        Changed?.Invoke();
    }

    // MARK: - Ablauf

    private sealed class NotConnectedException()
        : Exception("Der Rechner ist mit keinem WLAN verbunden. Bitte WLAN einschalten und verbinden, dann erneut messen.");

    private async Task<Measurement> MeasureAsync(Options options, CancellationToken cancel)
    {
        if (await Task.Run(WiFi.Snapshot, cancel) == null || WiFi.InterfaceId is not { } adapter) throw new NotConnectedException();
        var gateway = NetworkInfo.Gateway(adapter);
        var address = NetworkInfo.IPv4Address(adapter);

        // Signal und beide Pings laufen gleichzeitig.
        SetStage(new Stage($"Signal und Ping ({PingCount} s)", PingCount + 1));
        var gatewayPing = Pinger.RunAsync(gateway, address, PingCount, cancel);
        var internetPing = Pinger.RunAsync(NetworkInfo.InternetTarget, address, PingCount, cancel);
        try
        {
            for (var index = 0; index < SampleCount; index++)
            {
                if (index > 0) await Task.Delay(1000, cancel);
                if (await Task.Run(WiFi.Snapshot, cancel) is { } snapshot)
                {
                    Samples.Add(snapshot);
                    Changed?.Invoke();
                }
            }
            await Task.WhenAll(gatewayPing, internetPing);
        }
        catch (OperationCanceledException)
        {
            // Die Pings enden mit demselben Abbruch; ihr Ergebnis wird nicht mehr gebraucht.
            _ = Task.WhenAll(gatewayPing, internetPing).ContinueWith(task => task.Exception, TaskScheduler.Default);
            throw;
        }
        var toGateway = gatewayPing.Result;
        var toInternet = internetPing.Result;

        var measurement = MeasurementParsing.Summarize(Samples) with { Zeit = MeasurementParsing.Timestamp(DateTimeOffset.Now) };
        if (Samples.Count < SampleCount)
        {
            Messages.Add($"Nur {Samples.Count} von {SampleCount} Signal-Stichproben: Die WLAN-Verbindung war zeitweise weg.");
        }
        if (toGateway != null)
        {
            measurement = measurement with
            {
                PingGwVerlustPct = toGateway.LossPct, PingGwAvgMs = toGateway.AvgMs, PingGwMaxMs = toGateway.MaxMs,
            };
        }
        else
        {
            Messages.Add($"Ping zur Box ({gateway}) lieferte kein Ergebnis.");
        }
        if (toInternet != null)
        {
            measurement = measurement with
            {
                PingInetVerlustPct = toInternet.LossPct, PingInetAvgMs = toInternet.AvgMs, PingInetMaxMs = toInternet.MaxMs,
            };
        }
        else
        {
            Messages.Add($"Ping ins Internet ({NetworkInfo.InternetTarget}) lieferte kein Ergebnis.");
        }

        if (options.Speedtest)
        {
            var seconds = SpeedTest.Duration.TotalSeconds;
            SetStage(new Stage($"Internet-Speedtest: Download ({seconds:0} s)", seconds + 1));
            var down = await SpeedTest.DownloadAsync(address, cancel);
            SetStage(new Stage($"Internet-Speedtest: Upload ({seconds:0} s)", seconds + 1));
            var up = await SpeedTest.UploadAsync(address, cancel);
            measurement = measurement with { InetDownMbit = down, InetUpMbit = up };
            if (down == null || up == null) Messages.Add("Der Internet-Speedtest (speed.cloudflare.com) lieferte kein vollständiges Ergebnis.");
        }

        if (options.IperfServer is { } server)
        {
            if (NetworkInfo.IperfPath is { } iperf && address != null)
            {
                // -R: Der Server sendet, gemessen wird also der Download.
                foreach (var download in new[] { true, false })
                {
                    var title = download ? "Intern Download (iperf3, 10 s)" : "Intern Upload (iperf3, 10 s)";
                    SetStage(new Stage(title, 11));
                    string[] arguments = ["-c", server, "-B", address.ToString(), "-t", "10", "-P", IperfStreams.ToString(),
                                          .. download ? ["-R"] : Array.Empty<string>(), "-J"];
                    var value = await ProcessRunner.RunAsync(iperf, arguments, cancel) is { } output ? MeasurementParsing.Iperf(output) : null;
                    measurement = download ? measurement with { LanDownMbit = value } : measurement with { LanUpMbit = value };
                    if (value == null)
                    {
                        Messages.Add($"iperf3 lieferte kein Ergebnis ({title}). Läuft auf {server} „iperf3 -s“?");
                        break;
                    }
                }
            }
            else
            {
                Messages.Add("Interne Messung übersprungen: iperf3 oder die WLAN-IP-Adresse wurde nicht gefunden.");
            }
        }
        return measurement;
    }
}
