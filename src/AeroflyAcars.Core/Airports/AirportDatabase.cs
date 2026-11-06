using System.Globalization;

namespace AeroflyAcars.Core.Airports;

/// <param name="SourceIdent">OurAirports-Kennung (verknüpft mit runways.csv), falls abweichend vom angezeigten Ident.</param>
public sealed record Airport(string Ident, string Name, string Type, double Latitude, double Longitude, double? ElevationFt,
    string? Municipality, string? SourceIdent = null)
{
    public string DisplayName => Municipality is null || Name.Contains(Municipality, StringComparison.OrdinalIgnoreCase)
        ? $"{Ident} {Name}"
        : $"{Ident} {Name} ({Municipality})";
}

/// <summary>Sucht Flugplätze nach Position. Implementiert von <see cref="AirportDatabase"/>.</summary>
public interface IAirportLookup
{
    Airport? Nearest(double latitude, double longitude, double maxDistanceNm);
}

/// <summary>
/// Flugplätze aus der gemeinfreien OurAirports-Liste (airports.csv).
/// https://ourairports.com/data/ – Spalten u. a. ident, type, name, latitude_deg, longitude_deg, elevation_ft, municipality, icao_code.
/// </summary>
public sealed class AirportDatabase : IAirportLookup
{
    private static readonly HashSet<string> UsedTypes = new(StringComparer.Ordinal)
    {
        "large_airport", "medium_airport", "small_airport", "heliport", "seaplane_base",
    };

    private readonly List<Airport> _airports;
    // Grobes Raster (1° × 1°) für eine schnelle Umkreissuche.
    private readonly Dictionary<(int, int), List<Airport>> _grid = new();

    public AirportDatabase(IEnumerable<Airport> airports)
    {
        _airports = airports.ToList();
        foreach (var a in _airports)
        {
            var key = Cell(a.Latitude, a.Longitude);
            if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<Airport>();
            list.Add(a);
        }
    }

    public int Count => _airports.Count;

    /// <summary>Flugplatz nach Kennung (ICAO bzw. OurAirports-Ident), sonst null.</summary>
    public Airport? Find(string ident) =>
        _airports.FirstOrDefault(a => a.Ident.Equals(ident, StringComparison.OrdinalIgnoreCase)) ??
        _airports.FirstOrDefault(a => ident.Equals(a.SourceIdent, StringComparison.OrdinalIgnoreCase));

    public static AirportDatabase LoadCsv(string path)
    {
        using var reader = new StreamReader(path);
        return ParseCsv(reader);
    }

    public static AirportDatabase ParseCsv(TextReader reader)
    {
        var header = reader.ReadLine() ?? throw new InvalidDataException("Flughafenliste ist leer.");
        var columns = SplitCsvLine(header);
        int Col(string name)
        {
            int i = columns.IndexOf(name);
            if (i < 0) throw new InvalidDataException($"Spalte '{name}' fehlt in der Flughafenliste.");
            return i;
        }

        int cIdent = Col("ident"), cType = Col("type"), cName = Col("name"),
            cLat = Col("latitude_deg"), cLon = Col("longitude_deg"), cElev = Col("elevation_ft");
        int cCity = columns.IndexOf("municipality");
        int cIcao = columns.IndexOf("icao_code");
        int cGps = columns.IndexOf("gps_code");

        var list = new List<Airport>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var f = SplitCsvLine(line);
            if (f.Count <= Math.Max(cElev, Math.Max(cLat, cLon))) continue;
            if (!UsedTypes.Contains(f[cType])) continue;
            if (!double.TryParse(f[cLat], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(f[cLon], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                continue;
            double? elev = double.TryParse(f[cElev], NumberStyles.Float, CultureInfo.InvariantCulture, out var e) ? e : null;

            // Bevorzugt den ICAO-Code, sonst GPS-Code, sonst die OurAirports-Kennung.
            string ident = Field(f, cIcao) ?? Field(f, cGps) ?? f[cIdent];
            list.Add(new Airport(ident, f[cName], f[cType], lat, lon, elev, Field(f, cCity), f[cIdent]));
        }
        return new AirportDatabase(list);
    }

    /// <summary>
    /// Nächster Flugplatz im Umkreis. Flughäfen haben Vorrang; Hubschrauberlandeplätze kommen nur,
    /// wenn im Umkreis kein Flughafen liegt (z. B. Kliniklandeplatz neben dem Rollweg).
    /// </summary>
    public Airport? Nearest(double latitude, double longitude, double maxDistanceNm) =>
        Search(latitude, longitude, maxDistanceNm, heliports: false)
        ?? Search(latitude, longitude, maxDistanceNm, heliports: true);

    private Airport? Search(double latitude, double longitude, double maxDistanceNm, bool heliports)
    {
        var (cy, cx) = Cell(latitude, longitude);
        Airport? best = null;
        double bestDist = double.MaxValue;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int x = cx + dx;
            if (x < -180) x += 360;
            if (x >= 180) x -= 360;
            if (!_grid.TryGetValue((cy + dy, x), out var list)) continue;
            foreach (var a in list)
            {
                if ((a.Type == "heliport") != heliports) continue;
                double d = Geo.DistanceNm(latitude, longitude, a.Latitude, a.Longitude);
                if (d > maxDistanceNm) continue;
                // Große Flughäfen leicht bevorzugen: ihr Bezugspunkt liegt oft >1 NM vom Aufsetzpunkt entfernt.
                double score = d * Weight(a.Type);
                if (score < bestDist)
                {
                    bestDist = score;
                    best = a;
                }
            }
        }
        return best;
    }

    private static double Weight(string type) => type switch
    {
        "large_airport" or "medium_airport" => 1.0,
        "small_airport" => 1.3,
        "seaplane_base" => 2.0,
        _ => 4.0,
    };

    private static (int, int) Cell(double lat, double lon) => ((int)Math.Floor(lat), (int)Math.Floor(lon));

    private static string? Field(List<string> f, int index) =>
        index >= 0 && index < f.Count && !string.IsNullOrWhiteSpace(f[index]) ? f[index].Trim() : null;

    /// <summary>CSV-Zeile mit Anführungszeichen und doppelten "" innerhalb von Feldern.</summary>
    internal static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else quoted = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
