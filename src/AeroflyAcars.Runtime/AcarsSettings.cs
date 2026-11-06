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
    public int UdpPort { get; set; } = ForeFlightProtocol.DefaultPort;
    public int DisplayPort { get; set; } = 8080;
    public string? ReportFolder { get; set; }

    /// <summary>Adresse der phpVMS-Airline, z. B. https://www.meine-va.de</summary>
    public string? AirlineUrl { get; set; }

    /// <summary>API-Schlüssel, unter Windows mit DPAPI für den angemeldeten Benutzer verschlüsselt.</summary>
    public string? AirlineApiKeyProtected { get; set; }

    [JsonIgnore]
    public bool HasAirlineApiKey => !string.IsNullOrEmpty(AirlineApiKeyProtected);

    public string? GetAirlineApiKey()
    {
        if (string.IsNullOrEmpty(AirlineApiKeyProtected)) return null;
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
            AirlineApiKeyProtected = null;
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

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.FolderName);

    public static string FilePath => Path.Combine(Folder, "settings.json");

    [JsonIgnore]
    public string EffectiveReportFolder => string.IsNullOrWhiteSpace(ReportFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppInfo.Name, "Fluege")
        : ReportFolder;

    public static AcarsSettings Load()
    {
        MigrateLegacyFolders();
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

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
