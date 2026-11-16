using System.IO.Compression;
using System.Text.Json;
using AeroflyAcars.Core;
using AeroflyAcars.Core.Tracking;

namespace AeroflyAcars.Runtime;

/// <summary>Ein Zusatz-Soundpack (Kabinenbesatzung) auf GitHub bzw. auf diesem Computer.</summary>
public sealed record SoundPackInfo(string Id, string Name, long? SizeBytes, bool Installed);

/// <summary>
/// Zusätzliche Kabinenbesatzungen von AeRodri (RealFlightOps): 16 englischsprachige Soundpacks aus verschiedenen
/// Weltregionen. Sie sind zusammen über 500 MB groß und deshalb nicht in Flarelog eingebaut, sondern liegen als ZIPs
/// im GitHub-Release <see cref="ReleaseTag"/> und werden bei Auswahl einmal heruntergeladen – nach
/// Dokumente/Flarelog/Sounds/Packs/&lt;Name&gt;. Jedes Pack enthält dieselben sieben Ansagen wie das Standard-Pack.
/// </summary>
public static class SoundPacks
{
    public const string ReleaseTag = "soundpacks";
    public const string Credit = "AeRodri (RealFlightOps)";

    public static string Folder => Path.Combine(CabinAnnouncer.UserFolder, "Packs");

    public static string PackFolder(string id) => Path.Combine(Folder, id);

    /// <summary>Gültige Kennung: nur Buchstaben, Ziffern, Unterstrich, Bindestrich (Dateiname des ZIPs ohne Endung).</summary>
    public static bool ValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    /// <summary>„English_UnitedKingdom“ → „United Kingdom“, „English_NewZealand“ → „New Zealand“.</summary>
    public static string DisplayName(string id)
    {
        var name = id.StartsWith("English_", StringComparison.OrdinalIgnoreCase) ? id[8..] : id;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c is '_' or '-') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    public static bool IsInstalled(string id) =>
        ValidId(id) && Directory.Exists(PackFolder(id)) && Directory.EnumerateFiles(PackFolder(id), "*.wav").Any();

    public static IEnumerable<string> InstalledIds() =>
        Directory.Exists(Folder)
            ? Directory.EnumerateDirectories(Folder).Select(Path.GetFileName).Where(n => n is not null && IsInstalled(n)).Select(n => n!)
            : [];

    /// <summary>Liste der Packs: auf GitHub angebotene und bereits installierte. Ohne Internet nur die installierten.</summary>
    public static async Task<List<SoundPackInfo>> ListAsync(HttpClient http, CancellationToken ct = default)
    {
        var result = new Dictionary<string, SoundPackInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in InstalledIds()) result[id] = new SoundPackInfo(id, DisplayName(id), null, true);
        try
        {
            foreach (var (id, size) in await AssetsAsync(http, ct).ConfigureAwait(false))
                result[id] = new SoundPackInfo(id, DisplayName(id), size, IsInstalled(id));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // offline – dann eben nur die installierten
        }
        return result.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<List<(string Id, long Size)>> AssetsAsync(HttpClient http, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{AppInfo.GitHubRepository}/releases/tags/{ReleaseTag}");
        request.Headers.UserAgent.ParseAdd(AppInfo.UserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return [];
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var list = new List<(string, long)>();
        if (!doc.RootElement.TryGetProperty("assets", out var assets)) return list;
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            var id = name[..^4];
            if (!ValidId(id)) continue;
            list.Add((id, a.TryGetProperty("size", out var s) ? s.GetInt64() : 0));
        }
        return list;
    }

    /// <summary>
    /// Lädt ein Pack von GitHub und legt die sieben Ansagen in seinen Ordner – egal, ob sie im ZIP in einem Unterordner
    /// liegen. Fremde Dateien im ZIP werden übergangen. Gibt die Zahl der Ansagen zurück.
    /// </summary>
    public static async Task<int> InstallAsync(string id, HttpClient http, CancellationToken ct = default)
    {
        if (!ValidId(id)) throw new ArgumentException("Ungültiger Name.", nameof(id));
        var url = $"https://github.com/{AppInfo.GitHubRepository}/releases/download/{ReleaseTag}/{id}.zip";
        Directory.CreateDirectory(Folder);
        var tempZip = Path.Combine(Folder, id + ".download");
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.UserAgent.ParseAdd(AppInfo.UserAgent);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(tempZip);
                await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);
            }
            return ExtractPack(tempZip, PackFolder(id));
        }
        finally
        {
            try { File.Delete(tempZip); } catch (IOException) { }
        }
    }

    /// <summary>Holt die bekannten Ansagen aus einem ZIP in den Zielordner (für Tests auch direkt nutzbar).</summary>
    public static int ExtractPack(string zipPath, string targetFolder)
    {
        var wanted = new HashSet<string>(CabinCues.All, StringComparer.OrdinalIgnoreCase);
        var staging = targetFolder + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        int count = 0;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.Contains("__MACOSX", StringComparison.Ordinal) || entry.Name.StartsWith("._", StringComparison.Ordinal)) continue;
                var match = CabinCues.All.FirstOrDefault(c => string.Equals(c, entry.Name, StringComparison.OrdinalIgnoreCase));
                if (match is null || !wanted.Remove(match)) continue;
                entry.ExtractToFile(Path.Combine(staging, match), overwrite: true);
                count++;
            }
        }
        if (count == 0)
        {
            Directory.Delete(staging, true);
            throw new InvalidDataException("Im Paket sind keine Ansagen mit den erwarteten Dateinamen.");
        }
        if (Directory.Exists(targetFolder)) Directory.Delete(targetFolder, true);
        Directory.Move(staging, targetFolder);
        return count;
    }
}
