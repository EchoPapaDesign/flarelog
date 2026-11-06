using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AeroflyAcars.Core.Airline;

public sealed record PilotInfo(string? Ident, string? Name, string? AirlineIcao, string? CurrentAirport, string? Rank,
    string? AirlineId = null)
{
    public string Display => string.Join(" ", new[] { Ident, Name }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

/// <summary>Ein gebuchter Flug (phpVMS "Bid").</summary>
public sealed record AirlineBooking(
    string? BidId,
    string FlightId,
    string Ident,
    string? AirlineIcao,
    string? AirlineId,
    string? FlightNumber,
    string? RouteCode,
    string? RouteLeg,
    string Departure,
    string Arrival,
    string? Route,
    int? Level,
    double? DistanceNm,
    int? FlightTimeMin,
    IReadOnlyList<string> AircraftTypes,
    string? SimBriefId,
    string? AircraftId = null,
    string? AircraftRegistration = null,
    string? AircraftIcao = null)
{
    /// <summary>"FF215" statt "FF215/C.FTR/L.1" (phpVMS hängt Routencode und Leg an).</summary>
    public string ShortIdent => Ident.Split('/')[0];

    /// <summary>Zusätze wie "C.FTR/L.1" oder null.</summary>
    public string? IdentSuffix => Ident.Contains('/') ? Ident[(Ident.IndexOf('/') + 1)..] : null;
}

/// <summary>Ein Flugzeug der Airline-Flotte.</summary>
public sealed record AirlineAircraft(string Id, string Registration, string? Icao, string? SubfleetType, string? SubfleetName,
    string? AirportId, string? Status, int? State);

/// <summary>Daten zum Anmelden eines Flugs (POST /api/pireps/prefile).</summary>
public sealed record PirepPrefile(
    string AirlineId, string AircraftId, string? FlightId, string FlightNumber, string? RouteCode, string? RouteLeg,
    string Departure, string Arrival, int? Level, string? Route, double? PlannedDistanceNm, int? PlannedFlightTimeMin,
    DateTimeOffset? BlockOffTime, string SourceName);

/// <summary>Daten zum Einreichen (POST /api/pireps/{id}/file).</summary>
public sealed record PirepFile(
    double DistanceNm, int FlightTimeMin, int? BlockTimeMin, double? LandingRateFpm, DateTimeOffset? BlockOffTime,
    DateTimeOffset? BlockOnTime, int? Level, string? Route, string? Notes, string SourceName);

/// <summary>Ein Positionspunkt (POST /api/pireps/{id}/acars/position).</summary>
public sealed record PirepPosition(double Lat, double Lon, double AltitudeFt, double HeadingDeg, double VsFpm, double GsKt,
    DateTimeOffset Time, double? AglFt = null);

public sealed class AirlineException : Exception
{
    public AirlineException(string message, Exception? inner = null) : base(message, inner) { }

    public AirlineException(string message, HttpStatusCode statusCode) : base(message) { StatusCode = statusCode; }

    public HttpStatusCode? StatusCode { get; }
}

/// <summary>
/// Zugriff auf die Schnittstelle einer phpVMS-7-Airline.
/// Anmeldung per Header "X-API-Key" mit dem persönlichen API-Schlüssel des Piloten.
/// Lesen: Pilot, Buchungen, Flotte. Senden: Flug anmelden, Positionen, PIREP einreichen, abbrechen.
/// </summary>
public sealed class PhpVmsClient
{
    private readonly HttpClient _http;
    private readonly Uri _base;
    private readonly string _apiKey;

    public PhpVmsClient(string baseUrl, string apiKey, HttpClient? http = null)
    {
        _base = NormalizeBaseUrl(baseUrl);
        _apiKey = apiKey.Trim();
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public static Uri NormalizeBaseUrl(string url)
    {
        url = url.Trim();
        if (url.Length == 0) throw new AirlineException("Keine Airline-Adresse eingetragen.");
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
            throw new AirlineException($"Ungültige Airline-Adresse: {url}");
        return uri;
    }

    public async Task<PilotInfo> GetUserAsync(CancellationToken ct = default) =>
        PhpVmsParser.ParseUser(await GetAsync("api/user", ct).ConfigureAwait(false));

    public async Task<IReadOnlyList<AirlineBooking>> GetBidsAsync(CancellationToken ct = default) =>
        PhpVmsParser.ParseBids(await GetAsync("api/user/bids", ct).ConfigureAwait(false));

    /// <summary>Flugzeuge, die der Pilot fliegen darf (/api/user/fleet), sonst die ganze Flotte (/api/fleet).</summary>
    public async Task<(IReadOnlyList<AirlineAircraft> Aircraft, string Info)> GetFleetAsync(CancellationToken ct = default)
    {
        var info = new List<string>();
        try
        {
            var json = await GetAsync("api/user/fleet", ct).ConfigureAwait(false);
            var (aircraft, subfleets, _) = PhpVmsParser.ParseFleetPage(json);
            info.Add($"user/fleet: {subfleets} Unterflotten, {aircraft.Count} Flugzeuge");
            if (aircraft.Count > 0) return (aircraft, string.Join("; ", info));
        }
        catch (AirlineException ex)
        {
            info.Add($"user/fleet: {ex.Message}");
        }

        // Ganze Flotte, seitenweise (phpVMS paginiert /api/fleet).
        var all = new List<AirlineAircraft>();
        int totalSubfleets = 0;
        for (int page = 1; page <= 20; page++)
        {
            string json;
            try
            {
                json = await GetAsync($"api/fleet?page={page}", ct).ConfigureAwait(false);
            }
            catch (AirlineException ex)
            {
                info.Add($"fleet Seite {page}: {ex.Message}");
                break;
            }
            var (aircraft, subfleets, lastPage) = PhpVmsParser.ParseFleetPage(json);
            totalSubfleets += subfleets;
            all.AddRange(aircraft);
            if (subfleets == 0 || lastPage is null || page >= lastPage) break;
        }
        info.Add($"fleet: {totalSubfleets} Unterflotten, {all.Count} Flugzeuge");
        var unique = all.GroupBy(a => a.Id).Select(g => g.First()).ToList();
        return (unique, string.Join("; ", info));
    }

    /// <summary>Sucht ein Flugzeug über sein Kennzeichen (/api/fleet/aircraft/{reg}?type=registration).</summary>
    public async Task<AirlineAircraft?> FindAircraftByRegistrationAsync(string registration, CancellationToken ct = default)
    {
        var reg = registration.Trim().ToUpperInvariant();
        try
        {
            var json = await GetAsync($"api/fleet/aircraft/{Uri.EscapeDataString(reg)}?type=registration", ct).ConfigureAwait(false);
            return PhpVmsParser.ParseAircraft(json);
        }
        catch (AirlineException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Meldet den Flug an und gibt die PIREP-ID zurück.</summary>
    public async Task<string> PrefileAsync(PirepPrefile p, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["airline_id"] = p.AirlineId,
            ["aircraft_id"] = p.AircraftId,
            ["flight_id"] = p.FlightId,
            ["flight_number"] = p.FlightNumber,
            ["route_code"] = p.RouteCode,
            ["route_leg"] = p.RouteLeg,
            ["dpt_airport_id"] = p.Departure,
            ["arr_airport_id"] = p.Arrival,
            ["level"] = p.Level,
            ["route"] = p.Route,
            ["planned_distance"] = p.PlannedDistanceNm is { } d ? Math.Round(d, 1) : null,
            ["planned_flight_time"] = p.PlannedFlightTimeMin,
            ["block_off_time"] = Iso(p.BlockOffTime),
            ["source_name"] = p.SourceName,
        };
        var json = await SendAsync(HttpMethod.Post, "api/pireps/prefile", Clean(body), ct).ConfigureAwait(false);
        return PhpVmsParser.ParseId(json) ?? throw new AirlineException("Die Airline hat keine PIREP-ID zurückgegeben.");
    }

    public async Task SendPositionsAsync(string pirepId, IReadOnlyList<PirepPosition> positions, CancellationToken ct = default)
    {
        const int chunk = 100;
        for (int i = 0; i < positions.Count; i += chunk)
        {
            var items = positions.Skip(i).Take(chunk).Select(x => new Dictionary<string, object?>
            {
                ["lat"] = Math.Round(x.Lat, 6),
                ["lon"] = Math.Round(x.Lon, 6),
                ["altitude"] = Math.Round(x.AltitudeFt),
                ["altitude_msl"] = Math.Round(x.AltitudeFt),
                ["heading"] = Math.Round(x.HeadingDeg) % 360,
                ["vs"] = Math.Round(x.VsFpm),
                ["gs"] = Math.Round(x.GsKt),
                ["sim_time"] = Iso(x.Time),
                // Zeitpunkt der Messung – sonst tragen alle nachträglich gesendeten Punkte dieselbe Eingangszeit
                // und Karten, die danach sortieren, verbinden sie in falscher Reihenfolge.
                ["created_at"] = Iso(x.Time),
                ["altitude_agl"] = x.AglFt is { } agl ? Math.Round(Math.Max(0, agl)) : null,
            }).Select(Clean).ToList();
            var body = new Dictionary<string, object?> { ["positions"] = items };
            var path = $"api/pireps/{Uri.EscapeDataString(pirepId)}/acars/position";
            try
            {
                await SendAsync(HttpMethod.Post, path, body, ct).ConfigureAwait(false);
            }
            catch (AirlineException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Ältere Versionen: Mehrzahl im Pfad
                await SendAsync(HttpMethod.Post, path + "s", body, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Zusatzfelder am PIREP setzen (POST /api/pireps/{id}/fields, { fields: { Name: Wert } }).</summary>
    public async Task PostFieldsAsync(string pirepId, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
    {
        if (fields.Count == 0) return;
        var body = new Dictionary<string, object?> { ["fields"] = fields };
        await SendAsync(HttpMethod.Post, $"api/pireps/{Uri.EscapeDataString(pirepId)}/fields", body, ct).ConfigureAwait(false);
    }

    public async Task FileAsync(string pirepId, PirepFile f, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["distance"] = Math.Round(f.DistanceNm, 1),
            ["flight_time"] = f.FlightTimeMin,
            ["block_time"] = f.BlockTimeMin,
            ["landing_rate"] = f.LandingRateFpm is { } lr ? Math.Round(lr) : null,
            ["block_off_time"] = Iso(f.BlockOffTime),
            ["block_on_time"] = Iso(f.BlockOnTime),
            ["level"] = f.Level,
            ["route"] = f.Route,
            ["notes"] = f.Notes,
            ["source_name"] = f.SourceName,
        };
        await SendAsync(HttpMethod.Post, $"api/pireps/{Uri.EscapeDataString(pirepId)}/file", Clean(body), ct).ConfigureAwait(false);
    }

    public async Task CancelAsync(string pirepId, CancellationToken ct = default)
    {
        var path = $"api/pireps/{Uri.EscapeDataString(pirepId)}/cancel";
        try
        {
            await SendAsync(HttpMethod.Put, path, new Dictionary<string, object?>(), ct).ConfigureAwait(false);
        }
        catch (AirlineException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            await SendAsync(HttpMethod.Delete, path, null, ct).ConfigureAwait(false);
        }
    }

    public Uri PirepPageUrl(string pirepId) => new(_base, $"pireps/{Uri.EscapeDataString(pirepId)}");

    private static string? Iso(DateTimeOffset? t) =>
        t?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Clean(Dictionary<string, object?> body) =>
        body.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);

    private Task<string> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, ct);

    private async Task<string> SendAsync(HttpMethod method, string path, object? jsonBody, CancellationToken ct)
    {
        if (_apiKey.Length == 0) throw new AirlineException("Kein API-Schlüssel eingetragen.");

        using var request = new HttpRequestMessage(method, new Uri(_base, path));
        if (jsonBody is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(jsonBody), Encoding.UTF8, "application/json");
        request.Headers.Add("X-API-Key", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(AppInfo.UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AirlineException($"{_base.Host} ist nicht erreichbar: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AirlineException($"{_base.Host} hat nicht rechtzeitig geantwortet.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var status = response.StatusCode;
            var detail = PhpVmsParser.ParseError(body);
            if (status == HttpStatusCode.Unauthorized)
                throw new AirlineException("API-Schlüssel wurde abgelehnt. Bitte im Airline-Profil prüfen.", status);
            if (status == HttpStatusCode.NotFound)
                throw new AirlineException(detail ?? $"Nicht gefunden: {path}", status);
            if (!response.IsSuccessStatusCode)
                throw new AirlineException(
                    $"Die Airline meldet{(detail is null ? $" HTTP {(int)status}" : ": " + detail)}", status);
            if (!body.TrimStart().StartsWith('{') && !body.TrimStart().StartsWith('['))
                throw new AirlineException($"{_base.Host} liefert keine Schnittstellen-Daten (evtl. falsche Adresse).");
            return body;
        }
    }
}

/// <summary>Liest die JSON-Antworten von phpVMS 7 tolerant (Werte mal als Zahl, mal als Text, Objekte teils leer).</summary>
public static class PhpVmsParser
{
    public static PilotInfo ParseUser(string json)
    {
        using var doc = Parse(json);
        var u = Data(doc.RootElement);
        return new PilotInfo(
            Str(u, "ident") ?? Str(u, "pilot_id"),
            Str(u, "name") ?? Str(u, "name_private"),
            Str(u, "airline", "icao"),
            Str(u, "curr_airport_id") ?? Str(u, "curr_airport", "icao") ?? Str(u, "curr_airport"),
            Str(u, "rank", "name") ?? Str(u, "rank"),
            Str(u, "airline_id") ?? Str(u, "airline", "id"));
    }

    public static IReadOnlyList<AirlineBooking> ParseBids(string json)
    {
        using var doc = Parse(json);
        var data = Data(doc.RootElement);
        var list = new List<AirlineBooking>();
        if (data.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in data.EnumerateArray())
        {
            // Je nach Version: Liste von Bids (mit "flight") oder direkt Liste von Flügen.
            bool isBid = item.TryGetProperty("flight", out var flight) && flight.ValueKind == JsonValueKind.Object;
            if (!isBid) flight = item;

            var dep = Str(flight, "dpt_airport_id") ?? Str(flight, "dpt_airport", "icao");
            var arr = Str(flight, "arr_airport_id") ?? Str(flight, "arr_airport", "icao");
            var flightId = Str(flight, "id") ?? Str(item, "flight_id");
            if (dep is null || arr is null || flightId is null) continue;

            var airline = Str(flight, "airline", "icao");
            var number = Str(flight, "flight_number");
            var ident = Str(flight, "ident") ?? $"{airline}{number}";

            var types = new List<string>();
            if (TryGet(flight, out var subfleets, "subfleets") && subfleets.ValueKind == JsonValueKind.Array)
            {
                foreach (var sf in subfleets.EnumerateArray())
                {
                    var t = Str(sf, "type") ?? Str(sf, "name");
                    if (t is not null && !types.Contains(t)) types.Add(t);
                }
            }

            double? distance = Num(flight, "distance", "nmi") ?? Num(flight, "distance");
            list.Add(new AirlineBooking(
                isBid ? Str(item, "id") : null,
                flightId,
                ident,
                airline,
                Str(flight, "airline_id") ?? Str(flight, "airline", "id"),
                number,
                Str(flight, "route_code"),
                Str(flight, "route_leg"),
                dep.ToUpperInvariant(),
                arr.ToUpperInvariant(),
                Str(flight, "route"),
                (int?)Num(flight, "level"),
                distance,
                (int?)Num(flight, "flight_time"),
                types,
                Str(flight, "simbrief", "id") ?? Str(item, "simbrief", "id"),
                // Bei der Buchung festgelegtes Flugzeug (phpVMS-Bids können eins tragen)
                isBid ? Str(item, "aircraft_id") ?? Str(item, "aircraft", "id") ?? Str(item, "simbrief", "aircraft_id") : null,
                isBid ? Str(item, "aircraft", "registration") : null,
                isBid ? Str(item, "aircraft", "icao") : null));
        }
        return list;
    }

    public static IReadOnlyList<AirlineAircraft> ParseFleet(string json) => ParseFleetPage(json).Aircraft;

    /// <summary>
    /// Flugzeuge einer Flotten-Antwort. Versteht Unterflotten mit "aircraft" (Liste oder { data: [...] })
    /// und Listen, die direkt aus Flugzeugen bestehen. LastPage aus meta.last_page bzw. last_page.
    /// </summary>
    public static (IReadOnlyList<AirlineAircraft> Aircraft, int Subfleets, int? LastPage) ParseFleetPage(string json)
    {
        using var doc = Parse(json);
        var root = doc.RootElement;
        var data = Data(root);
        var list = new List<AirlineAircraft>();
        int? lastPage = (int?)(Num(root, "meta", "last_page") ?? Num(root, "last_page"));
        if (data.ValueKind != JsonValueKind.Array) return (list, 0, lastPage);

        int subfleets = 0;
        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("registration", out _))
            {
                // Direkt ein Flugzeug
                var a = ToAircraft(item, Str(item, "subfleet", "type"), Str(item, "subfleet", "name"));
                if (a is not null) list.Add(a);
                continue;
            }

            subfleets++;
            var type = Str(item, "type");
            var name = Str(item, "name");
            if (!TryGet(item, out var aircraft, "aircraft")) continue;
            aircraft = Data(aircraft);
            if (aircraft.ValueKind != JsonValueKind.Array) continue;
            foreach (var el in aircraft.EnumerateArray())
            {
                var a = ToAircraft(el, type, name);
                if (a is not null) list.Add(a);
            }
        }
        return (list, subfleets, lastPage);
    }

    public static AirlineAircraft? ParseAircraft(string json)
    {
        using var doc = Parse(json);
        var a = Data(doc.RootElement);
        return a.ValueKind == JsonValueKind.Object
            ? ToAircraft(a, Str(a, "subfleet", "type"), Str(a, "subfleet", "name"))
            : null;
    }

    private static AirlineAircraft? ToAircraft(JsonElement a, string? subfleetType, string? subfleetName)
    {
        var id = Str(a, "id");
        if (id is null) return null;
        return new AirlineAircraft(id, Str(a, "registration") ?? Str(a, "name") ?? id, Str(a, "icao"),
            subfleetType, subfleetName,
            (Str(a, "airport_id") ?? Str(a, "airport", "icao"))?.ToUpperInvariant(), Str(a, "status"),
            (int?)Num(a, "state"));
    }

    public static string? ParseId(string json)
    {
        using var doc = Parse(json);
        return Str(Data(doc.RootElement), "id");
    }

    /// <summary>Fehlermeldung aus einer phpVMS-Antwort ({ "error": { "message": … } }, { "message": … } oder Validierungsfehler).</summary>
    public static string? ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var msg = Str(root, "error", "message") ?? Str(root, "message") ?? Str(root, "error") ?? Str(root, "details");
            if (TryGet(root, out var errors, "errors") && errors.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var field in errors.EnumerateObject())
                {
                    var text = field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0
                        ? field.Value[0].ToString()
                        : field.Value.ToString();
                    parts.Add($"{field.Name}: {text}");
                }
                if (parts.Count > 0) msg = (msg is null ? "" : msg + " – ") + string.Join("; ", parts);
            }
            return msg;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AirlineException("Antwort der Airline konnte nicht gelesen werden.", ex);
        }
    }

    private static JsonElement Data(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) ? data : root;

    private static bool TryGet(JsonElement e, out JsonElement result, params string[] path)
    {
        result = e;
        foreach (var name in path)
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(name, out result))
                return false;
        }
        return true;
    }

    private static string? Str(JsonElement e, params string[] path)
    {
        if (!TryGet(e, out var v, path)) return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static double? Num(JsonElement e, params string[] path) =>
        double.TryParse(Str(e, path), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
