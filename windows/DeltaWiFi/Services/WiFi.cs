//
//  WiFi.cs
//  DeltaWiFi
//
//  Funkwerte über die Windows-WLAN-API (wlanapi.dll) sowie Gateway und IP-Adresse des WLAN-Adapters.
//

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using DeltaWiFi.Logic;

namespace DeltaWiFi.Services;

public static class WiFi
{
    private const int ErrorAccessDenied = 5;
    private const int InterfaceStateConnected = 1;

    // Abfragen von `WlanQueryInterface`
    private const int OpcodeCurrentConnection = 7;
    private const int OpcodeChannelNumber = 8;
    /// Ab Windows 11 24H2: Signal, Frequenz und Kanalbreite der laufenden Verbindung.
    private const int OpcodeRealtimeConnectionQuality = 19;
    private const int OpcodeRssi = 0x10000102;

    private static readonly Lock Gate = new();
    private static IntPtr handle;

    /// GUID des WLAN-Adapters, mit dem zuletzt gemessen wurde.
    public static Guid? InterfaceId { get; private set; }

    /// Windows verweigert SSID und BSSID, solange die Ortungsdienste für Desktop-Apps aus sind.
    public static bool IsAccessDenied { get; private set; }

    /// Aktuelle Funkwerte; `null`, wenn WLAN aus oder nicht verbunden ist.
    public static RadioSnapshot? Snapshot()
    {
        lock (Gate)
        {
            if (handle == IntPtr.Zero && WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0)
            {
                handle = IntPtr.Zero;
                return null;
            }
            if (ConnectedInterface() is not { } id)
            {
                InterfaceId = null;
                return null;
            }
            InterfaceId = id;
            return Snapshot(id);
        }
    }

    /// Der erste verbundene WLAN-Adapter.
    private static Guid? ConnectedInterface()
    {
        if (WlanEnumInterfaces(handle, IntPtr.Zero, out var list) != 0) return null;
        try
        {
            // WLAN_INTERFACE_INFO_LIST: Anzahl, Index, dann je Adapter GUID (16), Beschreibung (512), Zustand (4).
            var count = Marshal.ReadInt32(list, 0);
            for (var index = 0; index < count; index++)
            {
                var entry = list + 8 + index * 532;
                if (Marshal.ReadInt32(entry, 528) == InterfaceStateConnected) return Marshal.PtrToStructure<Guid>(entry);
            }
            return null;
        }
        finally
        {
            WlanFreeMemory(list);
        }
    }

    private static RadioSnapshot? Snapshot(Guid id)
    {
        string? ssid = null, bssid = null, phy = null;
        byte[]? mac = null;
        int? rssi = null, frequency = null, width = null, quality = null;
        double? txRate = null;

        // WLAN_CONNECTION_ATTRIBUTES: Zustand (0), Modus (4), Profilname (8…520), dann WLAN_ASSOCIATION_ATTRIBUTES:
        // SSID-Länge (520), SSID (524), BSSID (560), PHY-Typ (568), Signalqualität (576), Rx-Rate (580), Tx-Rate (584) in kbit/s.
        var result = WlanQueryInterface(handle, ref id, OpcodeCurrentConnection, IntPtr.Zero, out var size, out var data, IntPtr.Zero);
        IsAccessDenied = result == ErrorAccessDenied;
        if (result == 0)
        {
            try
            {
                if (size < 588 || Marshal.ReadInt32(data, 0) != InterfaceStateConnected) return null;
                var ssidBytes = new byte[Math.Clamp(Marshal.ReadInt32(data, 520), 0, 32)];
                Marshal.Copy(data + 524, ssidBytes, 0, ssidBytes.Length);
                ssid = Encoding.UTF8.GetString(ssidBytes);
                mac = new byte[6];
                Marshal.Copy(data + 560, mac, 0, 6);
                bssid = string.Join(":", mac.Select(part => part.ToString("x2")));
                phy = RadioChannel.Phy(Marshal.ReadInt32(data, 568));
                quality = Marshal.ReadInt32(data, 576);
                txRate = Rate(Marshal.ReadInt32(data, 584));
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }
        else if (result != ErrorAccessDenied)
        {
            return null;
        }

        // WLAN_REALTIME_CONNECTION_QUALITY: PHY (0), Qualität (4), Rx (8), Tx (12), MLO (16), Anzahl Links (20),
        // dann je Link: ID (24), Mittenfrequenz in MHz (28), Bandbreite in MHz (32), RSSI (36).
        if (WlanQueryInterface(handle, ref id, OpcodeRealtimeConnectionQuality, IntPtr.Zero, out size, out data, IntPtr.Zero) == 0)
        {
            try
            {
                if (size >= 40 && Marshal.ReadInt32(data, 20) >= 1)
                {
                    phy ??= RadioChannel.Phy(Marshal.ReadInt32(data, 0));
                    txRate ??= Rate(Marshal.ReadInt32(data, 12));
                    frequency = Positive(Marshal.ReadInt32(data, 28));
                    // Viele Treiber melden hier 0; dann hilft unten der Beacon des Access Points.
                    width = Positive(Marshal.ReadInt32(data, 32));
                    rssi = Negative(Marshal.ReadInt32(data, 36));
                }
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }
        rssi ??= Negative(QueryInt(id, OpcodeRssi));

        if (mac != null && (rssi == null || frequency == null || width == null)) ReadBeacon(id, mac, ref rssi, ref frequency, ref width);

        // Letzter Ausweg: Windows rechnet die Signalqualität linear aus −100…−50 dBm.
        rssi ??= quality is > 0 ? quality.Value / 2 - 100 : null;
        if (rssi is not { } signal) return null;

        return new RadioSnapshot(signal, null, txRate,
                                 frequency is { } mhz ? RadioChannel.Band(mhz) : null,
                                 (frequency is { } center ? RadioChannel.Channel(center) : null) ?? Positive(QueryInt(id, OpcodeChannelNumber)),
                                 width, phy, bssid, ssid);
    }

    /// Ergänzt fehlende Werte aus dem zuletzt empfangenen Beacon des verbundenen Access Points.
    private static void ReadBeacon(Guid id, byte[] mac, ref int? rssi, ref int? frequency, ref int? width)
    {
        if (WlanGetNetworkBssList(handle, ref id, IntPtr.Zero, 3, false, IntPtr.Zero, out var list) != 0) return;
        try
        {
            // WLAN_BSS_LIST: Gesamtgröße, Anzahl, dann je Eintrag 360 Bytes: BSSID (40), RSSI (56),
            // Mittenfrequenz in kHz (92), Lage (352) und Größe (356) der Information Elements.
            var total = Marshal.ReadInt32(list, 0);
            var count = Marshal.ReadInt32(list, 4);
            var entryMac = new byte[6];
            for (var index = 0; index < count; index++)
            {
                var entry = list + 8 + index * 360;
                Marshal.Copy(entry + 40, entryMac, 0, 6);
                if (!entryMac.AsSpan().SequenceEqual(mac)) continue;

                rssi ??= Negative(Marshal.ReadInt32(entry, 56));
                frequency ??= Positive(Marshal.ReadInt32(entry, 92) / 1000);
                var offset = Marshal.ReadInt32(entry, 352);
                var length = Marshal.ReadInt32(entry, 356);
                if (width == null && offset > 0 && length > 0 && 8 + index * 360 + (long)offset + length <= total)
                {
                    var elements = new byte[length];
                    Marshal.Copy(entry + offset, elements, 0, length);
                    width = RadioChannel.Width(elements);
                }
                return;
            }
        }
        finally
        {
            WlanFreeMemory(list);
        }
    }

    private static int? QueryInt(Guid id, int opcode)
    {
        if (WlanQueryInterface(handle, ref id, opcode, IntPtr.Zero, out var size, out var data, IntPtr.Zero) != 0) return null;
        try
        {
            return size >= 4 ? Marshal.ReadInt32(data) : null;
        }
        finally
        {
            WlanFreeMemory(data);
        }
    }

    private static double? Rate(int kilobit) => kilobit > 0 ? kilobit / 1000.0 : null;
    private static int? Positive(int? value) => value > 0 ? value : null;
    /// Pegel in dBm sind negativ; 0 und positive Werte melden Treiber, wenn sie nichts wissen.
    private static int? Negative(int? value) => value is < 0 and > -200 ? value : null;

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(int clientVersion, IntPtr reserved, out int negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opcode, IntPtr reserved,
                                                 out int dataSize, out IntPtr data, IntPtr opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern int WlanGetNetworkBssList(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr ssid, int bssType,
                                                    [MarshalAs(UnmanagedType.Bool)] bool securityEnabled, IntPtr reserved, out IntPtr bssList);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}

public static class NetworkInfo
{
    public const string FallbackGateway = "192.168.178.1";
    public const string InternetTarget = "1.1.1.1";

    private static IPInterfaceProperties? Properties(Guid id) =>
        NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(candidate => Guid.TryParse(candidate.Id, out var guid) && guid == id)?.GetIPProperties();

    /// Gateway des WLAN-Adapters (falls zusätzlich ein Kabel steckt, zählt trotzdem das WLAN).
    public static string Gateway(Guid id) =>
        Properties(id)?.GatewayAddresses
            .Select(gateway => gateway.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any))?.ToString()
        ?? FallbackGateway;

    /// IPv4-Adresse des Adapters; Ping, Speedtest und iperf3 senden von ihr aus, damit sie über das WLAN laufen.
    public static IPAddress? IPv4Address(Guid id) =>
        Properties(id)?.UnicastAddresses
            .Select(unicast => unicast.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);

    /// Pfad zu `iperf3.exe`: neben der App, im PATH oder in den WinGet-Verknüpfungen.
    public static string? IperfPath
    {
        get
        {
            var folders = new List<string> { AppContext.BaseDirectory };
            folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
            folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Links"));
            return folders.Select(folder => Path.Combine(folder.Trim(), "iperf3.exe")).FirstOrDefault(File.Exists);
        }
    }
}
