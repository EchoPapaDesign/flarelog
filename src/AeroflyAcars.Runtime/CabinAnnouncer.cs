using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AeroflyAcars.Core;

namespace AeroflyAcars.Runtime;

/// <summary>
/// Spielt Kabinenansagen (WAV) nacheinander ab – nie zwei gleichzeitig. Windows: winmm PlaySound, macOS: afplay.
/// Mitgelieferte Ansagen stecken im Programm und werden beim ersten Abspielen in den Zwischenspeicher entpackt.
/// Eine gleichnamige Datei in Dokumente/Flarelog/Sounds hat Vorrang – so lassen sich Ansagen austauschen.
/// </summary>
public sealed class CabinAnnouncer : IDisposable
{
    private const string ResourcePrefix = "Flarelog.Sounds.";

    private readonly BlockingCollection<string> _queue = new();
    private readonly Thread? _thread;
    private Process? _current;

    public CabinAnnouncer()
    {
        Available = OperatingSystem.IsWindows() || (OperatingSystem.IsMacOS() && File.Exists("/usr/bin/afplay"));
        if (!Available) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Kabinenansagen" };
        _thread.Start();
    }

    public bool Available { get; }

    /// <summary>Ordner für eigene Ansagen (Dokumente/Flarelog/Sounds).</summary>
    public static string UserFolder => Path.Combine(AppPaths.Documents, AppInfo.FolderName, "Sounds");

    private static string BuiltInFolder => Path.Combine(AppPaths.Cache, "Sounds");

    /// <summary>
    /// Legt die mitgelieferten Ansagen sichtbar im Sound-Ordner ab, wenn dort noch keine WAV-Dateien liegen –
    /// so sieht jeder, welche Dateien es gibt und wie sie heißen. Vorhandene Dateien bleiben unangetastet.
    /// </summary>
    public static void EnsureUserFolder()
    {
        try
        {
            if (Directory.Exists(UserFolder) && Directory.EnumerateFiles(UserFolder, "*.wav").Any())
                UpgradeOldDefaults();
            else
                CopyBuiltIns(overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dann spielen die eingebauten Ansagen – auch ohne Ordner.
        }
    }

    /// <summary>
    /// SHA-256 früher mitgelieferter Ansagen (Soundpack 1.0). Liegt eine davon unverändert im Sound-Ordner,
    /// ersetzt Flarelog sie durch die aktuelle Fassung – eigene Dateien haben andere Prüfsummen und bleiben.
    /// </summary>
    public static readonly HashSet<string> PreviousDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        "0f603e367de51ea13ce404cd94645b0d12ddd05466331aff789a832ac3d74136", // After_Touchdown 1.0
        "cadf0494bec6471275b09b7807bb007e27366b7b96cbf00915efdddbd4c15e04", // Climbing_Message 1.0
        "19f6c5c547ea323dfc0dbc55fe562f13a619702b017c93f5c2fd75444c98cd4c", // Cruise_Altitude 1.0
        "568d71fb7096e91815878861e036bed56530f19e5149dcd5c0ec10c2ea1f868e", // Descending_Start 1.0
        "8f94d9f0ad02d005ed337d501c814e312a29ad5d02d9d99702e3f141fff7771b", // Safety_Briefing 1.0
        "b3b8972a51cefcc77e5e1840552c79a4927a83bcfa507ecbc9735acbc0892cd3", // Welcome_Onboard 1.0
    };

    /// <summary>Unveränderte alte Standard-Ansagen im Sound-Ordner durch die aktuellen ersetzen; liefert die Anzahl.</summary>
    public static int UpgradeOldDefaults()
    {
        int replaced = 0;
        foreach (var res in typeof(CabinAnnouncer).Assembly.GetManifestResourceNames())
        {
            if (!res.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
            var target = Path.Combine(UserFolder, res[ResourcePrefix.Length..]);
            if (!File.Exists(target)) continue;
            string hash;
            using (var f = File.OpenRead(target))
                hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f));
            if (!PreviousDefaults.Contains(hash)) continue;
            using var stream = typeof(CabinAnnouncer).Assembly.GetManifestResourceStream(res)!;
            using (var file = File.Create(target + ".tmp")) stream.CopyTo(file);
            File.Move(target + ".tmp", target, overwrite: true);
            replaced++;
        }
        return replaced;
    }

    /// <summary>Mitgelieferte Ansagen in den Sound-Ordner kopieren; mit overwrite werden eigene Dateien ersetzt.</summary>
    public static int CopyBuiltIns(bool overwrite)
    {
        Directory.CreateDirectory(UserFolder);
        int copied = 0;
        foreach (var res in typeof(CabinAnnouncer).Assembly.GetManifestResourceNames())
        {
            if (!res.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
            var target = Path.Combine(UserFolder, res[ResourcePrefix.Length..]);
            if (!overwrite && File.Exists(target)) continue;
            using var stream = typeof(CabinAnnouncer).Assembly.GetManifestResourceStream(res)!;
            using (var file = File.Create(target + ".tmp")) stream.CopyTo(file);
            File.Move(target + ".tmp", target, overwrite: true);
            copied++;
        }
        return copied;
    }

    public void Play(string fileName)
    {
        if (Available && !_queue.IsAddingCompleted) _queue.Add(fileName);
    }

    /// <summary>Laufende und wartende Ansagen abbrechen (z. B. neuer Flug, Ansagen ausgeschaltet).</summary>
    public void Stop()
    {
        while (_queue.TryTake(out _)) { }
        StopCurrent();
    }

    /// <summary>Gewählte Zusatz-Besatzung (Ordnername unter Sounds/Packs) oder null für das Standard-Pack.</summary>
    public static string? ActivePack { get; set; }

    /// <summary>
    /// Datei zu einer Ansage: aus der gewählten Zusatz-Besatzung, sonst eigene Datei im Sound-Ordner, sonst die
    /// mitgelieferte; null, wenn es keine gibt.
    /// </summary>
    public static string? Resolve(string fileName)
    {
        try
        {
            if (ActivePack is { } pack && SoundPacks.ValidId(pack))
            {
                var inPack = Path.Combine(SoundPacks.PackFolder(pack), fileName);
                if (File.Exists(inPack)) return inPack;
            }
            var own = Path.Combine(UserFolder, fileName);
            if (File.Exists(own)) return own;
            return ExtractBuiltIn(fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ExtractBuiltIn(string fileName)
    {
        using var stream = typeof(CabinAnnouncer).Assembly.GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream is null) return null;
        var path = Path.Combine(BuiltInFolder, fileName);
        if (File.Exists(path) && new FileInfo(path).Length == stream.Length) return path;
        Directory.CreateDirectory(BuiltInFolder);
        var temp = path + ".tmp";
        using (var file = File.Create(temp)) stream.CopyTo(file);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    private void Run()
    {
        foreach (var name in _queue.GetConsumingEnumerable())
        {
            try
            {
                var path = Resolve(name);
                if (path is null) continue;
                if (OperatingSystem.IsWindows()) PlayWindows(path);
                else PlayMac(path);
            }
            catch (Exception)
            {
                // Eine fehlgeschlagene Ansage soll das Programm nicht stören.
            }
        }
    }

    private void PlayMac(string path)
    {
        var psi = new ProcessStartInfo("/usr/bin/afplay") { UseShellExecute = false };
        psi.ArgumentList.Add(path);
        using var p = Process.Start(psi);
        if (p is null) return;
        _current = p;
        p.WaitForExit(120_000);
        _current = null;
    }

    private const uint SndSync = 0x0000, SndNoDefault = 0x0002, SndFilename = 0x00020000;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    private static void PlayWindows(string path) => PlaySound(path, IntPtr.Zero, SndSync | SndFilename | SndNoDefault);

    private void StopCurrent()
    {
        try
        {
            if (OperatingSystem.IsWindows()) PlaySound(null, IntPtr.Zero, 0);
            else if (_current is { HasExited: false } p) p.Kill();
        }
        catch (Exception)
        {
            // nichts zu tun
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        Stop();
        _thread?.Join(TimeSpan.FromSeconds(2));
    }
}
