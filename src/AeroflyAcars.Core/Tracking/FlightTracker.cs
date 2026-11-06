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
    public const double ClimbVsFpm = 300;
    public const double DescentVsFpm = -300;
    public const double ApproachHeightFt = 3000;
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
    /// <summary>Über einer bekannten Bahn: höher als das ist kein Aufsetzen, sondern ein Abfangen in der Luft.</summary>
    public const double CandidateMaxAboveRunwayFt = 60;
    /// <summary>So nah an der späteren Rollhöhe gilt ein Messpunkt als Bodenkontakt (erster Kontakt vor Bounces).</summary>
    public const double FirstContactFt = 1.5;
    public static readonly TimeSpan FirstContactLookBack = TimeSpan.FromSeconds(30);

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
                    SetPhase(FlightPhase.TakeoffRoll, s.Time, $"Startlauf auf Kurs {s.HeadingDeg:000}°");
                }
                break;

            case FlightPhase.TakeoffRoll:
                DepartureElevationFt ??= s.AltitudeFt;
                if (s.AltitudeFt > DepartureElevationFt + LiftoffAboveGroundFt)
                {
                    Takeoff = _lastOnGround ?? s.Time;
                    LiftoffSpeedKt = s.GroundSpeedKt;
                    SetPhase(FlightPhase.Climb, Takeoff.Value,
                        string.Create(De, $"Abheben mit {s.GroundSpeedKt:F0} kt, Pitch {s.PitchDeg:F1}°, Querlage {Math.Abs(s.RollDeg):F1}°"));
                }
                else
                {
                    _lastOnGround = s.Time;
                    if (s.GroundSpeedKt < RejectTakeoffKt)
                        SetPhase(FlightPhase.TaxiOut, s.Time, "Startabbruch – wieder Rollen");
                }
                break;

            case FlightPhase.Climb:
            case FlightPhase.Cruise:
            case FlightPhase.Descent:
            case FlightPhase.Approach:
                if (!DetectTouchdown(s, prev))
                    UpdateAirbornePhase(s);
                break;

            case FlightPhase.Landing:
                if (s.GroundSpeedKt < TaxiInKt)
                    SetPhase(FlightPhase.TaxiIn, s.Time, "Rollen zur Parkposition");
                break;

            case FlightPhase.TaxiIn:
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
        if (Phase == FlightPhase.Approach && target == FlightPhase.Climb)
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
                HeightAboveGround(cand.Sample, cand.Airport) is { Height: { } h } g && Math.Abs(h) <= g.AnnounceLimit)
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
