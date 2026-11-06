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
    private (DateTimeOffset Time, double Lat, double Lon, double RawAltFt)? _lastRaw;

    /// <summary>Ab dieser scheinbaren Vertikalgeschwindigkeit (fpm) kann ein Höhensprung ein verstellter Höhenmesser sein.</summary>
    public const double AltimeterJumpMinFpm = 3000;
    public const double AltimeterJumpMinFt = 30;
    /// <summary>Größere Sprünge sind Slew/Neupositionierung und werden nicht ausgeglichen.</summary>
    public const double AltimeterJumpMaxFt = 2000;

    /// <summary>
    /// Aerofly sendet die Höhe so, wie der Höhenmesser sie anzeigt: Wird am Boden oder im Flug am Höhenmesser
    /// gedreht (z. B. auf 0 am Abflugplatz), springt die gemeldete Höhe. Solche Sprünge werden herausgerechnet,
    /// damit Bodenhöhe, Abheben, Landerate und Positionen für die Airline stimmen. Summe der Korrektur in ft.
    /// </summary>
    public double AltimeterCorrectionFt { get; private set; }
    public int AltimeterChanges { get; private set; }

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

        double rawAltFt = gps.AltitudeMslMeters * Geo.FeetPerMeter;
        double gsKt = gps.GroundSpeedMps * Geo.KnotsPerMeterPerSecond;
        if (_lastRaw is { } lr && time > lr.Time && time - lr.Time <= TimeSpan.FromSeconds(3))
        {
            double sec = (time - lr.Time).TotalSeconds;
            double jump = rawAltFt - lr.RawAltFt;
            double impliedFpm = jump / sec * 60;
            double movedNm = Geo.DistanceNm(lr.Lat, lr.Lon, gps.Latitude, gps.Longitude);
            bool positionSteady = movedNm <= gsKt / 3600 * sec * 1.5 + 0.05;   // kein Slew/Versetzen
            // Steigen/Sinken steiler, als es die Fluglage zulässt: Bahnwinkel mehr als 25° über dem Pitch.
            double pathDeg = Math.Atan2(Math.Abs(impliedFpm), Math.Max(gsKt, 0.1) * 101.27) * 180 / Math.PI;
            double pitch = Math.Abs(_lastAttitude?.PitchDeg ?? 0);
            if (positionSteady && Math.Abs(jump) >= AltimeterJumpMinFt && Math.Abs(jump) <= AltimeterJumpMaxFt &&
                Math.Abs(impliedFpm) >= AltimeterJumpMinFpm && pathDeg > pitch + 25)
            {
                // Erwartete Höhenänderung aus der bisherigen Vertikalgeschwindigkeit bleibt erhalten.
                double correction = -(jump - _lastVerticalSpeed / 60 * sec);
                AltimeterCorrectionFt += correction;
                AltimeterChanges++;
            }
        }
        _lastRaw = (time, gps.Latitude, gps.Longitude, rawAltFt);
        double altFt = rawAltFt + AltimeterCorrectionFt;

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
            gsKt,
            Geo.NormalizeDegrees(gps.TrackDeg),
            Geo.NormalizeDegrees(heading),
            _lastAttitude?.PitchDeg ?? 0,
            _lastAttitude?.RollDeg ?? 0,
            _lastVerticalSpeed);
    }
}
