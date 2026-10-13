//
//  SpeedTest.cs
//  DeltaWiFi
//
//  Internet-Speedtest gegen speed.cloudflare.com: mehrere parallele HTTP-Ströme,
//  gemessen wird der Durchsatz nach einer kurzen Anlaufzeit. Ersetzt `networkQuality`
//  der macOS-App; die Werte sind deshalb nur grob mit denen vom Mac vergleichbar.
//

using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using DeltaWiFi.Logic;

namespace DeltaWiFi.Services;

public static class SpeedTest
{
    public const int Streams = 4;
    /// Dauer je Richtung; die Anlaufzeit (TCP fährt erst hoch) zählt nicht mit.
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(2);

    private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=50000000";
    private const string UploadUrl = "https://speed.cloudflare.com/__up";
    private const int UploadSize = 25_000_000;

    /// Download in Mbit/s; `null`, wenn nichts ankam.
    public static Task<double?> DownloadAsync(IPAddress? source, CancellationToken cancellation) =>
        MeasureAsync(source, cancellation, async (client, count, stop) =>
        {
            using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, stop);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(stop);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await body.ReadAsync(buffer, stop)) > 0) count(read);
        });

    /// Upload in Mbit/s; `null`, wenn nichts gesendet werden konnte.
    public static Task<double?> UploadAsync(IPAddress? source, CancellationToken cancellation) =>
        MeasureAsync(source, cancellation, async (client, count, stop) =>
        {
            using var response = await client.PostAsync(UploadUrl, new CountingContent(UploadSize, count), stop);
            response.EnsureSuccessStatusCode();
        });

    private static async Task<double?> MeasureAsync(IPAddress? source, CancellationToken cancellation,
                                                    Func<HttpClient, Action<int>, CancellationToken, Task> transfer)
    {
        using var client = new HttpClient(Handler(source)) { Timeout = Timeout.InfiniteTimeSpan };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        long total = 0;

        var streams = Enumerable.Range(0, Streams).Select(_ => Task.Run(async () =>
        {
            // Ist eine Übertragung zu Ende, beginnt sofort die nächste, bis die Zeit um ist.
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await transfer(client, bytes => Interlocked.Add(ref total, bytes), stop.Token);
                }
                catch (Exception error) when (error is HttpRequestException or IOException or SocketException)
                {
                    try { await Task.Delay(300, stop.Token); } catch (OperationCanceledException) { }
                }
                catch (OperationCanceledException)
                {
                }
            }
        }, CancellationToken.None)).ToList();

        try
        {
            var clock = Stopwatch.StartNew();
            await Task.Delay(WarmUp, cancellation);
            var (startBytes, startTime) = (Interlocked.Read(ref total), clock.Elapsed);
            await Task.Delay(Duration - WarmUp, cancellation);
            return MeasurementParsing.Throughput(Interlocked.Read(ref total) - startBytes, clock.Elapsed - startTime);
        }
        finally
        {
            stop.Cancel();
            await Task.WhenAll(streams);
        }
    }

    /// Verbindungen gehen von der IP-Adresse des WLAN-Adapters aus.
    private static SocketsHttpHandler Handler(IPAddress? source) => new()
    {
        AutomaticDecompression = DecompressionMethods.None,
        ConnectCallback = async (context, cancellation) =>
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                if (source != null) socket.Bind(new IPEndPoint(source, 0));
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, AddressFamily.InterNetwork, cancellation);
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellation);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// Anfragekörper fester Länge, der mitzählt, was tatsächlich abgeschickt wurde.
    private sealed class CountingContent(int length, Action<int> count) : HttpContent
    {
        private static readonly byte[] Chunk = MakeChunk();

        private static byte[] MakeChunk()
        {
            // Zufallsdaten, damit unterwegs nichts komprimiert werden kann.
            var chunk = new byte[32 * 1024];
            Random.Shared.NextBytes(chunk);
            return chunk;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellation)
        {
            for (var sent = 0; sent < length;)
            {
                var size = Math.Min(Chunk.Length, length - sent);
                await stream.WriteAsync(Chunk.AsMemory(0, size), cancellation);
                count(size);
                sent += size;
            }
        }

        protected override bool TryComputeLength(out long computed)
        {
            computed = length;
            return true;
        }
    }
}
