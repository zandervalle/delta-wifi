//
//  Metric.cs
//  DeltaWiFi
//
//  Messgrößen mit ihren Grenzwerten und die Farbskala der Heatmap.
//

using System.Globalization;
using DeltaWiFi.Model;

namespace DeltaWiFi.Logic;

public enum Rating { Good, Okay, Critical }

public enum Metric { Rssi, Snr, LinkRate, GatewayLoss, GatewayMaxPing, InternetDown, LanDown }

public static class MetricInfo
{
    public static readonly IReadOnlyList<Metric> All = Enum.GetValues<Metric>();

    /// Zahlen wie im Deutschen üblich: Dezimalkomma, Tausenderpunkt.
    private static readonly NumberFormatInfo German = new()
    {
        NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NumberGroupSizes = [3], NegativeSign = "-", PositiveSign = "+",
    };

    public static string Title(this Metric metric) => metric switch
    {
        Metric.Rssi => "Signalstärke",
        Metric.Snr => "Signal-Rausch-Abstand",
        Metric.LinkRate => "Link-Rate",
        Metric.GatewayLoss => "Paketverlust zur Box",
        Metric.GatewayMaxPing => "Max. Ping zur Box",
        Metric.InternetDown => "Internet Download",
        _ => "Intern Download (iperf3)",
    };

    public static string Unit(this Metric metric) => metric switch
    {
        Metric.Rssi => "dBm",
        Metric.Snr => "dB",
        Metric.GatewayLoss => "%",
        Metric.GatewayMaxPing => "ms",
        _ => "Mbit/s",
    };

    public static bool HigherIsBetter(this Metric metric) => metric is not (Metric.GatewayLoss or Metric.GatewayMaxPing);

    /// Grenzwert, ab dem ein Wert als gut gilt.
    public static double Good(this Metric metric) => metric switch
    {
        Metric.Rssi => -60,
        Metric.Snr => 30,
        Metric.LinkRate => 400,
        Metric.GatewayLoss => 0,
        Metric.GatewayMaxPing => 30,
        Metric.InternetDown => 120,
        _ => 300,
    };

    /// Grenzwert, ab dem ein Wert noch okay ist; jenseits davon kritisch.
    public static double Okay(this Metric metric) => metric switch
    {
        Metric.Rssi => -70,
        Metric.Snr => 20,
        Metric.LinkRate => 150,
        Metric.GatewayLoss => 1,
        Metric.GatewayMaxPing => 100,
        Metric.InternetDown => 60,
        _ => 100,
    };

    /// Läufe liefern nur Funkwerte, keinen Ping und keinen Durchsatz.
    public static bool IsAvailableFromWalks(this Metric metric) => metric is Metric.Rssi or Metric.Snr or Metric.LinkRate;

    public static double? Value(this Metric metric, Measurement measurement) => metric switch
    {
        Metric.Rssi => measurement.RssiDBm,
        Metric.Snr => NoiseFloor.PlausibleSNR(measurement.SnrDB, measurement.RssiDBm),
        Metric.LinkRate => measurement.TxrateMbit,
        Metric.GatewayLoss => measurement.PingGwVerlustPct,
        Metric.GatewayMaxPing => measurement.PingGwMaxMs,
        Metric.InternetDown => measurement.InetDownMbit,
        _ => measurement.LanDownMbit,
    };

    public static double? Value(this Metric metric, SurveySample sample) => metric switch
    {
        Metric.Rssi => sample.Rssi,
        Metric.Snr => NoiseFloor.PlausibleSNR(sample.Snr, sample.Rssi),
        Metric.LinkRate => sample.Rate,
        _ => null,
    };

    /// Skala mit den festen Standardgrenzwerten. Projektbezogene Grenzen liefert `Project.Scale`.
    public static MetricScale Scale(this Metric metric) => new(metric, metric.Good(), metric.Okay());

    public static Rating Rating(this Metric metric, double value) => metric.Scale().Rating(value);
    public static double Score(this Metric metric, double value) => metric.Scale().Score(value);
    public static double Change(this Metric metric, double before, double after) => metric.Scale().Change(before, after);

    /// Zahl mit höchstens einer Nachkommastelle, z. B. „1,7“.
    public static string Number(double value) => value.ToString("#,0.#", German);

    public static string Format(this Metric metric, double value, bool withUnit = true) =>
        withUnit ? $"{Number(value)} {metric.Unit()}" : Number(value);

    /// Grenzwert als Text, z. B. „≥ -60 dBm“ oder „≤ 30 ms“.
    public static string ThresholdText(this Metric metric, double threshold)
    {
        if (!metric.HigherIsBetter() && threshold == 0) return metric.Format(threshold);
        return $"{(metric.HigherIsBetter() ? "≥" : "≤")} {metric.Format(threshold)}";
    }

    /// Einheit einer Differenz; der Unterschied zweier Pegel in dBm ist ein Wert in dB.
    public static string DifferenceUnit(this Metric metric) => metric == Metric.Rssi ? "dB" : metric.Unit();

    /// Differenz mit Vorzeichen, z. B. „+18 dB“ oder „-40 ms“.
    public static string FormatDifference(this Metric metric, double difference)
    {
        var rounded = Math.Round(difference, 1, MidpointRounding.AwayFromZero);
        var number = rounded == 0 ? "0" : rounded.ToString("+#,0.#;-#,0.#", German);
        return $"{number} {metric.DifferenceUnit()}";
    }
}

/// Eine Messgröße mit den Grenzwerten, die für ein Projekt gelten. Für den Internet-Download
/// richten sie sich nach der Anschlussrate, sofern der Nutzer sie eingetragen hat.
/// `Reference` ist der Bezugswert für Prozentangaben (Anschlussrate); `null` = keine Prozentangabe.
public readonly record struct MetricScale(Metric Metric, double Good, double Okay, double? Reference = null)
{
    /// Anteil des Abstands gut–okay, unter dem eine Veränderung als Messrauschen gilt.
    public const double UnchangedTolerance = 0.1;

    public Rating Rating(double value) => Metric.HigherIsBetter()
        ? value >= Good ? Logic.Rating.Good : value >= Okay ? Logic.Rating.Okay : Logic.Rating.Critical
        : value <= Good ? Logic.Rating.Good : value <= Okay ? Logic.Rating.Okay : Logic.Rating.Critical;

    /// Stetige Bewertung für die Farbskala: 1 = gut-Grenze (grün), 0,5 = okay-Grenze (gelb),
    /// 0 = noch einmal derselbe Abstand jenseits der okay-Grenze (rot).
    public double Score(double value) => Math.Clamp(0.5 + 0.5 * (value - Okay) / (Good - Okay), 0, 1);

    /// Veränderung von −1 (deutlich schlechter) bis +1 (deutlich besser).
    /// Volle Sättigung bei 1,5 × |gut − okay|.
    public double Change(double before, double after) => Math.Clamp((after - before) / (1.5 * (Good - Okay)), -1, 1);

    /// Richtung einer Veränderung. Unterschiede unter 10 % des Abstands gut–okay
    /// (z. B. unter 1 dB Signal oder 7 ms Ping) zählen als unverändert.
    public Trend Trend(double before, double after)
    {
        var steps = (after - before) / (Good - Okay);
        if (Math.Abs(steps) < UnchangedTolerance) return Logic.Trend.Unchanged;
        return steps > 0 ? Logic.Trend.Better : Logic.Trend.Worse;
    }

    /// Wert mit Einheit, bei bekannter Anschlussrate mit Anteil: „212 Mbit/s (85 %)“.
    public string Describe(double value) => Metric.Format(value) + (ShareText(value, Reference) ?? "");

    /// Anteil am Bezugswert als Zusatz „ (85 %)“; `null` ohne gültigen Bezugswert.
    public static string? ShareText(double? value, double? reference)
    {
        if (value is not { } number || reference is not { } whole || whole <= 0) return null;
        return $" ({(int)Flex.Rounded(number / whole * 100)} %)";
    }
}

/// Farben der Heatmap als sRGB-Anteile 0…1.
public readonly record struct HeatColor(double Red, double Green, double Blue)
{
    public static readonly HeatColor Critical = new(0.84, 0.19, 0.17);
    public static readonly HeatColor Okay = new(0.95, 0.77, 0.10);
    public static readonly HeatColor Good = new(0.16, 0.66, 0.31);
    public static readonly HeatColor Unchanged = new(0.55, 0.55, 0.55);

    public HeatColor Mixed(HeatColor other, double fraction)
    {
        // In dieser Form sind die Endpunkte exakt (fraction 0 bzw. 1).
        var rest = 1 - fraction;
        return new(Red * rest + other.Red * fraction, Green * rest + other.Green * fraction, Blue * rest + other.Blue * fraction);
    }

    /// Rot → Gelb → Grün für eine Bewertung 0…1 (siehe `MetricScale.Score`).
    public static HeatColor Scale(double score)
    {
        score = Math.Clamp(score, 0, 1);
        return score < 0.5 ? Critical.Mixed(Okay, score * 2) : Okay.Mixed(Good, score * 2 - 1);
    }

    /// Grau = unverändert, Grün = besser, Rot = schlechter für eine Veränderung −1…1.
    public static HeatColor Difference(double change)
    {
        change = Math.Clamp(change, -1, 1);
        return Unchanged.Mixed(change >= 0 ? Good : Critical, Math.Abs(change));
    }
}
