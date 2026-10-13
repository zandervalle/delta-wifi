//
//  WalkInterpolation.cs
//  DeltaWiFi
//
//  Ordnet Lauf-Samples über ihre Zeit einer Position zwischen zwei Taps zu.
//

using DeltaWiFi.Model;

namespace DeltaWiFi.Logic;

/// Ein Funk-Sample mit Zeitstempel (ms seit Epoch), noch ohne Position.
public sealed record RadioSample(long T, double? Rssi = null, double? Snr = null, double? Rate = null,
                                 string? Band = null, string? Bssid = null);

public static class WalkInterpolation
{
    /// Position zum Zeitpunkt `t` (ms), linear zwischen den umgebenden Taps interpoliert.
    /// `taps` müssen aufsteigend nach Zeit sortiert sein. Außerhalb des ersten/letzten Taps `null`.
    public static (double X, double Y)? Position(long t, IReadOnlyList<Tap> taps)
    {
        if (taps.Count == 0 || t < taps[0].T || t > taps[^1].T) return null;

        // Erster Tap mit Zeit >= t
        var low = 0;
        var high = taps.Count - 1;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (taps[mid].T < t) low = mid + 1; else high = mid;
        }
        var after = taps[low];
        if (low == 0 || after.T <= t) return (after.X, after.Y);

        var before = taps[low - 1];
        var fraction = (double)(t - before.T) / (after.T - before.T);
        return (before.X + (after.X - before.X) * fraction, before.Y + (after.Y - before.Y) * fraction);
    }

    /// Verortet alle Samples, die zeitlich zwischen erstem und letztem Tap liegen. Der Rest fällt weg.
    public static List<SurveySample> Locate(IReadOnlyList<RadioSample> samples, IReadOnlyList<Tap> taps)
    {
        var sortedTaps = taps.OrderBy(tap => tap.T).ToList();
        var located = new List<SurveySample>();
        foreach (var sample in samples)
        {
            if (Position(sample.T, sortedTaps) is not { } position) continue;
            located.Add(new SurveySample(position.X, position.Y, sample.Rssi, sample.Snr, sample.Rate, sample.Band, sample.Bssid));
        }
        return located;
    }
}
