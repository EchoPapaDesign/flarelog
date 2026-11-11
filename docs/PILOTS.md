# Guide for pilots

*[Deutsche Version](PILOTEN.md)*

## 1. Install

1. Download `Flarelog.exe` from **Releases** and put it anywhere, e.g. on your desktop.
2. Start it. The first time:
   - **"Windows protected your PC"** → *More info* → *Run anyway*.
     This appears because the program is not digitally signed.
   - **Windows Firewall** → allow access for *private networks*. This is how the data from Aerofly arrives.
   - The program downloads the airport and runway list once (about 15 MB in total).

**macOS:** Download the matching ZIP file (`AppleSilicon` for M1–M4, `Intel` for older Macs), unzip it and
drag `Flarelog.app` into *Applications*. When you open it for the first time, macOS reports that the app has not been
verified → *System Settings → Privacy & Security* → **Open Anyway** next to Flarelog. **Allow** incoming connections.
Flarelog opens its own window with a Dock icon; quit by closing the window or pressing Cmd+Q. In Aerofly, use the
same settings as on Windows
(broadcast to 127.0.0.1, port 49002).

There is no installer. To remove Flarelog, delete the .exe, and optionally also
`%APPDATA%\Flarelog` (settings) and `%LOCALAPPDATA%\Flarelog` (airport lists).
On macOS: `~/Library/Application Support/Flarelog` and `~/Library/Caches/Flarelog`, log file in
`~/Library/Logs/Flarelog.log`.

## 2. Set up Aerofly FS 4

Settings → **Miscellaneous settings**:

| Setting | Value |
|---|---|
| Broadcast flight info to IP address | **on** |
| Broadcast IP address | **127.000.000.001** |
| Broadcast IP port | **49002** |

When everything works, the program shows **"Connected to Aerofly FS 4"** in green at the top.

## 3. SimBrief (optional)

Gear icon at the bottom left → enter your SimBrief username or pilot ID → **Load plan**.
Flarelog loads the flight plan you generated last in SimBrief. With "Load automatically on start" this happens every
time Flarelog starts.

The route appears on the map, together with the next waypoint, distance to destination, ETA and distance to TOD.

At the top right, the route overview shows departure with runway and SID, the waypoints, and the STAR and destination.

## Time and weather (optional)

At the top right of the program: **Time** "Aerofly" or "Real", **Weather** "Aerofly" or "Real (Departure)".
With "Real", Flarelog writes the real UTC time or the current METAR weather of the departure airport (wind, gusts as
turbulence, visibility, up to three cloud layers) into Aerofly. Departure airport: SimBrief plan, otherwise the booking,
otherwise the ICAO code entered in the **Departure** field, otherwise the airport where the aircraft is or was last
parked.

This only works **before Aerofly starts** – Aerofly reads the values at startup. If Aerofly is running, Flarelog applies
the change after Aerofly is closed, for the next start. During the flight Flarelog changes nothing; the weather stays as
it was at startup. Aerofly cannot take over precipitation, thunderstorms, temperature or air pressure. Before every
change, Flarelog creates a backup file `main.mcf.flarelog-backup`.

## 4. Virtual airline (optional, phpVMS 7)

1. **Get your API key:** Log in to your VA's website → profile. The key is shown there, or there is a button
   "New API key". The key works like a password – don't share it.
   - Careful: a **new** key makes the old one invalid. If you already use another ACARS for the VA
     (e.g. Stratos or vmsACARS), it must use the same key. If your VA doesn't show the key, ask the VA staff.
   - If your VA gave you a ready-made version with `airline.json`, the website is already filled in.
2. In the program, click the **briefcase icon** → enter the VA website (e.g. `https://www.my-va.com`) and your key →
   **Connect & load bookings**. The top then shows "✓ Logged in as …".
3. Book a flight on the VA website → click **Refresh** in the program → **click** the flight (highlighted in green).
   Without a booking you can also fly a **free flight**: enter the flight number when submitting; departure and
   destination are taken from the recording. Whether the VA accepts free flights is up to the VA.
   If a loaded SimBrief plan doesn't match the booking, it is removed.

Whether your VA accepts PIREPs from Flarelog is up to the VA. If in doubt, ask beforehand.

**Live tracking:** If it is switched on in the airline window (default), Flarelog reports the flight to the VA as soon
as you start taxiing – you then appear on their live map and **LIVE** is shown at the top. This requires a booking or a
SimBrief plan, and an aircraft (from the booking or the SimBrief registration). If it doesn't work, the reason is shown
in the airline window and the flight is sent after landing as usual.

## 5. Fly

- Start **parked at the gate or on the apron**, not in the air.
- Start taxiing → the flight log shows "Block off", followed by all phases.
- After touchdown the program announces the landing rate: **Good** up to 240 fpm, **Mediocre** up to 400 fpm,
  **Hard** above that. The landing card also shows bounces, final approach, rollout and runway.
- **Stay at the parking position for 30 seconds** → "Block on", the report is saved.
  Alternatively, click **End** in the flight log.
- Pausing is allowed and is noted in the flight log.

## 6. Submit the PIREP

After block on, **Send to airline** appears in the flight log:

1. Check flight, distance, times and landing rate.
2. **Aircraft:** If it is set in the booking, it is already selected. Otherwise choose it from the list or type the
   registration. Below, Flarelog shows how it compares with the aircraft loaded in Aerofly.
3. Optionally add a remark → **Submit now**.

**Airline without API key or without phpVMS:** After landing, click **Copy data** in the flight log. Flight, times,
distance, landing rate and analysis are then on your clipboard and can be pasted into the manual PIREP form.

Afterwards, **PIREP ✓** takes you to the report on the VA website. If something goes wrong, the program withdraws the
submission and shows the VA's message.

## 7. Other

- **New flight:** in the flight log, after a report has been saved.
- **Reports:** folder icon at the bottom left → `Documents\Flarelog\Fluege` (text and JSON).
- **Landing callout:** gear icon → on/off, "Test voice". An English Windows voice sounds best
  (Windows Settings → Time & language → Language → add English with "Text-to-speech").
  On the Mac, the English voice "Samantha" is used if available.
- **Second monitor:** the display is also available in the browser at `http://localhost:8080`.

## Troubleshooting

| Problem | Solution |
|---|---|
| "No data – Aerofly with broadcast flight info …?" | Check the Aerofly settings from section 2; firewall permission; restart Aerofly |
| "UDP port 49002 is in use" | Is the program already running? Another tool on port 49002? |
| Landing not detected | Roll out until you come to a stop; helicopters are not supported yet |
| "API key was rejected" | Enter the key again; has a new one been generated in the meantime? |
| No aircraft when submitting | Type the registration; the diagnostic line below shows what the VA returned |
| Error when submitting | The message comes from the VA (e.g. "aircraft not at departure airport") – ask the VA staff |
