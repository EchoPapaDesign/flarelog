using System.Net;
using System.Text;
using System.Text.Json;

namespace AeroflyAcars.Runtime;

/// <summary>
/// Kleiner lokaler Webserver (nur localhost) für das Display.
/// GET  /                  → Display-Seite
/// GET  /api/state         → aktueller Zustand als JSON
/// POST /api/settings      → Einstellungen ändern ({ simBriefUser, voice, loadSimBriefOnStart, language })
/// POST /api/simbrief      → SimBrief-Plan laden ({ user })
/// POST /api/pirep-text    → Flugdaten als Text für manuelle PIREP-Formulare ({ ok, message, text })
/// POST /api/weather      → METAR und TAF ({ icao }) – Antwort mit data { icao, metar, summary, taf }
/// POST /api/conditions    → echte Zeit / Abflug-Wetter für Aerofly ein/aus ({ realTime, realWeather })
/// POST /api/finish        → Bericht jetzt speichern
/// POST /api/newflight     → neuen Flug beginnen
/// POST /api/open-reports  → Berichtsordner im Explorer öffnen
/// POST /api/voice-test    → Probeansage
/// POST /api/cabin-test    → Kabinenansage zur Probe (Begrüßung)
/// POST /api/open-sounds   → Ordner für eigene Kabinenansagen öffnen
/// POST /api/changelog     → Änderungsliste („Was ist neu?“) in der eingestellten Sprache
/// POST /api/news-seen     → „Was ist neu?“ für diese Version gesehen
/// POST /api/quit          → Flarelog beenden (nur Mac-Version, siehe AcarsEngine.QuitRequested)
/// POST /api/airline/connect → Airline-Adresse/Schlüssel speichern, anmelden, Buchungen laden ({ url, apiKey })
/// POST /api/airline/refresh → Buchungen neu laden
/// POST /api/airline/select  → Buchung für den Flug auswählen ({ flightId })
/// POST /api/airline/fleet   → Flotte laden
/// POST /api/airline/submit  → PIREP einreichen ({ aircraftId, notes })
/// POST-Aufrufe brauchen den Header "X-Acars: 1". Fremde Webseiten können den nicht setzen,
/// ohne dass der Browser vorher nachfragt (CORS) – und darauf antwortet dieser Server nicht.
/// </summary>
public sealed class LiveMapServer : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpListener _listener = new();
    private readonly AcarsEngine _engine;

    public LiveMapServer(int port, AcarsEngine engine)
    {
        Url = $"http://localhost:{port}/";
        _listener.Prefixes.Add(Url);
        _engine = engine;
    }

    public string Url { get; }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (!_listener.IsListening) break;
                await Task.Delay(100).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var request = ctx.Request;
        var response = ctx.Response;
        try
        {
            var path = request.Url?.AbsolutePath ?? "/";
            int status = 200;
            string contentType = "application/json; charset=utf-8";
            byte[] body;

            if (request.HttpMethod == "GET" && (path == "/" || path == "/index.html"))
            {
                contentType = "text/html; charset=utf-8";
                body = Encoding.UTF8.GetBytes(MapPage.Html.Replace("__LANG__", AeroflyAcars.Core.Lang.Code));
            }
            else if (request.HttpMethod == "GET" && path == "/api/state")
            {
                body = Serialize(_engine.GetState());
            }
            else if (request.HttpMethod == "POST" && path.StartsWith("/api/", StringComparison.Ordinal))
            {
                if (request.Headers["X-Acars"] != "1")
                {
                    status = 403;
                    body = Serialize(new ActionResult(false, "Nicht erlaubt."));
                }
                else
                {
                    string text;
                    using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                        text = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var result = await DispatchAsync(path, string.IsNullOrWhiteSpace(text) ? "{}" : text).ConfigureAwait(false);
                    if (result is null) status = 404;
                    body = Serialize(result ?? new ActionResult(false, "Unbekannter Befehl."));
                }
            }
            else
            {
                status = 404;
                contentType = "text/plain; charset=utf-8";
                body = Encoding.UTF8.GetBytes("Nicht gefunden");
            }

            response.StatusCode = status;
            response.ContentType = contentType;
            response.Headers["Cache-Control"] = "no-store";
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Browser hat die Verbindung geschlossen o. Ä. – für das Display unkritisch.
        }
        finally
        {
            try { response.Close(); } catch (Exception) { /* ignorieren */ }
        }
    }

    private async Task<ActionResult?> DispatchAsync(string path, string json)
    {
        try
        {
            return path switch
            {
                "/api/settings" => _engine.UpdateSettings(
                    JsonSerializer.Deserialize<SettingsUpdate>(json, Json) ?? new SettingsUpdate(null, null, null)),
                "/api/airline/connect" => await _engine.ConnectAirlineAsync(
                    JsonSerializer.Deserialize<AirlineConnectRequest>(json, Json) ?? new AirlineConnectRequest(null, null)).ConfigureAwait(false),
                "/api/airline/refresh" => await _engine.RefreshAirlineAsync().ConfigureAwait(false),
                "/api/airline/select" => _engine.SelectBooking(
                    JsonSerializer.Deserialize<BookingSelectRequest>(json, Json)?.FlightId),
                "/api/airline/fleet" => await _engine.LoadFleetAsync().ConfigureAwait(false),
                "/api/airline/live" => _engine.SetLiveTracking(JsonSerializer.Deserialize<LiveRequest>(json, Json)?.On ?? true),
                "/api/airline/submit" => await _engine.SubmitPirepAsync(
                    JsonSerializer.Deserialize<PirepSubmitRequest>(json, Json) ?? new PirepSubmitRequest(null, null)).ConfigureAwait(false),
                "/api/simbrief" => await _engine.LoadSimBriefAsync(
                    JsonSerializer.Deserialize<SimBriefRequest>(json, Json)?.User).ConfigureAwait(false),
                "/api/pirep-text" => _engine.PirepText(),
                "/api/weather" => await _engine.FetchWeatherAsync(
                    JsonSerializer.Deserialize<WeatherRequest>(json, Json)?.Icao).ConfigureAwait(false),
                "/api/conditions" => await _engine.UpdateConditionsAsync(
                    JsonSerializer.Deserialize<ConditionsRequest>(json, Json) ?? new ConditionsRequest(null, null)).ConfigureAwait(false),
                "/api/finish" => _engine.FinishFlight(),
                "/api/newflight" => _engine.NewFlight(),
                "/api/open-reports" => _engine.OpenReportFolder(),
                "/api/voice-test" => _engine.VoiceTest(),
                "/api/cabin-test" => _engine.CabinTest(),
                "/api/changelog" => _engine.Changelog(),
                "/api/news-seen" => _engine.NewsSeen(),
                "/api/open-sounds" => _engine.OpenSoundFolder(),
                "/api/quit" => _engine.RequestQuit(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return new ActionResult(false, "Ungültige Anfrage.");
        }
    }

    private static byte[] Serialize(object value) => JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), Json);

    public void Dispose()
    {
        try
        {
            if (_listener.IsListening) _listener.Stop();
            _listener.Close();
        }
        catch (Exception)
        {
            // ignorieren
        }
    }
}
