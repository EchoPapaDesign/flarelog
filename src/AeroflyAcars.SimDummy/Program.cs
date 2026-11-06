// Simulator-Ersatz: sendet einen künstlichen Flug im selben UDP-Format wie Aerofly FS 4.
// Aufruf: dotnet run --project src\AeroflyAcars.SimDummy [-- --host 127.0.0.1 --port 49002]

using System.Net.Sockets;
using System.Text;
using AeroflyAcars.Core.Sim;
using AeroflyAcars.Core.Testing;

Console.OutputEncoding = Encoding.UTF8;

string host = "127.0.0.1";
int port = ForeFlightProtocol.DefaultPort;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--host") host = args[++i];
    else if (args[i] == "--port" && int.TryParse(args[i + 1], out var p)) { port = p; i++; }
}

var frames = ScriptedFlight.Generate();
var duration = frames[^1].Offset;
Console.WriteLine("Aerofly-Ersatz – künstlicher Flug in Hamburg (EDDH)");
Console.WriteLine($"Sende {frames.Count} Positionen an {host}:{port}, Dauer ca. {duration.TotalMinutes:F0} Minuten.");
Console.WriteLine("Strg+C bricht ab.");
Console.WriteLine();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var udp = new UdpClient();
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
string? lastStage = null;

try
{
    foreach (var frame in frames)
    {
        if (frame.Stage != lastStage)
        {
            Console.WriteLine($"[{frame.Offset:mm\\:ss}] {frame.Stage}");
            lastStage = frame.Stage;
        }

        // Wie Aerofly: Attitude und GPS als einzelne Datagramme.
        foreach (var text in new[] { ForeFlightProtocol.Format(frame.Attitude), ForeFlightProtocol.Format(frame.Gps) })
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            await udp.SendAsync(bytes, bytes.Length, host, port);
        }

        await timer.WaitForNextTickAsync(cts.Token);
    }
    Console.WriteLine("Flug komplett gesendet.");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Abgebrochen.");
}
catch (SocketException ex)
{
    Console.WriteLine($"Senden fehlgeschlagen: {ex.Message}");
    return 1;
}
return 0;
