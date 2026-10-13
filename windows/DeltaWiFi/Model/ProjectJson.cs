//
//  ProjectJson.cs
//  DeltaWiFi
//
//  Lesen und Schreiben der Projektdatei (`.wlanprojekt`, JSON). Feldnamen und Aufbau
//  entsprechen der macOS-App, damit beide dieselben Dateien öffnen.
//

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeltaWiFi.Model;

public sealed partial class Project
{
    // MARK: Lesen

    public static Project Decode(byte[] data)
    {
        var hasBOM = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF;
        using var document = JsonDocument.Parse(hasBOM ? data.AsMemory(3) : data.AsMemory());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Die Datei enthält kein Projekt.");

        var project = new Project
        {
            Version = Flex.Int(root, "version") ?? 1,
            // Ältere Dateien kennen keine Listen: Dann gelten die vier früheren Standardetagen und die zwei Standardphasen.
            Floors = Items(root, "floors")?.Select(item => new Floor(Flex.String(item, "id") ?? "", Flex.String(item, "name") ?? "")).ToList()
                     ?? [.. Floor.Defaults],
            Phases = Items(root, "phases")?.Select(item => new Phase(Flex.String(item, "id") ?? "", Flex.String(item, "name") ?? "")).ToList()
                     ?? [.. Phase.Defaults],
            LineDownMbit = Flex.Double(root, "line_down_mbit") is > 0 and var down ? down : null,
            LineUpMbit = Flex.Double(root, "line_up_mbit") is > 0 and var up ? up : null,
            Plans = Members(root, "plans").ToDictionary(member => member.Name, member => ReadPlan(member.Value)),
            Points = Items(root, "points")?.Select(ReadPoint).ToList() ?? [],
            Measurements = [],
            History = [],
            Walks = Items(root, "walks")?.Select(ReadWalk).ToList() ?? [],
            Survey = Members(root, "survey").ToDictionary(
                member => member.Name,
                member => (IReadOnlyList<SurveySample>)RequireArray(member.Value, "survey").Select(ReadSample).ToList()),
        };
        foreach (var phase in Members(root, "measurements"))
        {
            var byPoint = new Dictionary<string, Measurement>();
            // Bei gleichen (normalisierten) Namen zählt der erste Eintrag.
            foreach (var point in Members(phase.Value)) byPoint.TryAdd(point.Name.NormalizedPointName(), ReadMeasurement(point.Value));
            project.Measurements[phase.Name] = byPoint;
        }
        foreach (var phase in Members(root, "history"))
        {
            var byPoint = new Dictionary<string, List<Measurement>>();
            foreach (var point in Members(phase.Value))
            {
                byPoint.TryAdd(point.Name.NormalizedPointName(), RequireArray(point.Value, "history").Select(ReadMeasurement).ToList());
            }
            project.History[phase.Name] = byPoint;
        }
        project.Normalize();
        return project;
    }

    /// Einträge einer Liste; `null`, wenn der Schlüssel fehlt.
    private static List<JsonElement>? Items(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return RequireArray(value, key).ToList();
    }

    private static IEnumerable<JsonElement> RequireArray(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : throw new InvalidDataException($"„{name}“ ist keine Liste.");

    private static IEnumerable<JsonProperty> Members(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? Members(value) : [];

    private static IEnumerable<JsonProperty> Members(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object ? value.EnumerateObject() : throw new InvalidDataException("Objekt erwartet.");

    private static Plan ReadPlan(JsonElement item) =>
        new(item.ValueKind == JsonValueKind.Object && item.TryGetProperty("src", out var src) && src.ValueKind == JsonValueKind.String
                ? src.GetString()! : throw new InvalidDataException("Grundriss ohne Bild."),
            Flex.Int(item, "w") ?? 0, Flex.Int(item, "h") ?? 0);

    private static MeasurePoint ReadPoint(JsonElement item)
    {
        var name = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new InvalidDataException("Messpunkt ohne Namen.");
        // Fehlt die Etage, ordnet das Projekt den Punkt beim Laden der ersten Etage zu.
        return new MeasurePoint(name.NormalizedPointName(), Flex.String(item, "floor") ?? "", Flex.Double(item, "x"), Flex.Double(item, "y"));
    }

    private static Measurement ReadMeasurement(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Messung erwartet.");
        string? Text(string key) => Flex.String(item, key)?.Trim() is { Length: > 0 } value ? value : null;
        double? Number(string key) => Flex.Double(item, key);
        int? Whole(string key) => Flex.ToInt(Number(key));

        return new Measurement
        {
            Zeit = Text("zeit"),
            Wiederholung = Whole("wiederholung"),
            Ssid = Text("ssid"),
            Bssid = Text("bssid"),
            BandGHz = Number("band_ghz") is { } band ? Flex.FormatNumber(band) : Text("band_ghz"),
            Kanal = Whole("kanal"),
            BreiteMHz = Whole("breite_mhz"),
            Phy = Text("phy"),
            Nss = Whole("nss"),
            RssiDBm = Number("rssi_dbm"),
            NoiseDBm = Number("noise_dbm"),
            SnrDB = Number("snr_db"),
            TxrateMbit = Number("txrate_mbit"),
            PingGwVerlustPct = Number("ping_gw_verlust_pct"),
            PingGwAvgMs = Number("ping_gw_avg_ms"),
            PingGwMaxMs = Number("ping_gw_max_ms"),
            PingInetVerlustPct = Number("ping_inet_verlust_pct"),
            PingInetAvgMs = Number("ping_inet_avg_ms"),
            PingInetMaxMs = Number("ping_inet_max_ms"),
            InetDownMbit = Number("inet_down_mbit"),
            InetUpMbit = Number("inet_up_mbit"),
            LanDownMbit = Number("lan_down_mbit"),
            LanUpMbit = Number("lan_up_mbit"),
            Notiz = Text("notiz"),
        };
    }

    private static Tap ReadTap(JsonElement item)
    {
        if (Flex.Long(item, "t") is { } t && Flex.Double(item, "x") is { } x && Flex.Double(item, "y") is { } y) return new Tap(t, x, y);
        throw new InvalidDataException("Tap braucht t, x und y.");
    }

    private static Walk ReadWalk(JsonElement item)
    {
        var taps = Items(item, "taps")?.Select(ReadTap).ToList() ?? [];
        var started = Flex.Long(item, "started") ?? taps.FirstOrDefault()?.T ?? 0;
        return new Walk(Flex.String(item, "id") ?? $"w{started}", Flex.String(item, "phase") ?? "", Flex.String(item, "floor") ?? "",
                        started, taps);
    }

    private static SurveySample ReadSample(JsonElement item)
    {
        var values = RequireArray(item, "sample").ToList();
        double? Number(int index) => index < values.Count ? Flex.AsDouble(values[index]) : null;
        string? Text(int index) => index < values.Count ? Flex.AsString(values[index]) : null;
        if (Number(0) is not { } x || Number(1) is not { } y) throw new InvalidDataException("Sample braucht x und y.");
        return new SurveySample(x, y, Number(2), Number(3), Number(4), Text(5), Text(6) is { Length: > 0 } bssid ? bssid : null);
    }

    // MARK: Schreiben

    public byte[] Encoded()
    {
        var root = new JsonObject
        {
            ["version"] = Version,
            ["floors"] = new JsonArray(Floors.Select(floor => (JsonNode)new JsonObject { ["id"] = floor.Id, ["name"] = floor.Name }).ToArray()),
            ["phases"] = new JsonArray(Phases.Select(phase => (JsonNode)new JsonObject { ["id"] = phase.Id, ["name"] = phase.Name }).ToArray()),
            ["plans"] = Object(Plans, plan => new JsonObject { ["src"] = plan.Src, ["w"] = plan.W, ["h"] = plan.H }),
            // Nicht gesetzte Positionen werden ausdrücklich als `null` geschrieben.
            ["points"] = new JsonArray(Points.Select(point => (JsonNode)new JsonObject
            {
                ["name"] = point.Name, ["floor"] = point.Floor, ["x"] = point.X, ["y"] = point.Y,
            }).ToArray()),
            ["measurements"] = Object(Measurements, byPoint => Object(byPoint, WriteMeasurement)),
            ["history"] = Object(History, byPoint => Object(byPoint, all => new JsonArray(all.Select(WriteMeasurement).ToArray()))),
            ["walks"] = new JsonArray(Walks.Select(walk => (JsonNode)new JsonObject
            {
                ["id"] = walk.Id, ["phase"] = walk.Phase, ["floor"] = walk.Floor, ["started"] = walk.Started,
                ["taps"] = new JsonArray(walk.Taps.Select(tap => (JsonNode)new JsonObject { ["t"] = tap.T, ["x"] = tap.X, ["y"] = tap.Y }).ToArray()),
            }).ToArray()),
            ["survey"] = Object(Survey, samples => new JsonArray(samples.Select(WriteSample).ToArray())),
        };
        // Ohne Eingabe stehen die Schlüssel der Anschlussrate nicht in der Datei.
        if (LineDownMbit is { } down) root["line_down_mbit"] = down;
        if (LineUpMbit is { } up) root["line_up_mbit"] = up;

        using var stream = new MemoryStream();
        // Umlaute und Schrägstriche unmaskiert, sonst blähen sich die Base64-Grundrisse auf.
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteSorted(writer, root);
        }
        return stream.ToArray();
    }

    /// Inhalt als Text; zwei Projekte mit gleichem Inhalt liefern denselben Text.
    public string ToJson() => Encoding.UTF8.GetString(Encoded());

    private static JsonObject Object<T>(IReadOnlyDictionary<string, T> entries, Func<T, JsonNode> write)
    {
        var result = new JsonObject();
        foreach (var (key, value) in entries) result[key] = write(value);
        return result;
    }

    private static JsonNode WriteMeasurement(Measurement m)
    {
        var result = new JsonObject();
        void Text(string key, string? value) { if (value != null) result[key] = value; }
        void Number(string key, double? value) { if (value is { } number) result[key] = number; }
        void Whole(string key, int? value) { if (value is { } number) result[key] = number; }

        Text("zeit", m.Zeit);
        Whole("wiederholung", m.Wiederholung);
        Text("ssid", m.Ssid);
        Text("bssid", m.Bssid);
        Text("band_ghz", m.BandGHz);
        Whole("kanal", m.Kanal);
        Whole("breite_mhz", m.BreiteMHz);
        Text("phy", m.Phy);
        Whole("nss", m.Nss);
        Number("rssi_dbm", m.RssiDBm);
        Number("noise_dbm", m.NoiseDBm);
        Number("snr_db", m.SnrDB);
        Number("txrate_mbit", m.TxrateMbit);
        Number("ping_gw_verlust_pct", m.PingGwVerlustPct);
        Number("ping_gw_avg_ms", m.PingGwAvgMs);
        Number("ping_gw_max_ms", m.PingGwMaxMs);
        Number("ping_inet_verlust_pct", m.PingInetVerlustPct);
        Number("ping_inet_avg_ms", m.PingInetAvgMs);
        Number("ping_inet_max_ms", m.PingInetMaxMs);
        Number("inet_down_mbit", m.InetDownMbit);
        Number("inet_up_mbit", m.InetUpMbit);
        Number("lan_down_mbit", m.LanDownMbit);
        Number("lan_up_mbit", m.LanUpMbit);
        Text("notiz", m.Notiz);
        return result;
    }

    private static JsonNode WriteSample(SurveySample sample)
    {
        var result = new JsonArray(sample.X, sample.Y, sample.Rssi, sample.Snr, sample.Rate, sample.Band);
        // Ohne BSSID bleibt es bei sechs Elementen.
        if (sample.Bssid != null) result.Add(sample.Bssid);
        return result;
    }

    /// Schreibt Objekte mit sortierten Schlüsseln, damit die Datei bei gleichem Inhalt gleich bleibt.
    private static void WriteSorted(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var (key, value) in obj.OrderBy(member => member.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    WriteSorted(writer, value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array) WriteSorted(writer, item);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}
