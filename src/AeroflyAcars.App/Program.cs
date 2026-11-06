// Konsolenversion (für Entwicklung und Fehlersuche). Das eigentliche Programm ist AeroflyAcars.Desktop.

using System.Net.Sockets;
using System.Text;
using AeroflyAcars.App;
using AeroflyAcars.Core.Reporting;
using AeroflyAcars.Core.Tracking;
using AeroflyAcars.Runtime;

Console.OutputEncoding = Encoding.UTF8;

AppOptions? parsed;
try
{
    parsed = AppOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.WriteLine(ex.Message);
    Console.WriteLine();
    Console.WriteLine(AppOptions.Usage);
    return 1;
}
if (parsed is null)
{
    Console.WriteLine(AppOptions.Usage);
    return 0;
}
AppOptions options = parsed;

Console.WriteLine($"Flarelog – Konsole {AeroflyAcars.Core.AppInfo.Version}");
Console.WriteLine(new string('-', 40));

if (options.VoiceTest)
{
    using var voice = SpeechAnnouncer.Create();
    var text = LandingCallout.Text(-153);
    Console.WriteLine(voice.Available ? $"Stimme: {voice.VoiceName}" : $"Sprachausgabe nicht verfügbar: {voice.Problem}");
    Console.WriteLine($"Test: \"{text}\"");
    voice.Say(text);
    await Task.Delay(TimeSpan.FromSeconds(6));
    return 0;
}

// Gespeicherte Einstellungen; Kommandozeile hat Vorrang (wird nicht gespeichert).
var settings = AcarsSettings.Load();
if (options.SimBriefUser is not null) { settings.SimBriefUser = options.SimBriefUser; settings.LoadSimBriefOnStart = true; }
if (options.UdpPort is { } udp) settings.UdpPort = udp;
if (options.MapPort is { } mapPort) settings.DisplayPort = mapPort;
if (!options.Voice) settings.Voice = false;
if (options.OutputDirectory is not null) settings.ReportFolder = options.OutputDirectory;
settings.DestinationElevationFt = options.DestinationElevationFt;

using var engine = new AcarsEngine(settings);
engine.Notice += message => { StatusLine.Clear(); Console.WriteLine($"  {message}"); };
engine.FlightEventRaised += e =>
{
    StatusLine.Clear();
    var label = e.PhaseChange ? e.Phase.ToGerman() : "Hinweis";
    Console.WriteLine($"[{e.Time.ToUniversalTime():HH:mm:ss} UTC] {label,-24} {e.Text}");
};
engine.CalloutSpoken += callout => { StatusLine.Clear(); Console.WriteLine($">>> {callout}"); };
engine.ReportSaved += (report, path) =>
{
    StatusLine.Clear();
    Console.WriteLine();
    Console.WriteLine(ReportWriter.ToText(report));
    Console.WriteLine($"Bericht gespeichert: {path}");
};

try
{
    engine.Start();
}
catch (SocketException ex)
{
    Console.WriteLine($"UDP-Port {settings.UdpPort} kann nicht geöffnet werden: {ex.Message}");
    Console.WriteLine("Läuft das Programm schon (auch als Desktop-Version)?");
    return 2;
}

Console.WriteLine($"Warte auf Aerofly auf UDP-Port {settings.UdpPort} …");
if (engine.DisplayUrl is { } url)
{
    Console.WriteLine($"Display: {url}");
    if (options.OpenBrowser) DisplayWindow.Open(url, options.AppWindow);
}
Console.WriteLine("Strg+C beendet die Aufzeichnung und speichert den Bericht.");
Console.WriteLine();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

while (!cts.IsCancellationRequested)
{
    try
    {
        await Task.Delay(1000, cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
    StatusLine.Write(engine.StatusText());
}

StatusLine.Clear();
if (engine.FlightInProgress) Console.WriteLine("Aufzeichnung abgebrochen – speichere Teilbericht.");
return 0;   // beim Verlassen speichert engine.Dispose() einen laufenden Flug als Teilbericht

internal static class StatusLine
{
    private static readonly object Gate = new();
    private static int _length;

    public static void Write(string text)
    {
        lock (Gate)
        {
            int width = 120;
            try { width = Math.Max(20, Console.WindowWidth - 1); } catch (Exception) { /* keine Konsole */ }
            if (text.Length > width) text = text[..width];
            Console.Write("\r" + text.PadRight(Math.Max(_length, text.Length)));
            _length = text.Length;
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            if (_length == 0) return;
            Console.Write("\r" + new string(' ', _length) + "\r");
            _length = 0;
        }
    }
}
