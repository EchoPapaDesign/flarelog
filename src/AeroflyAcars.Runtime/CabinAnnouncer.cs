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

    /// <summary>Datei zu einer Ansage: eigene Datei, sonst die mitgelieferte; null, wenn es keine gibt.</summary>
    public static string? Resolve(string fileName)
    {
        try
        {
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
