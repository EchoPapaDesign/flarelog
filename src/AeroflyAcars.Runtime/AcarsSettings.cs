using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AeroflyAcars.Core;
using AeroflyAcars.Core.Sim;

namespace AeroflyAcars.Runtime;

/// <summary>Einstellungen, gespeichert unter %APPDATA%\Flarelog\settings.json.</summary>
public sealed class AcarsSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string? SimBriefUser { get; set; }
    public bool LoadSimBriefOnStart { get; set; } = true;
    /// <summary>Echte UTC-Zeit an Aerofly übergeben (beim Start von Aerofly).</summary>
    public bool RealTime { get; set; }
    /// <summary>Wetter aus dem METAR des Abflugplatzes an Aerofly übergeben (beim Start von Aerofly).</summary>
    public bool RealWeather { get; set; }
    /// <summary>Selbst eingetragener Abflugplatz für das Wetter (gilt, wenn weder SimBrief-Plan noch Buchung da sind).</summary>
    public string? WeatherIcao { get; set; }
    /// <summary>Platz, an dem das Flugzeug zuletzt stand (Ersatz für das Wetter beim nächsten Start).</summary>
    public string? LastAirport { get; set; }
    public bool Voice { get; set; } = true;
    /// <summary>Kabinenansagen in den Flugphasen (Begrüßung, Sicherheitseinweisung, Steigflug …).</summary>
    public bool CabinAnnouncements { get; set; } = true;
    /// <summary>Gewählte Kabinenbesatzung (Zusatz-Soundpack von AeRodri) oder null für das Standard-Pack.</summary>
    public string? SoundPack { get; set; }
    /// <summary>Aufzeichnung erst mit dem Knopf „Start“ beginnen (sonst automatisch, sobald Aerofly Daten sendet).</summary>
    public bool ManualStart { get; set; }
    /// <summary>Flug bei Block on automatisch beenden und speichern (sonst erst mit „Beenden“).</summary>
    public bool AutoFinish { get; set; } = true;
    /// <summary>Version, deren „Was ist neu?“ zuletzt gezeigt wurde.</summary>
    public string? LastSeenVersion { get; set; }
    /// <summary>Sprache: "auto" (wie das System), "de" oder "en".</summary>
    public string Language { get; set; } = "auto";
    public int UdpPort { get; set; } = ForeFlightProtocol.DefaultPort;
    public int DisplayPort { get; set; } = 8080;
    public string? ReportFolder { get; set; }
    /// <summary>Letzte Fensterposition und -größe [links, oben, Breite, Höhe] – Flarelog öffnet dort wieder.</summary>
    public int[]? WindowBounds { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>Gespeicherte Fensterposition, wenn sie plausibel ist (sonst null → zentriert öffnen).</summary>
    [JsonIgnore]
    public (int X, int Y, int W, int H)? SavedWindow =>
        WindowBounds is { Length: 4 } b && b[2] >= 600 && b[3] >= 400 && b[2] <= 1600 && b[3] <= 1000 ? (b[0], b[1], b[2], b[3]) : null;

    /// <summary>Adresse der phpVMS-Airline, z. B. https://www.meine-va.de</summary>
    public string? AirlineUrl { get; set; }

    /// <summary>
    /// API-Schlüssel, unter Windows mit DPAPI für den angemeldeten Benutzer verschlüsselt. Unter macOS liegt er im
    /// Schlüsselbund, hier steht dann nur <see cref="MacKeychainMarker"/>.
    /// </summary>
    public string? AirlineApiKeyProtected { get; set; }

    /// <summary>Flug ab dem Losrollen live an die Airline melden (erscheint auf deren Live-Karte).</summary>
    public bool LiveTracking { get; set; } = true;

    [JsonIgnore]
    public bool HasAirlineApiKey => !string.IsNullOrEmpty(AirlineApiKeyProtected);

    public const string MacKeychainMarker = "macos-keychain";

    /// <summary>Stellt <see cref="Lang"/> nach <see cref="Language"/> ein („auto“: Deutsch, wenn das System Deutsch spricht).</summary>
    public void ApplyLanguage()
    {
        Lang.English = Language switch
        {
            "de" => false,
            "en" => true,
            _ => !Lang.SystemIsGerman(SystemLanguage()),
        };
    }

    /// <summary>
    /// Systemsprache. Unter macOS bekommen per Doppelklick gestartete Programme keine Spracheinstellung mit –
    /// dort zählt die bevorzugte Sprache aus den Systemeinstellungen (AppleLanguages).
    /// </summary>
    private static string? SystemLanguage()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/defaults", "read -g AppleLanguages")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            // Ausgabe wie: ( "de-DE", "en-DE" )
            var first = output.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => x.Trim().Length >= 2 && char.IsLetter(x.Trim()[0]));
            return first?.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? GetAirlineApiKey()
    {
        if (string.IsNullOrEmpty(AirlineApiKeyProtected)) return null;
        if (AirlineApiKeyProtected == MacKeychainMarker) return OperatingSystem.IsMacOS() ? MacKeychain.Load() : null;
        try
        {
            var raw = Convert.FromBase64String(AirlineApiKeyProtected);
            if (OperatingSystem.IsWindows())
                raw = ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(raw);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    public void SetAirlineApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (AirlineApiKeyProtected == MacKeychainMarker && OperatingSystem.IsMacOS()) MacKeychain.Delete();
            AirlineApiKeyProtected = null;
            return;
        }
        if (OperatingSystem.IsMacOS() && MacKeychain.Save(apiKey.Trim()))
        {
            AirlineApiKeyProtected = MacKeychainMarker;
            return;
        }
        var raw = Encoding.UTF8.GetBytes(apiKey.Trim());
        if (OperatingSystem.IsWindows())
            raw = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
        AirlineApiKeyProtected = Convert.ToBase64String(raw);
    }

    // Nicht ändern: Sonst lassen sich gespeicherte Schlüssel nicht mehr entschlüsseln (Name aus der Zeit vor Flarelog).
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AeroflyAcars.AirlineApiKey");

    /// <summary>Nur für die laufende Sitzung (Konsolenoption --dest-elev), wird nicht gespeichert.</summary>
    [JsonIgnore]
    public double? DestinationElevationFt { get; set; }

    public static string Folder => AppPaths.Settings;

    public static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Standardordner der Flugberichte: Dokumente/Flarelog/Flights (bis 0.9.0-beta.6: „Fluege“).</summary>
    public static string DefaultReportFolder => Path.Combine(AppPaths.Documents, AppInfo.Name, "Flights");

    [JsonIgnore]
    public string EffectiveReportFolder => string.IsNullOrWhiteSpace(ReportFolder) ? DefaultReportFolder : ReportFolder;

    public static AcarsSettings Load()
    {
        MigrateLegacyFolders();
        MigrateReportFolder();
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AcarsSettings>(File.ReadAllText(FilePath), Json) ?? new AcarsSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Defekte Datei: mit Standardwerten weitermachen.
        }
        return new AcarsSettings();
    }

    /// <summary>
    /// Umzug von „Aerofly ACARS“ (bis 0.9) zu Flarelog: Einstellungen samt API-Schlüssel, Flughafenlisten und
    /// Flugberichte wandern einmalig in die neuen Ordner. Fehler sind unkritisch – dann wird neu angelegt.
    /// </summary>
    private static void MigrateLegacyFolders()
    {
        if (!OperatingSystem.IsWindows()) return;   // vor Flarelog gab es nur die Windows-Version
        static void Move(string from, string to)
        {
            try
            {
                if (Directory.Exists(from) && !Directory.Exists(to)) Directory.Move(from, to);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Move(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.LegacyFolderName), Folder);
        Move(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.LegacyFolderName),
             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.FolderName));
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Move(Path.Combine(docs, "Aerofly ACARS"), Path.Combine(docs, AppInfo.Name));
    }

    /// <summary>
    /// „Fluege“ heißt seit 0.9.0-beta.7 „Flights“ – verständlich für alle. Vorhandene Berichte ziehen einmalig um;
    /// gibt es beide Ordner schon, wandern die alten Dateien hinüber, ohne etwas zu überschreiben.
    /// </summary>
    private static void MigrateReportFolder()
    {
        var old = Path.Combine(AppPaths.Documents, AppInfo.Name, "Fluege");
        var target = DefaultReportFolder;
        try
        {
            if (!Directory.Exists(old)) return;
            if (!Directory.Exists(target))
            {
                Directory.Move(old, target);
                return;
            }
            foreach (var file in Directory.GetFiles(old))
            {
                var dest = Path.Combine(target, Path.GetFileName(file));
                if (!File.Exists(dest)) File.Move(file, dest);
            }
            if (Directory.GetFileSystemEntries(old).Length == 0) Directory.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dann bleiben die alten Berichte, wo sie sind – neue landen in „Flights“.
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        // Außerhalb von Windows nur für den Benutzer lesbar (falls der Schlüsselbund nicht verfügbar war).
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
