using System.Reflection;

namespace AeroflyAcars.Core;

/// <summary>Programmname und Version (aus Directory.Build.props bzw. beim Veröffentlichen aus dem Release-Tag).</summary>
public static class AppInfo
{
    public const string Name = "Flarelog";

    /// <summary>Kurzbeschreibung. „Aerofly FS“ ist eine Marke der IPACS – nur beschreibend verwendet.</summary>
    public const string Tagline = "ACARS für Aerofly FS 4";

    /// <summary>Ordnername unter %APPDATA% und %LOCALAPPDATA%.</summary>
    public const string FolderName = "Flarelog";

    /// <summary>Ordnername bis Version 0.9 (wird beim ersten Start umgezogen).</summary>
    public const string LegacyFolderName = "AeroflyAcars";

    /// <summary>
    /// GitHub-Repository für die Update-Prüfung, z. B. "meinname/flarelog". Leer = keine Prüfung.
    /// Nach dem Anlegen des Repositorys hier eintragen.
    /// </summary>
    public const string GitHubRepository = "EchoPapaDesign/flarelog";

    public static string Version { get; } = ReadVersion();

    public static string UserAgent => $"Flarelog/{Version}";

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');   // Build-Metadaten (Commit-Hash) abschneiden
            return plus > 0 ? info[..plus] : info;
        }
        var v = typeof(AppInfo).Assembly.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
