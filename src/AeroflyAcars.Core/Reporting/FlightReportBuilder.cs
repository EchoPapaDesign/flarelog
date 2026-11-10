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
            Lang.T("Datenquelle: Aerofly UDP-Broadcast (ForeFlight-Format). Vertikalgeschwindigkeit und Landerate " +
                   "sind aus der GPS-Höhe berechnet; Fahrwerk, Klappen und Treibstoff sind über UDP nicht verfügbar.",
                   "Data source: Aerofly UDP broadcast (ForeFlight format). Vertical speed and landing rate are " +
                   "calculated from GPS altitude; gear, flaps and fuel are not available via UDP."),
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
                notes.Add(Lang.T($"Gelandet {touchdownToDest:F1} NM vom geplanten Ziel {plan.DestinationIcao} entfernt.", $"Landed {touchdownToDest:F1} NM from the planned destination {plan.DestinationIcao}."));
        }

        var analysis = FlightAnalysis.Analyze(tracker, runways);
        if (analysis.MaxSimRate > 1) notes.Add(Lang.T($"Zeitraffer erkannt: bis ca. {analysis.MaxSimRate:0.#}×.", $"Sim rate detected: up to approx. {analysis.MaxSimRate:0.#}×."));
        if (analysis.SlewEvents > 0) notes.Add(Lang.T($"Positionssprung (Slew/Versetzen) {analysis.SlewEvents}× erkannt.", $"Position jump (slew/reposition) detected {analysis.SlewEvents}×."));
        if (tracker.PauseCount > 0)
            notes.Add(Lang.T($"Simulator {tracker.PauseCount}× pausiert, zusammen {tracker.PausedTime:mm\\:ss} min (in Flug- und Blockzeit enthalten).", $"Simulator paused {tracker.PauseCount}×, {tracker.PausedTime:mm\\:ss} min in total (included in air and block time)."));
        if (plan is not null && tracker.ArrivalAirport is { } arr &&
            !arr.Ident.Equals(plan.DestinationIcao, StringComparison.OrdinalIgnoreCase))
            notes.Add(Lang.T($"Gelandet in {arr.DisplayName} statt in {plan.DestinationIcao}.", $"Landed at {arr.DisplayName} instead of {plan.DestinationIcao}."));
        if (booking is not null && tracker.ArrivalAirport is { } landed &&
            !landed.Ident.Equals(booking.Arrival, StringComparison.OrdinalIgnoreCase))
            notes.Add(Lang.T($"Buchung {booking.Ident} hatte Ziel {booking.Arrival}, gelandet in {landed.Ident}.", $"Booking {booking.Ident} was to {booking.Arrival}, landed at {landed.Ident}."));
        if (tracker.StartedAirborne) notes.Add(Lang.T("Aufzeichnung wurde im Flug gestartet – keine Startdaten.", "Recording started in flight – no takeoff data."));
        if (!tracker.IsFinished) notes.Add(Lang.T($"Flug nicht vollständig aufgezeichnet (letzte Phase: {tracker.Phase.ToDisplay()}).", $"Flight not completely recorded (last phase: {tracker.Phase.ToDisplay()})."));

        return new FlightReport
        {
            CreatedUtc = now.ToUniversalTime(),
            Complete = tracker.IsFinished,
            Flight = plan?.DisplayName ?? booking?.Ident,
            Booking = booking?.Ident,
            Origin = plan?.OriginIcao ?? booking?.Departure ?? tracker.DepartureAirport?.Ident,
            Destination = plan?.DestinationIcao ?? booking?.Arrival ?? tracker.ArrivalAirport?.Ident,
            OriginName = NameFor(plan?.OriginIcao ?? booking?.Departure ?? tracker.DepartureAirport?.Ident,
                                 tracker.DepartureAirport, plan?.OriginIcao, plan?.OriginName),
            DestinationName = NameFor(plan?.DestinationIcao ?? booking?.Arrival ?? tracker.ArrivalAirport?.Ident,
                                      tracker.ArrivalAirport, plan?.DestinationIcao, plan?.DestinationName),
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

    /// <summary>Name passend zum angezeigten Code – nie der Name eines anderen Platzes.</summary>
    private static string? NameFor(string? code, Airport? actual, string? planCode, string? planName)
    {
        if (code is null) return null;
        if (actual is not null && string.Equals(actual.Ident, code, StringComparison.OrdinalIgnoreCase)) return actual.Name;
        if (string.Equals(planCode, code, StringComparison.OrdinalIgnoreCase)) return planName;
        return null;
    }

    public static string RateLanding(double fpm) => fpm switch
    {
        >= -60 => Lang.T("sehr weich", "very soft"),
        >= -240 => Lang.T("gut", "good"),
        >= -400 => Lang.T("fest", "firm"),
        >= -600 => Lang.T("hart", "hard"),
        _ => Lang.T("sehr hart", "very hard"),
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
