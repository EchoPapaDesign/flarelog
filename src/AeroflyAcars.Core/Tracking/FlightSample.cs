namespace AeroflyAcars.Core.Tracking;

/// <summary>Ein aufbereiteter Messpunkt in Luftfahrt-Einheiten (ft, kt, fpm).</summary>
public sealed record FlightSample(
    DateTimeOffset Time,
    double Latitude,
    double Longitude,
    double AltitudeFt,
    double GroundSpeedKt,
    double TrackDeg,
    double HeadingDeg,
    double PitchDeg,
    double RollDeg,
    double VerticalSpeedFpm);

/// <summary>Ein Eintrag im Flight Log: ein Phasenwechsel oder ein Hinweis (z. B. Pause).</summary>
public sealed record FlightEvent(DateTimeOffset Time, FlightPhase Phase, string Text, bool PhaseChange = true);

public enum FlightPhase
{
    Preflight,
    TaxiOut,
    TakeoffRoll,
    Climb,
    Cruise,
    Descent,
    Approach,
    Landing,
    TaxiIn,
    Arrived,
}

public static class FlightPhaseExtensions
{
    public static string ToGerman(this FlightPhase phase) => phase switch
    {
        FlightPhase.Preflight => "Vorbereitung",
        FlightPhase.TaxiOut => "Rollen zum Start",
        FlightPhase.TakeoffRoll => "Startlauf",
        FlightPhase.Climb => "Steigflug",
        FlightPhase.Cruise => "Reiseflug",
        FlightPhase.Descent => "Sinkflug",
        FlightPhase.Approach => "Anflug",
        FlightPhase.Landing => "Landung",
        FlightPhase.TaxiIn => "Rollen zur Parkposition",
        FlightPhase.Arrived => "Angekommen",
        _ => phase.ToString(),
    };

    public static string ToEnglish(this FlightPhase phase) => phase switch
    {
        FlightPhase.Preflight => "Preflight",
        FlightPhase.TaxiOut => "Taxi out",
        FlightPhase.TakeoffRoll => "Takeoff roll",
        FlightPhase.Climb => "Climb",
        FlightPhase.Cruise => "Cruise",
        FlightPhase.Descent => "Descent",
        FlightPhase.Approach => "Approach",
        FlightPhase.Landing => "Landing",
        FlightPhase.TaxiIn => "Taxi to gate",
        FlightPhase.Arrived => "Arrived",
        _ => phase.ToString(),
    };

    /// <summary>Name der Phase in der eingestellten Sprache.</summary>
    public static string ToDisplay(this FlightPhase phase) => Lang.English ? phase.ToEnglish() : phase.ToGerman();

    public static bool IsAirborne(this FlightPhase phase) =>
        phase is FlightPhase.Climb or FlightPhase.Cruise or FlightPhase.Descent or FlightPhase.Approach;
}
