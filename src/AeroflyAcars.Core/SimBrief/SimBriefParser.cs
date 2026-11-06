using System.Globalization;
using System.Text.Json;

namespace AeroflyAcars.Core.SimBrief;

public sealed class SimBriefException : Exception
{
    public SimBriefException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Liest die JSON-Antwort von https://www.simbrief.com/api/xml.fetcher.php?...&amp;json=1.
/// SimBrief liefert fast alle Werte als Strings und leere Werte als {} – daher die toleranten Helfer.
/// </summary>
public static class SimBriefParser
{
    public static FlightPlan Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new SimBriefException("Die Antwort von SimBrief konnte nicht gelesen werden.", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            var status = Str(root, "fetch", "status");
            if (status is not null && !status.Equals("Success", StringComparison.OrdinalIgnoreCase))
                throw new SimBriefException($"SimBrief meldet: {status}");

            var origin = Str(root, "origin", "icao_code");
            var destination = Str(root, "destination", "icao_code");
            if (origin is null || destination is null)
                throw new SimBriefException("Im SimBrief-Flugplan fehlen Abflug- oder Zielflughafen.");

            var plan = new FlightPlan
            {
                Callsign = Str(root, "atc", "callsign"),
                Airline = Str(root, "general", "icao_airline"),
                FlightNumber = Str(root, "general", "flight_number"),
                AircraftType = Str(root, "aircraft", "icaocode") ?? Str(root, "aircraft", "icao_code"),
                AircraftRegistration = Str(root, "aircraft", "reg"),
                Airac = Str(root, "params", "airac"),

                OriginIcao = origin,
                OriginName = Str(root, "origin", "name"),
                OriginLatitude = Num(root, "origin", "pos_lat"),
                OriginLongitude = Num(root, "origin", "pos_long"),
                OriginElevationFt = Num(root, "origin", "elevation"),
                OriginRunway = Str(root, "origin", "plan_rwy"),
                OriginMetar = Str(root, "origin", "metar"),

                DestinationIcao = destination,
                DestinationName = Str(root, "destination", "name"),
                DestinationLatitude = Num(root, "destination", "pos_lat"),
                DestinationLongitude = Num(root, "destination", "pos_long"),
                DestinationElevationFt = Num(root, "destination", "elevation"),
                DestinationRunway = Str(root, "destination", "plan_rwy"),

                Sid = Str(root, "general", "sid_ident"),
                SidTransition = Str(root, "general", "sid_trans"),
                Star = Str(root, "general", "star_ident"),
                StarTransition = Str(root, "general", "star_trans"),
                CruiseAltitudeFt = Num(root, "general", "initial_altitude"),
                RouteDistanceNm = Num(root, "general", "route_distance"),
                Route = Str(root, "general", "route"),
                PlannedBlockTime = Seconds(Num(root, "times", "est_block")),
                PlannedAirTime = Seconds(Num(root, "times", "est_time_enroute")),
            };

            return WithWaypoints(plan, root);
        }
    }

    private static FlightPlan WithWaypoints(FlightPlan plan, JsonElement root)
    {
        var list = new List<Waypoint>();

        if (plan.OriginLatitude is { } olat && plan.OriginLongitude is { } olon)
            list.Add(new Waypoint(plan.OriginIcao, olat, olon, plan.OriginElevationFt));

        if (TryGet(root, out var fixes, "navlog", "fix"))
        {
            IEnumerable<JsonElement> items = fixes.ValueKind switch
            {
                JsonValueKind.Array => fixes.EnumerateArray(),
                JsonValueKind.Object => new[] { fixes },
                _ => Array.Empty<JsonElement>(),
            };

            foreach (var fix in items)
            {
                var ident = Str(fix, "ident");
                var lat = Num(fix, "pos_lat");
                var lon = Num(fix, "pos_long");
                if (ident is null || lat is null || lon is null) continue;
                if (ident == plan.OriginIcao || ident == plan.DestinationIcao) continue;
                list.Add(new Waypoint(ident, lat.Value, lon.Value, Num(fix, "altitude_feet"), Str(fix, "is_sid_star") == "1"));
            }
        }

        if (plan.DestinationLatitude is { } dlat && plan.DestinationLongitude is { } dlon)
            list.Add(new Waypoint(plan.DestinationIcao, dlat, dlon, plan.DestinationElevationFt));

        return plan with { Waypoints = list };
    }

    private static TimeSpan? Seconds(double? s) => s is { } v && v > 0 ? TimeSpan.FromSeconds(v) : null;

    private static bool TryGet(JsonElement element, out JsonElement result, params string[] path)
    {
        result = element;
        foreach (var name in path)
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(name, out result))
                return false;
        }
        return true;
    }

    private static string? Str(JsonElement element, params string[] path)
    {
        if (!TryGet(element, out var value, path)) return null;
        var s = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static double? Num(JsonElement element, params string[] path) =>
        double.TryParse(Str(element, path), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
}
