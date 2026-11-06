using System.Globalization;
using AeroflyAcars.Core.Airports;
using AeroflyAcars.Core.SimBrief;

namespace AeroflyAcars.Core.Tracking;

/// <summary>
/// Erkennt die Flugphasen aus den Messpunkten und zeichnet Block-, Start- und Landezeiten auf.
///
/// Einschränkung des UDP-Broadcasts: Es gibt keine Höhe über Grund, kein On-Ground-Signal,
/// kein Fahrwerk und keinen Treibstoff. Deshalb arbeitet die Erkennung mit Heuristiken:
/// - Bodenhöhe = Höhe beim Rollen am Abflugort; am Ziel die Elevation aus SimBrief, --dest-elev
///   oder des nächstgelegenen Flugplatzes (OurAirports)
/// - Aufsetzen = Sinken endet, die Position läuft weiter, die Höhe bleibt stehen,
///   danach wird das Flugzeug deutlich langsamer
/// - Pause = Position und Höhe bleiben exakt gleich, obwohl Groundspeed gemeldet wird
///   (Aerofly sendet in der Pause die letzten Werte weiter)
/// Für Hubschrauber (Schwebeflug!) ist diese Logik noch nicht geeignet.
/// </summary>
public sealed class FlightTracker
{
    public const double TaxiStartKt = 3;
    public const double TakeoffRollKt = 40;
    public const double RejectTakeoffKt = 30;
    public const double AirborneStartKt = 80;
    public const double LiftoffAboveGroundFt = 15;
    /// <summary>Im Startlauf: bis so hoch über der Bahn (ft) gilt ein Messpunkt sicher als am Boden.</summary>
    public const double RollOnGroundFt = 3;
    public const double ClimbVsFpm = 300;
    public const double DescentVsFpm = -300;
    public const double ApproachHeightFt = 3000;
    /// <summary>Als Durchstarten zählt erneutes Steigen nur, wenn der Anflug tiefer als das (ft über Platz) war.
    /// Höher ist es Manövrieren, etwa in der Platzrunde oder beim Warten.</summary>
    public const double GoAroundMaxHeightFt = 1000;
    public const int PhaseConfirmSamples = 5;
    public const double TouchdownMaxAltChangeFtPerSec = 1.0;
    public const double TouchdownMaxSampleGapSec = 3;
    public const double TouchdownToleranceFt = 30;
    public const double TouchdownMaxAboveFieldFt = 300;
    public const double TouchdownConfirmKt = 50;
    public const double TouchdownConfirmDecelKt = 8;
    public const double TaxiInKt = 30;
    public const double StoppedKt = 1;
    public static readonly TimeSpan PauseMinTime = TimeSpan.FromSeconds(3);
    public const double PauseMinExpectedMoveM = 50;
    public const double DepartureAirportRadiusNm = 3;
    public const double ArrivalAirportRadiusNm = 5;
    public static readonly TimeSpan ParkingTime = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan TouchdownAnnounceDelay = TimeSpan.FromSeconds(2.5);
    public const double TouchdownAnnounceFlatFt = 3;
    /// <summary>Sinkt die Höhe nach dem Kandidaten um mehr als das, war es nur das Abfangen (Flare/Ausschweben).</summary>
    public const double TouchdownSettleFt = 2;
    /// <summary>Wie <see cref="TouchdownSettleFt"/>, wenn die Bahnneigung bekannt ist und herausgerechnet wird.</summary>
    public const double TouchdownSettleOnRunwayFt = 1;
    /// <summary>Schnelle Ansage nur so nah an der Bahnoberfläche (ft) – mit bzw. ohne Abgleich am Abflugort.</summary>
    public const double AnnounceNearRunwayFt = 25;
    public const double AnnounceNearRunwayUncalibratedFt = 35;
    /// <summary>Ohne Bahndaten: so nah an der Platzhöhe (ft). Die Platzhöhe kann deutlich von der Bahn abweichen.</summary>
    public const double AnnounceNearFieldFt = 75;
    /// <summary>Ohne Bahndaten zusätzlich nötig: so viel langsamer (kt) seit dem Aufsetzen.</summary>
    public const double AnnounceMinDecelKt = 3;
    /// <summary>Über einer bekannten Bahn: höher als das ist kein Aufsetzen, sondern ein Abfangen in der Luft.</summary>
    public const double CandidateMaxAboveRunwayFt = 60;
    /// <summary>So nah an der späteren Rollhöhe gilt ein Messpunkt als Bodenkontakt (erster Kontakt vor Bounces).</summary>
    public const double FirstContactFt = 1.5;
    public static readonly TimeSpan FirstContactLookBack = TimeSpan.FromSeconds(30);
    /// <summary>
    /// So langsam fliegt kein Flächenflugzeug: Ist das Flugzeug „in der Luft“ (laut Phase) und am Platz so langsam,
    /// ist es gelandet, ohne dass das Aufsetzen erkannt wurde – typisch für stark geneigte Bahnen wie Courchevel,
    /// wo die Höhe beim Ausrollen bergauf weiter steigt.
    /// </summary>
    public const double SlowLandingKt = 20;
    /// <summary>Größte Bahnneigung, die beim Ausrollen angenommen wird (Courchevel: ca. 19 %).</summary>
    public const double MaxRunwaySlope = 0.25;
    /// <summary>So nah (ft) an der verlängerten Rollstrecke gilt ein Messpunkt als am Boden.</summary>
    public const double RollLineToleranceFt = 4;
    /// <summary>Nach dem Ausrollen wieder so schnell: neuer Start (Stop-and-go, Platzrunden).</summary>
    public const double RestartTakeoffKt = 40;

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private readonly List<FlightSample> _samples = new();
    private readonly List<FlightEvent> _events = new();
    private readonly IAirportLookup? _airports;
    private readonly IRunwayLookup? _runways;
    private bool _settling;
    private bool _announcedWhileSettling;
    /// <summary>Gemessene Höhe am Boden minus Bahnhöhe laut Datenbank, bestimmt am Startlauf.</summary>
    private double? _groundOffsetFt;

    private DateTimeOffset? _lastOnGround;
    private FlightPhase? _pendingPhase;
    private int _pendingCount;
    private DateTimeOffset _pendingSince;
    private TouchdownCandidate? _candidate;
    private double _approachMinAltFt = double.MaxValue;
    /// <summary>Startlauf: Strecke (ft) und Höhe der Messpunkte am Boden – für geneigte Bahnen.</summary>
    private readonly List<(double D, double Alt)> _roll = new();
    private double _rollDistFt;
    private DateTimeOffset? _stoppedSince;
    private int _frozenCount;
    private DateTimeOffset _frozenSince;

    private sealed class TouchdownCandidate(FlightSample sample, double landingRateFpm, bool fieldKnown, Airport? airport)
    {
        public FlightSample Sample { get; } = sample;
        public double LandingRateFpm { get; } = Math.Min(landingRateFpm, 0);
        public bool FieldElevationKnown { get; } = fieldKnown;
        public Airport? Airport { get; } = airport;
        public bool Announced { get; set; }
    }

    /// <param name="plan">SimBrief-Plan (optional).</param>
    /// <param name="destinationElevationFt">Zielelevation, falls ohne SimBrief bekannt; sonst aus dem Plan.</param>
    /// <param name="airports">Flughafendatenbank für Abflug-/Zielplatz und Elevation (optional).</param>
    /// <param name="runways">Bahndaten für die Bodenhöhe beim Aufsetzen (optional).</param>
    public FlightTracker(FlightPlan? plan = null, double? destinationElevationFt = null, IAirportLookup? airports = null,
        IRunwayLookup? runways = null)
    {
        _runways = runways;
        Plan = plan;
        DestinationElevationFt = destinationElevationFt ?? plan?.DestinationElevationFt;
        _airports = airports;
    }

    /// <summary>Elevation des Zielflughafens aus SimBrief oder --dest-elev.</summary>
    public double? DestinationElevationFt { get; }

    public FlightPlan? Plan { get; }
    public FlightPhase Phase { get; private set; } = FlightPhase.Preflight;
    public IReadOnlyList<FlightSample> Samples => _samples;
    public IReadOnlyList<FlightEvent> Events => _events;
    public FlightSample? LastSample => _samples.Count > 0 ? _samples[^1] : null;

    public DateTimeOffset? BlockOff { get; private set; }
    public DateTimeOffset? Takeoff { get; private set; }
    public DateTimeOffset? Touchdown { get; private set; }
    public DateTimeOffset? BlockOn { get; private set; }

    public Airport? DepartureAirport { get; private set; }
    public Airport? ArrivalAirport { get; private set; }

    public double? DepartureElevationFt { get; private set; }
    public double MaxAltitudeFt { get; private set; } = double.MinValue;
    public double? LandingRateFpm { get; private set; }
    public FlightSample? TouchdownSample { get; private set; }
    public int GoArounds { get; private set; }
    public bool StartedAirborne { get; private set; }
    public double? LiftoffSpeedKt { get; private set; }

    public bool IsPaused { get; private set; }
    public int PauseCount { get; private set; }
    public TimeSpan PausedTime { get; private set; }

    public bool IsFinished => Phase == FlightPhase.Arrived;

    /// <summary>Wird bei jedem Eintrag ins Flight Log ausgelöst (im Thread des Aufrufers von <see cref="Add"/>).</summary>
    public event Action<FlightEvent>? EventRaised;

    /// <summary>
    /// Wird möglichst früh nach dem Aufsetzen ausgelöst (Sample, Landerate in fpm):
    /// mit bekannter Platz- bzw. Bahnhöhe ca. 2,5 s nach dem Aufsetzen, sonst bei der Bestätigung beim Abbremsen.
    /// Bei einem Touch-and-go kann es pro Flug mehrfach kommen.
    /// </summary>
    public event Action<FlightSample, double>? TouchdownDetected;

    public void Add(FlightSample s)
    {
        if (IsFinished) return;

        var prev = LastSample;

        // Gleiche Position trotz gemeldeter Geschwindigkeit: enthält keine neue Information und wird nicht
        // ausgewertet. Als Pause gilt es erst, wenn die Position mindestens 3 s steht, obwohl das Flugzeug
        // sich laut Groundspeed deutlich (> 50 m) hätte bewegen müssen – langsames Rollen löst das nicht aus.
        if (prev is not null && s.GroundSpeedKt > StoppedKt && SamePosition(prev, s))
        {
            if (_frozenCount++ == 0) _frozenSince = prev.Time;
            var frozenFor = s.Time - _frozenSince;
            double expectedMoveM = s.GroundSpeedKt * 0.514444 * frozenFor.TotalSeconds;
            if (!IsPaused && frozenFor >= PauseMinTime && expectedMoveM > PauseMinExpectedMoveM)
            {
                IsPaused = true;
                PauseCount++;
                Log(_frozenSince, "Simulator pausiert");
            }
            return;
        }
        if (IsPaused)
        {
            IsPaused = false;
            PausedTime += s.Time - _frozenSince;
            Log(s.Time, "Simulator fortgesetzt");
        }
        _frozenCount = 0;

        _samples.Add(s);
        if (s.AltitudeFt > MaxAltitudeFt) MaxAltitudeFt = s.AltitudeFt;

        switch (Phase)
        {
            case FlightPhase.Preflight:
                if (_samples.Count == 1 && s.GroundSpeedKt > AirborneStartKt)
                {
                    StartedAirborne = true;
                    SetPhase(FlightPhase.Cruise, s.Time, "Aufzeichnung im Flug begonnen – Start wird nicht erfasst");
                    break;
                }
                if (s.GroundSpeedKt < TakeoffRollKt) DepartureElevationFt = s.AltitudeFt;
                if (s.GroundSpeedKt > TaxiStartKt)
                {
                    BlockOff = s.Time;
                    DepartureAirport = _airports?.Nearest(s.Latitude, s.Longitude, DepartureAirportRadiusNm);
                    SetPhase(FlightPhase.TaxiOut, s.Time, "Block off" + At(DepartureAirport) + " – Rollen zum Start");
                }
                break;

            case FlightPhase.TaxiOut:
                if (s.GroundSpeedKt < TakeoffRollKt)
                {
                    DepartureElevationFt = s.AltitudeFt;
                    _lastOnGround = s.Time;
                }
                else
                {
                    DepartureAirport ??= _airports?.Nearest(s.Latitude, s.Longitude, DepartureAirportRadiusNm);
                    CalibrateGround(s);
                    _roll.Clear(); _rollDistFt = 0;
                    SetPhase(FlightPhase.TakeoffRoll, s.Time, $"Startlauf auf Kurs {s.HeadingDeg:000}°");
                }
                break;

            case FlightPhase.TakeoffRoll:
                DepartureElevationFt ??= s.AltitudeFt;
                if (LiftedOff(s, prev))
                {
                    var liftoff = _lastOnGround ?? s.Time;
                    Takeoff ??= liftoff;               // bei Stop-and-go zählt der erste Start
                    LiftoffSpeedKt ??= s.GroundSpeedKt;
                    SetPhase(FlightPhase.Climb, liftoff,
                        string.Create(De, $"Abheben mit {s.GroundSpeedKt:F0} kt, Pitch {s.PitchDeg:F1}°, Querlage {Math.Abs(s.RollDeg):F1}°"));
                }
                else
                {
                    if (s.GroundSpeedKt < RejectTakeoffKt)
                        SetPhase(FlightPhase.TaxiOut, s.Time, "Startabbruch – wieder Rollen");
                }
                break;

            case FlightPhase.Climb:
            case FlightPhase.Cruise:
            case FlightPhase.Descent:
            case FlightPhase.Approach:
                if (!DetectTouchdown(s, prev) && !DetectSlowLanding(s))
                    UpdateAirbornePhase(s);
                break;

            case FlightPhase.Landing:
                if (s.GroundSpeedKt >= RestartTakeoffKt && s.GroundSpeedKt > (prev?.GroundSpeedKt ?? 0)) { Restart(s); break; }
                if (s.GroundSpeedKt < TaxiInKt)
                    SetPhase(FlightPhase.TaxiIn, s.Time, "Rollen zur Parkposition");
                break;

            case FlightPhase.TaxiIn:
                if (s.GroundSpeedKt >= RestartTakeoffKt) { Restart(s); break; }
                if (s.GroundSpeedKt < StoppedKt)
                {
                    _stoppedSince ??= s.Time;
                    if (s.Time - _stoppedSince.Value >= ParkingTime)
                    {
                        BlockOn = _stoppedSince;
                        SetPhase(FlightPhase.Arrived, _stoppedSince.Value, "Block on" + At(ArrivalAirport) + " – angekommen");
                    }
                }
                else
                {
                    _stoppedSince = null;
                }
                break;
        }
    }

    private void UpdateAirbornePhase(FlightSample s)
    {
        double groundRef = DestinationElevationFt ?? DepartureElevationFt ?? 0;
        double departureRef = DepartureElevationFt ?? groundRef;
        if (Phase == FlightPhase.Approach) _approachMinAltFt = Math.Min(_approachMinAltFt, s.AltitudeFt);

        FlightPhase target;
        if (s.VerticalSpeedFpm > ClimbVsFpm)
        {
            target = FlightPhase.Climb;
        }
        else if (s.VerticalSpeedFpm < DescentVsFpm)
        {
            target = s.AltitudeFt - groundRef < ApproachHeightFt ? FlightPhase.Approach : FlightPhase.Descent;
        }
        else
        {
            bool atCruiseLevel = Plan?.CruiseAltitudeFt is { } planned
                ? s.AltitudeFt >= planned - 1000
                : s.AltitudeFt >= MaxAltitudeFt - 500 && s.AltitudeFt - departureRef > 2000;
            target = atCruiseLevel && Phase != FlightPhase.Approach ? FlightPhase.Cruise : Phase;
        }

        // Im Anflug bleibt man im Anflug, außer es wird wieder gestiegen (Durchstarten).
        if (Phase == FlightPhase.Approach && target == FlightPhase.Descent)
            target = FlightPhase.Approach;

        if (target == Phase)
        {
            _pendingPhase = null;
            return;
        }

        if (_pendingPhase != target)
        {
            _pendingPhase = target;
            _pendingCount = 0;
            _pendingSince = s.Time;
        }

        if (++_pendingCount < PhaseConfirmSamples) return;

        string text;
        if (Phase == FlightPhase.Approach && target == FlightPhase.Climb && _approachMinAltFt - groundRef < GoAroundMaxHeightFt)
        {
            GoArounds++;
            text = "Durchstarten";
        }
        else if (target == FlightPhase.Cruise)
        {
            text = "Reiseflug in " + FormatAltitude(s.AltitudeFt);
        }
        else
        {
            text = target.ToGerman();
        }

        _pendingPhase = null;
        if (target == FlightPhase.Approach) _approachMinAltFt = s.AltitudeFt;
        SetPhase(target, _pendingSince, text);
    }

    /// <summary>Abgleich der Aerofly-Höhe mit der Bahnhöhe aus der Datenbank, solange das Flugzeug am Boden steht.</summary>
    private void CalibrateGround(FlightSample s)
    {
        if (DepartureAirport is null || DepartureElevationFt is null) return;
        var rwy = _runways?.GroundElevationAt(DepartureAirport, s.Latitude, s.Longitude);
        if (rwy is null) return;
        double offset = DepartureElevationFt.Value - rwy.Value;
        _groundOffsetFt = Math.Abs(offset) <= 100 ? offset : null;   // sonst passt die Datenbank nicht
    }

    /// <summary>
    /// Wie weit das Flugzeug über dem Boden ist, soweit bekannt: über einer Bahn genau (Bahnhöhe + Abgleich),
    /// sonst grob über der Platzhöhe. Dazu die Grenze, bis zu der eine sofortige Landeansage erlaubt ist.
    /// </summary>
    private (double? Height, double AnnounceLimit, bool OverRunway) HeightAboveGround(FlightSample s, Airport? airport)
    {
        if (airport is not null && _runways?.GroundElevationAt(airport, s.Latitude, s.Longitude) is { } rwy)
        {
            return _groundOffsetFt is { } off
                ? (s.AltitudeFt - (rwy + off), AnnounceNearRunwayFt, true)
                : (s.AltitudeFt - rwy, AnnounceNearRunwayUncalibratedFt, true);
        }
        double? field = DestinationElevationFt ?? airport?.ElevationFt;
        // Sind die Bahnen des Platzes bekannt, gibt es neben der Bahn keine schnelle Ansage: Die Platzhöhe liegt
        // oft weit über der Schwelle (KAFW: 722 ft gegenüber 664 ft an der 34R), ein Abfangen im Endanflug sähe
        // sonst wie ein Aufsetzen aus. Gemeldet wird dann erst beim Abbremsen.
        double limit = airport is not null && _runways?.HasRunwayElevations(airport) == true ? -1 : AnnounceNearFieldFt;
        return (s.AltitudeFt - field, limit, false);
    }

    /// <summary>
    /// Höhenunterschied a − b. Liegen beide Punkte auf einer bekannten Bahn, wird deren Neigung herausgerechnet.
    /// </summary>
    private double LevelDiff(FlightSample a, FlightSample b, Airport? airport) => LevelDiff(a, b, airport, out _);

    private double LevelDiff(FlightSample a, FlightSample b, Airport? airport, out bool runwayRelative)
    {
        runwayRelative = false;
        if (airport is not null && _runways is not null &&
            _runways.GroundElevationAt(airport, a.Latitude, a.Longitude) is { } ga &&
            _runways.GroundElevationAt(airport, b.Latitude, b.Longitude) is { } gb)
        {
            runwayRelative = true;
            return (a.AltitudeFt - ga) - (b.AltitudeFt - gb);
        }
        return a.AltitudeFt - b.AltitudeFt;
    }

    /// <summary>Gibt true zurück, wenn in diesem Schritt eine Landung bestätigt wurde.</summary>
    private bool DetectTouchdown(FlightSample s, FlightSample? prev)
    {
        if (prev is null) return false;
        double dt = (s.Time - prev.Time).TotalSeconds;
        if (dt <= 0) return false;

        var cand = _candidate;
        if (cand is not null)
        {
            double diff = LevelDiff(s, cand.Sample, cand.Airport, out bool onRunway);
            if (Math.Abs(diff) > TouchdownToleranceFt)
            {
                // Nur ein Abfangen in der Luft (Level-off, Durchstarten, Touch-and-go).
                _candidate = null;
                _settling = false;
                _announcedWhileSettling = false;
                return false;
            }
            // Über einer bekannten Bahn ist die Neigung herausgerechnet: Schon 1 ft weiteres Sinken heißt Ausschweben.
            // Ohne Bahndaten ist gleichmäßiges leichtes Sinken (< 1 ft/s) eher eine abschüssige Bahn.
            double sinceCand = (s.Time - cand.Sample.Time).TotalSeconds;
            bool settled = onRunway
                ? diff < -TouchdownSettleOnRunwayFt
                : diff < -TouchdownSettleFt && sinceCand > 0 && diff / sinceCand < -TouchdownMaxAltChangeFtPerSec;
            if (settled)
            {
                // Weiter gesunken: Das war das Abfangen bzw. Ausschweben, aufgesetzt wird erst noch.
                // (Ein Abfangen hoch im Endanflug ist dagegen einfach erledigt.)
                _candidate = null;
                _settling = HeightAboveGround(cand.Sample, cand.Airport).Height is not { } ch || ch <= TouchdownToleranceFt;
                _announcedWhileSettling |= cand.Announced;
            }
        }

        if (_candidate is null)
        {
            if (Phase is not (FlightPhase.Approach or FlightPhase.Descent)) { _settling = false; return false; }
            // Nach einer Pause oder Datenlücke ist die Höhenänderung pro Sekunde nicht aussagekräftig.
            if (dt > TouchdownMaxSampleGapSec) return false;

            // Platzhöhe: SimBrief/--dest-elev, sonst der nächste Flugplatz aus der Datenbank.
            var airport = _airports?.Nearest(s.Latitude, s.Longitude, ArrivalAirportRadiusNm);

            double altRate = LevelDiff(s, prev, airport) / dt;
            // Flach oder leicht steigend (Bahnneigung ohne Bahndaten). Nach dem Ausschweben zählt jedes Sinken.
            bool flat = altRate >= -TouchdownMaxAltChangeFtPerSec && altRate <= 2 * TouchdownMaxAltChangeFtPerSec;
            bool sinkingEnded = flat && (_settling || prev.VerticalSpeedFpm < -60);
            if (!sinkingEnded) return false;
            var (height, _, overRunway) = HeightAboveGround(s, airport);
            if (height is { } h && h > (overRunway ? CandidateMaxAboveRunwayFt : TouchdownMaxAboveFieldFt)) return false;

            // Landerate gegenüber der Bahn: Auf einer ansteigenden Bahn kommt der Boden dem Flugzeug entgegen.
            double rate = prev.VerticalSpeedFpm;
            if (airport is not null && _runways?.GroundElevationAt(airport, s.Latitude, s.Longitude) is { } gNow &&
                _runways!.GroundElevationAt(airport, prev.Latitude, prev.Longitude) is { } gPrev)
                rate -= (gNow - gPrev) / dt * 60;

            _candidate = new TouchdownCandidate(s, rate, height is not null, airport)
            {
                Announced = _announcedWhileSettling,
            };
            _settling = false;
            _announcedWhileSettling = false;
            return false;
        }

        cand = _candidate;

        // Bestätigung: deutlich langsamer geworden (auch für langsame Flugzeuge wie eine Cub).
        bool confirmed = s.GroundSpeedKt < TouchdownConfirmKt
                         && s.GroundSpeedKt <= cand.Sample.GroundSpeedKt - TouchdownConfirmDecelKt;
        if (!confirmed)
        {
            // Schnelle Meldung: seit 2,5 s flach und nah genug an der Bahn bzw. Platzhöhe.
            if (!cand.Announced && cand.FieldElevationKnown &&
                s.Time - cand.Sample.Time >= TouchdownAnnounceDelay &&
                Math.Abs(LevelDiff(s, cand.Sample, cand.Airport)) <= TouchdownAnnounceFlatFt &&
                HeightAboveGround(cand.Sample, cand.Airport) is { Height: { } h } g && Math.Abs(h) <= g.AnnounceLimit &&
                // Nur über die Platzhöhe (keine Bahndaten) ist das ungenau – Courchevel: Platz 6.583 ft, Schwelle rund
                // 300 ft tiefer, ein Abfangen im Anflug sähe wie ein Aufsetzen aus. Dann muss auch gebremst werden.
                (g.OverRunway || cand.Sample.GroundSpeedKt - s.GroundSpeedKt >= AnnounceMinDecelKt))
            {
                cand.Announced = true;
                var first = FirstContact(cand);
                TouchdownDetected?.Invoke(first.Sample, first.LandingRateFpm);
            }
            return false;
        }

        _candidate = null;
        // Nach Bounces liegt der Kandidat erst beim endgültigen Ausrollen – gezählt wird der erste Bodenkontakt.
        cand = FirstContact(cand);
        if (!cand.Announced) TouchdownDetected?.Invoke(cand.Sample, cand.LandingRateFpm);
        Touchdown = cand.Sample.Time;
        TouchdownSample = cand.Sample;
        LandingRateFpm = cand.LandingRateFpm;
        ArrivalAirport = cand.Airport ?? _airports?.Nearest(s.Latitude, s.Longitude, ArrivalAirportRadiusNm);
        SetPhase(FlightPhase.Landing, cand.Sample.Time,
            $"Aufsetzen mit {cand.LandingRateFpm:F0} fpm bei {cand.Sample.GroundSpeedKt:F0} kt" + At(ArrivalAirport));
        return true;
    }

    /// <summary>
    /// Sucht vor dem Kandidaten (Flugzeug rollt auf der Bahn) den ersten Bodenkontakt. Bei einer harten Landung
    /// springt das Flugzeug wieder hoch; jedes erneute Sinken gilt dann als Ausschweben, und der Kandidat landet
    /// erst beim letzten, sanften Kontakt. Die Höhe beim Ausrollen ist die echte Bodenhöhe im Simulator – der
    /// erste Messpunkt, der ihr auf 1,5 ft nahekommt, ist das eigentliche Aufsetzen.
    /// </summary>
    private TouchdownCandidate FirstContact(TouchdownCandidate cand)
    {
        int ci = _samples.LastIndexOf(cand.Sample);
        if (ci <= 0) return cand;

        double? gCand = cand.Airport is null ? null : _runways?.GroundElevationAt(cand.Airport, cand.Sample.Latitude, cand.Sample.Longitude);
        double Ground(FlightSample x) =>
            (cand.Airport is null ? null : _runways?.GroundElevationAt(cand.Airport, x.Latitude, x.Longitude)) ?? gCand ?? 0;
        double H(int i) => _samples[i].AltitudeFt - Ground(_samples[i]) - (cand.Sample.AltitudeFt - Ground(cand.Sample));

        // Rückwärts nur der zusammenhängende tiefe Teil direkt vor dem Kandidaten.
        int start = ci;
        for (int i = ci - 1; i >= 0; i--)
        {
            if (cand.Sample.Time - _samples[i].Time > FirstContactLookBack || H(i) > TouchdownToleranceFt) break;
            start = i;
        }

        for (int i = start; i < ci; i++)
        {
            double h = H(i);
            // Nur echte Bodennähe zählt. Ein Wiederansteigen im Flare (Ballooning) ein paar Fuß über der Bahn ist
            // kein Kontakt und darf nicht als Bounce gewertet werden.
            if (h > FirstContactFt) continue;
            if (i == 0) return cand;

            var x = _samples[i];
            var p = _samples[i - 1];
            double dt = (x.Time - p.Time).TotalSeconds;
            // Sinkrate gegenüber der Bahn direkt vor dem Kontakt (berechnete VS, Bahnneigung herausgerechnet).
            double slope = dt > 0 ? (Ground(x) - Ground(p)) / dt * 60 : 0;
            double rate = Math.Min(p.VerticalSpeedFpm, x.VerticalSpeedFpm) - slope;
            if (dt > 0) rate = Math.Min(rate, (H(i) - H(i - 1)) / dt * 60);
            return new TouchdownCandidate(x, Math.Min(rate, cand.LandingRateFpm), cand.FieldElevationKnown, cand.Airport)
            {
                Announced = cand.Announced,
            };
        }
        return cand;
    }

    /// <summary>
    /// Abgehoben, sobald die Höhe 15 ft über dem Boden liegt. Der Boden ist die Startbahn, wie sie sich im Startlauf
    /// zeigt: Höhe über Strecke als Gerade aus den letzten Messpunkten. Auf einer abschüssigen Bahn (Courchevel:
    /// 19 % bergab) sinkt die Höhe beim Startlauf – gegenüber der Höhe am Startpunkt hebt man dort nie „ab“.
    /// </summary>
    private bool LiftedOff(FlightSample s, FlightSample? prev)
    {
        if (prev is not null) _rollDistFt += Geo.DistanceNm(prev.Latitude, prev.Longitude, s.Latitude, s.Longitude) * 6076.12;
        double d = _rollDistFt;
        double ground = DepartureElevationFt ?? s.AltitudeFt;
        var pts = _roll.Count > 8 ? _roll.GetRange(_roll.Count - 8, 8) : _roll;
        if (pts.Count >= 3 && pts[^1].D - pts[0].D >= 100)
        {
            double mx = pts.Average(p => p.D), my = pts.Average(p => p.Alt);
            double sxx = pts.Sum(p => (p.D - mx) * (p.D - mx)), sxy = pts.Sum(p => (p.D - mx) * (p.Alt - my));
            double slope = Math.Clamp(sxy / sxx, -MaxRunwaySlope, MaxRunwaySlope);
            ground = my + slope * (d - mx);
        }
        double above = s.AltitudeFt - ground;
        if (above > LiftoffAboveGroundFt) return true;
        // Nur eindeutig am Boden liegende Punkte bilden die Bahn – sonst wanderte die Gerade beim flachen Steigen mit.
        if (above <= RollOnGroundFt)
        {
            _roll.Add((d, s.AltitudeFt));
            _lastOnGround = s.Time;
        }
        return false;
    }

    /// <summary>Nach einer Landung wieder beschleunigt: neuer Startlauf (Stop-and-go, Platzrunde).</summary>
    private void Restart(FlightSample s)
    {
        _stoppedSince = null;
        _candidate = null;
        _settling = false;
        DepartureElevationFt = s.AltitudeFt;
        _lastOnGround = s.Time;
        _roll.Clear(); _rollDistFt = 0;
        SetPhase(FlightPhase.TakeoffRoll, s.Time, $"Erneuter Startlauf auf Kurs {s.HeadingDeg:000}°");
    }

    /// <summary>
    /// Rückfall, wenn das Aufsetzen nicht erkannt wurde: Das Flugzeug ist „in der Luft“, aber am Platz langsamer als
    /// <see cref="SlowLandingKt"/>. Die Rollstrecke wird als Gerade (Höhe über Strecke) rückwärts verlängert – das
    /// gibt die Bahnneigung auch ohne Bahndaten. Aufgesetzt wurde am ersten Punkt, der auf dieser Geraden liegt.
    /// </summary>
    private bool DetectSlowLanding(FlightSample s)
    {
        if (s.GroundSpeedKt >= SlowLandingKt || _samples.Count < 3) return false;
        var airport = _airports?.Nearest(s.Latitude, s.Longitude, ArrivalAirportRadiusNm);
        if (_airports is not null && airport is null) return false;   // fernab jedes Platzes: eher starker Gegenwind

        // Strecke rückwärts vom aktuellen Punkt (ft).
        int end = _samples.Count - 1;
        var dist = new double[_samples.Count];
        int first = end;
        for (int i = end - 1; i >= 0; i--)
        {
            if (s.Time - _samples[i].Time > TimeSpan.FromSeconds(120)) break;
            dist[i] = dist[i + 1] + Geo.DistanceNm(_samples[i].Latitude, _samples[i].Longitude,
                _samples[i + 1].Latitude, _samples[i + 1].Longitude) * 6076.12;
            first = i;
        }

        // Bahnneigung aus den ersten Sekunden am Boden: Beim Ausrollen wird deutlich gebremst (bergauf auch durch die
        // Schwerkraft), in der Luft kaum. Der Beginn dieses Abbremsens liegt nah am Aufsetzen, die Punkte direkt danach
        // liegen noch auf der Bahn – weiter hinten kann ein flaches Vorfeld kommen (Courchevel oben).
        int last = end;
        while (last > first && _samples[last].GroundSpeedKt < 8) last--;
        // Das Ausrollen beginnt mit deutlichem Abbremsen (Bremsen, bergauf auch die Schwerkraft): Gesucht wird – vom
        // Ende her – der zusammenhängende Abschnitt, in dem die Geschwindigkeit innerhalb von 6 s um mindestens 12 kt
        // fällt, und darin die stärkste Verzögerung. Danach ist das Flugzeug sicher am Boden und noch auf der Bahn;
        // später kann es mit Leistung bergauf rollen oder auf ein flaches Vorfeld kommen.
        double Decel(int i)
        {
            double min = _samples[i].GroundSpeedKt;
            for (int j = i + 1; j <= last && _samples[j].Time - _samples[i].Time <= TimeSpan.FromSeconds(6); j++)
                min = Math.Min(min, _samples[j].GroundSpeedKt);
            return _samples[i].GroundSpeedKt - min;
        }
        int peak = -1;
        double peakDecel = 0;
        bool inRegion = false;
        for (int i = last - 1; i >= first && s.Time - _samples[i].Time <= TimeSpan.FromSeconds(90); i--)
        {
            double d = Decel(i);
            if (d >= 12)
            {
                inRegion = true;
                double dt1 = (_samples[i + 1].Time - _samples[i].Time).TotalSeconds;
                double step = dt1 > 0 ? (_samples[i].GroundSpeedKt - _samples[i + 1].GroundSpeedKt) / dt1 : 0;
                if (step >= peakDecel) { peakDecel = step; peak = i + 1; }
            }
            else if (inRegion) break;
        }

        var fit = new List<(double D, double Alt)>();
        int anchor = last;
        if (peak >= 0)
        {
            for (int i = peak; i <= last && _samples[i].Time - _samples[peak].Time <= TimeSpan.FromSeconds(5); i++)
                fit.Add((dist[i], _samples[i].AltitudeFt));
            anchor = peak;
        }
        else
        {
            for (int i = last; i >= first && _samples[i].GroundSpeedKt is >= 8 and < 40; i--) fit.Add((dist[i], _samples[i].AltitudeFt));
        }
        double slope = 0, at0;
        if (fit.Count >= 2 && Math.Abs(fit[^1].D - fit[0].D) >= 100)
        {
            double mx = fit.Average(f => f.D), my = fit.Average(f => f.Alt);
            double sxx = fit.Sum(f => (f.D - mx) * (f.D - mx)), sxy = fit.Sum(f => (f.D - mx) * (f.Alt - my));
            slope = Math.Clamp(sxy / sxx, -MaxRunwaySlope, MaxRunwaySlope);
            at0 = my - slope * mx;
        }
        else
        {
            at0 = _samples[anchor].AltitudeFt - slope * dist[anchor];
        }
        double Off(int i) => _samples[i].AltitudeFt - (at0 + slope * dist[i]);

        // Vom Ausrollen rückwärts, solange die Punkte auf der Geraden liegen.
        int td = anchor;
        for (int i = anchor - 1; i >= first; i--)
        {
            if (Math.Abs(Off(i)) > RollLineToleranceFt + Math.Abs(slope) * 30) break;   // etwas Spiel für die Messung
            td = i;
        }
        // Bodenkontakt: erster Punkt ab td, der dicht an der Geraden liegt (Toleranz je nach Messgenauigkeit der Geraden).
        double rms = fit.Count >= 3
            ? Math.Sqrt(fit.Average(f => Math.Pow(f.Alt - (at0 + slope * f.D), 2)))
            : 0;
        double contactFt = Math.Max(FirstContactFt, 2 * rms);
        int c = td;
        while (c < anchor && Off(c) > contactFt) c++;

        // Sinkrate gegenüber der Bahn = Änderung der Höhe über der Geraden. So fällt die Bahnneigung heraus:
        // Bergauf steigt das Flugzeug beim Aufsetzen, obwohl es auf die Bahn zu sinkt.
        // Gewertet werden die letzten Fuß vor dem Kontakt (wie bei der Landerate auf flacher Bahn), nicht der Anflug.
        double StepRate(int k)
        {
            double dt = (_samples[k].Time - _samples[k - 1].Time).TotalSeconds;
            return dt <= 0 || dt > TouchdownMaxSampleGapSec ? 0 : (Off(k) - Off(k - 1)) / dt * 60;
        }
        double rate = 0;
        bool any = false;
        for (int k = Math.Max(td, 1); k <= c; k++)
        {
            if (Off(k - 1) > 6) continue;
            rate = Math.Min(rate, StepRate(k));
            any = true;
        }
        if (!any && c >= 1) rate = Math.Min(0, StepRate(c));
        var x = _samples[c];

        // Was nach dem Aufsetzen als Steigen/Durchstarten erschien, war das Ausrollen bergauf.
        foreach (var e in _events.Where(e => e.Time > x.Time && e.PhaseChange && e.Phase.IsAirborne()).ToList())
        {
            if (e.Text == "Durchstarten") GoArounds--;
            _events.Remove(e);
        }

        _candidate = null;
        _settling = false;
        TouchdownDetected?.Invoke(x, rate);
        Touchdown = x.Time;
        TouchdownSample = x;
        LandingRateFpm = rate;
        ArrivalAirport = airport;
        SetPhase(FlightPhase.Landing, x.Time, $"Aufsetzen mit {rate:F0} fpm bei {x.GroundSpeedKt:F0} kt" + At(ArrivalAirport));
        return true;
    }

    private static bool SamePosition(FlightSample a, FlightSample b) =>
        Math.Abs(a.Latitude - b.Latitude) < 1e-7 &&
        Math.Abs(a.Longitude - b.Longitude) < 1e-7 &&
        Math.Abs(a.AltitudeFt - b.AltitudeFt) < 0.01;

    private static string At(Airport? airport) => airport is null ? "" : $" in {airport.Ident}";

    public static string FormatAltitude(double altFt) =>
        altFt >= 10000
            ? $"FL{Math.Round(altFt / 100):000}"
            : (Math.Round(altFt / 100) * 100).ToString("N0", De) + " ft";

    private void SetPhase(FlightPhase phase, DateTimeOffset time, string text)
    {
        Phase = phase;
        Raise(new FlightEvent(time, phase, text));
    }

    private void Log(DateTimeOffset time, string text) =>
        Raise(new FlightEvent(time, Phase, text, PhaseChange: false));

    private void Raise(FlightEvent e)
    {
        _events.Add(e);
        EventRaised?.Invoke(e);
    }
}
