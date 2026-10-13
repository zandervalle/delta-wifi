//
//  Heatmap.cs
//  DeltaWiFi
//
//  Interpolation der Messwerte über den Plan (inverse Distanzgewichtung)
//  und Umsetzung in Pixel.
//

using DeltaWiFi.Model;

namespace DeltaWiFi.Logic;

/// Ein Messwert mit Position relativ 0…1 auf dem Plan.
public readonly record struct FieldValue(double X, double Y, double Value);

/// Alle Werte einer Etage und Phase für eine Messgröße.
public sealed class FieldSource
{
    public List<FieldValue> Values { get; init; } = [];
    /// Mit Lauf-Daten liegen die Werte dicht, die Unsicherheit wächst dann schneller mit dem Abstand.
    public bool HasWalkData { get; set; }

    public bool ContentEquals(FieldSource? other) =>
        other != null && HasWalkData == other.HasWalkData && Values.SequenceEqual(other.Values);
}

public sealed record ScalarField(int Width, int Height, double[] Values, double[] Nearest);

/// Was die Karte zeigt: eine Phase oder (mit `CompareTo`) den Unterschied zwischen zwei frei gewählten Phasen.
public readonly record struct HeatmapView(string Phase, string? CompareTo = null);

/// Eingabe der Heatmap: Messgröße mit den Grenzwerten des Projekts, Breite / Höhe des Plans und die Werte.
/// Im Vergleich ist `Source` die Ausgangsphase und `CompareTo` die Zielphase.
public sealed record HeatmapInput(MetricScale Scale, double Aspect, FieldSource Source, FieldSource? CompareTo = null)
{
    public bool IsComparison => CompareTo != null;

    public bool ContentEquals(HeatmapInput? other) =>
        other != null && Scale == other.Scale && Aspect == other.Aspect && Source.ContentEquals(other.Source)
        && (CompareTo == null ? other.CompareTo == null : CompareTo.ContentEquals(other.CompareTo));
}

public static class Heatmap
{
    public const double MaxOpacity = 0.58;
    public const double MinOpacity = 0.18;
    /// Abstand (in Planhöhen), ab dem die Fläche nur noch mit Mindestdeckkraft gezeichnet wird.
    public const double FadeDistance = 0.32;
    public const double FadeDistanceWithWalkData = 0.18;
    /// Längere Kante des berechneten Rasters; das Bild wird beim Anzeigen weich hochskaliert.
    public const int GridLongSide = 160;

    /// `1 / (d² + 0.0004)^1.25`: Potenz 2,5, an den Messpunkten geglättet.
    public static double Weight(double distanceSquared)
    {
        var b = distanceSquared + 0.0004;
        return 1 / (b * Math.Sqrt(Math.Sqrt(b)));
    }

    public static (int Width, int Height) GridSize(double aspect, int longSide = GridLongSide)
    {
        if (!double.IsFinite(aspect) || aspect <= 0) aspect = 1;
        return aspect >= 1 ? (longSide, Math.Max(1, (int)Flex.Rounded(longSide / aspect)))
                           : (Math.Max(1, (int)Flex.Rounded(longSide * aspect)), longSide);
    }

    /// Inverse Distanzgewichtung über alle Werte. Distanzen seitenverhältnis-korrigiert:
    /// x wird mit Breite/Höhe multipliziert, y bleibt 0…1.
    public static ScalarField? Interpolate(IReadOnlyList<FieldValue> source, double aspect, int width, int height)
    {
        if (source.Count == 0 || width <= 0 || height <= 0) return null;
        var xs = source.Select(value => value.X * aspect).ToArray();
        var ys = source.Select(value => value.Y).ToArray();
        var vs = source.Select(value => value.Value).ToArray();
        var values = new double[width * height];
        var nearest = new double[width * height];

        Parallel.For(0, height, row =>
        {
            var py = (row + 0.5) / height;
            for (var column = 0; column < width; column++)
            {
                var px = (column + 0.5) / width * aspect;
                double weighted = 0, total = 0, minimum = double.PositiveInfinity;
                for (var index = 0; index < vs.Length; index++)
                {
                    var dx = xs[index] - px;
                    var dy = ys[index] - py;
                    var squared = dx * dx + dy * dy;
                    var w = Weight(squared);
                    weighted += w * vs[index];
                    total += w;
                    if (squared < minimum) minimum = squared;
                }
                values[row * width + column] = weighted / total;
                nearest[row * width + column] = Math.Sqrt(minimum);
            }
        });
        return new ScalarField(width, height, values, nearest);
    }

    /// Deckkraft nach Abstand zum nächsten Messwert: nah kräftig, weit weg blass.
    public static double Opacity(double nearest, bool hasWalkData) =>
        MaxOpacity - (MaxOpacity - MinOpacity) * Fade(nearest, hasWalkData);

    private static double Fade(double distance, bool hasWalkData)
    {
        var fraction = Math.Clamp(distance / (hasWalkData ? FadeDistanceWithWalkData : FadeDistance), 0, 1);
        return fraction * fraction;
    }

    /// Pixel der Heatmap als vormultipliziertes RGBA, zeilenweise von oben links.
    /// `null`, wenn es nichts zu zeichnen gibt (im Vergleich: sobald eine der beiden Phasen fehlt).
    public static byte[]? Rgba(HeatmapInput input, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        void Set(int index, HeatColor color, double alpha)
        {
            pixels[index * 4] = (byte)Flex.Rounded(color.Red * alpha * 255);
            pixels[index * 4 + 1] = (byte)Flex.Rounded(color.Green * alpha * 255);
            pixels[index * 4 + 2] = (byte)Flex.Rounded(color.Blue * alpha * 255);
            pixels[index * 4 + 3] = (byte)Flex.Rounded(alpha * 255);
        }

        if (input.CompareTo is { } to)
        {
            var from = input.Source;
            if (Interpolate(from.Values, input.Aspect, width, height) is not { } before
                || Interpolate(to.Values, input.Aspect, width, height) is not { } after) return null;
            for (var index = 0; index < before.Values.Length; index++)
            {
                // Die unsicherere der beiden Phasen bestimmt die Deckkraft.
                var fading = Math.Max(Fade(before.Nearest[index], from.HasWalkData), Fade(after.Nearest[index], to.HasWalkData));
                Set(index, HeatColor.Difference(input.Scale.Change(before.Values[index], after.Values[index])),
                    MaxOpacity - (MaxOpacity - MinOpacity) * fading);
            }
        }
        else
        {
            if (Interpolate(input.Source.Values, input.Aspect, width, height) is not { } field) return null;
            for (var index = 0; index < field.Values.Length; index++)
            {
                Set(index, HeatColor.Scale(input.Scale.Score(field.Values[index])),
                    Opacity(field.Nearest[index], input.Source.HasWalkData));
            }
        }
        return pixels;
    }

    /// Breite/Höhe des Plans; ohne (gültiges) Bild das Verhältnis des Platzhalters (1600 × 1120).
    public static double AspectRatio(Plan? plan) => plan is { W: > 0, H: > 0 } ? (double)plan.W / plan.H : 1600.0 / 1120.0;

    /// Alle verorteten Werte einer Etage und Phase: feste Punkte plus Lauf-Samples.
    public static FieldSource FieldSource(this Project project, Metric metric, string floor, string phase)
    {
        var source = new FieldSource();
        foreach (var point in project.Points.Where(point => point.Floor == floor))
        {
            if (point.X is not { } x || point.Y is not { } y) continue;
            if (project.Value(metric, point.Name, phase) is { } value) source.Values.Add(new FieldValue(x, y, value));
        }
        if (!metric.IsAvailableFromWalks()) return source;
        foreach (var walk in project.Walks.Where(walk => walk.Floor == floor && walk.Phase == phase))
        {
            foreach (var sample in project.Survey.GetValueOrDefault(walk.Id) ?? [])
            {
                if (metric.Value(sample) is not { } value) continue;
                source.Values.Add(new FieldValue(sample.X, sample.Y, value));
                source.HasWalkData = true;
            }
        }
        return source;
    }

    public static HeatmapInput HeatmapInput(this Project project, Metric metric, string floor, HeatmapView view) =>
        new(project.Scale(metric), AspectRatio(project.Plans.GetValueOrDefault(floor)),
            project.FieldSource(metric, floor, view.Phase),
            view.CompareTo is { } to ? project.FieldSource(metric, floor, to) : null);
}
