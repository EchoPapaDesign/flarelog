using System.Text.Json;

namespace AeroflyAcars.Core.Airline;

/// <summary>
/// Airline-Edition: Eine Airline legt neben die .exe eine Datei "airline.json", z. B.
/// <c>{ "name": "Meine VA", "url": "https://www.meine-va.de" }</c>.
/// Dann ist die Airline beim ersten Start schon eingetragen; der Pilot fügt nur noch seinen API-Schlüssel ein.
/// </summary>
public sealed record AirlinePreset(string? Name, string? Url)
{
    public const string FileName = "airline.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Liest airline.json aus dem Programmordner; null, wenn keine (gültige) Datei da ist.</summary>
    public static AirlinePreset? Load(string? folder = null)
    {
        try
        {
            var path = Path.Combine(folder ?? AppContext.BaseDirectory, FileName);
            if (!File.Exists(path)) return null;
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static AirlinePreset? Parse(string json)
    {
        try
        {
            var p = JsonSerializer.Deserialize<AirlinePreset>(json, Json);
            if (p is null) return null;
            var url = p.Url?.Trim();
            if (url is not null && !(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                     url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                url = null;
            var name = string.IsNullOrWhiteSpace(p.Name) ? null : p.Name.Trim();
            return name is null && url is null ? null : new AirlinePreset(name, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
