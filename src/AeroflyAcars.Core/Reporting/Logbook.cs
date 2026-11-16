using System.Text.Json;

namespace AeroflyAcars.Core.Reporting;

/// <summary>Eine Zeile im Logbuch – das Wichtigste eines gespeicherten Flugberichts.</summary>
public sealed record LogbookEntry(
    string Id,
    DateTimeOffset Date,
    string? Flight,
    string? Origin,
    string? Destination,
    string? OriginName,
    string? DestinationName,
    string? Aircraft,
    double? BlockMin,
    double? AirMin,
    double? DistanceNm,
    double? LandingRateFpm,
    string? LandingRating,
    bool Complete);

/// <summary>Summen über alle Flüge im Logbuch.</summary>
public sealed record LogbookTotals(int Flights, double AirMin, double BlockMin, double DistanceNm, int Airports, double? AvgLandingRateFpm);

/// <summary>
/// Logbuch: liest die gespeicherten Flugberichte (JSON) aus dem Berichtsordner. Es gibt keine eigene Datenbank –
/// der Ordner ist das Logbuch. Wer einen Bericht löscht oder hineinkopiert, sieht das beim nächsten Öffnen.
/// Gelesene Berichte werden nach Pfad, Größe und Änderungszeit zwischengespeichert, damit große Ordner schnell bleiben.
/// </summary>
public static class Logbook
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (long Length, DateTime Written, LogbookEntry? Entry)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Alle lesbaren Flüge im Ordner, neueste zuerst. Fehlt der Ordner, ist die Liste leer.</summary>
    public static List<LogbookEntry> Load(string folder)
    {
        var result = new List<LogbookEntry>();
        if (!Directory.Exists(folder)) return result;
        lock (Gate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
            {
                seen.Add(path);
                FileInfo info;
                try { info = new FileInfo(path); } catch (Exception) { continue; }
                if (!Cache.TryGetValue(path, out var c) || c.Length != info.Length || c.Written != info.LastWriteTimeUtc)
                {
                    c = (info.Length, info.LastWriteTimeUtc, ReadEntry(path));
                    Cache[path] = c;
                }
                if (c.Entry is not null) result.Add(c.Entry);
            }
            // Gelöschte Berichte auch aus dem Zwischenspeicher nehmen.
            var dir = Path.GetDirectoryName(Path.Combine(folder, "x.json"));
            foreach (var gone in Cache.Keys.Where(k => !seen.Contains(k) && string.Equals(Path.GetDirectoryName(k), dir, StringComparison.OrdinalIgnoreCase)).ToList())
                Cache.Remove(gone);
        }
        result.Sort((a, b) => b.Date.CompareTo(a.Date));
        return result;
    }

    /// <summary>Einen Bericht vollständig laden. Die Kennung ist der Dateiname ohne Endung; Pfadangaben werden abgelehnt.</summary>
    public static FlightReport? LoadReport(string folder, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..")) return null;
        var path = Path.Combine(folder, id + ".json");
        return File.Exists(path) ? Read(path) : null;
    }

    public static LogbookTotals Totals(IReadOnlyList<LogbookEntry> entries)
    {
        var airports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (!string.IsNullOrWhiteSpace(e.Origin)) airports.Add(e.Origin);
            if (!string.IsNullOrWhiteSpace(e.Destination)) airports.Add(e.Destination);
        }
        var rates = entries.Where(e => e.LandingRateFpm is not null).Select(e => e.LandingRateFpm!.Value).ToList();
        return new LogbookTotals(
            entries.Count,
            entries.Sum(e => e.AirMin ?? 0),
            entries.Sum(e => e.BlockMin ?? 0),
            entries.Sum(e => e.DistanceNm ?? 0),
            airports.Count,
            rates.Count > 0 ? rates.Average() : null);
    }

    public static LogbookEntry ToEntry(string id, FlightReport r) => new(
        id,
        r.BlockOffUtc ?? r.TakeoffUtc ?? r.Track.FirstOrDefault()?.Time ?? r.CreatedUtc,
        r.Flight, r.Origin, r.Destination, r.OriginName, r.DestinationName, r.Aircraft,
        r.BlockTime?.TotalMinutes, r.AirTime?.TotalMinutes, r.DistanceFlownNm,
        r.LandingRateFpm, r.LandingRating, r.Complete);

    private static LogbookEntry? ReadEntry(string path)
    {
        var r = Read(path);
        return r is null ? null : ToEntry(Path.GetFileNameWithoutExtension(path), r);
    }

    private static FlightReport? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var r = JsonSerializer.Deserialize<FlightReport>(stream, ReportWriter.JsonOptions);
            // Nur echte Flarelog-Berichte: andere JSON-Dateien im Ordner werden übergangen.
            return r is not null && (r.Track.Count > 0 || r.Origin is not null || r.TakeoffUtc is not null) ? r : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
