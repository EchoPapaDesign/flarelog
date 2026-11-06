using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AeroflyAcars.Core.Sim;

/// <summary>Eine Wolkenschicht aus dem METAR: Bedeckung in Achteln (FEW … OVC) und Untergrenze in ft über Grund.</summary>
public sealed record CloudLayer(string Cover, int BaseFt)
{
    /// <summary>Bedeckungsgrad 0–1 für Aerofly.</summary>
    public double Density => Cover switch
    {
        "FEW" => 0.2,
        "SCT" => 0.45,
        "BKN" => 0.75,
        "OVC" or "VV" => 1.0,
        _ => 0,
    };
}

/// <summary>Die für Aerofly verwertbaren Teile eines METAR.</summary>
public sealed record Metar(string Raw, string? Station, int? WindDirection, int WindKt, int? GustKt, int? VisibilityM,
    IReadOnlyList<CloudLayer> Clouds)
{
    /// <summary>Kurzfassung fürs Flight Log, z. B. "250/08 kt, Sicht 10 km, BKN 4.500 ft".</summary>
    public string Summary
    {
        get
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            string wind = WindKt == 0 ? "Wind still"
                : $"{(WindDirection is { } d ? d.ToString("000", de) : "VRB")}/{WindKt:00} kt" + (GustKt is { } g ? $" G{g}" : "");
            string vis = VisibilityM is { } v ? (v >= 9999 ? "Sicht ≥ 10 km" : v >= 5000 ? $"Sicht {v / 1000} km" : $"Sicht {v:N0} m".Replace(",", ".")) : "Sicht ?";
            string clouds = Clouds.Count == 0 ? "keine Wolken"
                : string.Join(" ", Clouds.Take(3).Select(c => $"{c.Cover} {c.BaseFt.ToString("N0", de)} ft"));
            return $"{wind}, {vis}, {clouds}";
        }
    }

    private static readonly Regex Wind = new(@"^(\d{3}|VRB)(\d{2,3})(?:G(\d{2,3}))?(KT|MPS|KMH)$", RegexOptions.Compiled);
    private static readonly Regex VisMeters = new(@"^(\d{4})(NDV)?$", RegexOptions.Compiled);
    private static readonly Regex VisSm = new(@"^(?:(\d+) )?(?:(\d+)/(\d+)|(\d+))SM$", RegexOptions.Compiled);
    private static readonly Regex Cloud = new(@"^(FEW|SCT|BKN|OVC|VV)(\d{3}|///)", RegexOptions.Compiled);

    /// <summary>Liest einen METAR. Unbekannte Gruppen werden übersprungen.</summary>
    public static Metar? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        // Trend- und Bemerkungsteil gehören nicht zum aktuellen Wetter.
        foreach (var cut in new[] { " RMK", " TEMPO", " BECMG", " NOSIG" })
        {
            int i = text.IndexOf(cut, StringComparison.Ordinal);
            if (i > 0) text = text[..i];
        }
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count == 0) return null;

        string? station = null;
        int start = 0;
        if (tokens[0] is "METAR" or "SPECI") start = 1;
        if (start < tokens.Count && Regex.IsMatch(tokens[start], "^[A-Z0-9]{4}$")) station = tokens[start++];

        int? dir = null, gust = null, vis = null;
        int speed = 0;
        bool windFound = false;
        var clouds = new List<CloudLayer>();

        for (int i = start; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (!windFound && Wind.Match(t) is { Success: true } w)
            {
                windFound = true;
                double factor = w.Groups[4].Value switch { "MPS" => 1.943844, "KMH" => 0.539957, _ => 1 };
                dir = w.Groups[1].Value == "VRB" ? null : int.Parse(w.Groups[1].Value, CultureInfo.InvariantCulture);
                speed = (int)Math.Round(int.Parse(w.Groups[2].Value, CultureInfo.InvariantCulture) * factor);
                if (w.Groups[3].Success) gust = (int)Math.Round(int.Parse(w.Groups[3].Value, CultureInfo.InvariantCulture) * factor);
                continue;
            }
            if (t == "CAVOK")
            {
                vis = 10000;
                continue;
            }
            if (vis is null && VisMeters.Match(t) is { Success: true } vm)
            {
                vis = int.Parse(vm.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }
            // US-Sicht in Meilen, auch "1 1/2SM" (zwei Gruppen)
            var smToken = t;
            if (vis is null && i + 1 < tokens.Count && Regex.IsMatch(t, @"^\d+$") && tokens[i + 1].EndsWith("SM", StringComparison.Ordinal))
                smToken = t + " " + tokens[++i];
            if (vis is null && VisSm.Match(smToken.TrimStart('P', 'M')) is { Success: true } sm)
            {
                double miles = 0;
                if (sm.Groups[1].Success) miles += int.Parse(sm.Groups[1].Value, CultureInfo.InvariantCulture);
                if (sm.Groups[4].Success) miles += int.Parse(sm.Groups[4].Value, CultureInfo.InvariantCulture);
                if (sm.Groups[2].Success)
                    miles += double.Parse(sm.Groups[2].Value, CultureInfo.InvariantCulture) / double.Parse(sm.Groups[3].Value, CultureInfo.InvariantCulture);
                vis = (int)Math.Round(miles * 1609.344);
                continue;
            }
            if (Cloud.Match(t) is { Success: true } c && c.Groups[2].Value != "///")
            {
                clouds.Add(new CloudLayer(c.Groups[1].Value, int.Parse(c.Groups[2].Value, CultureInfo.InvariantCulture) * 100));
            }
        }

        if (!windFound && vis is null && clouds.Count == 0) return null;
        return new Metar(raw.Trim(), station, dir, speed, gust, vis, clouds.OrderBy(c => c.BaseFt).ToList());
    }
}

/// <summary>
/// Schreibt Uhrzeit und Wetter in die Aerofly-Einstellungsdatei main.mcf. Aerofly liest die Werte beim Start;
/// solange Aerofly läuft, ist nichts zu machen (Aerofly überschreibt die Datei beim Beenden).
/// Umrechnung wie beim Aerofly Wettergerät/Missionsgerät (MIT): Sicht 100 % = 15 km, Wolkenhöhe 100 % = 10.000 ft
/// über Grund, Windstärke s (0–1+) ≙ 8·(s + s²) kt.
/// </summary>
public static class AeroflyConditions
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static bool IsAeroflyRunning()
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.ProcessName.Contains("aerofly_fs", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        catch (Exception)
        {
            // Prozessliste nicht lesbar – dann nicht blockieren.
        }
        return false;
    }

    /// <summary>Windstärke für Aerofly aus Knoten (Umkehrung von kt = 8·(s + s²)).</summary>
    public static double WindStrength(double kt) => kt <= 0 ? 0 : (-1 + Math.Sqrt(1 + kt / 2)) / 2;

    /// <summary>Schreibt Zeit und/oder Wetter (vorher Sicherung als main.mcf.flarelog-backup). Gibt die Zahl der geänderten Werte zurück.</summary>
    public static int WriteToMainMcf(DateTimeOffset? utc, Metar? metar, string? path = null)
    {
        path ??= AeroflyConfig.DefaultPath;
        if (!File.Exists(path)) throw new InvalidOperationException($"Aerofly-Einstellungen nicht gefunden: {path}");

        var bytes = File.ReadAllBytes(path);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(bom ? bytes[3..] : bytes);

        var updated = Apply(text, utc, metar, out int changed);
        if (changed == 0) return 0;

        File.Copy(path, path + ".flarelog-backup", overwrite: true);
        var temp = path + ".flarelog-tmp";
        File.WriteAllText(temp, updated, new UTF8Encoding(bom));
        File.Move(temp, path, overwrite: true);
        return changed;
    }

    /// <summary>Setzt die Werte in einer main.mcf; alles andere bleibt unverändert.</summary>
    public static string Apply(string mcf, DateTimeOffset? utc, Metar? metar, out int changed)
    {
        int count = 0;
        string Set(string text, string type, string name, string value)
        {
            var re = new Regex(@"(<\[" + Regex.Escape(type) + @"\]\[" + Regex.Escape(name) + @"\]\[)[^\]]*(\]>)");
            var matches = re.Matches(text);
            if (matches.Count != 1) return text;   // unbekanntes Format: lieber nichts ändern
            count++;
            return re.Replace(text, m => m.Groups[1].Value + value + m.Groups[2].Value, 1);
        }
        string F(double v) => v.ToString("0.######", Inv);

        var s = mcf;
        if (utc is { } t)
        {
            var u = t.UtcDateTime;
            s = Set(s, "int32", "time_year", u.Year.ToString(Inv));
            s = Set(s, "int32", "time_month", u.Month.ToString(Inv));
            s = Set(s, "int32", "time_day", u.Day.ToString(Inv));
            s = Set(s, "float64", "time_hours", F(u.TimeOfDay.TotalHours));
        }

        if (metar is not null)
        {
            if (metar.VisibilityM is { } vis) s = Set(s, "float64", "visibility", F(Math.Clamp(vis / 15000.0, 0.01, 1)));

            s = Set(s, "float64", "strength", F(WindStrength(metar.WindKt)));
            if (metar.WindDirection is { } dir) s = Set(s, "float64", "direction_in_degree", F(dir));
            // Turbulenz aus Böen, sonst leicht mit dem Wind zunehmend
            double turbulence = metar.GustKt is { } g
                ? Math.Clamp(0.2 + (g - metar.WindKt) / 20.0, 0, 1)
                : Math.Clamp(metar.WindKt / 40.0, 0, 0.3);
            s = Set(s, "float64", "turbulence", F(turbulence));

            // Drei Schichten: Cumulus (unterste), Cumulus mediocris (zweite), Cirrus (dritte/hohe)
            var layers = metar.Clouds.Take(3).ToList();
            (string density, string height)[] fields =
            {
                ("cumulus_density", "cumulus_height"),
                ("cumulus_mediocris_density", "cumulus_mediocris_height"),
                ("cirrus_density", "cirrus_height"),
            };
            for (int i = 0; i < fields.Length; i++)
            {
                var layer = i < layers.Count ? layers[i] : null;
                s = Set(s, "float64", fields[i].density, F(layer?.Density ?? 0));
                if (layer is not null) s = Set(s, "float64", fields[i].height, F(Math.Clamp(layer.BaseFt / 10000.0, 0.02, 1)));
            }
        }

        changed = count;
        return s;
    }
}

/// <summary>Holt den aktuellen METAR beim Aviation Weather Center (aviationweather.gov, frei verfügbar).</summary>
public sealed class MetarClient
{
    private readonly HttpClient _http;

    public MetarClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
    }

    /// <summary>Roher METAR oder null, wenn es für den Platz keinen gibt bzw. der Dienst nicht erreichbar ist.</summary>
    public async Task<string?> FetchAsync(string icao, CancellationToken ct = default)
    {
        var text = await GetAsync("metar", icao, ct).ConfigureAwait(false);
        var line = text?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return string.IsNullOrEmpty(line) ? null : line;
    }

    /// <summary>Roher TAF (mehrzeilig, je Änderungsgruppe eine Zeile) oder null.</summary>
    public async Task<string?> FetchTafAsync(string icao, CancellationToken ct = default)
    {
        var text = await GetAsync("taf", icao, ct).ConfigureAwait(false);
        return text is null ? null : FormatTaf(text);
    }

    /// <summary>TAF lesbar umbrechen: neue Zeile vor FM, BECMG, TEMPO und PROB.</summary>
    public static string? FormatTaf(string raw)
    {
        var flat = string.Join(" ", raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
        flat = System.Text.RegularExpressions.Regex.Replace(flat, @"\s+", " ").Trim();
        if (flat.Length == 0) return null;
        flat = System.Text.RegularExpressions.Regex.Replace(flat, @" (?=(FM\d{6}|BECMG|TEMPO|PROB\d{2}))", "\n  ");
        flat = System.Text.RegularExpressions.Regex.Replace(flat, @"(PROB\d{2})\n  (TEMPO)", "$1 $2");
        return flat;
    }

    private async Task<string?> GetAsync(string kind, string icao, CancellationToken ct)
    {
        try
        {
            var url = $"https://aviationweather.gov/api/data/{kind}?ids={Uri.EscapeDataString(icao.Trim().ToUpperInvariant())}&format=raw";
            var text = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
