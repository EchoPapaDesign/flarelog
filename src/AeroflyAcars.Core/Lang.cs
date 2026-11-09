using System.Globalization;

namespace AeroflyAcars.Core;

/// <summary>
/// Sprache der Oberfläche, Meldungen und Berichte: Deutsch oder Englisch. Texte stehen im Code in beiden Sprachen:
/// <c>Lang.T("Deutsch", "English")</c>. Die Auswahl gilt für das ganze Programm.
/// </summary>
public static class Lang
{
    private static volatile bool _english;

    public static bool English { get => _english; set => _english = value; }

    /// <summary>"de" oder "en".</summary>
    public static string Code => English ? "en" : "de";

    public static string T(string de, string en) => English ? en : de;

    /// <summary>Zahlen- und Datumsformat zur Sprache (1.234,5 bzw. 1,234.5).</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(English ? "en-US" : "de-DE");

    /// <summary>Spricht das System Deutsch? (Grundlage für die Einstellung „Automatisch“.)</summary>
    public static bool SystemIsGerman(string? systemLanguage = null)
    {
        var lang = systemLanguage ?? CultureInfo.CurrentUICulture.Name;
        return lang.StartsWith("de", StringComparison.OrdinalIgnoreCase);
    }
}
