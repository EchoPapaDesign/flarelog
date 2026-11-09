// Flarelog für macOS. Es gibt kein eigenes Programmfenster: Flarelog läuft im Hintergrund und öffnet die
// Oberfläche im Standardbrowser. Ein zweiter Start (Doppelklick auf Flarelog.app) öffnet sie erneut.
// Beenden über Einstellungen → „Flarelog beenden“ im Browser.

using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using AeroflyAcars.Core;
using AeroflyAcars.Runtime;

var settings = AcarsSettings.Load();
var log = OpenLog();
log($"{AppInfo.Name} {AppInfo.Version} (macOS) gestartet");

using var engine = new AcarsEngine(settings);
using var quit = new ManualResetEventSlim();
engine.QuitRequested += () => quit.Set();
engine.Notice += message => log(message);

try
{
    engine.Start();
}
catch (SocketException ex)
{
    // Der UDP-Port ist belegt – meist läuft Flarelog schon. Dann nur die Oberfläche wieder öffnen.
    log($"UDP-Port {settings.UdpPort} belegt ({ex.Message}) – Flarelog läuft vermutlich schon.");
    OpenBrowser($"http://localhost:{settings.DisplayPort}/");
    return 0;
}

if (engine.DisplayUrl is { } url) OpenBrowser(url);
else log("Die Oberfläche konnte nicht gestartet werden (Ports belegt).");

// Abmelden/Ausschalten oder Beenden über die Aktivitätsanzeige: sauber beenden, laufenden Flug speichern.
using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; quit.Set(); });
using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; quit.Set(); });

quit.Wait();
log("Flarelog wird beendet");
return 0;

static void OpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { url }, UseShellExecute = false })?.Dispose();
    }
    catch (Exception)
    {
        // Ohne Browser ist nichts zu machen – Adresse steht im Protokoll.
    }
}

// Ohne Fenster sieht niemand die Konsole: Meldungen landen in ~/Library/Logs/Flarelog.log.
static Action<string> OpenLog()
{
    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "Flarelog.log");
    var gate = new object();
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Delete(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    return message =>
    {
        try
        {
            lock (gate) File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    };
}
