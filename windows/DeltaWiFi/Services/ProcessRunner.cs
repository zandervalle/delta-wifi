//
//  ProcessRunner.cs
//  DeltaWiFi
//
//  Startet ein Kommandozeilenwerkzeug (iperf3) und liefert seine Ausgabe.
//  Beim Abbruch wird der Prozess beendet.
//

using System.Diagnostics;

namespace DeltaWiFi.Services;

public static class ProcessRunner
{
    /// Standardausgabe des Programms; `null`, wenn es sich nicht starten ließ.
    public static async Task<string?> RunAsync(string path, IEnumerable<string> arguments, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        try
        {
            // Die Fehlerausgabe mitlesen, damit der Prozess nicht an einem vollen Puffer hängen bleibt.
            var errors = process.StandardError.ReadToEndAsync(cancellation);
            var output = await process.StandardOutput.ReadToEndAsync(cancellation);
            await errors;
            await process.WaitForExitAsync(cancellation);
            return output;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
    }
}
