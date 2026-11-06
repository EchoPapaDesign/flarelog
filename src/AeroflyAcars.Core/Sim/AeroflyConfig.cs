using System.Text.RegularExpressions;

namespace AeroflyAcars.Core.Sim;

/// <summary>
/// Liest das in Aerofly FS 4 gewählte Flugzeug aus der Einstellungsdatei main.mcf
/// (Standard: Dokumente\Aerofly FS 4\main.mcf, Gruppe "tmsettings_aircraft", Eintrag "name", z. B. "c172").
/// Aerofly schreibt die Datei beim Ändern der Einstellungen; der Wert gibt also die letzte Auswahl wieder.
/// </summary>
public static class AeroflyConfig
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aerofly FS 4", "main.mcf");

    public sealed record SelectedAircraft(string Name, string? Paintscheme)
    {
        /// <summary>Lesbarer Name, z. B. "Boeing 737-800" für "b737_800".</summary>
        public string DisplayName => AircraftDisplayName(Name);
    }

    private static readonly Dictionary<string, string> KnownAircraft = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a319"] = "Airbus A319", ["a320"] = "Airbus A320", ["a320_neo"] = "Airbus A320neo", ["a321"] = "Airbus A321", ["a350_1000"] = "Airbus A350-1000",
        ["a380"] = "Airbus A380", ["antares"] = "Lange Antares", ["apache"] = "AH-64 Apache", ["asg29"] = "Schleicher ASG 29",
        ["ask21"] = "Schleicher ASK 21", ["b58"] = "Beechcraft Baron 58", ["b737"] = "Boeing 737", ["b737_800"] = "Boeing 737-800",
        ["b737_900"] = "Boeing 737-900", ["b737_max9"] = "Boeing 737 MAX 9", ["b747"] = "Boeing 747", ["b777_300er"] = "Boeing 777-300ER",
        ["b777f"] = "Boeing 777F", ["b787"] = "Boeing 787", ["b787_9"] = "Boeing 787-9", ["c172"] = "Cessna 172",
        ["c90gtx"] = "King Air C90 GTx", ["concorde"] = "Concorde", ["crj900"] = "Bombardier CRJ900", ["dr1"] = "Fokker Dr.I",
        ["dr400"] = "Robin DR400", ["ec135"] = "Airbus H135", ["extra330"] = "Extra 330", ["f15e"] = "F-15E Strike Eagle",
        ["f18"] = "F/A-18 Hornet", ["f4u"] = "F4U Corsair", ["ju52"] = "Junkers Ju 52", ["jungmeister"] = "Bücker Jungmeister",
        ["lj45"] = "Learjet 45", ["mb339"] = "Aermacchi MB-339", ["me262"] = "Messerschmitt Me 262", ["p38"] = "P-38 Lightning",
        ["pitts"] = "Pitts S-2B", ["q400"] = "Dash 8 Q400", ["r22"] = "Robinson R22", ["swift"] = "Aériane Swift",
        ["uh60"] = "UH-60 Black Hawk",
    };

    /// <summary>Lesbarer Name zu einer Aerofly-Flugzeugkennung; unbekannte Kennungen in Großbuchstaben.</summary>
    public static string AircraftDisplayName(string name) =>
        KnownAircraft.TryGetValue(name.Trim(), out var known) ? known : name.Trim().Replace('_', ' ').ToUpperInvariant();

    /// <summary>Aeroflys laufendes Protokoll (wird bei jedem Start neu geschrieben).</summary>
    public static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aerofly FS 4", "tm.log");

    private static readonly Regex LoadedModel = new(@"tmsimulator:\s+done loading model\s+(\S+)", RegexOptions.Compiled);

    /// <summary>
    /// Das gerade geflogene Flugzeug. Aerofly schreibt einen Flugzeugwechsel nicht sofort in main.mcf, wohl aber
    /// „done loading model …“ ins Protokoll tm.log. Ist das Protokoll neuer als main.mcf, gilt es, sonst main.mcf.
    /// </summary>
    public static SelectedAircraft? ReadCurrentAircraft(string? mcfPath = null, string? logPath = null)
    {
        mcfPath ??= DefaultPath;
        logPath ??= DefaultLogPath;
        var fromMcf = ReadSelectedAircraft(mcfPath);
        try
        {
            if (File.Exists(logPath) && (!File.Exists(mcfPath) || File.GetLastWriteTimeUtc(logPath) >= File.GetLastWriteTimeUtc(mcfPath)) &&
                LastLoadedModel(ReadTail(logPath, 512 * 1024)) is { } loaded)
            {
                return new SelectedAircraft(loaded,
                    fromMcf is not null && fromMcf.Name.Equals(loaded, StringComparison.OrdinalIgnoreCase) ? fromMcf.Paintscheme : null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return fromMcf;
    }

    /// <summary>Letztes „done loading model …“ im Protokolltext.</summary>
    public static string? LastLoadedModel(string log)
    {
        var matches = LoadedModel.Matches(log);
        return matches.Count == 0 ? null : matches[^1].Groups[1].Value.Trim();
    }

    private static string ReadTail(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maxBytes) stream.Seek(-maxBytes, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static SelectedAircraft? ReadSelectedAircraft(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return null;
            // Aerofly hält die Datei ggf. offen – mit FileShare lesen.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return ParseSelectedAircraft(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SelectedAircraft? ParseSelectedAircraft(string mcf)
    {
        int group = mcf.IndexOf("[tmsettings_aircraft]", StringComparison.Ordinal);
        if (group < 0) return null;
        var section = mcf[group..Math.Min(mcf.Length, group + 4000)];
        var name = Value(section, "name");
        return string.IsNullOrWhiteSpace(name) ? null : new SelectedAircraft(name, Value(section, "paintscheme"));
    }

    private static string? Value(string section, string key)
    {
        var m = Regex.Match(section, @"\[" + Regex.Escape(key) + @"\]\s*\[([^\]]*)\]");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}

/// <summary>Grober Abgleich zwischen gebuchtem Flugzeugtyp, Aerofly-Auswahl und Abhebegeschwindigkeit.</summary>
public static class AircraftCheck
{
    public sealed record Result(bool? Ok, string Text);

    private enum Category { Unknown, Light, Turboprop, Jet }

    public static Result Compare(string? bookedIcao, string? aeroflyName, double? liftoffKt)
    {
        var notes = new List<string>();
        bool? ok = null;

        // Sammeltypen der Airline (z. B. "CSE" = Cessna Single Engine) sind keine ICAO-Typen: nicht vergleichen.
        if (bookedIcao is not null && !bookedIcao.Any(char.IsDigit))
        {
            notes.Add($"Gebucht: Typgruppe {bookedIcao}.");
            bookedIcao = null;
        }

        if (bookedIcao is not null && aeroflyName is not null)
        {
            bool same = Family(bookedIcao) is { Length: > 0 } a && a == Family(aeroflyName);
            if (same)
            {
                ok = true;
                notes.Add($"In Aerofly gewählt: {aeroflyName} – passt zu {bookedIcao}.");
            }
            else
            {
                ok = false;
                notes.Add($"In Aerofly gewählt: {aeroflyName}, gebucht ist {bookedIcao}.");
            }
        }
        else if (aeroflyName is not null)
        {
            notes.Add($"In Aerofly gewählt: {aeroflyName}.");
        }

        if (bookedIcao is not null && liftoffKt is { } v)
        {
            var cat = CategoryOf(bookedIcao);
            bool implausible = (cat == Category.Light && v > 100) || (cat == Category.Jet && v < 95);
            if (implausible)
            {
                ok = false;
                notes.Add($"Abheben mit {v:F0} kt passt nicht zu {bookedIcao}.");
            }
            else if (cat != Category.Unknown)
            {
                ok ??= true;
                notes.Add($"Abhebegeschwindigkeit {v:F0} kt passt zu {bookedIcao}.");
            }
        }

        return new Result(ok, notes.Count == 0 ? "Kein Abgleich möglich." : string.Join(" ", notes));
    }

    /// <summary>"C172" → "c17", "B738" → "b73", "b737_max9" → "b73", "a320" → "a32".</summary>
    internal static string Family(string type)
    {
        var s = new string(type.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var m = Regex.Match(s, @"^[a-z]+\d{1,2}");
        return m.Success ? m.Value : s;
    }

    private static Category CategoryOf(string icao)
    {
        var t = icao.ToUpperInvariant();
        string[] light = { "C1", "C2", "PA", "P28", "DR4", "SR2", "DA4", "DA2", "BE3", "M20", "RV", "PC6", "EXTR", "J3", "PC12" };
        string[] turbo = { "DH8", "AT4", "AT7", "B350", "C208", "TBM", "KODI", "DHC6" };
        string[] jet = { "B7", "A3", "A2", "B3", "E1", "E2", "CRJ", "MD", "BCS", "C5", "C6", "C7", "LJ", "GLF", "CL" };
        if (turbo.Any(t.StartsWith)) return Category.Turboprop;
        if (light.Any(t.StartsWith)) return Category.Light;
        if (jet.Any(t.StartsWith)) return Category.Jet;
        return Category.Unknown;
    }
}
