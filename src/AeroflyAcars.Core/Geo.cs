namespace AeroflyAcars.Core;

/// <summary>Einheiten und einfache Großkreisrechnung.</summary>
public static class Geo
{
    public const double FeetPerMeter = 3.280839895;
    public const double KnotsPerMeterPerSecond = 1.943844492;
    private const double EarthRadiusNm = 3440.065;

    /// <summary>Großkreisentfernung in nautischen Meilen.</summary>
    public static double DistanceNm(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = ToRad(lat2 - lat1);
        double dLon = ToRad(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusNm * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>Punkt, der von (lat, lon) aus auf dem Kurs trackDeg nach distNm erreicht wird.</summary>
    public static (double Lat, double Lon) Move(double lat, double lon, double trackDeg, double distNm)
    {
        double d = distNm / EarthRadiusNm;
        double brg = ToRad(trackDeg);
        double lat1 = ToRad(lat);
        double lon1 = ToRad(lon);
        double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(brg));
        double lon2 = lon1 + Math.Atan2(Math.Sin(brg) * Math.Sin(d) * Math.Cos(lat1),
                                        Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));
        return (ToDeg(lat2), ToDeg(lon2));
    }

    public const double MetersPerNm = 1852.0;

    /// <summary>Rechtweisender Kurs von Punkt 1 zu Punkt 2 (0–360°).</summary>
    public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = ToRad(lat1), p2 = ToRad(lat2), dl = ToRad(lon2 - lon1);
        double y = Math.Sin(dl) * Math.Cos(p2);
        double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        return NormalizeDegrees(ToDeg(Math.Atan2(y, x)));
    }

    /// <summary>
    /// Lage eines Punktes zur Linie A→B: seitlicher Abstand (NM, Betrag) und Strecke entlang der Linie ab A (NM, negativ = vor A).
    /// </summary>
    public static (double CrossTrackNm, double AlongTrackNm) TrackOffsets(
        double latA, double lonA, double latB, double lonB, double lat, double lon)
    {
        double d13 = DistanceNm(latA, lonA, lat, lon) / EarthRadiusNm;
        double t13 = ToRad(BearingDeg(latA, lonA, lat, lon));
        double t12 = ToRad(BearingDeg(latA, lonA, latB, lonB));
        double xt = Math.Asin(Math.Clamp(Math.Sin(d13) * Math.Sin(t13 - t12), -1, 1));
        double at = Math.Acos(Math.Clamp(Math.Cos(d13) / Math.Cos(xt), -1, 1));
        if (Math.Cos(t13 - t12) < 0) at = -at;
        return (Math.Abs(xt) * EarthRadiusNm, at * EarthRadiusNm);
    }

    /// <summary>Kleinster Winkel zwischen zwei Kursen (0–180°).</summary>
    public static double AngleDiff(double a, double b)
    {
        double d = Math.Abs(NormalizeDegrees(a) - NormalizeDegrees(b));
        return d > 180 ? 360 - d : d;
    }

    /// <summary>Sonnenhöhe über dem Horizont in Grad (vereinfachte NOAA-Formel, Genauigkeit ~1°).</summary>
    public static double SunElevationDeg(DateTimeOffset time, double lat, double lon)
    {
        double d = (time.UtcDateTime - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;
        double g = ToRad(NormalizeDegrees(357.529 + 0.98560028 * d));
        double q = NormalizeDegrees(280.459 + 0.98564736 * d);
        double l = ToRad(NormalizeDegrees(q + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)));
        double e = ToRad(23.439 - 0.00000036 * d);
        double ra = Math.Atan2(Math.Cos(e) * Math.Sin(l), Math.Cos(l));
        double dec = Math.Asin(Math.Sin(e) * Math.Sin(l));
        double gmstDeg = NormalizeDegrees((18.697374558 + 24.06570982441908 * d) * 15);
        double ha = ToRad(gmstDeg + lon) - ra;
        double phi = ToRad(lat);
        return ToDeg(Math.Asin(Math.Sin(phi) * Math.Sin(dec) + Math.Cos(phi) * Math.Cos(dec) * Math.Cos(ha)));
    }

    public static double NormalizeDegrees(double deg)
    {
        deg %= 360.0;
        return deg < 0 ? deg + 360.0 : deg;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
    private static double ToDeg(double rad) => rad * 180.0 / Math.PI;
}
