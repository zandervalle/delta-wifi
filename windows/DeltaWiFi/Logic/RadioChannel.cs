//
//  RadioChannel.cs
//  DeltaWiFi
//
//  Band, Kanal, PHY-Modus und Kanalbreite aus den Rohwerten der Windows-WLAN-API.
//

namespace DeltaWiFi.Logic;

public static class RadioChannel
{
    /// Band als Text wie in der Projektdatei ("2.4", "5", "6") aus der Mittenfrequenz in MHz.
    public static string? Band(int frequencyMHz) => frequencyMHz switch
    {
        >= 2400 and < 2500 => "2.4",
        >= 5000 and < 5925 => "5",
        >= 5925 and <= 7125 => "6",
        _ => null,
    };

    /// Kanalnummer aus der Mittenfrequenz in MHz.
    public static int? Channel(int frequencyMHz) => frequencyMHz switch
    {
        2484 => 14,
        >= 2412 and <= 2472 => (frequencyMHz - 2407) / 5,
        >= 5000 and < 5925 => (frequencyMHz - 5000) / 5,
        >= 5955 and <= 7115 => (frequencyMHz - 5950) / 5,
        _ => null,
    };

    /// PHY-Modus aus `DOT11_PHY_TYPE`.
    public static string? Phy(int type) => type switch
    {
        4 => "11a",
        5 => "11b",
        6 => "11g",
        7 => "11n",
        8 => "11ac",
        9 => "11ad",
        10 => "11ax",
        11 => "11be",
        <= 0 => null,
        _ => $"PHY {type}",
    };

    /// Kanalbreite in MHz, mit der der Access Point sendet, aus den Information Elements seines Beacons:
    /// HT Operation (61) für 20/40 MHz, VHT Operation (192) für 80/160 MHz, im 6-GHz-Band HE Operation.
    /// `null`, wenn der Beacon nichts dazu sagt.
    public static int? Width(ReadOnlySpan<byte> elements)
    {
        int? ht = null, vht = null, he = null;
        while (elements.Length >= 2)
        {
            int id = elements[0], length = elements[1];
            if (elements.Length < 2 + length) break;
            var body = elements.Slice(2, length);
            elements = elements[(2 + length)..];

            if (id == 61 && body.Length >= 2)
            {
                // Sekundärkanal oberhalb (1) oder unterhalb (3) des Primärkanals bedeutet 40 MHz.
                ht = (body[1] & 0x03) is 1 or 3 ? 40 : 20;
            }
            else if (id == 192 && body.Length >= 3)
            {
                vht = body[0] switch
                {
                    // 80 MHz; mit einem zweiten Segment sind es 160 MHz (am Stück oder als 80+80).
                    1 => body[2] != 0 ? 160 : 80,
                    2 or 3 => 160,
                    _ => null,
                };
            }
            else if (id == 255 && body.Length >= 7 && body[0] == 36)
            {
                he = HeWidth(body[1..]);
            }
        }
        return he ?? vht ?? ht;
    }

    /// „6 GHz Operation Information“ aus HE Operation; nur im 6-GHz-Band vorhanden.
    private static int? HeWidth(ReadOnlySpan<byte> body)
    {
        var parameters = body[0] | body[1] << 8 | body[2] << 16;
        if ((parameters & 1 << 17) == 0) return null;
        // Parameter (3), BSS Color (1), MCS-Satz (2), dann wahlweise VHT-Angaben (3) und Co-Hosted BSS (1).
        var offset = 6 + ((parameters & 1 << 14) != 0 ? 3 : 0) + ((parameters & 1 << 15) != 0 ? 1 : 0);
        if (body.Length < offset + 2) return null;
        return (body[offset + 1] & 0x03) switch { 0 => 20, 1 => 40, 2 => 80, _ => 160 };
    }
}
