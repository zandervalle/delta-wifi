//
//  Summary.cs
//  DeltaWiFi
//
//  Auswertung der festen Messpunkte: Übersicht je Phase und Veränderung
//  zwischen zwei Phasen.
//

using DeltaWiFi.Model;

namespace DeltaWiFi.Logic;

public enum Trend { Better, Worse, Unchanged }

/// Kennzahlen einer Phase für eine Messgröße, über alle Punkte mit Wert.
public sealed record PhaseSummary(double? Median = null, int Good = 0, int Okay = 0, int Critical = 0)
{
    public int Count => Good + Okay + Critical;
}

/// Wie viele Punkte sich zwischen zwei Phasen verbessert oder verschlechtert haben.
public sealed record ChangeSummary(int Better = 0, int Worse = 0, int Unchanged = 0)
{
    /// Punkte, die in beiden Phasen einen Wert haben.
    public int Compared => Better + Worse + Unchanged;
}

public static class Summary
{
    /// Anteil der Anschlussrate, ab dem der Internet-Download als gut bzw. okay gilt.
    public const double LineGoodShare = 0.8;
    public const double LineOkayShare = 0.5;

    public static Trend Trend(this Metric metric, double before, double after) => metric.Scale().Trend(before, after);

    /// Grenzwerte einer Messgröße für dieses Projekt. Ist die Anschlussrate eingetragen,
    /// wird der Internet-Download an ihr gemessen statt an festen Mbit/s.
    public static MetricScale Scale(this Project project, Metric metric)
    {
        if (metric != Metric.InternetDown || project.LineDownMbit is not > 0) return metric.Scale();
        var line = project.LineDownMbit.Value;
        return new MetricScale(metric, LineGoodShare * line, LineOkayShare * line, line);
    }

    public static double? Value(this Project project, Metric metric, string point, string phase) =>
        project.Measurement(point, phase) is { } measurement ? metric.Value(measurement) : null;

    public static PhaseSummary PhaseSummary(this Project project, Metric metric, string phase)
    {
        var values = project.Points.Select(point => project.Value(metric, point.Name, phase)).OfType<double>().ToList();
        var scale = project.Scale(metric);
        return new PhaseSummary(MeasurementParsing.Median(values),
                                values.Count(value => scale.Rating(value) == Rating.Good),
                                values.Count(value => scale.Rating(value) == Rating.Okay),
                                values.Count(value => scale.Rating(value) == Rating.Critical));
    }

    public static ChangeSummary ChangeSummary(this Project project, Metric metric, string from, string to)
    {
        int better = 0, worse = 0, unchanged = 0;
        var scale = project.Scale(metric);
        foreach (var point in project.Points)
        {
            if (project.Value(metric, point.Name, from) is not { } before || project.Value(metric, point.Name, to) is not { } after) continue;
            switch (scale.Trend(before, after))
            {
                case Logic.Trend.Better: better++; break;
                case Logic.Trend.Worse: worse++; break;
                default: unchanged++; break;
            }
        }
        return new ChangeSummary(better, worse, unchanged);
    }
}
