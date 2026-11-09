namespace AeroflyAcars.Core;

/// <summary>
/// Ordner je Betriebssystem. Windows: Dokumente, %APPDATA%, %LOCALAPPDATA%.
/// macOS: ~/Documents und ~/Library/Application Support (die .NET-Standardordner wären dort ~/.config und
/// ~/.local/share, die ein Mac-Nutzer nicht findet).
/// </summary>
public static class AppPaths
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Dokumente-Ordner des Benutzers.</summary>
    public static string Documents => OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        : Path.Combine(Home, "Documents");

    /// <summary>Einstellungen von Flarelog (Windows: %APPDATA%\Flarelog).</summary>
    public static string Settings => OperatingSystem.IsMacOS()
        ? Path.Combine(Home, "Library", "Application Support", AppInfo.FolderName)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.FolderName);

    /// <summary>Zwischenspeicher wie die Flughafenlisten (Windows: %LOCALAPPDATA%\Flarelog).</summary>
    public static string Cache => OperatingSystem.IsMacOS()
        ? Path.Combine(Home, "Library", "Caches", AppInfo.FolderName)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.FolderName);

    /// <summary>
    /// Benutzerordner von Aerofly FS 4 (main.mcf, tm.log). Windows: Dokumente\Aerofly FS 4.
    /// macOS: je nach Version (App Store/Steam) im Container der App oder unter Application Support –
    /// genommen wird der erste Ordner, in dem eine main.mcf liegt.
    /// </summary>
    public static string AeroflyUserFolder
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return Path.Combine(Documents, "Aerofly FS 4");
            var candidates = new List<string>();
            var containers = Path.Combine(Home, "Library", "Containers");
            try
            {
                if (Directory.Exists(containers))
                    foreach (var dir in Directory.EnumerateDirectories(containers, "com.aerofly*"))
                        candidates.Add(Path.Combine(dir, "Data", "Library", "Application Support", "Aerofly FS 4"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            candidates.Add(Path.Combine(Home, "Library", "Application Support", "Aerofly FS 4"));
            candidates.Add(Path.Combine(Documents, "Aerofly FS 4"));
            return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "main.mcf"))) ?? candidates[0];
        }
    }
}
