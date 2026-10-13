//
//  MeasurementParsing.cs
//  DeltaWiFi
//
//  Auswertung der Messungen: Stichproben der Funkwerte, Ping, Durchsatz und iperf3.
//

using System.Globalization;
using System.Text.Json;
using DeltaWiFi.Model;

namespace DeltaWiFi.Logic;

/// Momentaufnahme der Funkwerte des WLAN-Adapters. Windows liefert keinen Rauschpegel, `Noise` bleibt
/// dort leer; BSSID und SSID gibt es nur mit Ortungsfreigabe.
public sealed record RadioSnapshot(int Rssi, int? Noise = null, double? TxRate = null, string? Band = null, int? Channel = null,
                                   int? Width = null, string? Phy = null, string? Bssid = null, string? Ssid = null)
{
    public int? Snr => Rssi - Noise;
}

/// Rauschpegel, die ein WLAN-Empfänger realistisch misst. macOS meldet zeitweise Werte wie −17 dBm,
/// die über dem Signal liegen; sie zählen als „nicht gemessen“ (betrifft Dateien aus der macOS-App).
public static class NoiseFloor
{
    public static bool IsPlausible(double noise) => noise is >= -110 and <= -75;

    /// SNR, sofern der zugehörige Rauschpegel (`rssi − snr`) plausibel ist. Ohne Signalwert bleibt der SNR ungeprüft.
    public static double? PlausibleSNR(double? snr, double? rssi)
    {
        if (snr is not { } ratio || rssi is not { } signal) return snr;
        return IsPlausible(signal - ratio) ? snr : null;
    }
}

/// Ergebnis einer Ping-Reihe. Durchschnitt und Maximum fehlen bei 100 % Verlust.
public sealed record PingResult(double LossPct, double? AvgMs = null, double? MaxMs = null);

public static class MeasurementParsing
{
    // MARK: Stichproben

    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }

    /// Häufigster Wert; bei Gleichstand der zuletzt gesehene.
    public static T? MostCommon<T>(IEnumerable<T> values) where T : notnull
    {
        var counts = new Dictionary<T, int>();
        var found = false;
        T? best = default;
        foreach (var value in values)
        {
            counts[value] = counts.GetValueOrDefault(value) + 1;
            if (found && counts[value] < counts[best!]) continue;
            best = value;
            found = true;
        }
        return best;
    }

    /// Fasst die Stichproben zusammen: Median (auf ganze Zahl gerundet) für die Pegel,
    /// häufigster Wert für Band, Kanal, BSSID usw.
    public static Measurement Summarize(IReadOnlyList<RadioSnapshot> samples)
    {
        static double? RoundedMedian(IEnumerable<double> values) => Median(values.ToList()) is { } median ? Flex.Rounded(median) : null;
        static int? CommonNumber(IEnumerable<int?> values) => values.OfType<int>().ToList() is { Count: > 0 } all ? MostCommon(all) : null;
        static string? CommonText(IEnumerable<string?> values) => MostCommon(values.OfType<string>());

        var rssi = RoundedMedian(samples.Select(sample => (double)sample.Rssi));
        var noise = RoundedMedian(samples.Select(sample => (double?)sample.Noise).OfType<double>().Where(NoiseFloor.IsPlausible));
        return new Measurement
        {
            RssiDBm = rssi,
            NoiseDBm = noise,
            SnrDB = rssi - noise,
            TxrateMbit = RoundedMedian(samples.Select(sample => sample.TxRate).OfType<double>()),
            BandGHz = CommonText(samples.Select(sample => sample.Band)),
            Kanal = CommonNumber(samples.Select(sample => sample.Channel)),
            BreiteMHz = CommonNumber(samples.Select(sample => sample.Width)),
            Phy = CommonText(samples.Select(sample => sample.Phy)),
            Bssid = CommonText(samples.Select(sample => sample.Bssid)),
            Ssid = CommonText(samples.Select(sample => sample.Ssid)),
        };
    }

    /// Zeitstempel einer Messung in Ortszeit: `2026-10-11 14:05:00`.
    public static string Timestamp(DateTimeOffset date, TimeZoneInfo? timeZone = null) =>
        TimeZoneInfo.ConvertTime(date, timeZone ?? TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // MARK: Werkzeuge

    /// Wertet eine Ping-Reihe aus: Laufzeit je Anfrage in ms, `null` = keine Antwort.
    public static PingResult? Ping(IReadOnlyList<double?> roundTrips)
    {
        if (roundTrips.Count == 0) return null;
        var received = roundTrips.OfType<double>().ToList();
        var loss = Round1(100.0 * (roundTrips.Count - received.Count) / roundTrips.Count);
        return received.Count == 0 ? new PingResult(loss) : new PingResult(loss, Round1(received.Average()), Round1(received.Max()));
    }

    /// Durchsatz aus übertragenen Bytes und Dauer, in Mbit/s; `null`, wenn nichts übertragen wurde.
    public static double? Throughput(long bytes, TimeSpan duration) =>
        bytes > 0 && duration > TimeSpan.Zero ? Megabit(bytes * 8 / duration.TotalSeconds) : null;

    /// JSON von `iperf3 -J`: `end.sum_received.bits_per_second` → Mbit/s.
    public static double? Iperf(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("end", out var end) && end.ValueKind == JsonValueKind.Object
                && end.TryGetProperty("sum_received", out var received) && received.ValueKind == JsonValueKind.Object
                && received.TryGetProperty("bits_per_second", out var bits) && bits.ValueKind == JsonValueKind.Number
                ? Megabit(bits.GetDouble()) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double Megabit(double bitsPerSecond) => Round1(bitsPerSecond / 1_000_000);

    private static double Round1(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
