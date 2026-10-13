//
//  FlexibleDecoding.cs
//  DeltaWiFi
//
//  Nachsichtiges Lesen von Zahlen und Texten: Ältere Projektdateien liefern
//  Zahlen teils als Text ("-79", "1,7") oder leer.
//

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DeltaWiFi.Model;

public static class Flex
{
    /// Zahl aus Text lesen. Akzeptiert Dezimalkomma, liefert `null` für leere oder unlesbare Werte.
    public static double? ParseNumber(string? text)
    {
        if (text == null) return null;
        var trimmed = text.Trim().Replace(',', '.');
        if (trimmed.Length == 0) return null;
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : null;
    }

    /// Ganze Zahlen ohne Nachkommastelle ("5"), sonst wie üblich ("2.4").
    public static string FormatNumber(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 1e15) return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// Punktnamen vergleichbar machen: getrimmt und Unicode-normalisiert (ä als ein Zeichen).
    public static string NormalizedPointName(this string name) => name.Trim().Normalize(NormalizationForm.FormC);

    /// Rundet wie Swift: bei ,5 vom Nullpunkt weg.
    public static double Rounded(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    // MARK: JSON

    public static double? AsDouble(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetDouble(out var number) && double.IsFinite(number) ? number : null,
        JsonValueKind.String => ParseNumber(element.GetString()),
        _ => null,
    };

    public static string? AsString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetDouble(out var number) ? FormatNumber(number) : null,
        JsonValueKind.String => element.GetString(),
        _ => null,
    };

    private static JsonElement? Member(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var value) ? value : null;

    public static double? Double(JsonElement obj, string key) => Member(obj, key) is { } value ? AsDouble(value) : null;

    public static string? String(JsonElement obj, string key) => Member(obj, key) is { } value ? AsString(value) : null;

    public static int? Int(JsonElement obj, string key) => ToInt(Double(obj, key));

    public static long? Long(JsonElement obj, string key)
    {
        if (Double(obj, key) is not { } value) return null;
        var rounded = Rounded(value);
        return Math.Abs(rounded) < 9e18 ? (long)rounded : null;
    }

    public static int? ToInt(double? value)
    {
        if (value is not { } number) return null;
        var rounded = Rounded(number);
        return rounded is >= int.MinValue and <= int.MaxValue ? (int)rounded : null;
    }
}
