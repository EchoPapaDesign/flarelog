<p align="center"><img src="docs/flarelog.png" width="110" alt="Flarelog logo"></p>

<h1 align="center">Flarelog</h1>
<p align="center"><b>ACARS for Aerofly FS 4</b> – live tracking, landing analysis with voice callout,<br>
SimBrief, weather and PIREPs to virtual airlines</p>

<p align="center"><a href="README.md">Deutsch</a> · <b>English</b></p>

> [!IMPORTANT]
> **Flarelog is a beta.** The program is new and is currently being tested by the first pilots.
> **Your feedback is very welcome:** What works, what doesn't, what are you missing?
> → [Report a bug or request a feature](https://github.com/EchoPapaDesign/flarelog/issues/new/choose)

## What is it?

Aerofly FS 4 has no SimConnect interface, so the usual ACARS programs such as smartCARS, vmsACARS or Stratos
don't work with Aerofly. **Flarelog** fills that gap: it reads Aerofly's built-in data broadcast, records your
flight, rates your landing and, if you like, reports the flight to your virtual airline – live during the flight
and as a PIREP afterwards.

Flarelog is free, runs entirely on your computer and needs no installation.

## Features

**Flight tracking**
- Map with flown and planned route, altitude, ground speed, vertical speed, air time, distance to destination,
  next waypoint, ETA
- Automatic flight phases: block off, taxi, takeoff, climb, cruise, descent, approach, landing, block on
- Pauses, time acceleration and slew are detected and noted in the report
- Shows which aircraft is currently loaded in Aerofly

**Landing analysis**
- Landing rate with voice callout: *"Touchdown with 180 feet per minute. Good."*
- Bounces, final approach stability, rollout, departure and landing runway, day/night
- Takes sloped runways into account (including Courchevel) and detects floating over the runway

**Cabin announcements**
- Welcome, safety briefing, climb, cruise, descent and after landing – automatically matched to the flight phase
- Soundpack by AeRodri (RealFlightOps); own announcements can be placed in `Documents/Flarelog/Sounds`

**Planning and weather**
- SimBrief: load your latest flight plan, route on the map, overview with SID, enroute and STAR
- Weather window with current **METAR and TAF** for any airport
- Optional **real time** and **real departure weather** when Aerofly starts

**Virtual airlines (phpVMS 7)**
- Load booked flights – or fly a free flight without a booking
- **Live tracking:** from the moment you start taxiing you appear on your airline's live map
- **PIREP** after the flight: times, distance, landing rate, positions and the full analysis
- Check of the booked aircraft against the one loaded in Aerofly
- For airlines without a suitable interface: **"Copy data"** for the manual PIREP form

**Language**
- German or English – automatically based on your system language, switchable in the settings

**Reports**
- Every flight saved as a text and JSON file in `Documents/Flarelog/Fluege`

## How Flarelog runs on Windows and macOS

Both versions are standalone apps with their own window: `Flarelog.exe` on Windows, `Flarelog.app` with a Dock icon
on macOS. No browser needed. The display is additionally available at `http://localhost:8080`, e.g. for a second
monitor or tablet.

## Quick start

**1. Download**
Get the latest version from [**Releases**](https://github.com/EchoPapaDesign/flarelog/releases):
- **Windows 10/11 (64-bit):** `Flarelog.exe` – put it anywhere, e.g. on your desktop
- **macOS 12 or later:** `Flarelog-macOS-AppleSilicon.zip` (M1/M2/M3/M4) or `Flarelog-macOS-Intel.zip` –
  unzip it and drag `Flarelog.app` into your *Applications* folder. Which Mac do you have? Apple menu →
  *About This Mac* → "Chip" (Apple M…) or "Processor" (Intel).

**2. Start**
*Windows:* Double-click `Flarelog.exe`. On first start:
- "Windows protected your PC" → **More info** → **Run anyway**
  (the program is not digitally signed yet)
- Windows Firewall → allow access for **private networks**

*macOS:* Double-click `Flarelog.app` – Flarelog opens its own window. On first start:
- "Flarelog can't be opened" (not verified by Apple) → **System Settings → Privacy & Security**,
  scroll down to Flarelog → **Open Anyway** → confirm with your password
- "Accept incoming network connections?" → **Allow** (this is how the data from Aerofly arrives)
- To quit: close the window or press Cmd+Q.

**3. Set up Aerofly** – Settings → **Miscellaneous settings**:

| Setting | Value |
|---|---|
| Broadcast flight info to IP address | **on** |
| Broadcast IP address | **127.000.000.001** |
| Broadcast IP port | **49002** |

Flarelog then shows **"Connected to Aerofly FS 4"** in green at the top.

**4. Optional setup**
- **SimBrief:** gear icon → SimBrief username → **Load plan**
- **Airline:** briefcase icon → your VA's website and personal API key → **Connect & load bookings**
  → click on the booked flight
- **Time/weather:** gear icon → "Time and weather in Aerofly" → **Real**, before you start Aerofly

**5. Fly**
Start at the gate or on the apron and begin taxiing – Flarelog records everything automatically. After landing,
stay at your parking position for 30 seconds (or click **End**), then click **"Send to airline"** in the flight log.

📖 Detailed guide with troubleshooting: **[docs/PILOTS.md](docs/PILOTS.md)**

## Feedback

Flarelog gets better with your feedback. Please report:
- **Bugs** – e.g. landing detected incorrectly, PIREP rejected, program freezes
- **Wishes** – what you are missing in flight or with your airline

→ [**Create a new report**](https://github.com/EchoPapaDesign/flarelog/issues/new/choose)
(free GitHub account required). The flight report helps most (`.txt` from `Documents/Flarelog/Fluege`),
plus aircraft, airport and what exactly happened.

## For VA operators

Flarelog uses the standard ACARS interface of phpVMS 7 – nothing needs to be installed on the airline's side.
Details (currently in German): **[docs/VA-BETREIBER.md](docs/VA-BETREIBER.md)**

## What the Aerofly broadcast provides – and what it doesn't

Aerofly sends position, GPS altitude, track, ground speed as well as heading, pitch and roll. Flarelog
calculates everything else from that. **Not available** are fuel, weights, landing gear, flaps, wind,
height above ground and an "on ground" signal. That's why the landing rate is calculated from GPS altitude.

## Known limitations

- Windows 10/11 (64-bit) and macOS 12 or later. The Mac version is new and has had little testing so far.
- **Helicopters** are not supported yet (hovering, landings without rollout).
- Aerofly doesn't provide fuel, gear, flaps, lights or g-force – these values are missing from the PIREP.
- Time and weather can only be set before Aerofly starts, not during the flight.
- Updates are announced but not installed automatically.
- The program is not digitally signed; Windows and macOS show a warning on first start.

## Privacy

- Everything runs locally. There is no telemetry and no Flarelog server.
- The airline API key is stored encrypted – on Windows with the Windows Data Protection API
  (`%APPDATA%\Flarelog\settings.json`), on macOS in the Keychain – and is only sent to the airline you entered.
- Network access: SimBrief (flight plan), your airline (bookings, PIREP), OurAirports
  (airport and runway list, every 30 days), OpenStreetMap (map tiles), Aviation Weather Center (METAR, only with
  "Weather: Real"), GitHub (update check).

## For developers

See the [German README](README.md#für-entwickler) for the project structure and build instructions
(requires the .NET 8 SDK).

## Data and credits

- Airports and runways: [OurAirports](https://ourairports.com/data/) (public domain)
- Map: © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors, [Leaflet](https://leafletjs.com)
- Flight plans: [SimBrief](https://www.simbrief.com)
- Cabin announcements: Flarelog Soundpack by AeRodri / [RealFlightOps](https://www.youtube.com/@RealFlightOpsOfficial) – see [sounds/CREDITS.md](sounds/CREDITS.md)
- Aerofly FS is a trademark of IPACS. This project is not an official IPACS product.

## License

MIT – see [LICENSE](LICENSE). © 2026 EP Design
