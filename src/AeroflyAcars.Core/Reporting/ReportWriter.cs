using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroflyAcars.Core.Reporting;

public static class ReportWriter
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(FlightReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static string ToText(FlightReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine($"  ACARS-Bericht  {r.Flight ?? "Flug"}  {r.Origin ?? "????"} → {r.Destination ?? "????"}");
        sb.AppendLine("==================================================");
        if (r.OriginName is not null) sb.AppendLine($"Abflug:          {r.Origin} {r.OriginName}");
        if (r.DestinationName is not null) sb.AppendLine($"Ziel:            {r.Destination} {r.DestinationName}");
        if (r.Booking is not null) sb.AppendLine($"Buchung:         {r.Booking}");
        if (r.Aircraft is not null) sb.AppendLine($"Muster:          {r.Aircraft}");
        if (r.Airac is not null) sb.AppendLine($"AIRAC (SimBrief): {r.Airac}");
        if (r.Route is not null) sb.AppendLine($"Route:           {r.Route}");
        sb.AppendLine();

        sb.AppendLine("Zeiten (UTC)");
        sb.AppendLine($"  Block off:     {Time(r.BlockOffUtc)}");
        sb.AppendLine($"  Abheben:       {Time(r.TakeoffUtc)}");
        sb.AppendLine($"  Aufsetzen:     {Time(r.TouchdownUtc)}");
        sb.AppendLine($"  Block on:      {Time(r.BlockOnUtc)}");
        sb.AppendLine();

        sb.AppendLine("Soll / Ist                 Plan        Ist      Abw.");
        sb.AppendLine(Row("Blockzeit", Dur(r.PlannedBlockTime), Dur(r.BlockTime), Delta(r.BlockTime, r.PlannedBlockTime)));
        sb.AppendLine(Row("Flugzeit", Dur(r.PlannedAirTime), Dur(r.AirTime), Delta(r.AirTime, r.PlannedAirTime)));
        sb.AppendLine(Row("Strecke (NM)", Num(r.PlannedDistanceNm), Num(r.DistanceFlownNm), NumDelta(r.DistanceFlownNm, r.PlannedDistanceNm)));
        sb.AppendLine(Row("Max. Höhe (ft)", Num(r.PlannedCruiseAltitudeFt), Num(r.MaxAltitudeFt), NumDelta(r.MaxAltitudeFt, r.PlannedCruiseAltitudeFt)));
        sb.AppendLine();

        sb.AppendLine("Landung");
        sb.AppendLine($"  Landerate:     {(r.LandingRateFpm is { } lr ? lr.ToString("F0", De) + " fpm (" + r.LandingRating + ")" : "–")}");
        sb.AppendLine($"  Groundspeed:   {(r.TouchdownGroundSpeedKt is { } gs ? gs.ToString("F0", De) + " kt" : "–")}");
        sb.AppendLine($"  Pitch:         {(r.TouchdownPitchDeg is { } p ? p.ToString("F1", De) + "°" : "–")}");
        if (r.TouchdownDistanceToDestinationNm is { } dd)
            sb.AppendLine($"  Abstand Ziel:  {dd.ToString("F1", De)} NM");
        sb.AppendLine($"  Durchstarten:  {r.GoArounds}");
        if (r.Analysis is { } a)
        {
            sb.AppendLine($"  Bounces:       {a.Bounces}");
            if (a.ApproachVsSigmaFpm is { } sig)
                sb.AppendLine($"  Endanflug:     {a.ApproachRating}, Streuung {sig.ToString("F0", De)} fpm, " +
                              $"Mittel {a.ApproachVsMeanFpm!.Value.ToString("F0", De)} fpm " +
                              $"(steilste {a.ApproachVsSteepestFpm!.Value.ToString("F0", De)}, flachste {a.ApproachVsFlattestFpm!.Value.ToString("F0", De)})");
            if (a.RolloutM is { } ro) sb.AppendLine($"  Rollout:       {ro.ToString("N0", De)} m");
            sb.AppendLine();
            sb.AppendLine("Auswertung");
            sb.AppendLine($"  Bahnen:        {a.DepartureRunway ?? "–"} → {a.ArrivalRunway ?? "–"}");
            sb.AppendLine($"  Rollzeiten:    Taxi-out {Dur(a.TaxiOut)}, Taxi-in {Dur(a.TaxiIn)}");
            sb.AppendLine($"  Pausen:        {Dur(a.PausedTime)}");
            sb.AppendLine($"  Zeitraffer:    max. {a.MaxSimRate.ToString("0.#", De)}×");
            sb.AppendLine($"  Slew:          {a.SlewEvents}");
            sb.AppendLine($"  Tageslicht:    {a.DaylightTakeoff ?? "–"} → {a.DaylightLanding ?? "–"}");
        }
        sb.AppendLine();

        sb.AppendLine("Ablauf");
        foreach (var e in r.Phases)
            sb.AppendLine($"  {e.Time:HH:mm:ss}  {e.Text}");
        sb.AppendLine();

        if (r.Notes.Count > 0)
        {
            sb.AppendLine("Hinweise");
            foreach (var n in r.Notes) sb.AppendLine($"  - {n}");
        }
        return sb.ToString();
    }

    /// <summary>Speichert JSON- und Textbericht und gibt beide Pfade zurück.</summary>
    public static (string JsonPath, string TextPath) Save(FlightReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        var stamp = (report.BlockOffUtc ?? report.CreatedUtc).ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
        var name = Sanitize($"{stamp}_{report.Origin ?? "XXXX"}-{report.Destination ?? "XXXX"}");

        var basePath = Path.Combine(directory, name);
        for (int i = 2; File.Exists(basePath + ".json"); i++)
            basePath = Path.Combine(directory, $"{name}_{i}");

        var jsonPath = basePath + ".json";
        var textPath = basePath + ".txt";
        File.WriteAllText(jsonPath, ToJson(report), new UTF8Encoding(false));
        File.WriteAllText(textPath, ToText(report), new UTF8Encoding(true));
        return (jsonPath, textPath);
    }

    private static string Row(string label, string plan, string actual, string delta) =>
        $"  {label,-20}{plan,10} {actual,10} {delta,9}";

    private static string Time(DateTimeOffset? t) => t is { } v ? v.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "–";

    private static string Dur(TimeSpan? t) =>
        t is { } v ? $"{(int)v.TotalHours:00}:{v.Minutes:00}:{v.Seconds:00}" : "–";

    private static string Delta(TimeSpan? actual, TimeSpan? plan)
    {
        if (actual is not { } a || plan is not { } p) return "";
        var d = a - p;
        var sign = d < TimeSpan.Zero ? "-" : "+";
        d = d.Duration();
        return $"{sign}{(int)d.TotalHours:00}:{d.Minutes:00}";
    }

    private static string Num(double? v) => v is { } x ? x.ToString("N0", De) : "–";

    private static string NumDelta(double? actual, double? plan) =>
        actual is { } a && plan is { } p ? (a - p).ToString("+#,##0;-#,##0;0", De) : "";

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
