using AeroflyAcars.Core.Sim;

namespace AeroflyAcars.Core.Tracking;

/// <summary>
/// Baut aus XGPS + letzter XATT einen <see cref="FlightSample"/>.
/// Der UDP-Broadcast liefert keine Vertikalgeschwindigkeit, deshalb wird sie
/// aus der Höhenänderung über ein kurzes Zeitfenster berechnet.
/// </summary>
public sealed class SampleBuilder
{
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Aerofly sendet mehrmals pro Sekunde, oft mit doppelter Position. Ausgewertet wird höchstens
    /// ein Messpunkt pro Sekunde – darauf sind Vertikalgeschwindigkeit, Pausen- und Landeerkennung ausgelegt.
    /// </summary>
    public static readonly TimeSpan MinSampleInterval = TimeSpan.FromSeconds(0.9);

    private DateTimeOffset? _lastEmitted;

    private readonly TimeSpan _vsWindow;
    private readonly Queue<(DateTimeOffset Time, double AltitudeFt)> _history = new();
    private AttitudeMessage? _lastAttitude;
    private DateTimeOffset? _lastGpsTime;
    private double _lastVerticalSpeed;

    public SampleBuilder(TimeSpan? verticalSpeedWindow = null)
    {
        _vsWindow = verticalSpeedWindow ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Gibt bei GPS-Nachrichten einen neuen Messpunkt zurück, sonst null.</summary>
    public FlightSample? Add(SimMessage message, DateTimeOffset receivedAt)
    {
        switch (message)
        {
            case AttitudeMessage attitude:
                _lastAttitude = attitude;
                return null;
            case GpsMessage gps:
                return Build(gps, receivedAt);
            default:
                return null;
        }
    }

    private FlightSample? Build(GpsMessage gps, DateTimeOffset time)
    {
        if (_lastEmitted is { } emitted && time >= emitted && time - emitted < MinSampleInterval)
            return null;
        _lastEmitted = time;

        double altFt = gps.AltitudeMslMeters * Geo.FeetPerMeter;

        // Pause im Sim, Neustart oder Zeitsprung: Verlauf verwerfen.
        if (_lastGpsTime is { } last && (time - last > MaxGap || time < last))
        {
            _history.Clear();
            _lastVerticalSpeed = 0;
        }
        _lastGpsTime = time;

        _history.Enqueue((time, altFt));
        // Ältesten Eintrag behalten, der gerade noch mindestens ein Fenster alt ist.
        while (_history.Count > 2 && time - _history.ElementAt(1).Time >= _vsWindow)
            _history.Dequeue();

        var oldest = _history.Peek();
        double dt = (time - oldest.Time).TotalSeconds;
        if (dt >= 0.5)
            _lastVerticalSpeed = (altFt - oldest.AltitudeFt) / dt * 60.0;

        double heading = _lastAttitude?.HeadingDeg ?? gps.TrackDeg;
        return new FlightSample(
            time,
            gps.Latitude,
            gps.Longitude,
            altFt,
            gps.GroundSpeedMps * Geo.KnotsPerMeterPerSecond,
            Geo.NormalizeDegrees(gps.TrackDeg),
            Geo.NormalizeDegrees(heading),
            _lastAttitude?.PitchDeg ?? 0,
            _lastAttitude?.RollDeg ?? 0,
            _lastVerticalSpeed);
    }
}
