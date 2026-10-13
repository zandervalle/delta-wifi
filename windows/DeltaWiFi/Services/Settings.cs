//
//  Settings.cs
//  DeltaWiFi
//
//  Einstellungen, die nicht zum Projekt gehören (Speedtest an/aus, iperf3-Server …),
//  als JSON unter %APPDATA%\DeltaWiFi.
//

using System.Text.Json;

namespace DeltaWiFi.Services;

public sealed class Settings
{
    public bool SpeedtestEnabled { get; set; } = true;
    public bool IperfEnabled { get; set; }
    public string IperfServer { get; set; } = "";
    public bool ShowWalkPaths { get; set; } = true;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeltaWiFi", "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Einstellungen sind Komfort; ohne Schreibrecht gelten sie eben nur für diese Sitzung.
        }
    }
}
