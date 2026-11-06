using System.Globalization;
using System.Text;
using AeroflyAcars.Core;
using AeroflyAcars.Core.Airline;
using AeroflyAcars.Core.Airports;
using AeroflyAcars.Core.Reporting;
using AeroflyAcars.Core.Sim;
using AeroflyAcars.Core.SimBrief;
using AeroflyAcars.Core.Testing;
using AeroflyAcars.Core.Tracking;

Console.OutputEncoding = Encoding.UTF8;
// Absichtlich deutsche Kultur: Zahlen müssen trotzdem mit Punkt gelesen werden.
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

var t = new TestRunner();

t.Test("XGPS wird gelesen (auch unter de-DE)", () =>
{
    t.True(ForeFlightProtocol.TryParseLine("XGPSAerofly FS 4,10.00512,53.63021,16.2,233.50,51.4\0", out var m), "parse");
    var gps = t.Is<GpsMessage>(m);
    t.Equal("Aerofly FS 4", gps.Source);
    t.Near(10.00512, gps.Longitude, 1e-9);
    t.Near(53.63021, gps.Latitude, 1e-9);
    t.Near(16.2, gps.AltitudeMslMeters, 1e-9);
    t.Near(233.5, gps.TrackDeg, 1e-9);
    t.Near(51.4, gps.GroundSpeedMps, 1e-9);
});

t.Test("XATT wird gelesen, Zusatzfelder werden ignoriert", () =>
{
    t.True(ForeFlightProtocol.TryParseLine("XATTAerofly FS 4,233.1,-2.5,10.25,0,0,0", out var m), "parse");
    var att = t.Is<AttitudeMessage>(m);
    t.Near(233.1, att.HeadingDeg, 1e-9);
    t.Near(-2.5, att.PitchDeg, 1e-9);
    t.Near(10.25, att.RollDeg, 1e-9);
});

t.Test("Ungültige Zeilen werden verworfen", () =>
{
    t.False(ForeFlightProtocol.TryParseLine("", out _), "leer");
    t.False(ForeFlightProtocol.TryParseLine("XGPSAerofly,1,2", out _), "zu kurz");
    t.False(ForeFlightProtocol.TryParseLine("XGPSAerofly,abc,53,1,1,1", out _), "keine Zahl");
    t.False(ForeFlightProtocol.TryParseLine("XGPSAerofly,10,95,1,1,1", out _), "Breite > 90");
    t.False(ForeFlightProtocol.TryParseLine("XGPSAerofly FS 4,0.000000,0.000000,0.0,0.00,0.0", out _), "0/0 beim Laden");
    t.False(ForeFlightProtocol.TryParseLine("XTRAFFICAerofly,1,2,3,4,5,6,7,8,X", out _), "Traffic");
});

t.Test("Format und Parse sind umkehrbar", () =>
{
    var original = new GpsMessage("Test", 10.123456, 53.654321, 1234.5, 90.25, 77.7);
    t.True(ForeFlightProtocol.TryParseLine(ForeFlightProtocol.Format(original), out var m), "parse");
    var gps = t.Is<GpsMessage>(m);
    t.Near(original.Latitude, gps.Latitude, 1e-6);
    t.Near(original.AltitudeMslMeters, gps.AltitudeMslMeters, 1e-9);
});

t.Test("Vertikalgeschwindigkeit aus der Höhe", () =>
{
    var b = new SampleBuilder();
    var start = DateTimeOffset.UnixEpoch;
    FlightSample? last = null;
    for (int i = 0; i <= 10; i++)
    {
        // 1000 fpm = 16,67 ft/s = 5,08 m/s
        double altM = 100 + i * 1000.0 / 60.0 / 3.280839895;
        last = b.Add(new GpsMessage("T", 10, 53, altM, 0, 50), start.AddSeconds(i));
    }
    t.Near(1000, last!.VerticalSpeedFpm, 5);
});

t.Test("Kompletter künstlicher Flug: Phasen, Zeiten, Landerate", () =>
{
    var tracker = Fly(null);
    var phases = tracker.Events.Where(e => e.PhaseChange).Select(e => e.Phase).ToArray();
    var expected = new[]
    {
        FlightPhase.TaxiOut, FlightPhase.TakeoffRoll, FlightPhase.Climb, FlightPhase.Cruise,
        FlightPhase.Descent, FlightPhase.Approach, FlightPhase.Landing, FlightPhase.TaxiIn, FlightPhase.Arrived,
    };
    t.Equal(string.Join(" → ", expected), string.Join(" → ", phases));
    t.True(tracker.IsFinished, "Flug beendet");
    t.Range(tracker.LandingRateFpm!.Value, -200, -90, "Landerate");
    t.Range((tracker.Touchdown!.Value - tracker.Takeoff!.Value).TotalMinutes, 8, 13, "Flugzeit (min)");
    t.True(tracker.BlockOn > tracker.Touchdown, "Block on nach Aufsetzen");
    t.True(tracker.BlockOff < tracker.Takeoff, "Block off vor Abheben");
    t.Equal(0, tracker.GoArounds);

    var report = FlightReportBuilder.Build(tracker, DateTimeOffset.UnixEpoch);
    t.Range(report.DistanceFlownNm!.Value, 20, 60, "Strecke (NM)");
    t.Equal("gut", report.LandingRating);
    var text = ReportWriter.ToText(report);
    t.True(text.Contains("Landerate"), "Textbericht enthält Landerate");
    t.True(ReportWriter.ToJson(report).Contains("\"landingRateFpm\""), "JSON enthält landingRateFpm");
});

t.Test("Mit SimBrief-Plan: Reiseflughöhe und Zielelevation werden genutzt", () =>
{
    var plan = new FlightPlan
    {
        OriginIcao = "EDDH",
        DestinationIcao = "EDDH",
        DestinationElevationFt = ScriptedFlight.GroundElevationFt,
        DestinationLatitude = 53.6304,
        DestinationLongitude = 9.9883,
        CruiseAltitudeFt = 4500,
        PlannedAirTime = TimeSpan.FromMinutes(10),
    };
    var tracker = Fly(plan);
    t.True(tracker.IsFinished, "Flug beendet");
    t.True(tracker.Events.Any(e => e.Phase == FlightPhase.Cruise), "Reiseflug erkannt");
    var report = FlightReportBuilder.Build(tracker, DateTimeOffset.UnixEpoch);
    t.True(report.TouchdownDistanceToDestinationNm < 15, "Aufsetzpunkt in Zielnähe");
    t.True(ReportWriter.ToText(report).Contains("Flugzeit"), "Soll/Ist-Zeile vorhanden");
});

t.Test("Aufzeichnung im Flug begonnen", () =>
{
    var tracker = new FlightTracker();
    var b = new SampleBuilder();
    var start = DateTimeOffset.UnixEpoch;
    tracker.Add(b.Add(new GpsMessage("T", 10, 53, 3000, 90, 120), start)!);
    t.True(tracker.StartedAirborne, "StartedAirborne");
    t.Equal(FlightPhase.Cruise, tracker.Phase);
});

t.Test("SimBrief-JSON wird gelesen (Strings, einzelner Fix)", () =>
{
    const string json = """
    {
      "fetch": { "status": "Success" },
      "params": { "airac": "2610" },
      "general": { "icao_airline": "DLH", "flight_number": "123", "initial_altitude": "36000",
                   "route": "DCT XAMOD Y900", "route_distance": "245" },
      "atc": { "callsign": "DLH123" },
      "aircraft": { "icaocode": "A320" },
      "origin": { "icao_code": "EDDH", "name": "Hamburg", "pos_lat": "53.630389", "pos_long": "9.988228", "elevation": "53" },
      "destination": { "icao_code": "EDDM", "name": "Munich", "pos_lat": "48.353783", "pos_long": "11.786086", "elevation": "1487" },
      "times": { "est_time_enroute": "3420", "est_block": "4200" },
      "navlog": { "fix": { "ident": "XAMOD", "pos_lat": "52.1", "pos_long": "10.5", "altitude_feet": "36000" } },
      "fuel": {}
    }
    """;
    var plan = SimBriefParser.Parse(json);
    t.Equal("DLH123", plan.Callsign);
    t.Equal("EDDM", plan.DestinationIcao);
    t.Equal("2610", plan.Airac);
    t.Near(1487, plan.DestinationElevationFt!.Value, 1e-9);
    t.Near(36000, plan.CruiseAltitudeFt!.Value, 1e-9);
    t.Equal(TimeSpan.FromSeconds(4200), plan.PlannedBlockTime);
    t.Equal(3, plan.Waypoints.Count);
    t.Equal("XAMOD", plan.Waypoints[1].Ident);
});

t.Test("SimBrief-Fehler wird gemeldet", () =>
{
    try
    {
        SimBriefParser.Parse("""{ "fetch": { "status": "Error: Unknown UserID" } }""");
        t.Fail("keine Exception");
    }
    catch (SimBriefException ex)
    {
        t.True(ex.Message.Contains("Unknown UserID"), "Fehlertext");
    }
});

t.Test("Landeansage: Text und Bewertung", () =>
{
    t.Equal("Touchdown with 177 feet per minute. Good.", LandingCallout.Text(-177.2));
    t.Equal("Touchdown with 320 feet per minute. Mediocre.", LandingCallout.Text(-320));
    t.Equal("Touchdown with 512 feet per minute. Hard.", LandingCallout.Text(-512));
    t.Equal("Good", LandingCallout.Rating(-240));
    t.Equal("Mediocre", LandingCallout.Rating(-400));
});

t.Test("Landeansage mit Zielelevation: einmal, ca. 2–3 s nach dem Aufsetzen", () =>
{
    var calls = new List<(FlightSample Sample, double Rate)>();
    var tracker = FlyWith(null, ScriptedFlight.GroundElevationFt, (s, r) => calls.Add((s, r)), out var announcedAt);
    t.Equal(1, calls.Count);
    var delay = (announcedAt[0] - tracker.Touchdown!.Value).TotalSeconds;
    t.Range(delay, 1, 4, "Verzögerung (s)");
    t.Near(tracker.LandingRateFpm!.Value, calls[0].Rate, 1e-9);
});

t.Test("Landeansage ohne Zielelevation: einmal, bei der Bestätigung", () =>
{
    var calls = 0;
    var tracker = FlyWith(null, null, (_, _) => calls++, out var announcedAt);
    t.Equal(1, calls);
    t.True(announcedAt[0] > tracker.Touchdown!.Value.AddSeconds(5), "erst nach dem Ausrollen");
});

const string AirportCsv = """
"id","ident","type","name","latitude_deg","longitude_deg","elevation_ft","continent","iso_country","iso_region","municipality","scheduled_service","icao_code","iata_code","gps_code","local_code"
2212,"EDDH","large_airport","Hamburg Helmut Schmidt Airport",53.630402,9.988230,53,"EU","DE","DE-HH","Hamburg","yes","EDDH","HAM","EDDH",""
2213,"DE-0001","heliport","Klinik, Hamburg",53.6390,10.0030,60,"EU","DE","DE-HH","Hamburg","no","","","",""
2214,"EDDW","large_airport","Bremen Airport",53.0475,8.78667,14,"EU","DE","DE-HB","Bremen","yes","EDDW","BRE","EDDW",""
2215,"EDHX","small_airport","Testfeld Nord",53.7170,10.0700,53,"EU","DE","DE-SH","","no","EDHX","","EDHX",""
9999,"XX-CLOSED","closed","Alter Platz",53.6400,10.0000,50,"EU","DE","DE-HH","","no","","","",""
""";

t.Test("Flughafenliste: CSV mit Anführungszeichen und Komma im Namen", () =>
{
    var db = AirportDatabase.ParseCsv(new StringReader(AirportCsv));
    t.Equal(4, db.Count);
    var heli = db.Nearest(53.6390, 10.0030, 0.1);
    t.Equal("Klinik, Hamburg", heli?.Name);
    t.Equal("EDDW", db.Nearest(53.05, 8.79, 5)?.Ident);
    t.True(db.Nearest(50.0, 8.0, 5) is null, "nichts im Umkreis");
});

t.Test("Flughafenliste: Flughafen schlägt nahen Hubschrauberlandeplatz", () =>
{
    var db = AirportDatabase.ParseCsv(new StringReader(AirportCsv));
    // Position näher am Heliport (0,2 NM) als am Flughafenbezugspunkt (0,55 NM)
    t.Equal("EDDH", db.Nearest(53.6370, 9.9990, 5)?.Ident);
});

t.Test("Mit Flughafenliste: Abflug/Ziel erkannt, Ansage ~2–3 s nach dem Aufsetzen", () =>
{
    var db = AirportDatabase.ParseCsv(new StringReader(AirportCsv));
    var tracker = FlyWith(null, null, null, out var announcedAt, airports: db);
    t.Equal("EDDH", tracker.DepartureAirport?.Ident);
    t.Equal("EDHX", tracker.ArrivalAirport?.Ident);
    t.Equal(1, announcedAt.Count);
    t.Range((announcedAt[0] - tracker.Touchdown!.Value).TotalSeconds, 1, 4, "Verzögerung (s)");
    var report = FlightReportBuilder.Build(tracker, DateTimeOffset.UnixEpoch);
    t.Equal("EDDH", report.Origin);
    t.True(ReportWriter.ToText(report).Contains("EDDH → EDHX"), "Kopfzeile mit Plätzen");
});

t.Test("Pause im Anflug zählt nicht als Landung", () =>
{
    var tracker = FlyWith(null, null, null, out var announcedAt, pauseInStage: "Anflug", pauseSeconds: 40);
    t.Equal(1, tracker.PauseCount);
    t.Range(tracker.PausedTime.TotalSeconds, 35, 45, "Pausendauer (s)");
    t.Equal(1, announcedAt.Count);
    t.Range(tracker.LandingRateFpm!.Value, -200, -90, "Landerate");
    t.True(tracker.Events.Any(e => e.Text == "Simulator pausiert"), "Log: pausiert");
    t.True(tracker.Events.Any(e => e.Text == "Simulator fortgesetzt"), "Log: fortgesetzt");
    t.True(tracker.IsFinished, "Flug beendet");
});

t.Test("Hohe Senderate mit doppelten Positionen beim Rollen: keine Pause", () =>
{
    var tracker = new FlightTracker();
    var b = new SampleBuilder();
    var start = DateTimeOffset.UnixEpoch;
    double lat = 53.0;
    // 20 Pakete pro Sekunde, Position ändert sich nur alle 2 s (wie bei grober Auflösung), 8 kt Rollen
    for (int i = 0; i < 20 * 60; i++)
    {
        double sec = i / 20.0;
        if (i % 40 == 0) lat += 8 * 2 / 3600.0 / 60.0;
        var sample = b.Add(new GpsMessage("T", 10, Math.Round(lat, 5), 10, 0, 8 / Geo.KnotsPerMeterPerSecond),
            start.AddSeconds(sec));
        if (sample is not null) tracker.Add(sample);
    }
    t.Equal(0, tracker.PauseCount);
    t.Equal(FlightPhase.TaxiOut, tracker.Phase);
});

t.Test("Langsames Flugzeug hält im Anflug die Höhe: keine Landung", () =>
{
    var tracker = new FlightTracker();
    var b = new SampleBuilder();
    var start = DateTimeOffset.UnixEpoch;
    double lat = 53.0, alt = 2000;
    int sec = 0;
    void Feed(double gsKt, double vsFpm, int seconds)
    {
        for (int i = 0; i < seconds; i++, sec++)
        {
            alt += vsFpm / 60;
            lat += gsKt / 3600 / 60;   // nach Norden
            var sample = b.Add(new GpsMessage("T", 10, lat, Math.Round(alt / Geo.FeetPerMeter, 1), 0, gsKt / Geo.KnotsPerMeterPerSecond),
                start.AddSeconds(sec));
            tracker.Add(sample!);
        }
    }
    // Start-Simulation: am Boden losrollen, abheben
    alt = 100;
    Feed(0, 0, 5); Feed(15, 0, 10); Feed(45, 0, 5); Feed(45, 600, 120);
    // Sinken auf Platzrundenhöhe, dann 2 Minuten Höhe halten bei 45 kt
    Feed(45, -500, 60); Feed(45, 0, 120); Feed(44, 0, 30);
    t.True(tracker.Touchdown is null, "keine Landung erkannt");
    t.True(tracker.Phase.IsAirborne(), $"noch in der Luft (Phase {tracker.Phase})");
});

t.Test("Routenfortschritt: nächster Wegpunkt, DTG, TOD", () =>
{
    var route = new List<Waypoint>
    {
        new("AAAA", 50.0, 10.0, null), new("WPT1", 51.0, 10.0, null), new("TOD", 52.0, 10.0, null), new("BBBB", 53.0, 10.0, null),
    };
    var p = RouteProgress.Compute(route, 50.5, 10.0)!;
    t.Equal("WPT1", p.Next.Ident);
    t.Near(30, p.DistanceToNextNm, 0.5);
    t.Near(150, p.DistanceToGoNm, 1);
    t.Near(90, p.DistanceToTodNm!.Value, 1);
    var q = RouteProgress.Compute(route, 52.5, 10.0)!;
    t.Equal("BBBB", q.Next.Ident);
    t.True(q.DistanceToTodNm is null, "TOD schon passiert");
});

t.Test("phpVMS: Pilot wird gelesen", () =>
{
    const string json = """
    { "data": { "id": 501, "pilot_id": 1001, "ident": "XVA1001", "name": "Max M.", "curr_airport_id": "KDFW", "airline_id": 3,
                "airline": { "icao": "XVA", "name": "Example Virtual" }, "rank": { "name": "Captain" } } }
    """;
    var p = PhpVmsParser.ParseUser(json);
    t.Equal("XVA1001 Max M.", p.Display);
    t.Equal("KDFW", p.CurrentAirport);
    t.Equal("XVA", p.AirlineIcao);
    t.Equal("Captain", p.Rank);
    t.Equal("3", p.AirlineId);
});

t.Test("Airline-Edition: airline.json", () =>
{
    var p = AirlinePreset.Parse("""
    {
      // Kommentare und ein Komma am Ende sind erlaubt
      "Name": "Example Virtual",
      "url": " https://www.example-va.de ",
    }
    """);
    t.Equal("Example Virtual", p?.Name);
    t.Equal("https://www.example-va.de", p?.Url);
    t.True(AirlinePreset.Parse("""{ "name": "X", "url": "javascript:alert(1)" }""")?.Url is null, "nur http(s)");
    t.True(AirlinePreset.Parse("kein json") is null, "ungültig");
    t.True(AirlinePreset.Parse("{}") is null, "leer");
});

t.Test("phpVMS: Buchungen (Bids mit Flug und als reine Flugliste)", () =>
{
    const string bids = """
    { "data": [
      { "id": 77, "user_id": 501, "flight_id": "abc",
        "flight": { "id": "abc", "ident": "XVA123", "flight_number": 123, "airline": { "icao": "XVA" },
                    "dpt_airport_id": "eddw", "arr_airport_id": "EDDM", "route": "DCT TOBAK", "level": 340,
                    "distance": { "mi": 345.1, "nmi": 300.2, "km": 555.9 }, "flight_time": 75,
                    "subfleets": [ { "type": "A320", "name": "Airbus A320" }, { "type": "A320", "name": "A320 neo" } ] } },
      { "id": 78, "flight": { "id": "kaputt" } }
    ] }
    """;
    var list = PhpVmsParser.ParseBids(bids);
    t.Equal(1, list.Count);
    var b = list[0];
    t.Equal("XVA123", b.Ident);
    t.Equal("EDDW", b.Departure);
    t.Equal("EDDM", b.Arrival);
    t.Equal("77", b.BidId);
    t.Near(300.2, b.DistanceNm!.Value, 1e-9);
    t.Equal(75, b.FlightTimeMin);
    t.Equal(1, b.AircraftTypes.Count);

    const string flights = """
    { "data": [ { "id": "xyz", "airline": { "icao": "XVA" }, "flight_number": "9", "dpt_airport_id": "KDFW",
                  "arr_airport_id": "KAUS", "distance": 165 } ] }
    """;
    var f = PhpVmsParser.ParseBids(flights).Single();
    t.Equal("XVA9", f.Ident);
    t.True(f.BidId is null, "keine Bid-ID");
    t.Near(165, f.DistanceNm!.Value, 1e-9);
});

t.Test("phpVMS: Flotte, PIREP-ID, Fehlermeldungen, Flugnummer", () =>
{
    const string fleet = """
    { "data": [ { "id": 3, "type": "B738", "name": "Boeing 737-800",
                  "aircraft": [ { "id": 41, "registration": "D-AXVA", "icao": "B738", "airport_id": "kdfw", "state": 0 },
                                { "id": 42, "registration": "D-AXVB", "icao": "B738", "airport_id": "KCLE", "state": 2 } ] },
                { "id": 4, "type": "C172", "aircraft": [] } ] }
    """;
    var list = PhpVmsParser.ParseFleet(fleet);
    t.Equal(2, list.Count);
    t.Equal("41", list[0].Id);
    t.Equal("KDFW", list[0].AirportId);
    t.Equal("B738", list[0].SubfleetType);
    t.Equal(2, list[1].State);

    var paged = PhpVmsParser.ParseFleetPage("""{ "data": [ { "id": 5, "type": "C172", "aircraft": { "data": [ { "id": 7, "registration": "D-EXVA" } ] } } ], "meta": { "last_page": 3 } }""");
    t.Equal(1, paged.Aircraft.Count);
    t.Equal(1, paged.Subfleets);
    t.Equal(3, paged.LastPage);
    var direct = PhpVmsParser.ParseFleetPage("""{ "data": [ { "id": 9, "registration": "N172AB", "subfleet": { "type": "C172" } } ] }""");
    t.Equal("C172", direct.Aircraft[0].SubfleetType);
    t.Equal("N172AB", PhpVmsParser.ParseAircraft("""{ "data": { "id": 9, "registration": "N172AB" } }""")!.Registration);

    t.Equal("abc123", PhpVmsParser.ParseId("""{ "data": { "id": "abc123", "state": 0 } }"""));
    t.Equal("Aircraft is not at the departure airport",
        PhpVmsParser.ParseError("""{ "error": { "status": 400, "message": "Aircraft is not at the departure airport" } }"""));
    t.Equal("The given data was invalid. – flight_time: The flight time field is required.",
        PhpVmsParser.ParseError("""{ "message": "The given data was invalid.", "errors": { "flight_time": ["The flight time field is required."] } }"""));
    t.True(PhpVmsParser.ParseError("<html>") is null, "kein JSON");

    var bids = PhpVmsParser.ParseBids("""
    { "data": [ { "id": 1, "flight": { "id": "f1", "ident": "XV215/C.FTR/L.1", "airline_id": 1, "flight_number": "215",
      "route_code": "FTR", "route_leg": "1", "dpt_airport_id": "KDFW", "arr_airport_id": "KCLE" } } ] }
    """);
    var b = bids.Single();
    t.Equal("XV215", b.ShortIdent);
    t.Equal("C.FTR/L.1", b.IdentSuffix);
    t.Equal("1", b.AirlineId);
    t.Equal("215", b.FlightNumber);
    t.Equal("FTR", b.RouteCode);
    t.True(b.AircraftId is null, "kein Flugzeug in der Buchung");

    var withAircraft = PhpVmsParser.ParseBids("""
    { "data": [ { "id": 2, "aircraft_id": 17, "aircraft": { "id": 17, "registration": "N172XV", "icao": "C172" },
      "flight": { "id": "f2", "ident": "XV1001/C.TEST", "airline_id": 1, "flight_number": "1001",
                  "dpt_airport_id": "KDFW", "arr_airport_id": "KDFW" } } ] }
    """).Single();
    t.Equal("17", withAircraft.AircraftId);
    t.Equal("N172XV", withAircraft.AircraftRegistration);
    t.Equal("C172", withAircraft.AircraftIcao);
});

t.Test("Aerofly-Flugzeug aus main.mcf und Abgleich mit der Buchung", () =>
{
    const string mcf = """
    <[file][][]
        <[tmsettings_aircraft][aircraft][]
            <[string8][name][c172]>
            <[string8][paintscheme][default]>
        >
        <[tmsettings_flight][flight][]
            <[string8][name][something_else]>
        >
    >
    """;
    var sel = AeroflyConfig.ParseSelectedAircraft(mcf)!;
    t.Equal("c172", sel.Name);
    t.Equal("default", sel.Paintscheme);
    t.True(AeroflyConfig.ParseSelectedAircraft("<[file][][]>") is null, "ohne Gruppe");
    t.Equal("Boeing 737-800", AeroflyConfig.AircraftDisplayName("b737_800"));
    t.Equal("Cessna 172", sel.DisplayName == "Cessna 172" ? "Cessna 172" : AeroflyConfig.AircraftDisplayName("c172"));
    t.Equal("XYZ 9", AeroflyConfig.AircraftDisplayName("xyz_9"));

    t.Equal(true, AircraftCheck.Compare("C172", "c172", 61).Ok);
    t.Equal(true, AircraftCheck.Compare("B738", "b737", 145).Ok);
    t.Equal(false, AircraftCheck.Compare("C172", "b737", null).Ok);
    t.Equal(false, AircraftCheck.Compare("B738", null, 60).Ok);
    t.Equal(false, AircraftCheck.Compare("C172", "c172", 140).Ok);
    t.True(AircraftCheck.Compare(null, null, null).Ok is null, "nichts bekannt");
    t.True(AircraftCheck.Compare("CSE", "c172", 61).Ok is null, "Typgruppe CSE: keine Warnung");
});

t.Test("phpVMS: Adresse wird ergänzt", () =>
{
    t.Equal("https://www.example-va.de/", PhpVmsClient.NormalizeBaseUrl("www.example-va.de").ToString());
    t.Equal("https://www.example-va.de/api/user",
        new Uri(PhpVmsClient.NormalizeBaseUrl("https://www.example-va.de/"), "api/user").ToString());
});

t.Test("Auswertung des Testflugs: Bounces, Anflug, Rollout, Zeitraffer, Slew, Rollzeiten", () =>
{
    var tracker = Fly(null);
    var an = FlightAnalysis.Analyze(tracker);
    t.Equal(0, an.Bounces);
    t.Equal("stabil", an.ApproachRating);
    t.Range(an.ApproachVsMeanFpm!.Value, -1100, -600, "Ø Sinkrate Endanflug");
    t.Range(an.RolloutM!.Value, 300, 2500, "Rollout (m)");
    t.Equal(1.0, an.MaxSimRate);
    t.Equal(0, an.SlewEvents);
    t.Range(an.TaxiOut!.Value.TotalSeconds, 60, 120, "Taxi-out (s)");
    t.True(an.TaxiIn is not null, "Taxi-in vorhanden");
    t.True(an.DaylightTakeoff is not null, "Tageslicht bestimmt");
    var text = ReportWriter.ToText(FlightReportBuilder.Build(tracker, DateTimeOffset.UnixEpoch));
    t.True(text.Contains("Bounces:") && text.Contains("Zeitraffer:"), "Auswertung im Textbericht");
});

t.Test("Zeitraffer und Slew werden erkannt", () =>
{
    FlightAnalysis Run(double factor, bool slew)
    {
        var tr = new FlightTracker();
        var b = new SampleBuilder();
        var start = DateTimeOffset.UnixEpoch;
        double lat = 50, lon = 8;
        // am Boden losrollen, dann 3 min mit 120 kt (laut GS) geradeaus – die Position wandert factor-mal so schnell
        for (int i = 0; i < 240; i++)
        {
            double gs = i < 20 ? 10 : 120;
            (lat, lon) = Geo.Move(lat, lon, 90, gs * factor / 3600);
            if (slew && i == 150) (lat, lon) = Geo.Move(lat, lon, 90, 5);
            var smp = b.Add(new GpsMessage("T", lon, lat, 300, 90, gs / Geo.KnotsPerMeterPerSecond), start.AddSeconds(i));
            if (smp is not null) tr.Add(smp);
        }
        return FlightAnalysis.Analyze(tr);
    }
    t.Equal(1.0, Run(1, false).MaxSimRate);
    t.Equal(2.0, Run(2, false).MaxSimRate);
    t.Equal(4.0, Run(4, false).MaxSimRate);
    t.Equal(1, Run(1, true).SlewEvents);
    t.Equal(0, Run(4, false).SlewEvents);
});

t.Test("Bahnerkennung aus Position und Kurs", () =>
{
    const string csv = """
"id","airport_ref","airport_ident","length_ft","width_ft","surface","lighted","closed","le_ident","le_latitude_deg","le_longitude_deg","le_elevation_ft","le_heading_degT","le_displaced_threshold_ft","he_ident","he_latitude_deg","he_longitude_deg","he_elevation_ft","he_heading_degT","he_displaced_threshold_ft"
1,1,"XXAA",7300,150,"ASP",1,0,"18",53.02,10.0,50,180,,"36",53.0,10.0,50,0,
2,1,"XXAA",3000,100,"GRS",0,0,"09",,,,90,,"27",,,,270,
3,1,"XXAA",3000,100,"GRS",0,1,"05",53.0,10.0,,45,,"23",53.01,10.01,,225,
""";
    var db = RunwayDatabase.ParseCsv(new StringReader(csv));
    t.Equal(2, db.Count);
    var apt = new Airport("XXAA", "Testplatz", "small_airport", 53.01, 10.0, 50, null, "XXAA");
    t.Equal("36", db.Match(apt, 53.002, 10.0, 2)?.Ident);
    t.Equal("18", db.Match(apt, 53.015, 10.0, 181)?.Ident);
    t.True(db.Match(apt, 53.01, 10.02, 0) is null, "1,3 km neben der Bahn");
    t.Equal("27", db.Match(apt, 53.01, 10.0, 268)?.Ident);   // nur über den Kurs (keine Koordinaten)
});

t.Test("Landung auf geneigter Bahn: kein Fehlalarm im Endanflug, Aufsetzen nach dem Ausschweben", () =>
{
    // Wie KAFW 34R: Schwelle 664 ft, Gegenende 723 ft, Platzhöhe 722 ft. Bahn nach Norden.
    const string rwyCsv = """
"id","airport_ref","airport_ident","length_ft","width_ft","surface","lighted","closed","le_ident","le_latitude_deg","le_longitude_deg","le_elevation_ft","le_heading_degT","le_displaced_threshold_ft","he_ident","he_latitude_deg","he_longitude_deg","he_elevation_ft","he_heading_degT","he_displaced_threshold_ft"
1,1,"XAFW",11000,150,"CON",1,0,"16",33.030,-97.0,723,180,,"34",33.000,-97.0,664,0,
""";
    var runways = RunwayDatabase.ParseCsv(new StringReader(rwyCsv));
    var apt = new Airport("XAFW", "Testplatz", "medium_airport", 33.015, -97.0, 722, null, "XAFW");
    t.Near(664, runways.GroundElevationAt(apt, 33.0, -97.0)!.Value, 0.5);
    t.Near(693.5, runways.GroundElevationAt(apt, 33.015, -97.0)!.Value, 0.5);
    t.True(runways.GroundElevationAt(apt, 32.99, -97.0) is null, "1 km vor der Schwelle nicht auf der Bahn");

    var airports = new AirportDatabase(new[] { apt });
    var tracker = new FlightTracker(null, null, airports, runways);
    var announced = new List<(DateTimeOffset Time, double Rate)>();
    DateTimeOffset now = default;
    tracker.TouchdownDetected += (_, r) => announced.Add((now, r));

    const double refFt = 5;   // Aerofly meldet die Höhe des Flugzeugbezugspunkts, ein paar Fuß über dem Boden
    double Ground(double lat) => 664 + (723 - 664) * Math.Clamp((lat - 33.0) / 0.030, 0, 1);
    var b = new SampleBuilder();
    var start = new DateTimeOffset(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);
    double lat = 32.936, alt = 2200, gs = 90;
    int sec = 0;
    DateTimeOffset contact = default;
    void Step(double vsFpm, double decelKt = 0)
    {
        gs = Math.Max(0, gs - decelKt);
        lat += gs / 3600.0 / 60.0;
        alt += vsFpm / 60.0;
        double floor = Ground(lat) + refFt;
        if (alt < floor) alt = floor;
        now = start.AddSeconds(++sec);
        var smp = b.Add(new GpsMessage("T", -97.0, lat, Math.Round(alt / Geo.FeetPerMeter, 1), 0, gs / Geo.KnotsPerMeterPerSecond), now);
        if (smp is not null) tracker.Add(smp);
    }

    while (alt > 664 + 130 + refFt) Step(-600);                  // Endanflug
    for (int i = 0; i < 6; i++) Step(0);                          // Abfangen ~130 ft über Schwellenhöhe, 500 m vor der Bahn
    t.Equal(0, announced.Count);
    while (lat < 33.0005) Step(-500);                              // weiter bis über die Schwelle
    while (alt > Ground(lat) + refFt + 20) Step(-500);
    while (alt > Ground(lat) + refFt + 6) Step(-150);             // Flare
    Step(0); Step(0);                                              // ausschweben, ein paar Fuß über der Bahn
    while (alt > Ground(lat) + refFt + 0.01) Step(-60);            // sanft absetzen
    contact = now;
    for (int i = 0; i < 40 && gs > 0; i++) Step(0, decelKt: 2);   // ausrollen (Höhe folgt der Bahn)

    t.True(tracker.Touchdown is not null, "Landung erkannt");
    t.Range((tracker.Touchdown!.Value - contact).TotalSeconds, -1.5, 1.5, "Aufsetzzeitpunkt (s ggü. Bodenkontakt)");
    t.Range(tracker.LandingRateFpm!.Value, -200, -30, "Landerate nach dem Ausschweben");
    t.Equal(1, announced.Count);
    t.True(announced[0].Time >= contact, "Ansage nicht vor dem Bodenkontakt");
    t.Range((announced[0].Time - contact).TotalSeconds, 0, 4, "Ansage kurz nach dem Aufsetzen");
});

t.Test("METAR lesen (Europa, USA, CAVOK, Meilenbrüche)", () =>
{
    var a = Metar.Parse("LOWI 061220Z 25008KT 9999 FEW045 BKN080 08/02 Q1018 NOSIG")!;
    t.Equal("LOWI", a.Station);
    t.Equal(250, a.WindDirection);
    t.Equal(8, a.WindKt);
    t.Equal(9999, a.VisibilityM);
    t.Equal(2, a.Clouds.Count);
    t.Equal("BKN", a.Clouds[1].Cover);
    t.Equal(8000, a.Clouds[1].BaseFt);
    t.Equal("250/08 kt, Sicht ≥ 10 km, FEW 4.500 ft BKN 8.000 ft", a.Summary);

    var b = Metar.Parse("METAR KDFW 061253Z 18015G25KT 10SM SCT030 OVC250 22/12 A2992 RMK AO2 SLP132")!;
    t.Equal(25, b.GustKt);
    t.Range(b.VisibilityM!.Value, 16000, 16200, "10 SM");
    t.Equal(25000, b.Clouds[1].BaseFt);

    var c = Metar.Parse("EDDH 061220Z VRB02KT CAVOK 10/05 Q1020")!;
    t.True(c.WindDirection is null, "VRB");
    t.Equal(10000, c.VisibilityM);
    t.Equal(0, c.Clouds.Count);

    var d = Metar.Parse("KSFO 061256Z 28010KT 1 1/2SM BR OVC004 12/11 A3001")!;
    t.Range(d.VisibilityM!.Value, 2400, 2430, "1 1/2 SM");
    t.Equal(400, d.Clouds[0].BaseFt);
    t.True(Metar.Parse("") is null && Metar.Parse("kein metar") is null, "ungültig");

    var taf = MetarClient.FormatTaf("TAF LOWI 061100Z 0612/0712 26008KT 9999 FEW040\n      TEMPO 0614/0618 30015G25KT PROB30 TEMPO 0618/0622 4000 SHRA BECMG 0700/0702 VRB03KT FM070600 08005KT CAVOK")!;
    t.Equal("TAF LOWI 061100Z 0612/0712 26008KT 9999 FEW040\n  TEMPO 0614/0618 30015G25KT\n  PROB30 TEMPO 0618/0622 4000 SHRA\n  BECMG 0700/0702 VRB03KT\n  FM070600 08005KT CAVOK", taf);
});

t.Test("Zeit und Wetter in Aeroflys main.mcf setzen", () =>
{
    var mcf = string.Join("\r\n", new[]
    {
        "        <[tm_time_utc][time_utc][]",
        "            <[int32][time_year][2022]>",
        "            <[int32][time_month][8]>",
        "            <[int32][time_day][15]>",
        "            <[float64][time_hours][18.1308324655725]>",
        "        >",
        "        <[float64][visibility][0.825930228184443]>",
        "        <[tmsettings_wind][wind][]",
        "            <[float64][strength][0.484883741638623]>",
        "            <[float64][direction_in_degree][340.493021697365]>",
        "            <[float64][turbulence][0.1]>",
        "            <[float64][thermal_activity][0]>",
        "        >",
        "        <[tmsettings_clouds][clouds][]",
        "            <[float64][cumulus_density][0.390261639433447]>",
        "            <[float64][cumulus_height][0.5]>",
        "            <[float64][cumulus_mediocris_density][0.15]>",
        "            <[float64][cumulus_mediocris_height][0.6]>",
        "            <[float64][cirrus_height][0.65]>",
        "            <[float64][cirrus_density][0.8]>",
        "        >",
        "        <[float64][graphics_hdr_strength][0.3]>",
    });
    var metar = Metar.Parse("LOWI 061220Z 25016KT 7500 SCT045 12/02 Q1018")!;
    var r = AeroflyConditions.Apply(mcf, new DateTimeOffset(2026, 11, 6, 12, 30, 0, TimeSpan.Zero), metar, out int changed);
    t.Equal(12, changed);
    t.True(r.Contains("<[int32][time_year][2026]>") && r.Contains("<[int32][time_month][11]>") && r.Contains("<[float64][time_hours][12.5]>"), "Zeit");
    t.True(r.Contains("<[float64][visibility][0.5]>"), "7500 m = 50 %");
    t.True(r.Contains("<[float64][strength][1]>"), "16 kt = 1");
    t.True(r.Contains("<[float64][direction_in_degree][250]>"), "Richtung");
    t.True(r.Contains("<[float64][cumulus_density][0.45]>") && r.Contains("<[float64][cumulus_height][0.45]>"), "SCT 4500 ft");
    t.True(r.Contains("<[float64][cirrus_density][0]>") && r.Contains("<[float64][cirrus_height][0.65]>"), "ohne dritte Schicht");
    t.True(r.Contains("<[float64][graphics_hdr_strength][0.3]>") && r.Contains("<[float64][thermal_activity][0]>"), "Rest bleibt");
    t.Near(0, AeroflyConditions.WindStrength(0), 1e-9);
    t.Near(1, AeroflyConditions.WindStrength(16), 1e-9);

    var onlyTime = AeroflyConditions.Apply(mcf, DateTimeOffset.UnixEpoch, null, out int n);
    t.Equal(4, n);
    t.True(onlyTime.Contains("<[float64][visibility][0.825930228184443]>"), "Wetter unverändert");
});

t.Test("Sonnenstand", () =>
{
    t.Range(Geo.SunElevationDeg(new DateTimeOffset(2026, 6, 21, 11, 0, 0, TimeSpan.Zero), 53.63, 9.99), 58, 61, "Hamburg Mittag Sommer");
    t.Range(Geo.SunElevationDeg(new DateTimeOffset(2026, 12, 21, 23, 0, 0, TimeSpan.Zero), 53.63, 9.99), -70, -50, "Hamburg Mitternacht Winter");
});

t.Test("Versionsvergleich für die Update-Prüfung", () =>
{
    t.True(UpdateChecker.IsNewer("v0.9.0", "0.8.0"), "0.9.0 > 0.8.0");
    t.True(UpdateChecker.IsNewer("v1.0.0", "0.10.2"), "1.0.0 > 0.10.2");
    t.False(UpdateChecker.IsNewer("v0.8.0", "0.8.0"), "gleich");
    t.False(UpdateChecker.IsNewer("v0.7.5", "0.8.0"), "älter");
    t.True(UpdateChecker.IsNewer("v0.9.0-beta.1", "0.8.0"), "Vorabversion zählt über Zahlen");
    t.True(UpdateChecker.IsNewer("v0.9.0-beta.2", "0.9.0-beta.1"), "beta.2 > beta.1");
    t.True(UpdateChecker.IsNewer("v0.9.0", "0.9.0-beta.3"), "fertige Version > Beta");
    t.False(UpdateChecker.IsNewer("v0.9.0-beta.1", "0.9.0"), "Beta < fertige Version");
    t.True(UpdateChecker.IsNewer("v0.9.0-beta.10", "0.9.0-beta.9"), "Zahlen, nicht Text");
    t.False(UpdateChecker.IsNewer("quatsch", "0.8.0"), "ungültig");
    t.True(AppInfo.Version.Length > 0, "Version vorhanden");
});

t.Test("Höhenformat", () =>
{
    t.Equal("4.500 ft", FlightTracker.FormatAltitude(4512));
    t.Equal("FL350", FlightTracker.FormatAltitude(34990));
});

return t.Summary();

static FlightTracker Fly(FlightPlan? plan) => FlyWith(plan, null, null, out _);

static FlightTracker FlyWith(FlightPlan? plan, double? destElevation, Action<FlightSample, double>? onTouchdown,
    out List<DateTimeOffset> announcedAt, IAirportLookup? airports = null, string? pauseInStage = null, int pauseSeconds = 0)
{
    var tracker = new FlightTracker(plan, destElevation, airports);
    var times = new List<DateTimeOffset>();
    DateTimeOffset now = default;
    tracker.TouchdownDetected += (s, r) => { times.Add(now); onTouchdown?.Invoke(s, r); };
    announcedAt = times;
    var builder = new SampleBuilder();
    var start = new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
    var frames = ScriptedFlight.Generate();
    // Optional: Pause mitten in einer Flugphase – Aerofly sendet dann immer wieder dasselbe Paket.
    int pauseAt = pauseInStage is null ? -1 : frames.ToList().FindIndex(f => f.Stage == pauseInStage) + 20;
    var shift = TimeSpan.Zero;
    for (int i = 0; i < frames.Count; i++)
    {
        var f = frames[i];
        var time = start + f.Offset + shift;
        now = time;
        builder.Add(f.Attitude, time);
        var sample = builder.Add(f.Gps, time);
        if (sample is not null) tracker.Add(sample);

        if (i == pauseAt)
        {
            for (int p = 1; p <= pauseSeconds; p++)
            {
                now = time.AddSeconds(p);
                builder.Add(f.Attitude, now);
                var frozen = builder.Add(f.Gps, now);
                if (frozen is not null) tracker.Add(frozen);
            }
            shift += TimeSpan.FromSeconds(pauseSeconds);
        }
    }
    return tracker;
}

internal sealed class TestRunner
{
    private int _passed;
    private readonly List<string> _failed = new();

    public void Test(string name, Action body)
    {
        try
        {
            body();
            _passed++;
            Console.WriteLine($"  OK    {name}");
        }
        catch (Exception ex)
        {
            _failed.Add(name);
            Console.WriteLine($"  FEHLER {name}: {ex.Message}");
        }
    }

    public int Summary()
    {
        Console.WriteLine();
        Console.WriteLine(_failed.Count == 0
            ? $"Alle {_passed} Tests bestanden."
            : $"{_failed.Count} von {_passed + _failed.Count} Tests fehlgeschlagen.");
        return _failed.Count == 0 ? 0 : 1;
    }

    public void Fail(string message) => throw new Exception(message);
    public void True(bool? condition, string what) { if (condition != true) Fail($"erwartet wahr: {what}"); }
    public void False(bool condition, string what) { if (condition) Fail($"erwartet falsch: {what}"); }

    public void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) Fail($"erwartet {expected}, war {actual}");
    }

    public void Near(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance) Fail($"erwartet {expected} ± {tolerance}, war {actual}");
    }

    public void Range(double value, double min, double max, string what)
    {
        if (value < min || value > max) Fail($"{what}: {value:F1} liegt nicht zwischen {min} und {max}");
    }

    public T Is<T>(object? value) where T : class =>
        value as T ?? throw new Exception($"erwartet {typeof(T).Name}, war {value?.GetType().Name ?? "null"}");
}
