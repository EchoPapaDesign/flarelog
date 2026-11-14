// Flarelog für macOS: eigenes Programmfenster mit Dock-Symbol. Die Oberfläche läuft darin in der eingebauten
// WebKit-Ansicht von macOS (über Photino) – wie unter Windows mit WebView2. Programmlogik, Berichte und
// Einstellungen sind dieselben wie bisher.
// Lässt sich das Fenster nicht öffnen, startet Flarelog wie früher im Hintergrund und öffnet die Oberfläche im Browser.

using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using AeroflyAcars.Core;
using AeroflyAcars.Mac;
using AeroflyAcars.Runtime;
using Photino.NET;

var settings = AcarsSettings.Load();
var log = OpenLog();
log($"{AppInfo.Name} {AppInfo.Version} (macOS) gestartet");

using var engine = new AcarsEngine(settings);   // stellt auch die Sprache ein
engine.Notice += message => log(message);

try
{
    engine.Start();
}
catch (SocketException ex)
{
    // Der UDP-Port ist belegt – meist läuft Flarelog schon (z. B. eine ältere Version). Dann nur die Oberfläche öffnen.
    log($"UDP-Port {settings.UdpPort} belegt ({ex.Message}) – Flarelog läuft vermutlich schon.");
    OpenExternal($"http://localhost:{settings.DisplayPort}/");
    return 0;
}

var url = engine.DisplayUrl;
if (url is null)
{
    log("Die Oberfläche konnte nicht gestartet werden (Ports belegt).");
    return 1;
}

try
{
    RunWindow(engine, url, log);
    return 0;
}
catch (Exception ex)
{
    // Z. B. fehlende Systembibliothek: lieber im Browser weiterlaufen als gar nicht.
    log($"Programmfenster konnte nicht geöffnet werden ({ex.GetType().Name}: {ex.Message}) – Oberfläche im Browser.");
    RunInBrowser(engine, url);
}

log("Flarelog wird beendet");
return 0;

static void RunWindow(AcarsEngine engine, string url, Action<string> log)
{
    bool quitting = false;
    PhotinoWindow window = null!;

    window = new PhotinoWindow()
        .SetTitle(Lang.T("Flarelog – ACARS für Aerofly FS 4", "Flarelog – ACARS for Aerofly FS 4"))
        .SetUseOsDefaultSize(false)
        .SetSize(1200, 760)   // passt auf jedes MacBook (13" Air: 1280×832 Punkte) neben Menüleiste und Dock
        .SetMinSize(1000, 640)
        .Center()
        .SetResizable(true)
        .SetContextMenuEnabled(false)
        .SetDevToolsEnabled(false)
        // Die Oberfläche meldet externe Links („open:https://…“) – die öffnen im normalen Browser.
        .RegisterWebMessageReceivedHandler((_, message) =>
        {
            if (message.StartsWith("open:", StringComparison.Ordinal))
            {
                var target = message[5..];
                if (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    OpenExternal(target);
            }
        })
        // Gefragt wird vorher (MacQuitGuard). Hier ist das Fenster schon zu – Photino kann das auf dem Mac nicht mehr
        // verhindern. Deshalb immer aufräumen: laufenden Flug als Teilbericht speichern, Ports freigeben.
        .RegisterWindowClosingHandler((_, _) =>
        {
            engine.Dispose();
            log("Flarelog wird beendet");
            return false;
        })
        .RegisterWindowCreatedHandler((_, _) =>
        {
            if (!MacQuitGuard.Install(MayQuit))
                log("Rückfrage beim Beenden nicht verfügbar – ein laufender Flug wird beim Schließen gespeichert.");
        })
        .Load(new Uri(url));

    // Vor dem Schließen (roter Knopf) und Beenden (Cmd+Q, Dock): Läuft ein Flug, wird gefragt.
    // Ja = speichern und beenden, Nein = Flarelog läuft einfach weiter.
    bool MayQuit()
    {
        if (quitting || !engine.FlightInProgress) { quitting = true; return true; }
        var answer = window.ShowMessage(Lang.T("Flarelog beenden?", "Quit Flarelog?"),
            Lang.T("Es wird gerade ein Flug aufgezeichnet.\n\nFlarelog jetzt beenden? Der bisherige Flug wird als Teilbericht gespeichert.",
                   "A flight is being recorded.\n\nDo you want to quit? The flight so far will be saved as a partial report."),
            PhotinoDialogButtons.YesNo, PhotinoDialogIcon.Warning);
        if (answer != PhotinoDialogResult.Yes) return false;
        quitting = true;
        engine.Dispose();   // sofort speichern, bevor macOS das Programm beendet
        return true;
    }

    // „Flarelog beenden“ in den Einstellungen, Abmelden/Ausschalten oder Beenden über die Aktivitätsanzeige:
    // Fenster schließen, danach wird der laufende Flug beim Aufräumen gespeichert.
    void CloseWindow()
    {
        quitting = true;
        try { window.Invoke(window.Close); }
        catch (Exception ex) { log($"Fenster ließ sich nicht schließen: {ex.Message}"); Environment.Exit(0); }
    }
    engine.QuitRequested += CloseWindow;
    // Cmd+Q bzw. „Beenden“ im Dock geht am Fenster vorbei direkt zum Prozessende.
    AppDomain.CurrentDomain.ProcessExit += (_, _) => engine.Dispose();
    using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; CloseWindow(); });
    using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; CloseWindow(); });

    window.WaitForClose();
}

static void RunInBrowser(AcarsEngine engine, string url)
{
    using var quit = new ManualResetEventSlim();
    engine.QuitRequested += () => quit.Set();
    using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; quit.Set(); });
    using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; quit.Set(); });
    OpenExternal(url);
    quit.Wait();
}

static void OpenExternal(string url)
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

// Meldungen landen in ~/Library/Logs/Flarelog/Flarelog.log (eigener Ordner, damit man ihn beim Support findet).
static Action<string> OpenLog()
{
    var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs");
    var path = Path.Combine(logs, "Flarelog", "Flarelog.log");
    // Alte Datei direkt in ~/Library/Logs (bis 0.9.0-beta.5) aufräumen.
    try { File.Delete(Path.Combine(logs, "Flarelog.log")); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
