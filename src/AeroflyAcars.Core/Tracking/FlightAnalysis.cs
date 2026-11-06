using AeroflyAcars.Core.Airports;

namespace AeroflyAcars.Core.Tracking;

/// <summary>Auswertung eines aufgezeichneten Flugs – alles, was sich aus dem Aerofly-UDP-Broadcast ableiten lässt.</summary>
public sealed record FlightAnalysis
{
    public int Bounces { get; init; }

    /// <summary>Sinkrate im Endanflug (1000 ft bis 50 ft über Aufsetzhöhe): Mittel, Streuung, steilster und flachster Wert.</summary>
    public double? ApproachVsMeanFpm { get; init; }
    public double? ApproachVsSigmaFpm { get; init; }
    public double? ApproachVsSteepestFpm { get; init; }
    public double? ApproachVsFlattestFpm { get; init; }
    public int ApproachSamples { get; init; }
    public string? ApproachRating { get; init; }

    /// <summary>Strecke vom Aufsetzen bis unter 30 kt.</summary>
    public double? RolloutM { get; init; }

    public string? DepartureRunway { get; init; }
    public string? ArrivalRunway { get; init; }

    /// <summary>Höchster erkannter Zeitraffer (1 = Echtzeit).</summary>
    public double MaxSimRate { get; init; } = 1;
    public int SlewEvents { get; init; }

    public string? DaylightTakeoff { get; init; }
    public string? DaylightLanding { get; init; }

    public TimeSpan? TaxiOut { get; init; }
    public TimeSpan? TaxiIn { get; init; }
    public TimeSpan PausedTime { get; init; }

    public const double ApproachTopFt = 1000;
    public const double ApproachBottomFt = 50;
    /// <summary>Darüber gilt ein Messpunkt als „nicht sinkend“ (fpm).</summary>
    public const double ApproachLevelVsFpm = -100;
    public static readonly TimeSpan ApproachLevelBreak = TimeSpan.FromSeconds(10);
    public const double BounceVsFpm = 100;
    public static readonly TimeSpan BounceWindow = TimeSpan.FromSeconds(20);
    public const double SimRateMinGsKt = 30;
    public const double SlewMinJumpNm = 0.3;
    public const double SlewMinImpliedKt = 250;
    public const double SlewMinRateFactor = 4.5;
    public const double SlewMinAltJumpFt = 2000;

    public static FlightAnalysis Analyze(FlightTracker t, RunwayDatabase? runways = null)
    {
        var samples = t.Samples;
        var td = t.TouchdownSample;

        // --- Bounces: nach dem Aufsetzen wieder Steigen (> 100 fpm) innerhalb von 20 s, solange noch schnell ---
        int bounces = 0;
        if (td is not null)
        {
            bool rising = false;
            foreach (var s in samples)
            {
                if (s.Time <= td.Time) continue;
                if (s.Time - td.Time > BounceWindow || s.GroundSpeedKt < 30) break;
                // Gegenüber der Bahn: Bergauf steigt die Höhe beim Ausrollen, ohne dass das Flugzeug abhebt.
                double vs = s.VerticalSpeedFpm - (t.TouchdownSlope ?? 0) * s.GroundSpeedKt * 101.27;
                if (!rising && vs > BounceVsFpm) { rising = true; bounces++; }
                else if (rising && vs < 0) rising = false;
            }
        }

        // --- Endanflug-Stabilität ---
        double? mean = null, sigma = null, steep = null, flat = null;
        int n = 0;
        string? rating = null;
        if (td is not null)
        {
            // Vom Aufsetzen rückwärts: nur der letzte zusammenhängende Sinkflug unter 1000 ft. Schluss ist, wo es
            // höher wird oder das Flugzeug davor mindestens 10 s lang nicht gesunken ist (Platzrunde, Level-off,
            // Durchstarten) – das gehört nicht zum Endanflug.
            var vs = new List<double>();
            int levelRun = 0;               // Anzahl der zuletzt hinzugefügten Werte ohne Sinken
            DateTimeOffset? levelFrom = null;
            for (int i = samples.Count - 1; i >= 0; i--)
            {
                var s = samples[i];
                if (s.Time >= td.Time) continue;
                double height = s.AltitudeFt - td.AltitudeFt;
                if (height > ApproachTopFt) break;
                if (td.Time - s.Time > TimeSpan.FromMinutes(15)) break;
                if (height < ApproachBottomFt) continue;

                if (s.VerticalSpeedFpm > ApproachLevelVsFpm)
                {
                    levelFrom ??= s.Time;
                    if (levelFrom.Value - s.Time >= ApproachLevelBreak)
                    {
                        vs.RemoveRange(vs.Count - levelRun, levelRun);
                        break;
                    }
                    levelRun++;
                }
                else
                {
                    levelFrom = null;
                    levelRun = 0;
                }
                vs.Add(s.VerticalSpeedFpm);
            }
            n = vs.Count;
            if (n >= 5)
            {
                mean = vs.Average();
                double m = mean.Value;
                sigma = Math.Sqrt(vs.Sum(v => (v - m) * (v - m)) / n);
                steep = vs.Min();
                flat = vs.Max();
                rating = sigma <= 300 ? "stabil" : sigma <= 500 ? "leichte Schwankungen" : "unstabil";
            }
        }

        // --- Rollout ---
        double? rollout = null;
        if (td is not null)
        {
            double dist = 0;
            FlightSample? prev = null;
            foreach (var s in samples)
            {
                if (s.Time < td.Time) continue;
                if (prev is not null) dist += Geo.DistanceNm(prev.Latitude, prev.Longitude, s.Latitude, s.Longitude) * Geo.MetersPerNm;
                prev = s;
                if (s.GroundSpeedKt < 30) break;
            }
            rollout = dist;
        }

        // --- Zeitraffer und Slew: gefahrene Strecke im Vergleich zur gemeldeten Groundspeed ---
        var rates = new List<double>();
        double maxRate = 1;
        int slews = 0;
        var from = t.BlockOff ?? DateTimeOffset.MinValue;
        var to = t.BlockOn ?? DateTimeOffset.MaxValue;
        for (int i = 1; i < samples.Count; i++)
        {
            var a = samples[i - 1];
            var b = samples[i];
            if (b.Time < from || b.Time > to) continue;
            double dt = (b.Time - a.Time).TotalSeconds;
            if (dt <= 0.5 || dt > 5) continue;

            double distNm = Geo.DistanceNm(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
            double impliedKt = distNm / (dt / 3600);
            double gs = (a.GroundSpeedKt + b.GroundSpeedKt) / 2;

            bool jump = distNm > SlewMinJumpNm && impliedKt > Math.Max(SlewMinImpliedKt, gs * SlewMinRateFactor);
            bool altJump = Math.Abs(b.AltitudeFt - a.AltitudeFt) > SlewMinAltJumpFt;
            if (jump || altJump)
            {
                slews++;
                continue;
            }

            if (gs >= SimRateMinGsKt)
            {
                rates.Add(impliedKt / gs);
                if (rates.Count >= 5)
                {
                    // Median der letzten 5 Werte gegen Ausreißer
                    var last = rates.Skip(rates.Count - 5).OrderBy(x => x).ToList();
                    maxRate = Math.Max(maxRate, last[2]);
                }
            }
        }

        // --- Bahnen ---
        string? depRwy = null, arrRwy = null;
        if (runways is not null)
        {
            var rollStart = t.Events.FirstOrDefault(e => e.PhaseChange && e.Phase == FlightPhase.TakeoffRoll)?.Time;
            if (t.DepartureAirport is { } dep && rollStart is { } rs && t.Takeoff is { } liftoff)
            {
                // Mittel aus Position/Kurs der ersten Sekunden des Startlaufs
                var roll = samples.Where(s => s.Time >= rs && s.Time <= liftoff).ToList();
                if (roll.Count >= 2)
                {
                    var first = roll[0];
                    double track = Geo.BearingDeg(first.Latitude, first.Longitude, roll[^1].Latitude, roll[^1].Longitude);
                    depRwy = runways.Match(dep, first.Latitude, first.Longitude, track)?.Ident;
                }
            }
            if (t.ArrivalAirport is { } arr && td is not null)
            {
                var after = samples.Where(s => s.Time >= td.Time).Take(8).ToList();
                double track = after.Count >= 2
                    ? Geo.BearingDeg(after[0].Latitude, after[0].Longitude, after[^1].Latitude, after[^1].Longitude)
                    : td.TrackDeg;
                arrRwy = runways.Match(arr, td.Latitude, td.Longitude, track)?.Ident;
            }
        }

        // --- Tageslicht ---
        string? Daylight(FlightSample? s) =>
            s is null ? null : Geo.SunElevationDeg(s.Time, s.Latitude, s.Longitude) switch
            {
                >= -0.833 => "Tag",
                >= -6 => "Dämmerung",
                _ => "Nacht",
            };
        var takeoffSample = t.Takeoff is { } tko ? samples.FirstOrDefault(s => s.Time >= tko) : null;

        // --- Rollzeiten ---
        var rollStartTime = t.Events.FirstOrDefault(e => e.PhaseChange && e.Phase == FlightPhase.TakeoffRoll)?.Time;
        var taxiInStart = t.Events.LastOrDefault(e => e.PhaseChange && e.Phase == FlightPhase.TaxiIn)?.Time;

        return new FlightAnalysis
        {
            Bounces = bounces,
            ApproachVsMeanFpm = mean,
            ApproachVsSigmaFpm = sigma,
            ApproachVsSteepestFpm = steep,
            ApproachVsFlattestFpm = flat,
            ApproachSamples = n,
            ApproachRating = rating,
            RolloutM = rollout,
            DepartureRunway = depRwy,
            ArrivalRunway = arrRwy,
            MaxSimRate = Math.Round(maxRate * 2, MidpointRounding.AwayFromZero) / 2,
            SlewEvents = slews,
            DaylightTakeoff = Daylight(takeoffSample),
            DaylightLanding = Daylight(td),
            TaxiOut = t.BlockOff is { } bo && rollStartTime is { } rst ? rst - bo : null,
            TaxiIn = taxiInStart is { } ti && t.BlockOn is { } bn ? bn - ti : null,
            PausedTime = t.PausedTime,
        };
    }
}
