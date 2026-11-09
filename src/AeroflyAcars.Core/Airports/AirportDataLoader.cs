namespace AeroflyAcars.Core.Airports;

/// <summary>
/// Lädt die OurAirports-Listen (Flugplätze, Bahnen) beim ersten Start herunter und hält sie lokal vor
/// (Standard: %LOCALAPPDATA%\Flarelog\). Aktualisierung alle 30 Tage.
/// Ohne Internet werden vorhandene Dateien weiterverwendet.
/// </summary>
public static class AirportDataLoader
{
    public const string SourceUrl = "https://davidmegginson.github.io/ourairports-data/airports.csv";
    public const string RunwaysUrl = "https://davidmegginson.github.io/ourairports-data/runways.csv";
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    public static string Folder => AppPaths.Cache;

    public static string DefaultPath => Path.Combine(Folder, "airports.csv");
    public static string RunwaysPath => Path.Combine(Folder, "runways.csv");

    /// <returns>Datenbank (oder null) und eine Meldung für die Konsole.</returns>
    public static async Task<(AirportDatabase? Database, string Message)> LoadAsync(
        string? path = null, HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        path ??= DefaultPath;
        var (ok, note) = await EnsureFileAsync(SourceUrl, path, "latitude_deg", http, cancellationToken).ConfigureAwait(false);
        if (!ok) return (null, $"Flughafenliste nicht verfügbar{note}.");

        try
        {
            var db = AirportDatabase.LoadCsv(path);
            return (db, $"{db.Count:N0} Flugplätze geladen{note}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return (null, $"Flughafenliste konnte nicht gelesen werden: {ex.Message}");
        }
    }

    public static async Task<(RunwayDatabase? Database, string Message)> LoadRunwaysAsync(
        string? path = null, HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        path ??= RunwaysPath;
        var (ok, note) = await EnsureFileAsync(RunwaysUrl, path, "le_ident", http, cancellationToken).ConfigureAwait(false);
        if (!ok) return (null, $"Bahnenliste nicht verfügbar{note}.");

        try
        {
            var db = RunwayDatabase.LoadCsv(path);
            return (db, $"{db.Count:N0} Bahnen geladen{note}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return (null, $"Bahnenliste konnte nicht gelesen werden: {ex.Message}");
        }
    }

    /// <summary>Lädt die Datei, wenn sie fehlt oder älter als 30 Tage ist. Ok = Datei ist danach vorhanden.</summary>
    private static async Task<(bool Ok, string Note)> EnsureFileAsync(
        string url, string path, string headerMarker, HttpClient? http, CancellationToken ct)
    {
        bool exists = File.Exists(path);
        bool stale = !exists || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > MaxAge;
        if (!stale) return (true, "");

        try
        {
            await DownloadAsync(url, path, headerMarker, http, ct).ConfigureAwait(false);
            return (true, exists ? " (aktualisiert)" : " (heruntergeladen)");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            return exists
                ? (true, " (Aktualisierung fehlgeschlagen, nutze vorhandene Liste)")
                : (false, $" ({ex.Message})");
        }
    }

    private static async Task DownloadAsync(string url, string path, string headerMarker, HttpClient? http, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var client = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        try
        {
            var tmp = path + ".download";
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(tmp);
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
            }

            // Kurze Plausibilitätsprüfung, bevor die alte Datei ersetzt wird.
            using (var check = new StreamReader(tmp))
            {
                var header = await check.ReadLineAsync(ct).ConfigureAwait(false);
                if (header is null || !header.Contains(headerMarker))
                    throw new IOException("Heruntergeladene Datei hat ein unerwartetes Format.");
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (http is null) client.Dispose();
        }
    }
}
