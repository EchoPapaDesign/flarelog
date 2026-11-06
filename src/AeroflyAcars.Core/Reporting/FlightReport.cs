using AeroflyAcars.Core.Tracking;

namespace AeroflyAcars.Core.Reporting;

public sealed record PhaseEntry(DateTimeOffset Time, FlightPhase Phase, string Text);

public sealed record TrackPoint(DateTimeOffset Time, double Latitude, double Longitude, double AltitudeFt, double GroundSpeedKt, double VerticalSpeedFpm);

/// <summary>Auswertung eines Fluges (Ist-Werte und, falls vorhanden, Soll-Werte aus SimBrief).</summary>
public sealed class FlightReport
{
    public string Tool { get; init; } = $"{AppInfo.Name} {AppInfo.Version}";
    public DateTimeOffset CreatedUtc { get; init; }
    public bool Complete { get; init; }

    public string? Flight { get; init; }
    public string? Booking { get; init; }
    public string? Origin { get; init; }
    public string? Destination { get; init; }
    public string? OriginName { get; init; }
    public string? DestinationName { get; init; }
    public int Pauses { get; init; }
    public TimeSpan? PausedTime { get; init; }
    public string? Aircraft { get; init; }
    public string? Route { get; init; }
    public string? Airac { get; init; }

    public DateTimeOffset? BlockOffUtc { get; init; }
    public DateTimeOffset? TakeoffUtc { get; init; }
    public DateTimeOffset? TouchdownUtc { get; init; }
    public DateTimeOffset? BlockOnUtc { get; init; }

    public TimeSpan? BlockTime { get; init; }
    public TimeSpan? PlannedBlockTime { get; init; }
    public TimeSpan? AirTime { get; init; }
    public TimeSpan? PlannedAirTime { get; init; }

    public double? DistanceFlownNm { get; init; }
    public double? PlannedDistanceNm { get; init; }
    public double? MaxAltitudeFt { get; init; }
    public double? PlannedCruiseAltitudeFt { get; init; }

    public double? LandingRateFpm { get; init; }
    public string? LandingRating { get; init; }
    public double? TouchdownGroundSpeedKt { get; init; }
    public double? TouchdownPitchDeg { get; init; }
    public double? TouchdownDistanceToDestinationNm { get; init; }
    public int GoArounds { get; init; }

    /// <summary>Bounces, Anflugstabilität, Rollout, Bahnen, Zeitraffer, Slew, Tageslicht, Rollzeiten.</summary>
    public FlightAnalysis? Analysis { get; init; }

    public List<PhaseEntry> Phases { get; init; } = new();
    public List<string> Notes { get; init; } = new();
    public List<TrackPoint> Track { get; init; } = new();
}
