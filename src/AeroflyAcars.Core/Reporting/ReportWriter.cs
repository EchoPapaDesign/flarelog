using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroflyAcars.Core.Reporting;

public static class ReportWriter
{
    private static CultureInfo De => Lang.Culture;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string L(string de, string en) => Lang.T(de, en);

    public static string ToJson(FlightReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static string ToText(FlightReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine($"  {L("ACARS-Bericht", "ACARS report")}  {r.Flight ?? L("Flug", "Flight")}  {r.Origin ?? "????"} → {r.Destination ?? "????"}");
        sb.AppendLine("==================================================");
        if (r.Origin is not null) sb.AppendLine($"{L("Abflug:", "Departure:"),-17}{r.Origin} {r.OriginName}".TrimEnd());
        if (r.Destination is not null) sb.AppendLine($"{L("Ziel:", "Destination:"),-17}{r.Destination} {r.DestinationName}".TrimEnd());
        if (r.Booking is not null) sb.AppendLine($"{L("Buchung:", "Booking:"),-17}{r.Booking}");
        if (r.Aircraft is not null) sb.AppendLine($"{L("Muster:", "Aircraft type:"),-17}{r.Aircraft}");
        if (r.Airac is not null) sb.AppendLine($"AIRAC (SimBrief): {r.Airac}");
        if (r.Route is not null) sb.AppendLine($"Route:           {r.Route}");
        sb.AppendLine();

        sb.AppendLine(L("Zeiten (UTC)", "Times (UTC)"));
        sb.AppendLine($"  Block off:     {Time(r.BlockOffUtc)}");
        sb.AppendLine($"  {L("Abheben:", "Takeoff:"),-15}{Time(r.TakeoffUtc)}");
        sb.AppendLine($"  {L("Aufsetzen:", "Touchdown:"),-15}{Time(r.TouchdownUtc)}");
        sb.AppendLine($"  Block on:      {Time(r.BlockOnUtc)}");
        sb.AppendLine();

        sb.AppendLine(L("Soll / Ist                 Plan        Ist      Abw.", "Planned / actual           Plan     Actual      Diff"));
        sb.AppendLine(Row(L("Blockzeit", "Block time"), Dur(r.PlannedBlockTime), Dur(r.BlockTime), Delta(r.BlockTime, r.PlannedBlockTime)));
        sb.AppendLine(Row(L("Flugzeit", "Air time"), Dur(r.PlannedAirTime), Dur(r.AirTime), Delta(r.AirTime, r.PlannedAirTime)));
        sb.AppendLine(Row(L("Strecke (NM)", "Distance (NM)"), Num(r.PlannedDistanceNm), Num(r.DistanceFlownNm), NumDelta(r.DistanceFlownNm, r.PlannedDistanceNm)));
        sb.AppendLine(Row(L("Max. Höhe (ft)", "Max. altitude (ft)"), Num(r.PlannedCruiseAltitudeFt), Num(r.MaxAltitudeFt), NumDelta(r.MaxAltitudeFt, r.PlannedCruiseAltitudeFt)));
        sb.AppendLine();

        sb.AppendLine(L("Landung", "Landing"));
        sb.AppendLine($"  {L("Landerate:", "Landing rate:"),-15}{(r.LandingRateFpm is { } lr ? lr.ToString("F0", De) + " fpm (" + r.LandingRating + ")" : "–")}");
        sb.AppendLine($"  Groundspeed:   {(r.TouchdownGroundSpeedKt is { } gs ? gs.ToString("F0", De) + " kt" : "–")}");
        sb.AppendLine($"  Pitch:         {(r.TouchdownPitchDeg is { } p ? p.ToString("F1", De) + "°" : "–")}");
        if (r.TouchdownDistanceToDestinationNm is { } dd)
            sb.AppendLine($"  {L("Abstand Ziel:", "Dist. to dest.:"),-15}{dd.ToString("F1", De)} NM");
        sb.AppendLine($"  {L("Durchstarten:", "Go-arounds:"),-15}{r.GoArounds}");
        if (r.Analysis is { } a)
        {
            sb.AppendLine($"  Bounces:       {a.Bounces}");
            if (a.ApproachVsSigmaFpm is { } sig)
                sb.AppendLine($"  {L("Endanflug:", "Approach:"),-15}{a.ApproachRating}, {L("Streuung", "spread")} {sig.ToString("F0", De)} fpm, " +
                              $"{L("Mittel", "mean")} {a.ApproachVsMeanFpm!.Value.ToString("F0", De)} fpm " +
                              $"({L("steilste", "steepest")} {a.ApproachVsSteepestFpm!.Value.ToString("F0", De)}, {L("flachste", "flattest")} {a.ApproachVsFlattestFpm!.Value.ToString("F0", De)})");
            if (a.RolloutM is { } ro) sb.AppendLine($"  Rollout:       {ro.ToString("N0", De)} m");
            sb.AppendLine();
            sb.AppendLine(L("Auswertung", "Analysis"));
            sb.AppendLine($"  {L("Bahnen:", "Runways:"),-15}{a.DepartureRunway ?? "–"} → {a.ArrivalRunway ?? "–"}");
            sb.AppendLine($"  {L("Rollzeiten:", "Taxi times:"),-15}Taxi-out {Dur(a.TaxiOut)}, Taxi-in {Dur(a.TaxiIn)}");
            sb.AppendLine($"  {L("Pausen:", "Paused:"),-15}{Dur(a.PausedTime)}");
            sb.AppendLine($"  {L("Zeitraffer:", "Sim rate:"),-15}max. {a.MaxSimRate.ToString("0.#", De)}×");
            sb.AppendLine($"  Slew:          {a.SlewEvents}");
            sb.AppendLine($"  {L("Tageslicht:", "Daylight:"),-15}{a.DaylightTakeoff ?? "–"} → {a.DaylightLanding ?? "–"}");
        }
        sb.AppendLine();

        sb.AppendLine(L("Ablauf", "Flight log"));
        foreach (var e in r.Phases)
            sb.AppendLine($"  {e.Time:HH:mm:ss}  {e.Text}");
        sb.AppendLine();

        if (r.Notes.Count > 0)
        {
            sb.AppendLine(L("Hinweise", "Notes"));
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
