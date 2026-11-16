# Changes

## 0.9.0-beta.8 – Logbook, AeRodri's open points, report folder, window position

- **16 new cabin crews by AeRodri (RealFlightOps):** Australia, Canada, Hong Kong, India, Ireland, Japan, Kenya,
  New Zealand, Nigeria, Philippines, Singapore, South Africa, Spain, Tanzania, United Kingdom and USA – all recorded and
  produced by AeRodri himself. Settings → Cabin announcements → Cabin crew; Flarelog downloads the chosen crew once
  (about 30 MB each). Thank you, AeRodri!
- **Landing callout with sign:** "Touchdown with minus 356 feet per minute"
- **More accurate landing rate:** it now uses the sink rate in the last second before touchdown. Before, the least
  favourable of several values counted, including a 2-second average that still contained the sink before the flare –
  landings looked harder than they were.
- **Start the flight with a button:** Settings → Flight recording → "Start recording only with Start". Recording – and
  the welcome announcement – then begin only when you click Start in the flight log.
- **Finish the flight manually:** turn off "Finish the flight automatically at block on" – the flight stays open and the
  taxi track keeps being drawn until you click "Finish". Automatic stays the default.
- **Approach subdivided:** "Initial approach" passing FL100, "Approach" at 5,000 ft above the airport, "Final approach"
  from 3,000 ft
- **Window size limited** to at most 1600 × 1000 so the layout stays intact

- **Logbook:** new icon in the left bar. Shows all your flights from the report folder with date, route, aircraft,
  air time, distance and landing rate – plus totals (flights, air and block time, distance, airports, avg. landing
  rate) and a search. A click opens the flight with its track on the map, all data and the flight log. No airline
  or account needed. Test flights can be deleted right in the logbook. Idea by AeRodri.
- **Choose your report folder:** Settings → Flight reports: enter a path, "Apply" – existing reports can move
  along. "Default" goes back to `Documents/Flarelog/Flights`. Requested by ApfelFlieger.
- **Window position:** Flarelog opens where it was last (Windows and Mac). If that spot is no longer on any screen, it
  opens centred. Requested by ApfelFlieger.
- **Mac menu tidied up:** Copy and Paste are in the "Edit" menu, "Quit Flarelog" is the last line of the app menu –
  like other Mac apps. Requested by ApfelFlieger.
- **Own confirmation dialogs:** questions like "Delete flight?" or "Stop recording?" now appear in the Flarelog
  style with clear buttons (e.g. "Delete"/"Cancel") instead of the system box saying "localhost:8080 says"

## 0.9.0-beta.7 – Safer quitting, automatic backup, tidier settings

- **New soundpack 1.1 by AeRodri:** cleaner recordings, balanced volume, sound effects and boarding ambience in
  the welcome. Unchanged old announcements in the sound folder are replaced automatically; your own files stay.
- **Mac: "No" when quitting now really cancels.** Previously Flarelog quit anyway and the flight was lost. You are
  asked on the red button and on Cmd+Q; "Yes" saves the flight so far as a partial report.
- **Automatic backup:** a flight in progress is saved every minute – after a crash or power cut at most the last
  minute is missing. The final report replaces the backup.
- **Report folder is now called "Flights"** (previously "Fluege"); existing reports move automatically
- **Visible sound folder:** the announcements are in `Documents/Flarelog/Sounds` and can be replaced with your own
  there; "Restore defaults" brings back the originals
- **Discard SimBrief plan** with a button; without "Load automatically on start" Flarelog starts without a plan
- **Settings reordered:** language, SimBrief, time and weather, cabin announcements, landing callout, flight reports,
  Aerofly and about
- Left bar tidied up: no logo and no folder icon anymore (report folder is in the settings), selection highlighted instantly
- Waypoint tag appears above the waypoint, no longer under the mouse pointer
- Recording started in flight: air time and distance from the start of recording; no more empty partial reports
- Mac: window opens at a size that fits every MacBook; full version number (with "beta")
- **Mac (Apple Silicon):** the first launch after a longer break could fail with "quit unexpectedly". The window
  library is now shipped signed inside Flarelog.app instead of being unpacked at every launch.

## 0.9.0-beta.6 – Mac app with its own window, cabin announcements, feedback from the Mac test

- **Standalone Mac app:** on the Mac, Flarelog opens its own program window with a Dock icon – no browser needed
  anymore. Features, detection, reports and settings stay the same.
- Closing the window during a flight asks first and saves the flight so far as a partial report
- External links (update, PIREP on the VA website, map sources) open in your normal browser
- If the window can't be opened, Flarelog starts in the browser as before (note in `~/Library/Logs/Flarelog/Flarelog.log`)
- **Cabin announcements:** welcome at the gate, pushback and safety briefing, climb (passing FL100), cruise altitude, descent and
  after landing – automatically matched to the flight phase, each announcement once per flight. Soundpack by AeRodri.
- Settings → Cabin announcements: on/off, "Test announcement", "Own announcements …" (WAV files with the same name in
  `Documents/Flarelog/Sounds` replace the built-in ones)
- **What's new:** after an update Flarelog shows the changes once; later via Settings → About → "What's new?"
- **Touchdown detected right away, also on uneven runways:** if the runway dropped away after the touchdown point
  (e.g. LEAS 29), Flarelog took it for floating and only detected the landing during the rollout – with a wrong
  landing rate, ground speed and rollout distance, and the callout 10–15 s late. Now the actual touchdown counts and
  the callout comes after a few seconds.
- **Standing on the runway is no longer block on:** recording continues until you stop at the parking position
- **Smoother taxi track:** Aerofly reports the position only to about 10 m; on the ground the line is now smoothed
- **Clearer interface:** every icon on the left opens its own view instead of a window over the map; the flight log
  stays visible. Making the window smaller no longer covers the flight log.
- **Settings in one place:** time and weather for Aerofly are now in the settings
- **Discreet update notice:** a red dot on the gear icon and a short message instead of a fixed button at the top
- **Flight log in past tense with altitude:** e.g. "Descent started from FL150", "Step climb from FL350"
- **Waypoints on the map:** the name appears as an easy-to-read light tag on hover

## 0.9.0-beta.4 – Feedback from the first Mac tests

- **Landing rate with sign:** the landing popup and landing card now show the rate with a minus, matching the flight
  report (e.g. −77 fpm), instead of a positive value
- **SimBrief route easier to see:** departure and arrival airports in red, waypoints and route line in orange
- **English instructions:** `README.en.md` and `docs/PILOTS.md`
- **Airport names in the report:** code and name always match (previously e.g. "EDDL" could appear with the name of the
  actual departure airport)
- **Repositioned into the air:** moving the aircraft from the ground into the air (e.g. onto final) no longer counts as
  a takeoff, but as "recording started in flight"

## 0.9.0-beta.3 – English

- **English interface:** Flarelog speaks German or English – automatically based on the system language or switchable
  under Settings → Language. Also applies to the flight log, messages and flight reports.

## 0.9.0-beta.2 – Mac version

- **Mac version:** Flarelog.app for macOS 12+ (Apple Silicon and Intel). Runs in the background, the interface opens
  in the browser; API key in the Keychain, landing callout with the Mac voice

## 0.9.0-beta.1 – first public beta

- Live tracking via the UDP broadcast of Aerofly FS 4, flight phases and pause detection
- Landing rate with voice callout, bounces, final approach stability, rollout, departure and landing runway
  (runway slope is taken into account, floating is detected)
- Time acceleration and slew detection, check of the aircraft loaded in Aerofly against the booking
- SimBrief: load plan, route on the map, route overview (SID, enroute, STAR), planned vs. actual
- Virtual airlines with phpVMS 7: load bookings, submit PIREPs (also free flights), airline edition via `airline.json`
- Live tracking: flight on the airline's live map from the moment you start taxiing
- "Copy data" for manual PIREP forms
- Weather window with METAR and TAF; optionally real time and departure weather when Aerofly starts
- Flight reports as text and JSON
- Steep runways like Courchevel: downhill takeoff and uphill landing are detected, landing rate relative to the slope
- Bounces: the first ground contact counts; flaring doesn't count as a bounce
- Altimeter set wrong (Aerofly sends the indicated altitude) is compensated
- Traffic patterns and stop-and-go: no false go-around, a new takeoff is detected
