using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace AeroflyAcars.Core.Sim;

/// <summary>
/// Liest und schreibt das ForeFlight-UDP-Textformat, das Aerofly FS 4 mit
/// "Broadcast flight info" sendet (Standardport 49002).
/// Beispiel: <c>XGPSAerofly FS 4,10.00512,53.63021,16.2,233.50,51.4</c>
/// </summary>
public static class ForeFlightProtocol
{
    public const int DefaultPort = 49002;

    private static readonly char[] TrimChars = { ' ', '\0', '\r', '\n', '\t' };

    /// <summary>Ein UDP-Paket kann theoretisch mehrere Zeilen enthalten.</summary>
    public static IEnumerable<SimMessage> ParsePacket(string packet)
    {
        foreach (var line in packet.Split('\n'))
        {
            if (TryParseLine(line, out var message))
                yield return message;
        }
    }

    public static bool TryParseLine(string line, [NotNullWhen(true)] out SimMessage? message)
    {
        message = null;
        line = line.Trim(TrimChars);

        if (line.StartsWith("XGPS", StringComparison.Ordinal))
        {
            var p = line[4..].Split(',');
            if (p.Length < 6) return false;
            if (!TryNumber(p[1], out var lon) || !TryNumber(p[2], out var lat) ||
                !TryNumber(p[3], out var alt) || !TryNumber(p[4], out var track) ||
                !TryNumber(p[5], out var gs))
                return false;
            if (lat is < -90 or > 90 || lon is < -180 or > 180) return false;
            // Beim Laden eines Flugs meldet Aerofly kurz die Position 0° N / 0° O – kein echter Messpunkt.
            if (Math.Abs(lat) < 0.01 && Math.Abs(lon) < 0.01) return false;

            message = new GpsMessage(p[0].Trim(), lon, lat, alt, track, gs);
            return true;
        }

        if (line.StartsWith("XATT", StringComparison.Ordinal))
        {
            var p = line[4..].Split(',');
            if (p.Length < 4) return false;
            if (!TryNumber(p[1], out var heading) || !TryNumber(p[2], out var pitch) ||
                !TryNumber(p[3], out var roll))
                return false;

            message = new AttitudeMessage(p[0].Trim(), heading, pitch, roll);
            return true;
        }

        return false;
    }

    public static string Format(GpsMessage m) => string.Create(CultureInfo.InvariantCulture,
        $"XGPS{m.Source},{m.Longitude:F6},{m.Latitude:F6},{m.AltitudeMslMeters:F1},{m.TrackDeg:F2},{m.GroundSpeedMps:F1}");

    public static string Format(AttitudeMessage m) => string.Create(CultureInfo.InvariantCulture,
        $"XATT{m.Source},{m.HeadingDeg:F2},{m.PitchDeg:F2},{m.RollDeg:F2}");

    // Wichtig: immer InvariantCulture, sonst scheitert das Parsen auf deutschem Windows (Komma statt Punkt).
    private static bool TryNumber(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);
}
