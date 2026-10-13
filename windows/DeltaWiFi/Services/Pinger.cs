//
//  Pinger.cs
//  DeltaWiFi
//
//  ICMP-Ping über die Windows-API (`IcmpSendEcho2Ex`), mit fester Absenderadresse,
//  damit die Anfragen über den WLAN-Adapter gehen.
//

using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using DeltaWiFi.Logic;

namespace DeltaWiFi.Services;

public static class Pinger
{
    /// Wie lange auf eine Antwort gewartet wird. Großzügig, damit ein mehrsekündiger Hänger
    /// als langer Ping erscheint und nicht nur als Verlust.
    public const int TimeoutMs = 4000;

    /// Sendet `count` Anfragen im Sekundentakt und wertet sie aus. `null`, wenn sich gar nichts senden ließ.
    public static async Task<PingResult?> RunAsync(string target, IPAddress? source, int count, CancellationToken cancellation)
    {
        if (!IPAddress.TryParse(target, out var address)) return null;
        var pending = new List<Task<double?>>();
        var clock = Stopwatch.StartNew();
        for (var index = 0; index < count; index++)
        {
            // Jede Anfrage läuft für sich, damit eine späte Antwort den Sekundentakt nicht verschiebt.
            pending.Add(Task.Run(() => Send(address, source), CancellationToken.None));
            var next = TimeSpan.FromSeconds(index + 1) - clock.Elapsed;
            if (index < count - 1 && next > TimeSpan.Zero) await Task.Delay(next, cancellation);
        }
        var roundTrips = await Task.WhenAll(pending).WaitAsync(cancellation);
        return MeasurementParsing.Ping(roundTrips);
    }

    /// Eine Anfrage; Laufzeit in ms oder `null` ohne Antwort.
    private static double? Send(IPAddress target, IPAddress? source)
    {
        var icmp = IcmpCreateFile();
        if (icmp == IntPtr.Zero || icmp == new IntPtr(-1)) return null;
        // ICMP_ECHO_REPLY (40 Bytes) plus Nutzdaten und Platz für eine ICMP-Fehlermeldung.
        const int replySize = 40 + 32 + 16;
        var reply = Marshal.AllocHGlobal(replySize);
        try
        {
            var payload = new byte[32];
            var clock = Stopwatch.StartNew();
            var replies = IcmpSendEcho2Ex(icmp, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, Number(source), Number(target),
                                          payload, (ushort)payload.Length, IntPtr.Zero, reply, replySize, TimeoutMs);
            var elapsed = clock.Elapsed.TotalMilliseconds;
            // Status 0 = Antwort vom Ziel; alles andere (Zeitüberschreitung, „nicht erreichbar“) zählt als Verlust.
            return replies > 0 && Marshal.ReadInt32(reply, 4) == 0 ? elapsed : null;
        }
        finally
        {
            Marshal.FreeHGlobal(reply);
            IcmpCloseHandle(icmp);
        }
    }

    private static uint Number(IPAddress? address) => address == null ? 0 : BitConverter.ToUInt32(address.GetAddressBytes(), 0);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern IntPtr IcmpCreateFile();

    [DllImport("iphlpapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IcmpCloseHandle(IntPtr icmpHandle);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint IcmpSendEcho2Ex(IntPtr icmpHandle, IntPtr eventHandle, IntPtr apcRoutine, IntPtr apcContext,
                                               uint sourceAddress, uint destinationAddress, byte[] requestData, ushort requestSize,
                                               IntPtr requestOptions, IntPtr replyBuffer, int replySize, int timeout);
}
