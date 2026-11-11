using System.Diagnostics;
using System.Globalization;
using System.Net;
using AeroflyAcars.Core;
using AeroflyAcars.Core.Airline;
using AeroflyAcars.Core.Airports;
using AeroflyAcars.Core.Reporting;
using AeroflyAcars.Core.Sim;
using AeroflyAcars.Core.SimBrief;
using AeroflyAcars.Core.Tracking;

namespace AeroflyAcars.Runtime;

public sealed record ActionResult(bool Ok, string Message, string? Text = null, object? Data = null);
public sealed record SettingsUpdate(string? SimBriefUser, bool? Voice, bool? LoadSimBriefOnStart,
    string? AirlineUrl = null, string? AirlineApiKey = null, string? Language = null, bool? CabinAnnouncements = null);
public sealed record SimBriefRequest(string? User);
public sealed record WeatherRequest(string? Icao);
public sealed record ConditionsRequest(bool? RealTime, bool? RealWeather, string? WeatherIcao = null);
public sealed record AirlineConnectRequest(string? Url, string? ApiKey);
public sealed record BookingSelectRequest(string? FlightId);
public sealed record LiveRequest(bool On);
public sealed record PirepSubmitRequest(string? AircraftId, string? Notes, string? Registration = null, string? FlightNumber = null);

/// <summary>
/// Das eigentliche ACARS: UDP-Empfang, Auswertung, Berichte, Sprachansage und der Server für das Display.
/// Wird von der Konsole und vom Desktop-Programm gleichermaßen benutzt. Alle Zustände hinter einer Sperre.
/// </summary>
public sealed class AcarsEngine : IDisposable
{
    private const int MaxNotices = 8;

    private readonly object _gate = new();
    private readonly AirportsProxy _airports = new();
    private readonly RunwaysProxy _runwaysProxy = new();
    private readonly List<string> _notices = new();

    private SampleBuilder _builder = new();
    private FlightTracker _tracker;
    private FlightPlan? _plan;
    private SimUdpListener? _listener;
    private LiveMapServer? _server;
    private SpeechAnnouncer? _speech;
    private CabinAnnouncer? _cabin;
    private CabinCues _cabinCues = new();
    private CancellationTokenSource? _cts;
    private DateTimeOffset? _lastReceived;
    private string? _source;
    private bool _reportSaved;
    private string? _lastReportPath;
    private object? _lastCallout;
    private bool _disposed;

    private PilotInfo? _pilot;
    private IReadOnlyList<AirlineBooking> _bookings = Array.Empty<AirlineBooking>();
    private AirlineBooking? _selectedBooking;
    private DateTimeOffset? _bookingsUpdated;
    private IReadOnlyList<AirlineAircraft> _fleet = Array.Empty<AirlineAircraft>();
    private string? _fleetInfo;
    private AeroflyConfig.SelectedAircraft? _aeroflyAircraft;
    private volatile RunwayDatabase? _runways;
    private UpdateInfo? _update;
    private FlightAnalysis? _analysisCache;
    private int _analysisCacheCount = -1;
    private FlightTracker? _analysisCacheTracker;
    private string? _submittedPirepId;
    private string? _submittedUrl;
    private bool _submitting;

    public const string SourceName = AppInfo.Name;
    public static string Version => AppInfo.Version;

    public AcarsEngine(AcarsSettings settings, AirlinePreset? preset = null)
    {
        Settings = settings;
        Settings.ApplyLanguage();
        Preset = preset ?? AirlinePreset.Load();
        // Airline-Edition: Adresse beim ersten Start übernehmen, eine eigene Eingabe des Piloten bleibt.
        if (Preset?.Url is { } presetUrl && string.IsNullOrWhiteSpace(Settings.AirlineUrl))
        {
            Settings.AirlineUrl = presetUrl;
            try { Settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        _tracker = CreateTracker();
    }

    /// <summary>Vorgaben der Airline aus airline.json neben der .exe (Airline-Edition), sonst null.</summary>
    public AirlinePreset? Preset { get; }

    public AcarsSettings Settings { get; }
    public string? DisplayUrl => _server?.Url;
    public FlightPlan? Plan { get { lock (_gate) return _plan; } }

    /// <summary>Hinweise für die Konsole (Flughafenliste geladen, SimBrief, Fehler …).</summary>
    public event Action<string>? Notice;
    public event Action<FlightEvent>? FlightEventRaised;
    public event Action<string>? CalloutSpoken;
    public event Action<FlightReport, string>? ReportSaved;

    /// <summary>
    /// Das Display bittet darum, Flarelog zu beenden (Knopf in den Einstellungen). Nur die Mac-Version meldet sich
    /// dafür an (schließt dann ihr Fenster bzw. beendet sich im Browser-Notbetrieb).
    /// </summary>
    public event Action? QuitRequested;

    private static string L(string de, string en) => Lang.T(de, en);
    private static string LiveTroublePrefix => L("Live-Verbindung gestört: ", "Live connection disrupted: ");

    public ActionResult RequestQuit()
    {
        if (QuitRequested is null) return new ActionResult(false, L("Bitte das Programmfenster schließen.", "Please close the program window."));
        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false);   // Antwort noch an die Seite schicken
            QuitRequested?.Invoke();
        });
        return new ActionResult(true, L("Flarelog wird beendet …", "Flarelog is quitting …"));
    }

    /// <summary>Es wird gerade ein Flug aufgezeichnet, dessen Bericht noch nicht gespeichert ist.</summary>
    public bool FlightInProgress
    {
        get { lock (_gate) return _tracker.Samples.Count > 0 && _tracker.Phase != FlightPhase.Preflight && !_reportSaved; }
    }

    /// <summary>
    /// Startet UDP-Empfang und Display-Server sofort; Flughafenliste und SimBrief laden im Hintergrund.
    /// Wirft eine <see cref="System.Net.Sockets.SocketException"/>, wenn der UDP-Port belegt ist.
    /// </summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new SimUdpListener(Settings.UdpPort);
        _ = _listener.RunAsync(OnMessage, _cts.Token);
        _server = StartServer();
        if (Settings.Voice) _speech = SpeechAnnouncer.Create();
        if (Settings.CabinAnnouncements) _cabin = new CabinAnnouncer();
        _ = Task.Run(LoadInBackgroundAsync);
        _ = Task.Run(() => WatchAeroflyAsync(_cts.Token));
        _ = Task.Run(() => LiveLoopAsync(_cts.Token));
    }

    private LiveMapServer? StartServer()
    {
        if (Settings.DisplayPort <= 0) return null;
        for (int port = Settings.DisplayPort; port < Settings.DisplayPort + 10; port++)
        {
            var server = new LiveMapServer(port, this);
            try
            {
                server.Start();
                return server;
            }
            catch (HttpListenerException)
            {
                server.Dispose();
            }
        }
        AddNotice(L($"Display konnte nicht gestartet werden (Ports {Settings.DisplayPort}–{Settings.DisplayPort + 9} belegt).", $"Display could not be started (ports {Settings.DisplayPort}–{Settings.DisplayPort + 9} in use)."));
        return null;
    }

    private async Task LoadInBackgroundAsync()
    {
        _ = Task.Run(async () =>
        {
            var update = await UpdateChecker.CheckAsync(AppInfo.GitHubRepository, AppInfo.Version).ConfigureAwait(false);
            if (update is null) return;
            lock (_gate) _update = update;
            AddNotice(L($"Neue Version {update.Version} verfügbar.", $"New version {update.Version} available."));
        });

        var airportsTask = AirportDataLoader.LoadAsync();
        if (Settings.LoadSimBriefOnStart && !string.IsNullOrWhiteSpace(Settings.SimBriefUser))
            await LoadSimBriefAsync(Settings.SimBriefUser).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(Settings.AirlineUrl) && Settings.HasAirlineApiKey)
            await RefreshAirlineAsync(announce: true).ConfigureAwait(false);

        var (db, message) = await airportsTask.ConfigureAwait(false);
        _airports.Database = db;
        AddNotice(message);

        var (runways, runwayMessage) = await AirportDataLoader.LoadRunwaysAsync().ConfigureAwait(false);
        _runways = runways;
        _runwaysProxy.Database = runways;
        AddNotice(runwayMessage);
        await ApplyConditionsAsync().ConfigureAwait(false);
    }

    private void OnMessage(SimMessage message)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            _lastReceived = now;
            _source ??= message.Source;

            var sample = _builder.Add(message, now);
            if (sample is null) return;

            _tracker.Add(sample);
            PlayCabin(_cabinCues.OnSample(sample, _tracker.Phase));
            if (_tracker.IsFinished && !_reportSaved)
                SaveReportLocked();
        }
    }

    private FlightTracker CreateTracker()
    {
        var tracker = new FlightTracker(_plan, Settings.DestinationElevationFt, _airports, _runwaysProxy);
        var cues = new CabinCues();
        _cabinCues = cues;
        tracker.EventRaised += e =>
        {
            PlayCabin(cues.OnEvent(e));
            // Beim Losrollen festhalten, welches Flugzeug in Aerofly gewählt ist.
            if (e.PhaseChange && e.Phase == FlightPhase.TaxiOut)
            {
                _aeroflyAircraft = AeroflyConfig.ReadCurrentAircraft() ?? _aeroflyAircraft;
                if (Settings.LiveTracking && Settings.HasAirlineApiKey) _ = Task.Run(StartLiveAsync);
            }
            FlightEventRaised?.Invoke(e);
        };
        tracker.TouchdownDetected += (sample, rate) =>
        {
            var text = LandingCallout.Text(rate);
            if (Settings.Voice) _speech?.Say(text);
            _lastCallout = new
            {
                id = sample.Time.ToUnixTimeMilliseconds(),
                text,
                rate,
                rating = LandingCallout.Rating(rate),
                gsKt = sample.GroundSpeedKt,
            };
            CalloutSpoken?.Invoke(text);
        };
        return tracker;
    }

    private void PlayCabin(IReadOnlyList<string> cues)
    {
        if (!Settings.CabinAnnouncements || _cabin is null) return;
        foreach (var cue in cues) _cabin.Play(cue);
    }

    // ---------- Bedienung (Display-Knöpfe, Konsole) ----------

    public ActionResult FinishFlight()
    {
        lock (_gate)
        {
            if (_reportSaved) return new ActionResult(true, L("Bericht ist bereits gespeichert.", "Report has already been saved."));
            if (_tracker.Samples.Count == 0) return new ActionResult(false, L("Noch keine Flugdaten aufgezeichnet.", "No flight data recorded yet."));
            var path = SaveReportLocked();
            return path is null
                ? new ActionResult(false, L("Bericht konnte nicht gespeichert werden.", "Report could not be saved."))
                : new ActionResult(true, L("Bericht gespeichert: ", "Report saved: ") + Path.GetFileName(path));
        }
    }

    public ActionResult NewFlight()
    {
        lock (_gate)
        {
            string extra = "";
            if (_tracker.Samples.Count > 0 && _tracker.Phase != FlightPhase.Preflight && !_reportSaved)
            {
                SaveReportLocked();
                extra = L(" Der bisherige Flug wurde als Teilbericht gespeichert.", " The previous flight was saved as a partial report.");
            }
            ResetLocked();
            AddNotice(L("Neuer Flug – bereit zur Aufzeichnung.", "New flight – ready to record."));
            return new ActionResult(true, L("Neuer Flug gestartet.", "New flight started.") + extra);
        }
    }

    public async Task<ActionResult> LoadSimBriefAsync(string? user)
    {
        user = string.IsNullOrWhiteSpace(user) ? Settings.SimBriefUser : user.Trim();
        if (string.IsNullOrWhiteSpace(user))
            return new ActionResult(false, L("Kein SimBrief-Benutzer eingetragen.", "No SimBrief user entered."));

        FlightPlan plan;
        try
        {
            plan = await new SimBriefClient().FetchLatestAsync(user).ConfigureAwait(false);
        }
        catch (SimBriefException ex)
        {
            AddNotice($"SimBrief: {ex.Message}");
            return new ActionResult(false, ex.Message);
        }

        string message;
        lock (_gate)
        {
            _plan = plan;
            bool canApplyNow = _tracker.Phase == FlightPhase.Preflight || _tracker.IsFinished || _reportSaved;
            if (canApplyNow)
            {
                ResetLocked();
                message = $"SimBrief: {plan.Summary}";
            }
            else
            {
                message = $"SimBrief: {plan.Summary}" + L(" – gilt ab dem nächsten Flug", " – applies from the next flight");
            }
        }
        AddNotice(message);
        _ = ApplyConditionsAsync();
        return new ActionResult(true, message);
    }

    // ---------- Zeit und Wetter für Aerofly ----------

    private string? _conditionsStatus;
    private bool _conditionsPending;
    private bool _aeroflyWasRunning;
    private DateTimeOffset _aeroflyLastRunning = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _conditionsLock = new(1, 1);

    public async Task<ActionResult> UpdateConditionsAsync(ConditionsRequest request)
    {
        if (request.RealTime is { } rt) Settings.RealTime = rt;
        if (request.RealWeather is { } rw) Settings.RealWeather = rw;
        if (request.WeatherIcao is { } wi)
        {
            var icao = new string(wi.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
            Settings.WeatherIcao = icao.Length is >= 3 and <= 6 ? icao : null;
        }
        try { Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* gilt trotzdem für diese Sitzung */ }

        if (!Settings.RealTime && !Settings.RealWeather)
        {
            lock (_gate) { _conditionsStatus = null; _conditionsPending = false; }
            return new ActionResult(true, L("Zeit und Wetter stellst du wieder in Aerofly ein.", "Time and weather are set in Aerofly again."));
        }
        return await ApplyConditionsAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Abflugplatz für das Wetter: SimBrief-Plan, sonst Buchung, sonst selbst eingetragen, sonst der Platz, an dem das
    /// Flugzeug gerade steht bzw. zuletzt stand.
    /// </summary>
    private (string? Icao, string Source) DepartureForWeatherLocked()
    {
        if (_plan?.OriginIcao is { Length: > 0 } p) return (p, L("SimBrief-Plan", "SimBrief plan"));
        if (_selectedBooking?.Departure is { Length: > 0 } b) return (b, L("Buchung", "booking"));
        if (!string.IsNullOrWhiteSpace(Settings.WeatherIcao)) return (Settings.WeatherIcao, L("eingetragen", "entered"));
        if (_tracker.LastSample is { } s && _airports.Nearest(s.Latitude, s.Longitude, 3)?.Ident is { } here) return (here, L("Standort", "current position"));
        if (!string.IsNullOrWhiteSpace(Settings.LastAirport)) return (Settings.LastAirport, L("letzter Standort", "last position"));
        return (null, "");
    }

    /// <summary>
    /// Schreibt echte Zeit und/oder das Abflug-Wetter in Aerofly, sofern eingeschaltet. Läuft Aerofly, wird es nach dem
    /// Beenden nachgeholt (für den nächsten Start) – im laufenden Aerofly lässt sich nichts ändern.
    /// </summary>
    private async Task<ActionResult> ApplyConditionsAsync()
    {
        if (!Settings.RealTime && !Settings.RealWeather) return new ActionResult(true, "");
        await _conditionsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (AeroflyConditions.IsAeroflyRunning())
            {
                lock (_gate)
                {
                    _conditionsPending = true;
                    _conditionsStatus = L("Aerofly läuft – wird beim nächsten Aerofly-Start übernommen.", "Aerofly is running – will be applied the next time Aerofly starts.");
                }
                return new ActionResult(true, L("Aerofly läuft – Zeit/Wetter werden nach dem Beenden für den nächsten Start eingetragen.", "Aerofly is running – time/weather will be set for the next start after you quit Aerofly."));
            }

            string? icao;
            FlightPlan? plan;
            lock (_gate) { icao = DepartureForWeatherLocked().Icao; plan = _plan; }

            Metar? metar = null;
            string? weatherProblem = null;
            if (Settings.RealWeather)
            {
                if (icao is null) weatherProblem = L("Abflugplatz unbekannt – bitte ICAO eintragen", "departure airport unknown – please enter ICAO");
                else
                {
                    // Aktueller METAR; der aus dem SimBrief-Plan ist nur Ersatz (er kann Stunden alt sein).
                    var raw = await new MetarClient().FetchAsync(icao).ConfigureAwait(false);
                    if (raw is null && plan is not null && plan.OriginIcao.Equals(icao, StringComparison.OrdinalIgnoreCase))
                        raw = plan.OriginMetar;
                    metar = Metar.Parse(raw);
                    if (metar is null) weatherProblem = L($"kein METAR für {icao}", $"no METAR for {icao}");
                }
            }

            var now = DateTimeOffset.UtcNow;
            int changed = AeroflyConditions.WriteToMainMcf(Settings.RealTime ? now : null, metar);

            var parts = new List<string>();
            if (Settings.RealTime) parts.Add(L("Zeit", "Time") + $" {now:HH:mm}z");
            if (metar is not null) parts.Add(L("Wetter", "Weather") + $" {icao}: {metar.Summary}");
            if (weatherProblem is not null) parts.Add(L("Wetter nicht übernommen", "Weather not applied") + $" ({weatherProblem})");
            string text = changed > 0 ? "✓ " + string.Join(" · ", parts) : L("Aerofly-Einstellungen nicht erkannt – nichts geändert.", "Aerofly settings not recognised – nothing changed.");
            lock (_gate) { _conditionsStatus = text; _conditionsPending = false; }
            if (changed > 0) AddNotice(L("An Aerofly übergeben: ", "Passed to Aerofly: ") + string.Join(" · ", parts));
            return new ActionResult(changed > 0, text);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            lock (_gate) _conditionsStatus = L("Nicht übernommen: ", "Not applied: ") + ex.Message;
            return new ActionResult(false, L("Zeit/Wetter nicht übernommen: ", "Time/weather not applied: ") + ex.Message);
        }
        finally
        {
            _conditionsLock.Release();
        }
    }

    /// <summary>Holt Zeit/Wetter nach, sobald Aerofly beendet wurde (prüft alle 3 s, nur wenn eingeschaltet).</summary>
    private async Task WatchAeroflyAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            RefreshSimAircraft();
            if (!Settings.RealTime && !Settings.RealWeather) continue;

            var now = DateTimeOffset.UtcNow;
            if (AeroflyConditions.IsAeroflyRunning())
            {
                _aeroflyWasRunning = true;
                _aeroflyLastRunning = now;
                continue;
            }
            bool pending;
            lock (_gate) pending = _conditionsPending;
            // Aerofly schreibt seine Einstellungen beim Beenden – ein paar Sekunden Abstand halten.
            if ((_aeroflyWasRunning || pending) && now - _aeroflyLastRunning >= TimeSpan.FromSeconds(5))
            {
                _aeroflyWasRunning = false;
                await ApplyConditionsAsync().ConfigureAwait(false);
            }
        }
    }

    private AeroflyConfig.SelectedAircraft? _simAircraftNow;
    private DateTime _mainMcfWriteTime;
    private DateTime _logWriteTime;

    /// <summary>Aktuell in Aerofly gewähltes Flugzeug für die Statusleiste (liest main.mcf nur, wenn sie sich geändert hat).</summary>
    private void RefreshSimAircraft()
    {
        RememberStandingAirport();
        try
        {
            var path = AeroflyConfig.DefaultPath;
            var log = AeroflyConfig.DefaultLogPath;
            var write = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
            var logWrite = File.Exists(log) ? File.GetLastWriteTimeUtc(log) : default;
            if (write == _mainMcfWriteTime && logWrite == _logWriteTime) return;
            _mainMcfWriteTime = write;
            _logWriteTime = logWrite;
            var selected = AeroflyConfig.ReadCurrentAircraft(path, log);
            lock (_gate)
            {
                _simAircraftNow = selected ?? _simAircraftNow;
                // Vor dem Losrollen gilt jeweils die aktuelle Auswahl für den Abgleich mit der Buchung.
                if (_tracker.BlockOff is null && selected is not null) _aeroflyAircraft = selected;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>METAR und TAF für das Wetter-Fenster (ohne Angabe: Abflugplatz).</summary>
    public async Task<ActionResult> FetchWeatherAsync(string? icao)
    {
        string? station = string.IsNullOrWhiteSpace(icao)
            ? null
            : new string(icao.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (station is null) lock (_gate) station = DepartureForWeatherLocked().Icao;
        if (string.IsNullOrEmpty(station) || station.Length is < 3 or > 6)
            return new ActionResult(false, L("Bitte einen ICAO-Code eingeben, z. B. EDDF.", "Please enter an ICAO code, e.g. EGLL."));

        var client = new MetarClient();
        var metarTask = client.FetchAsync(station);
        var tafTask = client.FetchTafAsync(station);
        await Task.WhenAll(metarTask, tafTask).ConfigureAwait(false);
        var metarRaw = metarTask.Result;
        var taf = tafTask.Result;
        if (metarRaw is null && taf is null)
            return new ActionResult(false, L($"Kein METAR/TAF für {station} – Code prüfen oder Wetterdienst nicht erreichbar.", $"No METAR/TAF for {station} – check the code, or the weather service is unreachable."));

        var metar = Metar.Parse(metarRaw);
        var data = new
        {
            icao = station,
            name = _airports.Database?.Find(station)?.Name,
            metar = metarRaw,
            summary = metar?.Summary,
            taf,
            fetched = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
        };
        return new ActionResult(true, L($"Wetter {station} abgerufen.", $"Weather for {station} retrieved."), null, data);
    }

    private DateTimeOffset _lastAirportCheck;

    /// <summary>Merkt sich alle 30 s den Platz, an dem das Flugzeug steht (für das Wetter beim nächsten Start).</summary>
    private void RememberStandingAirport()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastAirportCheck < TimeSpan.FromSeconds(30)) return;
        _lastAirportCheck = now;
        string? here;
        lock (_gate)
        {
            if (_tracker.LastSample is not { GroundSpeedKt: < 2 } s || now - s.Time > TimeSpan.FromSeconds(30)) return;
            here = _airports.Nearest(s.Latitude, s.Longitude, 3)?.Ident;
        }
        if (here is null || here.Equals(Settings.LastAirport, StringComparison.OrdinalIgnoreCase)) return;
        Settings.LastAirport = here;
        // Neuer Standort: Ein früher eingetragener Wetter-Flugplatz gehört zum alten Flug und gilt nicht mehr.
        if (!string.IsNullOrWhiteSpace(Settings.WeatherIcao) && !here.Equals(Settings.WeatherIcao, StringComparison.OrdinalIgnoreCase))
            Settings.WeatherIcao = null;
        try { Settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------- Live-Tracking (phpVMS) ----------

    private string? _livePirepId;
    private string? _liveAircraftId;
    private readonly HashSet<DateTimeOffset> _liveSent = new();
    private string? _liveStatusCode;
    private string? _liveInfo;
    private int _liveStarting;

    public ActionResult SetLiveTracking(bool on)
    {
        Settings.LiveTracking = on;
        try { Settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new ActionResult(true, on ? L("Live-Tracking an: Der Flug erscheint ab dem Losrollen bei der Airline.", "Live tracking on: the flight appears at the airline from taxi-out.")
                                         : L("Live-Tracking aus: Der Flug wird erst nach der Landung gesendet.", "Live tracking off: the flight is sent after landing."));
    }

    /// <summary>Beim Losrollen: Flug bei der Airline anmelden, damit er auf ihrer Live-Karte erscheint.</summary>
    private async Task StartLiveAsync()
    {
        if (Interlocked.Exchange(ref _liveStarting, 1) == 1) return;
        try
        {
            var client = CreateAirlineClient(out _);
            if (client is null) return;

            string? dep, arr, airlineId, flightNumber, bookedAircraft, reg, typeHint, flightId, routeCode, routeLeg, route;
            int? level, plannedMin;
            double? plannedNm;
            DateTimeOffset? blockOff;
            lock (_gate)
            {
                if (_livePirepId is not null || _pilot is null) return;
                var t = _tracker;
                var b = _selectedBooking;
                var plan = _plan ?? t.Plan;
                dep = b?.Departure ?? plan?.OriginIcao ?? t.DepartureAirport?.Ident;
                arr = b?.Arrival ?? plan?.DestinationIcao;
                airlineId = b?.AirlineId ?? _pilot.AirlineId;
                flightNumber = b is not null
                    ? b.FlightNumber ?? new string(b.ShortIdent.Where(char.IsDigit).ToArray())
                    : CleanFlightNumber(plan?.FlightNumber);
                bookedAircraft = b?.AircraftId;
                reg = plan?.AircraftRegistration;
                typeHint = b?.AircraftIcao ?? plan?.AircraftType;
                flightId = b?.FlightId;
                routeCode = b?.RouteCode;
                routeLeg = b?.RouteLeg;
                bool planFits = plan is not null && dep is not null && arr is not null &&
                                plan.OriginIcao.Equals(dep, StringComparison.OrdinalIgnoreCase) &&
                                plan.DestinationIcao.Equals(arr, StringComparison.OrdinalIgnoreCase);
                route = (planFits ? plan!.Route : null) ?? b?.Route;
                level = planFits && plan!.CruiseAltitudeFt is { } c ? (int)Math.Round(c / 100) : null;
                plannedNm = b?.DistanceNm ?? (planFits ? plan!.RouteDistanceNm : null);
                plannedMin = b?.FlightTimeMin;
                blockOff = t.BlockOff;
            }

            string? missing = dep is null ? L("Abflugplatz", "departure airport") : arr is null ? L("Ziel (Buchung oder SimBrief-Plan)", "destination (booking or SimBrief plan)")
                : airlineId is null ? L("Airline-ID", "airline ID") : string.IsNullOrEmpty(flightNumber) ? L("Flugnummer (Buchung oder SimBrief-Plan)", "flight number (booking or SimBrief plan)") : null;
            if (missing is not null)
            {
                SetLiveInfo(L($"Live-Tracking nicht gestartet – es fehlt: {missing}. Der Flug wird nach der Landung gesendet.", $"Live tracking not started – missing: {missing}. The flight will be sent after landing."));
                return;
            }

            // Flugzeug: aus der Buchung, sonst SimBrief-Kennzeichen, sonst passender Typ am Abflugort.
            string? aircraftId = bookedAircraft;
            try
            {
                if (aircraftId is null && !string.IsNullOrWhiteSpace(reg))
                    aircraftId = (await client.FindAircraftByRegistrationAsync(reg).ConfigureAwait(false))?.Id;
                if (aircraftId is null && typeHint is not null)
                {
                    var (fleet, _) = await client.GetFleetAsync().ConfigureAwait(false);
                    aircraftId = fleet.FirstOrDefault(a =>
                        string.Equals(a.AirportId, dep, StringComparison.OrdinalIgnoreCase) &&
                        (string.Equals(a.Icao, typeHint, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a.SubfleetType, typeHint, StringComparison.OrdinalIgnoreCase)))?.Id;
                }
            }
            catch (AirlineException) { /* dann ohne Flugzeug – siehe unten */ }
            if (aircraftId is null)
            {
                SetLiveInfo(L("Live-Tracking nicht gestartet – kein Flugzeug bestimmbar (Buchung mit Flugzeug oder SimBrief-Kennzeichen). " +
                            "Der Flug wird nach der Landung gesendet.",
                            "Live tracking not started – no aircraft could be determined (booking with aircraft or SimBrief registration). " +
                            "The flight will be sent after landing."));
                return;
            }

            var prefile = new PirepPrefile(airlineId!, aircraftId, flightId, flightNumber!, routeCode, routeLeg, dep!, arr!, level,
                route, plannedNm, plannedMin, blockOff, SourceName);
            try
            {
                var id = await client.PrefileAsync(prefile).ConfigureAwait(false);
                lock (_gate)
                {
                    _livePirepId = id;
                    _liveAircraftId = aircraftId;
                    _liveInfo = L("Live bei der Airline: ", "Live at the airline: ") + $"{dep} → {arr}";
                }
                AddNotice(L($"Live-Tracking gestartet: {dep} → {arr} erscheint auf der Karte der Airline.", $"Live tracking started: {dep} → {arr} appears on the airline's map."));
                await LiveTickAsync().ConfigureAwait(false);
            }
            catch (AirlineException ex)
            {
                SetLiveInfo(L($"Live-Tracking nicht gestartet: {ex.Message}. Der Flug wird nach der Landung gesendet.", $"Live tracking not started: {ex.Message}. The flight will be sent after landing."));
            }
        }
        finally
        {
            Interlocked.Exchange(ref _liveStarting, 0);
        }
    }

    private void SetLiveInfo(string text)
    {
        lock (_gate) _liveInfo = text;
        AddNotice(text);
    }

    private async Task LiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            try { await LiveTickAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is AirlineException or HttpRequestException or TaskCanceledException)
            {
                lock (_gate) _liveInfo = LiveTroublePrefix + $"{ex.Message} – " + L("wird erneut versucht.", "retrying.");
            }
        }
    }

    /// <summary>Neue Positionen (alle 10 s) und die Flugphase an den laufenden PIREP senden.</summary>
    private async Task LiveTickAsync()
    {
        string? id;
        List<PirepPosition> batch;
        string? code;
        lock (_gate)
        {
            id = _livePirepId;
            if (id is null || _submittedPirepId is not null || _submitting) return;
            var t = _tracker;
            batch = new List<PirepPosition>();
            DateTimeOffset? last = _liveSent.Count > 0 ? _liveSent.Max() : null;
            double? groundRef = t.DepartureElevationFt;
            foreach (var s in t.Samples)
            {
                if (last is not null && s.Time - last.Value < TimeSpan.FromSeconds(10)) continue;
                batch.Add(new PirepPosition(s.Latitude, s.Longitude, s.AltitudeFt, s.HeadingDeg, s.VerticalSpeedFpm,
                    s.GroundSpeedKt, s.Time, groundRef is { } g ? s.AltitudeFt - g : null));
                last = s.Time;
            }
            code = t.Phase switch
            {
                FlightPhase.TaxiOut => PirepStatus.Taxi,
                FlightPhase.TakeoffRoll => PirepStatus.Takeoff,
                FlightPhase.Climb => PirepStatus.InitialClimb,
                FlightPhase.Cruise or FlightPhase.Descent => PirepStatus.Enroute,
                FlightPhase.Approach => PirepStatus.Approach,
                FlightPhase.Landing => PirepStatus.Landing,
                FlightPhase.TaxiIn => PirepStatus.Landed,
                FlightPhase.Arrived => PirepStatus.Arrived,
                _ => null,
            };
            if (code == _liveStatusCode) code = null;
        }

        var client = CreateAirlineClient(out _);
        if (client is null) return;
        if (batch.Count > 0)
        {
            await client.SendPositionsAsync(id, batch).ConfigureAwait(false);
            lock (_gate) foreach (var p in batch) _liveSent.Add(p.Time);
        }
        if (code is not null)
        {
            await client.UpdateAsync(id, new Dictionary<string, object?> { ["status"] = code }).ConfigureAwait(false);
            lock (_gate) _liveStatusCode = code;
        }
        lock (_gate)
            if (_livePirepId == id && _liveInfo?.StartsWith(LiveTroublePrefix, StringComparison.Ordinal) == true)
                _liveInfo = L("Live bei der Airline – Verbindung wieder da.", "Live at the airline – connection restored.");
    }

    // ---------- Airline (phpVMS) – nur lesend ----------

    /// <summary>Speichert Adresse/Schlüssel (falls angegeben), meldet den Piloten an und lädt die Buchungen.</summary>
    public async Task<ActionResult> ConnectAirlineAsync(AirlineConnectRequest request)
    {
        if (request.Url is not null) Settings.AirlineUrl = request.Url.Trim();
        if (!string.IsNullOrWhiteSpace(request.ApiKey)) Settings.SetAirlineApiKey(request.ApiKey);
        try { Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* trotzdem verbinden */ }
        return await RefreshAirlineAsync(announce: false).ConfigureAwait(false);
    }

    public async Task<ActionResult> RefreshAirlineAsync(bool announce = false)
    {
        var url = Settings.AirlineUrl;
        var key = Settings.GetAirlineApiKey();
        if (string.IsNullOrWhiteSpace(url)) return new ActionResult(false, L("Keine Airline-Adresse eingetragen.", "No airline address entered."));
        if (string.IsNullOrWhiteSpace(key)) return new ActionResult(false, L("Kein API-Schlüssel eingetragen.", "No API key entered."));

        try
        {
            var client = new PhpVmsClient(url, key);
            var pilot = await client.GetUserAsync().ConfigureAwait(false);
            var bookings = await client.GetBidsAsync().ConfigureAwait(false);
            lock (_gate)
            {
                _pilot = pilot;
                _bookings = bookings;
                _bookingsUpdated = DateTimeOffset.UtcNow;
                if (_selectedBooking is { } current)
                    _selectedBooking = bookings.FirstOrDefault(b => b.FlightId == current.FlightId);
            }
            var message = L($"Airline: angemeldet als {pilot.Display} – {bookings.Count} gebuchte(r) Flug/Flüge", $"Airline: signed in as {pilot.Display} – {bookings.Count} booked flight(s)");
            if (announce) AddNotice(message);
            return new ActionResult(true, message);
        }
        catch (AirlineException ex)
        {
            lock (_gate) _pilot = null;
            AddNotice($"Airline: {ex.Message}");
            return new ActionResult(false, ex.Message);
        }
    }

    public async Task<ActionResult> LoadFleetAsync()
    {
        var client = CreateAirlineClient(out var error);
        if (client is null) return new ActionResult(false, error!);
        try
        {
            var (fleet, info) = await client.GetFleetAsync().ConfigureAwait(false);
            var selected = AeroflyConfig.ReadCurrentAircraft();
            lock (_gate)
            {
                if (_tracker.BlockOff is null && selected is not null) _aeroflyAircraft = selected;
                _fleet = fleet;
                _fleetInfo = info;
            }
            return fleet.Count > 0
                ? new ActionResult(true, L($"{fleet.Count} Flugzeuge geladen.", $"{fleet.Count} aircraft loaded."))
                : new ActionResult(false, L("Die Airline hat keine Flugzeuge geliefert – bitte Kennzeichen eingeben.", "The airline returned no aircraft – please enter a registration."));
        }
        catch (AirlineException ex)
        {
            return new ActionResult(false, L("Flotte konnte nicht geladen werden: ", "Could not load the fleet: ") + ex.Message);
        }
    }

    /// <summary>
    /// Reicht den abgeschlossenen Flug als PIREP ein: anmelden (prefile), Strecke senden, einreichen (file).
    /// Geht unterwegs etwas schief, wird die Anmeldung wieder zurückgezogen (cancel).
    /// </summary>
    public async Task<ActionResult> SubmitPirepAsync(PirepSubmitRequest request)
    {
        var client = CreateAirlineClient(out var error);
        if (client is null) return new ActionResult(false, error!);
        // Nichts gewählt, aber die Buchung trägt ein Flugzeug: das nehmen.
        string? bookedAircraft;
        lock (_gate) bookedAircraft = _selectedBooking?.AircraftId ?? _liveAircraftId;
        if (string.IsNullOrWhiteSpace(request.AircraftId) && string.IsNullOrWhiteSpace(request.Registration) && bookedAircraft is not null)
            request = request with { AircraftId = bookedAircraft };

        if (string.IsNullOrWhiteSpace(request.AircraftId))
        {
            if (string.IsNullOrWhiteSpace(request.Registration))
                return new ActionResult(false, L("Bitte ein Flugzeug auswählen oder ein Kennzeichen eingeben.", "Please select an aircraft or enter a registration."));
            AirlineAircraft? found;
            try
            {
                found = await client.FindAircraftByRegistrationAsync(request.Registration).ConfigureAwait(false);
            }
            catch (AirlineException ex)
            {
                return new ActionResult(false, L("Kennzeichen konnte nicht gesucht werden: ", "Could not look up the registration: ") + ex.Message);
            }
            if (found is null)
                return new ActionResult(false, L($"Kennzeichen {request.Registration.Trim().ToUpperInvariant()} ist bei der Airline nicht bekannt.", $"Registration {request.Registration.Trim().ToUpperInvariant()} is not known to the airline."));
            request = request with { AircraftId = found.Id };
        }

        PirepPrefile prefile;
        PirepFile file;
        Dictionary<string, string> fields;
        List<PirepPosition> positions;
        string? livePirep, liveAircraft;
        lock (_gate)
        {
            var problem = SubmitProblemLocked();
            if (problem is not null) return new ActionResult(false, problem);
            _submitting = true;

            var t = _tracker;
            var booking = _selectedBooking;
            string dep = booking?.Departure ?? t.DepartureAirport!.Ident;
            string arr = booking?.Arrival ?? t.ArrivalAirport!.Ident;
            // SimBrief-Plan nur verwenden, wenn er zu Abflug und Ziel passt (sonst falsche Route/Höhe im PIREP).
            var plan = t.Plan is { } tp &&
                       tp.OriginIcao.Equals(dep, StringComparison.OrdinalIgnoreCase) &&
                       tp.DestinationIcao.Equals(arr, StringComparison.OrdinalIgnoreCase)
                ? tp
                : null;

            string? flightNumber = booking is not null
                ? booking.FlightNumber ?? new string(booking.ShortIdent.Where(char.IsDigit).ToArray())
                : CleanFlightNumber(request.FlightNumber) ?? CleanFlightNumber(plan?.FlightNumber);
            if (string.IsNullOrEmpty(flightNumber))
            {
                _submitting = false;
                return new ActionResult(false, L("Bitte eine Flugnummer eingeben (freier Flug ohne Buchung).", "Please enter a flight number (free flight without booking)."));
            }

            var report = FlightReportBuilder.Build(t, DateTimeOffset.UtcNow, booking, _runways);
            var an = report.Analysis!;
            int? level = plan?.CruiseAltitudeFt is { } cruise ? (int)Math.Round(cruise / 100)
                : report.MaxAltitudeFt is { } max ? (int)Math.Round(max / 100) : null;
            var route = plan?.Route ?? booking?.Route;

            prefile = new PirepPrefile(
                booking?.AirlineId ?? _pilot!.AirlineId!, request.AircraftId!, booking?.FlightId,
                flightNumber, booking?.RouteCode, booking?.RouteLeg, dep, arr, level, route,
                booking?.DistanceNm ?? plan?.RouteDistanceNm, booking?.FlightTimeMin, t.BlockOff, SourceName);

            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.Notes)) notes.Add(request.Notes.Trim());
            notes.Add(L($"Aufgezeichnet mit {SourceName} {Version} (Aerofly FS 4, UDP-Broadcast). " +
                      "Landerate aus GPS-Höhe berechnet, Treibstoff nicht verfügbar.",
                      $"Recorded with {SourceName} {Version} (Aerofly FS 4, UDP broadcast). " +
                      "Landing rate calculated from GPS altitude, fuel not available."));
            notes.Add(AnalysisSummary(an, t));

            fields = AnalysisFields(an, t);
            if (_aeroflyAircraft is { } simAc) fields["Sim Aircraft"] = simAc.Name;

            file = new PirepFile(
                report.DistanceFlownNm ?? 0,
                Math.Max(1, (int)Math.Round(report.AirTime?.TotalMinutes ?? 0)),
                report.BlockTime is { } bt ? (int)Math.Round(bt.TotalMinutes) : null,
                t.LandingRateFpm,
                t.BlockOff, t.BlockOn ?? t.LastSample?.Time,
                level, route, string.Join("\n", notes), SourceName);

            positions = BuildPositions(t);
            livePirep = _livePirepId;
            liveAircraft = _liveAircraftId;
            // Schon live gesendete Punkte nicht noch einmal schicken
            // (Der Aufsetzpunkt geht trotzdem mit – er trägt die gemessene Landerate.)
            var tdTime = t.TouchdownSample?.Time;
            if (livePirep is not null) positions = positions.Where(p => !_liveSent.Contains(p.Time) || p.Time == tdTime).ToList();
        }

        string? pirepId = null;
        try
        {
            if (livePirep is not null)
            {
                pirepId = livePirep;
                if (request.AircraftId is not null && request.AircraftId != liveAircraft)
                    await client.UpdateAsync(pirepId, new Dictionary<string, object?> { ["aircraft_id"] = request.AircraftId }).ConfigureAwait(false);
            }
            else
            {
                pirepId = await client.PrefileAsync(prefile).ConfigureAwait(false);
            }
            if (positions.Count > 0) await client.SendPositionsAsync(pirepId, positions).ConfigureAwait(false);
            try
            {
                await client.PostFieldsAsync(pirepId, fields).ConfigureAwait(false);
            }
            catch (AirlineException ex)
            {
                // Zusatzfelder sind nicht entscheidend – der PIREP geht trotzdem raus.
                AddNotice(L("PIREP: Zusatzfelder nicht übernommen", "PIREP: extra fields not accepted") + $" ({ex.Message})");
            }
            await client.FileAsync(pirepId, file).ConfigureAwait(false);

            var url = client.PirepPageUrl(pirepId).ToString();
            lock (_gate)
            {
                _submittedPirepId = pirepId;
                _submittedUrl = url;
                _submitting = false;
            }
            var message = L($"PIREP {prefile.FlightNumber} eingereicht ({positions.Count} Positionen).", $"PIREP {prefile.FlightNumber} submitted ({positions.Count} positions).");
            AddNotice(message);
            return new ActionResult(true, message);
        }
        catch (AirlineException ex)
        {
            // Ein Live-Flug bleibt bestehen (erneut einreichen möglich); nur ein eben angelegter wird zurückgezogen.
            if (pirepId is not null && livePirep is null)
            {
                try { await client.CancelAsync(pirepId).ConfigureAwait(false); }
                catch (AirlineException) { /* Rückzug fehlgeschlagen – Meldung unten reicht */ }
            }
            lock (_gate) _submitting = false;
            var step = pirepId is null ? L("Fehler beim Anmelden", "Error filing") : L("Fehler beim Einreichen", "Error submitting");
            AddNotice($"PIREP: {step} – {ex.Message}");
            return new ActionResult(false, $"{step}: {ex.Message}");
        }
    }

    /// <summary>
    /// Positionen für den PIREP: alle 10 s, rund um Start und Landung jede Sekunde (Airlines berechnen daraus
    /// Landerate, Aufsetzpunkt und Rollout). Der Aufsetzpunkt trägt die gemessene Landerate.
    /// Höhe über Grund: bis zur Hälfte des Flugs bezogen auf die Abflughöhe, danach auf die Aufsetzhöhe.
    /// </summary>
    private static List<PirepPosition> BuildPositions(FlightTracker t)
    {
        var list = new List<PirepPosition>();
        var td = t.TouchdownSample;
        var takeoff = t.Takeoff;
        var midpoint = takeoff is { } a && td is not null ? a + (td.Time - a) / 2 : (DateTimeOffset?)null;
        DateTimeOffset? last = null;

        bool Dense(DateTimeOffset time) =>
            (td is not null && time >= td.Time - TimeSpan.FromSeconds(90) && time <= td.Time + TimeSpan.FromSeconds(40)) ||
            (takeoff is { } to && time >= to - TimeSpan.FromSeconds(40) && time <= to + TimeSpan.FromSeconds(30));

        foreach (var s in t.Samples)
        {
            bool isTouchdown = td is not null && s.Time == td.Time;
            if (!isTouchdown && last is not null && s.Time - last.Value < TimeSpan.FromSeconds(Dense(s.Time) ? 0.9 : 10)) continue;

            double? groundRef = midpoint is { } m && s.Time >= m ? td?.AltitudeFt : t.DepartureElevationFt ?? td?.AltitudeFt;
            double vs = isTouchdown && t.LandingRateFpm is { } lr ? lr : s.VerticalSpeedFpm;
            list.Add(new PirepPosition(s.Latitude, s.Longitude, s.AltitudeFt, s.HeadingDeg, vs, s.GroundSpeedKt, s.Time,
                groundRef is { } g ? s.AltitudeFt - g : null));
            last = s.Time;
        }
        return list;
    }

    private static string AnalysisSummary(FlightAnalysis a, FlightTracker t)
    {
        var parts = new List<string>
        {
            L("Landerate", "Landing rate") + $" {t.LandingRateFpm:F0} fpm",
            $"Bounces {a.Bounces}",
        };
        if (a.ApproachVsSigmaFpm is { } sig) parts.Add(L("Endanflug", "Final approach") + $" {a.ApproachRating} (σ {sig:F0} fpm, Ø {a.ApproachVsMeanFpm:F0} fpm)");
        if (a.RolloutM is { } ro) parts.Add($"Rollout {ro:F0} m");
        if (a.DepartureRunway is not null || a.ArrivalRunway is not null)
            parts.Add(L("Bahnen", "Runways") + $" {a.DepartureRunway ?? "?"} → {a.ArrivalRunway ?? "?"}");
        parts.Add(L("Zeitraffer", "Sim rate") + $" max. {a.MaxSimRate:0.#}×");
        parts.Add($"Slew {a.SlewEvents}×");
        parts.Add(L("Pausen", "Paused") + $" {a.PausedTime:hh\\:mm\\:ss}");
        if (a.DaylightTakeoff is not null) parts.Add(L("Tageslicht", "Daylight") + $" {a.DaylightTakeoff} → {a.DaylightLanding}");
        return string.Join(" · ", parts);
    }

    /// <summary>Zusatzfelder für den PIREP (Werte als Text, Einheiten im Namen).</summary>
    private static Dictionary<string, string> AnalysisFields(FlightAnalysis a, FlightTracker t)
    {
        var inv = CultureInfo.InvariantCulture;
        var f = new Dictionary<string, string>
        {
            ["Simulator"] = "Aerofly FS 4",
            ["Network"] = "offline",
            ["Tracker"] = $"{SourceName} {Version}",
            ["Bounces"] = a.Bounces.ToString(inv),
            ["Sim Rate Max"] = a.MaxSimRate.ToString("0.#", inv),
            ["Slew Count"] = a.SlewEvents.ToString(inv),
            ["Pause Time"] = a.PausedTime.ToString(@"hh\:mm\:ss", inv),
        };
        if (t.LandingRateFpm is { } lr) f["Landing Rate (fpm)"] = lr.ToString("F0", inv);
        if (t.TouchdownSample is { } td) f["Touchdown Speed (kt)"] = td.GroundSpeedKt.ToString("F0", inv);
        if (a.ApproachVsSigmaFpm is { } sig) f["Approach VS Sigma (fpm)"] = sig.ToString("F0", inv);
        if (a.ApproachVsMeanFpm is { } mean) f["Approach VS Mean (fpm)"] = mean.ToString("F0", inv);
        if (a.RolloutM is { } ro) f["Rollout (m)"] = ro.ToString("F0", inv);
        if (a.DepartureRunway is { } dr) f["Departure Runway"] = dr;
        if (a.ArrivalRunway is { } ar) f["Arrival Runway"] = ar;
        if (a.TaxiOut is { } to) f["Taxi Out"] = to.ToString(@"hh\:mm\:ss", inv);
        if (a.TaxiIn is { } ti) f["Taxi In"] = ti.ToString(@"hh\:mm\:ss", inv);
        if (a.DaylightTakeoff is { } dt) f["Daylight Takeoff"] = dt;
        if (a.DaylightLanding is { } dl) f["Daylight Landing"] = dl;
        return f;
    }

    /// <summary>Auswertung des aktuellen Flugs, zwischengespeichert bis neue Messpunkte kommen.</summary>
    private FlightAnalysis? AnalysisLocked()
    {
        if (_tracker.Touchdown is null) return null;
        if (_analysisCache is null || !ReferenceEquals(_analysisCacheTracker, _tracker) || _analysisCacheCount != _tracker.Samples.Count)
        {
            _analysisCache = FlightAnalysis.Analyze(_tracker, _runways);
            _analysisCacheCount = _tracker.Samples.Count;
            _analysisCacheTracker = _tracker;
        }
        return _analysisCache;
    }

    /// <summary>Warum gerade nicht eingereicht werden kann (null = es geht).</summary>
    /// <summary>Flugnummer für phpVMS: nur Buchstaben und Ziffern, ohne Airline-Code davor (z. B. "XVA1022" → "1022").</summary>
    private static string? Join(string? proc, string? transition) =>
        proc is null ? null : string.IsNullOrWhiteSpace(transition) ? proc : $"{proc}.{transition}";

    private static string? CleanFlightNumber(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = new string(input.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        int firstDigit = s.IndexOfAny("0123456789".ToCharArray());
        if (firstDigit > 0 && firstDigit <= 3) s = s[firstDigit..];
        return s.Length is > 0 and <= 10 ? s : null;
    }

    private string? SubmitProblemLocked()
    {
        if (_submitting) return L("Wird bereits gesendet …", "Already sending …");
        if (_submittedPirepId is not null) return L("Dieser Flug wurde bereits eingereicht.", "This flight has already been submitted.");
        if (_selectedBooking is null)
        {
            // Freier Flug ohne Buchung: Airline des Piloten, Plätze aus der Aufzeichnung.
            if (_pilot is null) return L("Erst bei der Airline anmelden (Koffer-Symbol → Verbinden).", "Sign in to the airline first (briefcase icon → Connect).");
            if (_pilot.AirlineId is null) return L("Die Airline hat keine Airline-ID zum Piloten geliefert – bitte einen gebuchten Flug verwenden.", "The airline returned no airline ID for the pilot – please use a booked flight.");
            if (_tracker.DepartureAirport is null || _tracker.ArrivalAirport is null)
                return L("Abflug- oder Zielplatz unbekannt – bitte einen gebuchten Flug verwenden.", "Departure or destination unknown – please use a booked flight.");
        }
        else if (_selectedBooking.AirlineId is null) return L("Die Buchung enthält keine Airline-ID.", "The booking contains no airline ID.");
        if (_tracker.Takeoff is null || _tracker.Touchdown is null) return L("Start und Landung müssen aufgezeichnet sein.", "Takeoff and landing must be recorded.");
        if (!_reportSaved) return L("Erst den Flug beenden (Block on oder „Beenden“).", "Finish the flight first (block on or \"Finish\").");
        return null;
    }

    private PhpVmsClient? CreateAirlineClient(out string? error)
    {
        var url = Settings.AirlineUrl;
        var key = Settings.GetAirlineApiKey();
        error = string.IsNullOrWhiteSpace(url) ? L("Keine Airline-Adresse eingetragen.", "No airline address entered.")
            : string.IsNullOrWhiteSpace(key) ? L("Kein API-Schlüssel eingetragen.", "No API key entered.") : null;
        if (error is not null) return null;
        try
        {
            return new PhpVmsClient(url!, key!);
        }
        catch (AirlineException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public ActionResult SelectBooking(string? flightId)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(flightId))
            {
                _selectedBooking = null;
                return new ActionResult(true, L("Keine Buchung ausgewählt.", "No booking selected."));
            }
            var booking = _bookings.FirstOrDefault(b => b.FlightId == flightId);
            if (booking is null) return new ActionResult(false, L("Buchung nicht gefunden – bitte Liste aktualisieren.", "Booking not found – please refresh the list."));
            _selectedBooking = booking;

            // Passt der SimBrief-Plan nicht zur Buchung, wird er verworfen. Vor dem Flug sofort,
            // während eines Flugs erst für den nächsten (der laufende Flug behält seinen Plan).
            var plan = _plan ?? _tracker.Plan;
            string extra = "";
            if (plan is not null &&
                (!plan.OriginIcao.Equals(booking.Departure, StringComparison.OrdinalIgnoreCase) ||
                 !plan.DestinationIcao.Equals(booking.Arrival, StringComparison.OrdinalIgnoreCase)))
            {
                _plan = null;
                if (_tracker.Phase == FlightPhase.Preflight)
                {
                    ResetLocked();
                    extra = L($" SimBrief-Plan {plan.OriginIcao} → {plan.DestinationIcao} passt nicht und wurde entfernt.", $" SimBrief plan {plan.OriginIcao} → {plan.DestinationIcao} does not match and was removed.");
                }
                else
                {
                    extra = L($" SimBrief-Plan {plan.OriginIcao} → {plan.DestinationIcao} passt nicht – gilt nur noch für den laufenden Flug.", $" SimBrief plan {plan.OriginIcao} → {plan.DestinationIcao} does not match – only applies to the current flight.");
                }
            }
            var message = L($"Flug {booking.ShortIdent} {booking.Departure} → {booking.Arrival} ausgewählt.", $"Flight {booking.ShortIdent} {booking.Departure} → {booking.Arrival} selected.") + extra;
            AddNotice(message);
            _ = Task.Run(ApplyConditionsAsync);   // Abflugplatz kann sich geändert haben
            return new ActionResult(true, message);
        }
    }

    public ActionResult UpdateSettings(SettingsUpdate update)
    {
        if (update.AirlineUrl is not null) Settings.AirlineUrl = update.AirlineUrl.Trim();
        if (!string.IsNullOrWhiteSpace(update.AirlineApiKey)) Settings.SetAirlineApiKey(update.AirlineApiKey);
        if (update.SimBriefUser is not null) Settings.SimBriefUser = update.SimBriefUser.Trim();
        if (update.LoadSimBriefOnStart is { } load) Settings.LoadSimBriefOnStart = load;
        if (update.Language is { } language)
        {
            Settings.Language = language is "de" or "en" ? language : "auto";
            Settings.ApplyLanguage();
        }
        if (update.Voice is { } voice)
        {
            Settings.Voice = voice;
            if (voice && _speech is null) _speech = SpeechAnnouncer.Create();
        }
        if (update.CabinAnnouncements is { } cabin)
        {
            Settings.CabinAnnouncements = cabin;
            if (cabin) _cabin ??= new CabinAnnouncer();
            else _cabin?.Stop();
        }

        try
        {
            Settings.Save();
            return new ActionResult(true, L("Einstellungen gespeichert.", "Settings saved."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ActionResult(false, L("Einstellungen konnten nicht gespeichert werden: ", "Settings could not be saved: ") + ex.Message);
        }
    }

    public ActionResult VoiceTest()
    {
        _speech ??= SpeechAnnouncer.Create();
        if (!_speech.Available) return new ActionResult(false, _speech.Problem ?? L("Sprachausgabe nicht verfügbar.", "Speech output not available."));
        _speech.Say(LandingCallout.Text(-153));
        return new ActionResult(true, L("Test mit ", "Test with ") + (_speech.VoiceName ?? L("Standardstimme", "default voice")));
    }

    /// <summary>Flugdaten als Text zum Einfügen in ein manuelles PIREP-Formular (für Airlines ohne API-Schlüssel).</summary>
    public ActionResult PirepText()
    {
        lock (_gate)
        {
            var t = _tracker;
            if (t.Touchdown is null)
                return new ActionResult(false, L("Noch keine Landung aufgezeichnet.", "No landing recorded yet."));

            var r = FlightReportBuilder.Build(t, DateTimeOffset.UtcNow, _selectedBooking, _runways);
            var an = FlightAnalysis.Analyze(t, _runways);
            static string Time(DateTimeOffset? x) => x is { } v ? v.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + "z" : "–";
            static string Dur(TimeSpan? x) => x is { } v ? $"{(int)v.TotalHours:00}:{v.Minutes:00}" : "–";

            var lines = new List<string>
            {
                L("Flug: ", "Flight: ") + (r.Flight ?? r.Booking ?? "–"),
                L("Strecke: ", "Route: ") + $"{r.Origin ?? "?"} → {r.Destination ?? "?"}",
            };
            var aircraft = new[] { r.Aircraft, _aeroflyAircraft is { } ac ? L("in Aerofly: ", "in Aerofly: ") + ac.Name : null }.Where(x => x is not null);
            if (aircraft.Any()) lines.Add(L("Flugzeug: ", "Aircraft: ") + string.Join(" · ", aircraft));
            lines.Add($"Block off {Time(r.BlockOffUtc)} · {L("Abheben", "Takeoff")} {Time(r.TakeoffUtc)} · {L("Aufsetzen", "Touchdown")} {Time(r.TouchdownUtc)} · Block on {Time(r.BlockOnUtc)}");
            lines.Add($"{L("Flugzeit", "Air time")} {Dur(r.AirTime)} · {L("Blockzeit", "Block time")} {Dur(r.BlockTime)}");
            if (r.DistanceFlownNm is { } d) lines.Add(L("Distanz: ", "Distance: ") + $"{d:F0} NM");
            if (r.MaxAltitudeFt is { } m) lines.Add(L("Max. Höhe: ", "Max. altitude: ") + FlightTracker.FormatAltitude(m));
            lines.Add(L("Landerate: ", "Landing rate: ") + $"{r.LandingRateFpm:F0} fpm ({r.LandingRating})");
            if (r.Route is not null) lines.Add($"Route: {r.Route}");
            lines.Add(L("Auswertung: ", "Analysis: ") + AnalysisSummary(an, t));
            lines.Add($"Simulator: Aerofly FS 4 · {L("aufgezeichnet mit", "recorded with")} {SourceName} {Version}");

            return new ActionResult(true, L("Flugdaten in die Zwischenablage kopiert.", "Flight data copied to the clipboard."), string.Join(Environment.NewLine, lines));
        }
    }

    /// <summary>Probe: spielt die Begrüßung.</summary>
    public ActionResult CabinTest()
    {
        _cabin ??= new CabinAnnouncer();
        if (!_cabin.Available) return new ActionResult(false, L("Tonausgabe ist auf diesem System nicht verfügbar.", "Sound output is not available on this system."));
        if (CabinAnnouncer.Resolve(CabinCues.WelcomeOnboard) is null)
            return new ActionResult(false, L("Keine Begrüßungsansage gefunden.", "No welcome announcement found."));
        _cabin.Stop();
        _cabin.Play(CabinCues.WelcomeOnboard);
        return new ActionResult(true, L("Begrüßung wird abgespielt.", "Playing the welcome announcement."));
    }

    /// <summary>Ordner für eigene Ansagen öffnen (Dokumente/Flarelog/Sounds).</summary>
    public ActionResult OpenSoundFolder()
    {
        try
        {
            var folder = CabinAnnouncer.UserFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            return new ActionResult(true, folder);
        }
        catch (Exception ex)
        {
            return new ActionResult(false, L("Ordner konnte nicht geöffnet werden: ", "Could not open the folder: ") + ex.Message);
        }
    }

    public ActionResult OpenReportFolder()
    {
        try
        {
            var folder = Settings.EffectiveReportFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            return new ActionResult(true, folder);
        }
        catch (Exception ex)
        {
            return new ActionResult(false, L("Ordner konnte nicht geöffnet werden: ", "Could not open the folder: ") + ex.Message);
        }
    }

    // ---------- Zustand ----------

    /// <summary>Kurzer Status für die Konsolen-Statuszeile.</summary>
    public string StatusText()
    {
        lock (_gate)
        {
            var s = _tracker.LastSample;
            bool connected = IsConnectedLocked();
            if (s is null)
                return connected ? "Daten kommen an, warte auf erste GPS-Position …" : "Keine Daten von Aerofly …";
            var phase = _tracker.IsPaused ? "PAUSE" : _tracker.Phase.ToDisplay();
            return string.Create(CultureInfo.InvariantCulture,
                $"{(connected ? "●" : "○")} {phase,-24} ALT {s.AltitudeFt,6:F0} ft  GS {s.GroundSpeedKt,4:F0} kt  VS {s.VerticalSpeedFpm,6:F0} fpm  HDG {s.HeadingDeg,4:F0}°");
        }
    }

    /// <summary>Zustand für das Display (/api/state).</summary>
    public object GetState()
    {
        lock (_gate)
        {
            var tracker = _tracker;
            var plan = _plan;
            var s = tracker.LastSample;
            var samples = tracker.Samples;
            int step = Math.Max(1, samples.Count / 1500);
            var track = new List<double[]>(samples.Count / step + 2);
            for (int i = 0; i < samples.Count; i += step)
                track.Add(new[] { samples[i].Latitude, samples[i].Longitude });
            if (s is not null) track.Add(new[] { s.Latitude, s.Longitude });

            var now = DateTimeOffset.UtcNow;
            bool connected = IsConnectedLocked();
            double? airTimeSec = tracker.Takeoff is { } to
                ? ((tracker.Touchdown ?? (connected ? now : s?.Time ?? now)) - to).TotalSeconds
                : null;

            // Der Plan gehört nur dann zum laufenden Flug, wenn der Tracker mit ihm erzeugt wurde.
            var flightPlan = tracker.Plan;
            var routePlan = _plan ?? flightPlan;
            var progress = flightPlan is not null && s is not null && !tracker.IsFinished
                ? RouteProgress.Compute(flightPlan.Waypoints, s.Latitude, s.Longitude)
                : null;
            double? etaMin = progress is not null && s is not null && s.GroundSpeedKt > 30
                ? progress.DistanceToGoNm / s.GroundSpeedKt * 60
                : null;

            return new
            {
                connected,
                paused = tracker.IsPaused,
                source = _source,
                phase = tracker.Phase.ToDisplay(),
                finished = tracker.IsFinished,
                inProgress = samples.Count > 0 && tracker.Phase != FlightPhase.Preflight && !_reportSaved,
                reportSaved = _reportSaved,
                lastReport = _lastReportPath is null ? null : Path.GetFileName(_lastReportPath),
                header = new
                {
                    flight = flightPlan?.DisplayName ?? _selectedBooking?.ShortIdent,
                    origin = flightPlan?.OriginIcao ?? _selectedBooking?.Departure ?? tracker.DepartureAirport?.Ident,
                    destination = flightPlan?.DestinationIcao ?? _selectedBooking?.Arrival ?? tracker.ArrivalAirport?.Ident,
                    aircraft = flightPlan?.AircraftType ?? (_selectedBooking is { AircraftTypes.Count: > 0 } selected ? selected.AircraftTypes[0] : null),
                    simbrief = flightPlan is not null,
                    simAircraft = (_simAircraftNow ?? _aeroflyAircraft)?.DisplayName,
                    simAircraftCode = (_simAircraftNow ?? _aeroflyAircraft)?.Name,
                    booking = _selectedBooking?.ShortIdent,
                },
                submit = new
                {
                    available = SubmitProblemLocked() is null,
                    problem = SubmitProblemLocked(),
                    sending = _submitting,
                    pirepId = _submittedPirepId,
                    url = _submittedUrl,
                    flight = _selectedBooking?.ShortIdent,
                    free = _selectedBooking is null,
                    flightNumberHint = CleanFlightNumber(flightPlan?.FlightNumber),
                    dep = _selectedBooking?.Departure ?? tracker.DepartureAirport?.Ident,
                    arr = _selectedBooking?.Arrival ?? tracker.ArrivalAirport?.Ident,
                    aircraftHint = _selectedBooking?.AircraftIcao ?? flightPlan?.AircraftType,
                    bookedAircraftId = _selectedBooking?.AircraftId,
                    bookedAircraft = _selectedBooking is { AircraftId: not null } ba
                        ? (ba.AircraftRegistration ?? "ID " + ba.AircraftId) + (ba.AircraftIcao is null ? "" : $" · {ba.AircraftIcao}")
                        : null,
                    blockMin = tracker.BlockOff is { } bo && tracker.BlockOn is { } bn ? (bn - bo).TotalMinutes : (double?)null,
                    airMin = tracker.Takeoff is { } tko && tracker.Touchdown is { } tdn ? (tdn - tko).TotalMinutes : (double?)null,
                    landingRate = tracker.LandingRateFpm,
                    landedAt = tracker.ArrivalAirport?.Ident,
                    aeroflyAircraft = _aeroflyAircraft?.Name,
                    aircraftCheck = AircraftCheck.Compare(
                        _selectedBooking?.AircraftIcao ?? flightPlan?.AircraftType,
                        _aeroflyAircraft?.Name,
                        tracker.LiftoffSpeedKt),
                },
                airline = new
                {
                    presetName = Preset?.Name,
                    liveTracking = Settings.LiveTracking,
                    live = _livePirepId is not null,
                    liveInfo = _liveInfo,
                    url = Settings.AirlineUrl,
                    hasKey = Settings.HasAirlineApiKey,
                    pilot = _pilot?.Display,
                    pilotAirport = _pilot?.CurrentAirport,
                    updated = _bookingsUpdated?.ToUniversalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
                    selected = _selectedBooking?.FlightId,
                    fleetInfo = _fleetInfo,
                    fleet = _fleet.Select(a => new
                    {
                        id = a.Id,
                        reg = a.Registration,
                        icao = a.Icao,
                        type = a.SubfleetType,
                        airport = a.AirportId,
                        state = a.State,
                    }),
                    bookings = _bookings.Select(b => new
                    {
                        flightId = b.FlightId,
                        ident = b.ShortIdent,
                        suffix = b.IdentSuffix,
                        dep = b.Departure,
                        arr = b.Arrival,
                        aircraft = b.AircraftRegistration is not null
                            ? $"{b.AircraftRegistration}{(b.AircraftIcao is null ? "" : " · " + b.AircraftIcao)}"
                            : string.Join(", ", b.AircraftTypes),
                        timeMin = b.FlightTimeMin,
                        distanceNm = b.DistanceNm,
                        route = b.Route,
                    }),
                },
                pendingPlan = plan is not null && !ReferenceEquals(plan, flightPlan) ? plan.Summary : null,
                sample = s is null ? null : new
                {
                    lat = s.Latitude,
                    lon = s.Longitude,
                    altFt = s.AltitudeFt,
                    gsKt = s.GroundSpeedKt,
                    vsFpm = s.VerticalSpeedFpm,
                    hdg = s.HeadingDeg,
                },
                airTimeSec,
                progress = progress is null ? null : new
                {
                    next = progress.Next.Ident,
                    nextNm = progress.DistanceToNextNm,
                    dtgNm = progress.DistanceToGoNm,
                    todNm = progress.DistanceToTodNm,
                    etaMin,
                },
                landing = tracker.LandingRateFpm is { } rate ? new
                {
                    rate,
                    analysis = AnalysisLocked(),
                    rating = LandingCallout.Rating(rate),
                    gsKt = tracker.TouchdownSample?.GroundSpeedKt,
                    airport = tracker.ArrivalAirport?.Ident,
                } : null,
                lastCallout = _lastCallout,
                track,
                route = flightPlan?.Waypoints.Select(w => new { ident = w.Ident, lat = w.Latitude, lon = w.Longitude }),
                events = tracker.Events.TakeLast(60).Select(e => new
                {
                    time = e.Time.ToUniversalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    text = e.Text,
                    phaseChange = e.PhaseChange,
                }),
                notices = _notices.ToArray(),
                settings = new
                {
                    simBriefUser = Settings.SimBriefUser,
                    loadSimBriefOnStart = Settings.LoadSimBriefOnStart,
                    realTime = Settings.RealTime,
                    realWeather = Settings.RealWeather,
                    conditionsStatus = _conditionsStatus,
                    weatherStations = new[]
                    {
                        _plan?.OriginIcao ?? _selectedBooking?.Departure,
                        _plan?.DestinationIcao ?? _selectedBooking?.Arrival,
                    }.Where(x => !string.IsNullOrEmpty(x)).Distinct(),
                    weatherIcao = Settings.WeatherIcao,
                    weatherAuto = DepartureForWeatherLocked() is { Icao: { } wIcao } wd ? $"{wIcao} ({wd.Source})" : null,
                    routeInfo = routePlan is null ? null : new
                    {
                        dep = routePlan.OriginIcao,
                        depRwy = routePlan.OriginRunway,
                        sid = Join(routePlan.Sid, routePlan.SidTransition),
                        waypoints = routePlan.EnrouteWaypoints.Select(w => w.Ident),
                        star = Join(routePlan.Star, routePlan.StarTransition),
                        arrRwy = routePlan.DestinationRunway,
                        arr = routePlan.DestinationIcao,
                    },
                    voice = Settings.Voice,
                    cabin = Settings.CabinAnnouncements,
                    soundFolder = CabinAnnouncer.UserFolder,
                    reportFolder = Settings.EffectiveReportFolder,
                    udpPort = Settings.UdpPort,
                    version = AppInfo.Version,
                    canQuit = QuitRequested is not null,
                    lang = Lang.Code,
                    language = Settings.Language,
                },
                update = _update is null ? null : new { version = _update.Version, url = _update.Url },
            };
        }
    }

    // ---------- intern ----------

    private bool IsConnectedLocked() =>
        _lastReceived is { } t && DateTimeOffset.UtcNow - t < TimeSpan.FromSeconds(5);

    private void ResetLocked()
    {
        if (_livePirepId is { } live && _submittedPirepId is null)
        {
            // Nicht eingereichter Live-Flug: bei der Airline zurückziehen.
            var client = CreateAirlineClient(out _);
            if (client is not null) _ = Task.Run(async () =>
            {
                try { await client.CancelAsync(live).ConfigureAwait(false); AddNotice(L("Live-Flug bei der Airline zurückgezogen.", "Live flight withdrawn at the airline.")); }
                catch (AirlineException) { /* verfällt bei der Airline von selbst */ }
            });
        }
        _livePirepId = null;
        _liveAircraftId = null;
        _liveSent.Clear();
        _liveStatusCode = null;
        _liveInfo = null;
        _builder = new SampleBuilder();
        _cabin?.Stop();
        _tracker = CreateTracker();
        _reportSaved = false;
        _lastCallout = null;
        _submittedPirepId = null;
        _submittedUrl = null;
    }

    private string? SaveReportLocked()
    {
        if (_tracker.Samples.Count == 0) return null;
        try
        {
            var report = FlightReportBuilder.Build(_tracker, DateTimeOffset.UtcNow, _selectedBooking, _runways);
            var (_, textPath) = ReportWriter.Save(report, Settings.EffectiveReportFolder);
            _reportSaved = true;
            _lastReportPath = textPath;
            ReportSaved?.Invoke(report, textPath);
            return textPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddNotice(L("Bericht konnte nicht gespeichert werden: ", "Report could not be saved: ") + ex.Message);
            return null;
        }
    }

    private void AddNotice(string text)
    {
        lock (_gate)
        {
            _notices.Add(text);
            if (_notices.Count > MaxNotices) _notices.RemoveAt(0);
        }
        Notice?.Invoke(text);
    }

    /// <summary>Speichert einen laufenden Flug als Teilbericht und gibt alles frei.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            if (_tracker.Samples.Count > 0 && _tracker.Phase != FlightPhase.Preflight && !_reportSaved)
                SaveReportLocked();
        }
        _cts?.Cancel();
        _listener?.Dispose();
        _server?.Dispose();
        _speech?.Dispose();
        _cabin?.Dispose();
        _cts?.Dispose();
    }

    /// <summary>Flughafenliste, die erst im Hintergrund fertig geladen wird.</summary>
    private sealed class AirportsProxy : IAirportLookup
    {
        public volatile AirportDatabase? Database;

        public Airport? Nearest(double latitude, double longitude, double maxDistanceNm) =>
            Database?.Nearest(latitude, longitude, maxDistanceNm);
    }

    /// <summary>Bahnenliste, die erst im Hintergrund fertig geladen wird.</summary>
    private sealed class RunwaysProxy : IRunwayLookup
    {
        public volatile RunwayDatabase? Database;

        public double? GroundElevationAt(Airport airport, double latitude, double longitude) =>
            Database?.GroundElevationAt(airport, latitude, longitude);

        public bool HasRunwayElevations(Airport airport) => Database?.HasRunwayElevations(airport) == true;
    }
}
