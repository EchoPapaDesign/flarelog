using System.Globalization;

namespace AeroflyAcars.Core.Tracking;

/// <summary>Ansagetext nach dem Aufsetzen, z. B. "Touchdown with 177 feet per minute. Good."</summary>
public static class LandingCallout
{
    /// <summary>Bis zu dieser Sinkrate (Betrag, fpm) gilt die Landung als "Good".</summary>
    public const double GoodLimitFpm = 240;

    /// <summary>Bis zu dieser Sinkrate (Betrag, fpm) gilt die Landung als "Mediocre", darüber "Hard".</summary>
    public const double MediocreLimitFpm = 400;

    public static string Rating(double landingRateFpm)
    {
        double sink = Math.Abs(landingRateFpm);
        return sink <= GoodLimitFpm ? "Good" : sink <= MediocreLimitFpm ? "Mediocre" : "Hard";
    }

    public static string Text(double landingRateFpm)
    {
        int fpm = (int)Math.Round(Math.Abs(landingRateFpm));
        return string.Create(CultureInfo.InvariantCulture,
            $"Touchdown with {fpm} feet per minute. {Rating(landingRateFpm)}.");
    }
}
