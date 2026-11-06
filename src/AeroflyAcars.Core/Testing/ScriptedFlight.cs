using AeroflyAcars.Core.Sim;

namespace AeroflyAcars.Core.Testing;

public sealed record ScriptedFrame(TimeSpan Offset, string Stage, GpsMessage Gps, AttitudeMessage Attitude);

/// <summary>
/// Erzeugt einen künstlichen, kurzen Flug (ca. 13 Minuten, 1 Bild pro Sekunde) in Hamburg:
/// Rollen, Start auf 23, Steigflug auf 4.500 ft MSL, Kurve, Sinkflug, Anflug, Landung mit ca. -180 fpm, Rollen.
/// Wird vom Simulator-Ersatz und vom Selbsttest genutzt.
/// </summary>
public static class ScriptedFlight
{
    public const string SourceName = "Aerofly FS 4";
    public const double GroundElevationFt = 53;
    public const double ExpectedLandingRateFpm = -180;

    public static IReadOnlyList<ScriptedFrame> Generate()
    {
        var g = new Generator(lat: 53.6395, lon: 10.0040, altFt: GroundElevationFt, trackDeg: 143);
        double ground = GroundElevationFt;

        g.Run("Parkposition", seconds: 20, targetGs: 0, accel: 3);
        g.Run("Rollen", seconds: 25, targetGs: 15, accel: 2);
        g.Run("Rollen", seconds: 20, targetGs: 15, accel: 2, turnRate: 4.5);   // 143° → 233°
        g.Run("Rollen", seconds: 15, targetGs: 15, accel: 2);
        g.Run("Halteposition", seconds: 10, targetGs: 0, accel: 3);
        g.RunUntil("Startlauf", () => g.GsKt >= 145, targetGs: 150, accel: 3);
        g.RunUntil("Steigflug", () => g.AltFt >= ground + 4500, targetGs: 220, accel: 1, vsFpm: 1500);
        g.Run("Reiseflug", seconds: 30, targetGs: 230, accel: 1);
        g.Run("Reiseflug (Kurve)", seconds: 60, targetGs: 230, accel: 1, turnRate: 3);   // 233° → 53°
        g.Run("Reiseflug", seconds: 30, targetGs: 230, accel: 1);
        g.RunUntil("Sinkflug", () => g.AltFt <= ground + 1500, targetGs: 200, accel: 1, vsFpm: -1000);
        g.RunUntil("Anflug", () => g.AltFt <= ground + 30, targetGs: 140, accel: 1, vsFpm: -700);
        g.RunUntil("Abfangen", () => g.AltFt <= ground, targetGs: 135, accel: 1, vsFpm: ExpectedLandingRateFpm, floorFt: ground);
        g.RunUntil("Ausrollen", () => g.GsKt <= 20, targetGs: 15, accel: 4);
        g.Run("Rollen", seconds: 20, targetGs: 15, accel: 2, turnRate: -4.5);
        g.Run("Rollen", seconds: 25, targetGs: 15, accel: 2);
        g.Run("Parkposition", seconds: 45, targetGs: 0, accel: 3);

        return g.Frames;
    }

    private sealed class Generator
    {
        private double _lat;
        private double _lon;
        private double _track;
        private int _t;

        public Generator(double lat, double lon, double altFt, double trackDeg)
        {
            _lat = lat;
            _lon = lon;
            AltFt = altFt;
            _track = trackDeg;
        }

        public double AltFt { get; private set; }
        public double GsKt { get; private set; }
        public List<ScriptedFrame> Frames { get; } = new();

        public void Run(string stage, int seconds, double targetGs, double accel, double vsFpm = 0, double turnRate = 0)
        {
            for (int i = 0; i < seconds; i++) Step(stage, targetGs, accel, vsFpm, turnRate, null);
        }

        public void RunUntil(string stage, Func<bool> done, double targetGs, double accel, double vsFpm = 0,
            double turnRate = 0, double? floorFt = null, int maxSeconds = 1800)
        {
            for (int i = 0; i < maxSeconds && !done(); i++) Step(stage, targetGs, accel, vsFpm, turnRate, floorFt);
        }

        private void Step(string stage, double targetGs, double accel, double vsFpm, double turnRate, double? floorFt)
        {
            GsKt = GsKt < targetGs ? Math.Min(targetGs, GsKt + accel) : Math.Max(targetGs, GsKt - accel);
            _track = Geo.NormalizeDegrees(_track + turnRate);
            AltFt += vsFpm / 60.0;
            if (floorFt is { } floor && AltFt < floor) AltFt = floor;
            (_lat, _lon) = Geo.Move(_lat, _lon, _track, GsKt / 3600.0);
            _t++;

            double pitch = vsFpm > 300 ? 8 : vsFpm < -300 ? -2 : vsFpm < 0 ? 3 : 0;
            double roll = Math.Clamp(turnRate * 7, -25, 25);

            // Aerofly sendet die Höhe in Metern mit einer Nachkommastelle – hier genauso gerundet.
            var gps = new GpsMessage(SourceName, _lon, _lat,
                Math.Round(AltFt / Geo.FeetPerMeter, 1), _track,
                Math.Round(GsKt / Geo.KnotsPerMeterPerSecond, 1));
            var att = new AttitudeMessage(SourceName, _track, pitch, roll);
            Frames.Add(new ScriptedFrame(TimeSpan.FromSeconds(_t), stage, gps, att));
        }
    }
}
