namespace AeroflyAcars.Core.Sim;

/// <summary>Eine vom Simulator per UDP empfangene Nachricht.</summary>
public abstract record SimMessage(string Source);

/// <summary>
/// XGPS-Nachricht (ForeFlight-Format, ca. 1 Hz):
/// Längengrad, Breitengrad, Höhe MSL in Metern, Track (rechtweisend) in Grad, Groundspeed in m/s.
/// </summary>
public sealed record GpsMessage(
    string Source,
    double Longitude,
    double Latitude,
    double AltitudeMslMeters,
    double TrackDeg,
    double GroundSpeedMps) : SimMessage(Source);

/// <summary>
/// XATT-Nachricht (ForeFlight-Format): rechtweisender Steuerkurs, Pitch (positiv = Nase hoch),
/// Roll (positiv = rechts), jeweils in Grad.
/// </summary>
public sealed record AttitudeMessage(
    string Source,
    double HeadingDeg,
    double PitchDeg,
    double RollDeg) : SimMessage(Source);
