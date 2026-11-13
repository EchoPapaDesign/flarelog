# Changes

## Unreleased – Cabin announcements and feedback from the Mac test

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
- **Mac:** log file now in `~/Library/Logs/Flarelog/Flarelog.log`

- **Cabin announcements:** welcome at the gate, pushback and safety briefing, climb (passing FL100), cruise altitude, descent and
  after landing – automatically matched to the flight phase, each announcement once per flight. Soundpack by AeRodri.
- Settings → Cabin announcements: on/off, "Test announcement", "Own announcements …" (WAV files with the same name in
  `Documents/Flarelog/Sounds` replace the built-in ones)
- **What's new:** after an update Flarelog shows the changes once; later via Settings → About → "What's new?"

## 0.9.0-beta.5 – Mac app with its own window

- **Standalone Mac app:** on the Mac, Flarelog opens its own program window with a Dock icon – no browser needed
  anymore. Features, detection, reports and settings stay the same.
- Closing the window during a flight asks first and saves the flight so far as a partial report
- External links (update, PIREP on the VA website, map sources) open in your normal browser
- If the window can't be opened, Flarelog starts in the browser as before (note in `~/Library/Logs/Flarelog.log`)

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
