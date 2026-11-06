using AeroflyAcars.Core.Airline;
using AeroflyAcars.Core.Airports;
using AeroflyAcars.Core.Tracking;

namespace AeroflyAcars.Core.Reporting;

public static class FlightReportBuilder
{
    private static readonly TimeSpan TrackInterval = TimeSpan.FromSeconds(5);

    public static FlightReport Build(FlightTracker tracker, DateTimeOffset now, AirlineBooking? booking = null,
        RunwayDatabase? runways = null)
    {
        var plan = tracker.Plan;
        var samples = tracker.Samples;
        var notes = new List<string>
        {
            "Datenquelle: Aerofly UDP-Broadcast (ForeFlight-Format). Vertikalgeschwindigkeit und Landerate " +
            "sind aus der GPS-Höhe berechnet; Fahrwerk, Klappen und Treibstoff sind über UDP nicht verfügbar.",
        };

        TimeSpan? blockTime = tracker.BlockOff is { } off && tracker.BlockOn is { } on ? on - off : null;
        TimeSpan? airTime = tracker.Takeoff is { } to && tracker.Touchdown is { } td ? td - to : null;

        double? distance = null;
        if (tracker.Takeoff is { } start)
            distance = DistanceNm(samples, start, tracker.Touchdown ?? DateTimeOffset.MaxValue);

        double? touchdownToDest = null;
        var tdSample = tracker.TouchdownSample;
        if (tdSample is not null && plan?.DestinationLatitude is { } dlat && plan.DestinationLongitude is { } dlon)
        {
            touchdownToDest = Geo.DistanceNm(tdSample.Latitude, tdSample.Longitude, dlat, dlon);
            if (touchdownToDest > 5)
                notes.Add($"Gelandet {touchdownToDest:F1} NM vom geplanten Ziel {plan.DestinationIcao} entfernt.");
        }

        var analysis = FlightAnalysis.Analyze(tracker, runways);
        if (analysis.MaxSimRate > 1) notes.Add($"Zeitraffer erkannt: bis ca. {analysis.MaxSimRate:0.#}×.");
        if (analysis.SlewEvents > 0) notes.Add($"Positionssprung (Slew/Versetzen) {analysis.SlewEvents}× erkannt.");
        if (tracker.PauseCount > 0)
            notes.Add($"Simulator {tracker.PauseCount}× pausiert, zusammen {tracker.PausedTime:mm\\:ss} min (in Flug- und Blockzeit enthalten).");
        if (plan is not null && tracker.ArrivalAirport is { } arr &&
            !arr.Ident.Equals(plan.DestinationIcao, StringComparison.OrdinalIgnoreCase))
            notes.Add($"Gelandet in {arr.DisplayName} statt in {plan.DestinationIcao}.");
        if (booking is not null && tracker.ArrivalAirport is { } landed &&
            !landed.Ident.Equals(booking.Arrival, StringComparison.OrdinalIgnoreCase))
            notes.Add($"Buchung {booking.Ident} hatte Ziel {booking.Arrival}, gelandet in {landed.Ident}.");
        if (tracker.StartedAirborne) notes.Add("Aufzeichnung wurde im Flug gestartet – keine Startdaten.");
        if (!tracker.IsFinished) notes.Add($"Flug nicht vollständig aufgezeichnet (letzte Phase: {tracker.Phase.ToGerman()}).");

        return new FlightReport
        {
            CreatedUtc = now.ToUniversalTime(),
            Complete = tracker.IsFinished,
            Flight = plan?.DisplayName ?? booking?.Ident,
            Booking = booking?.Ident,
            Origin = plan?.OriginIcao ?? booking?.Departure ?? tracker.DepartureAirport?.Ident,
            Destination = plan?.DestinationIcao ?? booking?.Arrival ?? tracker.ArrivalAirport?.Ident,
            OriginName = tracker.DepartureAirport?.Name ?? plan?.OriginName,
            DestinationName = tracker.ArrivalAirport?.Name ?? plan?.DestinationName,
            Pauses = tracker.PauseCount,
            PausedTime = tracker.PauseCount > 0 ? tracker.PausedTime : null,
            Aircraft = plan?.AircraftType,
            Route = plan?.Route,
            Airac = plan?.Airac,

            BlockOffUtc = tracker.BlockOff?.ToUniversalTime(),
            TakeoffUtc = tracker.Takeoff?.ToUniversalTime(),
            TouchdownUtc = tracker.Touchdown?.ToUniversalTime(),
            BlockOnUtc = tracker.BlockOn?.ToUniversalTime(),

            BlockTime = blockTime,
            PlannedBlockTime = plan?.PlannedBlockTime,
            AirTime = airTime,
            PlannedAirTime = plan?.PlannedAirTime,

            DistanceFlownNm = distance,
            PlannedDistanceNm = plan?.RouteDistanceNm,
            MaxAltitudeFt = samples.Count > 0 ? tracker.MaxAltitudeFt : null,
            PlannedCruiseAltitudeFt = plan?.CruiseAltitudeFt,

            LandingRateFpm = tracker.LandingRateFpm,
            LandingRating = tracker.LandingRateFpm is { } rate ? RateLanding(rate) : null,
            TouchdownGroundSpeedKt = tdSample?.GroundSpeedKt,
            TouchdownPitchDeg = tdSample?.PitchDeg,
            TouchdownDistanceToDestinationNm = touchdownToDest,
            GoArounds = tracker.GoArounds,
            Analysis = analysis,

            Phases = tracker.Events.Select(e => new PhaseEntry(e.Time.ToUniversalTime(), e.Phase, e.Text)).ToList(),
            Notes = notes,
            Track = Thin(samples),
        };
    }

    public static string RateLanding(double fpm) => fpm switch
    {
        >= -60 => "sehr weich",
        >= -240 => "gut",
        >= -400 => "fest",
        >= -600 => "hart",
        _ => "sehr hart",
    };

    private static double DistanceNm(IReadOnlyList<FlightSample> samples, DateTimeOffset from, DateTimeOffset to)
    {
        double total = 0;
        FlightSample? prev = null;
        foreach (var s in samples)
        {
            if (s.Time < from || s.Time > to) continue;
            if (prev is not null)
                total += Geo.DistanceNm(prev.Latitude, prev.Longitude, s.Latitude, s.Longitude);
            prev = s;
        }
        return total;
    }

    private static List<TrackPoint> Thin(IReadOnlyList<FlightSample> samples)
    {
        var result = new List<TrackPoint>();
        DateTimeOffset? last = null;
        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            bool isLast = i == samples.Count - 1;
            if (last is null || s.Time - last.Value >= TrackInterval || isLast)
            {
                result.Add(new TrackPoint(s.Time.ToUniversalTime(), Math.Round(s.Latitude, 6), Math.Round(s.Longitude, 6),
                    Math.Round(s.AltitudeFt), Math.Round(s.GroundSpeedKt), Math.Round(s.VerticalSpeedFpm)));
                last = s.Time;
            }
        }
        return result;
    }
}
