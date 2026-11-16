using System.Globalization;

namespace AeroflyAcars.Core.Tracking;

/// <summary>Ansagetext nach dem Aufsetzen, z. B. "Touchdown with minus 177 feet per minute. Good."</summary>
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

    /// <summary>
    /// Ausgeschrieben „minus“ statt „-“: Sprachausgaben lesen das Vorzeichen sonst nicht vor (Testbericht AeRodri),
    /// und eine Sinkrate ist beim Aufsetzen immer negativ.
    /// </summary>
    public static string Text(double landingRateFpm)
    {
        int fpm = (int)Math.Round(Math.Abs(landingRateFpm));
        string sign = landingRateFpm < 0 && fpm > 0 ? "minus " : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"Touchdown with {sign}{fpm} feet per minute. {Rating(landingRateFpm)}.");
    }
}
