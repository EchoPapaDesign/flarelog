using System.Globalization;

namespace AeroflyAcars.Core.Airports;

/// <summary>Ein Bahnende, z. B. "05R" mit Schwellenkoordinaten und rechtweisendem Kurs.</summary>
public sealed record RunwayEnd(string Ident, double? Latitude, double? Longitude, double? HeadingTrue, double? ElevationFt = null);

/// <summary>Eine Bahn mit beiden Enden (OurAirports: le_* = "low end", he_* = "high end").</summary>
public sealed record Runway(string AirportIdent, double? LengthFt, RunwayEnd Low, RunwayEnd High);

/// <summary>Bodenhöhe auf einer Bahn. Implementiert von <see cref="RunwayDatabase"/>.</summary>
public interface IRunwayLookup
{
    /// <summary>Höhe der Bahnoberfläche (ft MSL) an der Position, falls sie auf einer bekannten Bahn liegt.</summary>
    double? GroundElevationAt(Airport airport, double latitude, double longitude);

    /// <summary>True, wenn für den Platz Bahnen mit Koordinaten und Höhen bekannt sind.</summary>
    bool HasRunwayElevations(Airport airport);
}

/// <summary>
/// Bahnen aus der gemeinfreien OurAirports-Liste runways.csv.
/// Erkennt anhand von Position und Kurs, auf welcher Bahn gestartet bzw. gelandet wurde.
/// </summary>
public sealed class RunwayDatabase : IRunwayLookup
{
    public const double MaxHeadingDiffDeg = 30;
    public const double MaxCrossTrackM = 200;
    public const double MaxBeyondEndsM = 600;

    private readonly Dictionary<string, List<Runway>> _byAirport;

    public RunwayDatabase(IEnumerable<Runway> runways)
    {
        _byAirport = runways.GroupBy(r => r.AirportIdent, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _byAirport.Values.Sum(l => l.Count);

    public IReadOnlyList<Runway> ForAirport(string ident) =>
        _byAirport.TryGetValue(ident, out var list) ? list : Array.Empty<Runway>();

    public static RunwayDatabase LoadCsv(string path)
    {
        using var reader = new StreamReader(path);
        return ParseCsv(reader);
    }

    public static RunwayDatabase ParseCsv(TextReader reader)
    {
        var header = reader.ReadLine() ?? throw new InvalidDataException("Bahnenliste ist leer.");
        var cols = AirportDatabase.SplitCsvLine(header);
        int C(string name) => cols.IndexOf(name);
        int cApt = C("airport_ident"), cLen = C("length_ft"), cClosed = C("closed");
        int cLeId = C("le_ident"), cLeLat = C("le_latitude_deg"), cLeLon = C("le_longitude_deg"), cLeHdg = C("le_heading_degT"), cLeElev = C("le_elevation_ft");
        int cHeId = C("he_ident"), cHeLat = C("he_latitude_deg"), cHeLon = C("he_longitude_deg"), cHeHdg = C("he_heading_degT"), cHeElev = C("he_elevation_ft");
        if (cApt < 0 || cLeId < 0 || cHeId < 0) throw new InvalidDataException("Spalten fehlen in der Bahnenliste.");

        var list = new List<Runway>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var f = AirportDatabase.SplitCsvLine(line);
            string? Get(int i) => i >= 0 && i < f.Count && !string.IsNullOrWhiteSpace(f[i]) ? f[i].Trim() : null;
            double? Num(int i) => double.TryParse(Get(i), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

            if (Get(cClosed) == "1") continue;
            var apt = Get(cApt);
            var le = Get(cLeId);
            var he = Get(cHeId);
            if (apt is null || (le is null && he is null)) continue;

            list.Add(new Runway(apt, Num(cLen),
                new RunwayEnd(le ?? "?", Num(cLeLat), Num(cLeLon), Num(cLeHdg), Num(cLeElev)),
                new RunwayEnd(he ?? "?", Num(cHeLat), Num(cHeLon), Num(cHeHdg), Num(cHeElev))));
        }
        return new RunwayDatabase(list);
    }

    /// <summary>Seitlicher Abstand zur Bahnachse, bis zu dem eine Position als „auf der Bahn“ gilt.</summary>
    public const double OnRunwayCrossTrackM = 60;
    public const double OnRunwayBeyondEndsM = 60;

    /// <summary>Bahnende nach Platz und Bezeichnung (z. B. "KDFW", "35L"), mit dem Gegenende und der Bahn.</summary>
    public (Runway Runway, RunwayEnd End, RunwayEnd Other)? FindEnd(string airportIdent, string runwayIdent)
    {
        string Norm(string s) => s.Trim().TrimStart('0').ToUpperInvariant();
        var want = Norm(runwayIdent);
        foreach (var rwy in ForAirport(airportIdent))
        {
            if (Norm(rwy.Low.Ident) == want) return (rwy, rwy.Low, rwy.High);
            if (Norm(rwy.High.Ident) == want) return (rwy, rwy.High, rwy.Low);
        }
        return null;
    }

    public bool HasRunwayElevations(Airport airport)
    {
        var runways = ForAirport(airport.SourceIdent ?? airport.Ident);
        if (runways.Count == 0 && airport.SourceIdent is not null) runways = ForAirport(airport.Ident);
        return runways.Any(r => r.Low.Latitude is not null && r.High.Latitude is not null &&
                                (r.Low.ElevationFt ?? r.High.ElevationFt) is not null);
    }

    /// <summary>
    /// Höhe der Bahnoberfläche an der Position: zwischen den Schwellenhöhen interpoliert (Bahnen können
    /// deutlich geneigt sein, z. B. KAFW 34R: 664 ft, 16L: 723 ft). Null, wenn die Position auf keiner Bahn
    /// mit bekannten Koordinaten und Höhen liegt.
    /// </summary>
    public double? GroundElevationAt(Airport airport, double lat, double lon)
    {
        var runways = ForAirport(airport.SourceIdent ?? airport.Ident);
        if (runways.Count == 0 && airport.SourceIdent is not null) runways = ForAirport(airport.Ident);

        double? best = null;
        double bestXt = double.MaxValue;
        foreach (var rwy in runways)
        {
            var (a, b) = (rwy.Low, rwy.High);
            if (a.Latitude is null || a.Longitude is null || b.Latitude is null || b.Longitude is null) continue;
            double? ea = a.ElevationFt ?? b.ElevationFt, eb = b.ElevationFt ?? a.ElevationFt;
            if (ea is null || eb is null) continue;

            var (xtNm, atNm) = Geo.TrackOffsets(a.Latitude.Value, a.Longitude.Value, b.Latitude.Value, b.Longitude.Value, lat, lon);
            double lengthM = Geo.DistanceNm(a.Latitude.Value, a.Longitude.Value, b.Latitude.Value, b.Longitude.Value) * Geo.MetersPerNm;
            double xtM = Math.Abs(xtNm * Geo.MetersPerNm), atM = atNm * Geo.MetersPerNm;
            if (lengthM <= 0 || xtM > OnRunwayCrossTrackM || atM < -OnRunwayBeyondEndsM || atM > lengthM + OnRunwayBeyondEndsM) continue;
            if (xtM >= bestXt) continue;

            double f = Math.Clamp(atM / lengthM, 0, 1);
            best = ea.Value + (eb.Value - ea.Value) * f;
            bestXt = xtM;
        }
        return best;
    }

    /// <summary>
    /// Bahnende, auf dem sich ein Flugzeug an der Position mit dem Kurs bewegt (Start: Anfang des Startlaufs,
    /// Landung: Aufsetzpunkt). Mit Koordinaten wird zusätzlich die Lage zur Bahnachse geprüft.
    /// </summary>
    public RunwayEnd? Match(Airport airport, double lat, double lon, double trackTrue)
    {
        var runways = ForAirport(airport.SourceIdent ?? airport.Ident);
        if (runways.Count == 0 && airport.SourceIdent is not null) runways = ForAirport(airport.Ident);

        RunwayEnd? best = null;
        double bestScore = double.MaxValue;
        foreach (var rwy in runways)
        {
            foreach (var (end, other) in new[] { (rwy.Low, rwy.High), (rwy.High, rwy.Low) })
            {
                bool coords = end.Latitude is not null && end.Longitude is not null &&
                              other.Latitude is not null && other.Longitude is not null;
                double? heading = end.HeadingTrue ??
                                  (coords ? Geo.BearingDeg(end.Latitude!.Value, end.Longitude!.Value, other.Latitude!.Value, other.Longitude!.Value) : null);
                if (heading is null) continue;

                double diff = Geo.AngleDiff(heading.Value, trackTrue);
                if (diff > MaxHeadingDiffDeg) continue;

                double score = diff;
                if (coords)
                {
                    var (xtNm, atNm) = Geo.TrackOffsets(end.Latitude!.Value, end.Longitude!.Value,
                        other.Latitude!.Value, other.Longitude!.Value, lat, lon);
                    double lengthM = Geo.DistanceNm(end.Latitude.Value, end.Longitude.Value,
                        other.Latitude.Value, other.Longitude.Value) * Geo.MetersPerNm;
                    double xtM = xtNm * Geo.MetersPerNm, atM = atNm * Geo.MetersPerNm;
                    if (xtM > MaxCrossTrackM || atM < -MaxBeyondEndsM || atM > lengthM + MaxBeyondEndsM) continue;
                    score += xtM / 10;
                }
                else
                {
                    score += 50;   // ohne Koordinaten nur über den Kurs – schwächer gewichten
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = end;
                }
            }
        }
        return best;
    }
}
