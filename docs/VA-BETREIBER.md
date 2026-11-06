# Informationen für VA-Betreiber (phpVMS 7)

Flarelog bringt Piloten von **Aerofly FS 4** in phpVMS-7-Airlines. Es nutzt ausschließlich die normale
ACARS-Schnittstelle von phpVMS – auf Seiten der Airline muss **nichts installiert** werden.
Ob PIREPs aus Flarelog angenommen werden, entscheidet die Airline.

## Als Airline anbieten (Airline-Edition)

Am einfachsten für Ihre Piloten: `Flarelog.exe` zusammen mit einer Datei `airline.json` im selben Ordner
zum Download anbieten (z. B. als ZIP):

```json
{
  "name": "Meine Virtual Airline",
  "url": "https://www.meine-va.de"
}
```

Beim ersten Start ist Ihre Airline dann schon eingetragen; der Pilot fügt nur noch seinen API-Schlüssel ein.
Auf dem Server ist nichts zu installieren. Aerofly erscheint in Ihren PIREPs als `source_name = "Flarelog"`
und im Feld `Simulator` = `Aerofly FS 4`.

## Anmeldung

Jeder Pilot trägt die Adresse der VA und **seinen persönlichen API-Schlüssel** ein (Header `X-API-Key`).
phpVMS kennt pro Pilot nur einen Schlüssel – nutzt der Pilot zusätzlich ein anderes ACARS, müssen beide denselben
Schlüssel verwenden. Hilfreich ist es, wenn das Pilotenprofil den Schlüssel anzeigt.

## Was gelesen wird

| Aufruf | Zweck |
|---|---|
| `GET /api/user` | Pilot, aktueller Standort |
| `GET /api/user/bids` | gebuchte Flüge, inkl. ggf. bei der Buchung festgelegtem Flugzeug |
| `GET /api/user/fleet`, ersatzweise `GET /api/fleet?page=…` | Flugzeugauswahl beim Einreichen |
| `GET /api/fleet/aircraft/{Kennzeichen}?type=registration` | Flugzeug per Kennzeichen |

## Was gesendet wird

Erst **nach dem Flug** und erst, wenn der Pilot es bestätigt. Mit Buchung werden `flight_id`, Flugnummer und
Strecke aus dem Bid übernommen. **Ohne Buchung** (freier Flug) sendet das Programm die Airline-ID des Piloten
(`/api/user` → `airline_id`), die vom Piloten eingegebene Flugnummer sowie Abflug- und Zielplatz aus der Aufzeichnung,
ohne `flight_id`. Erlauben Ihre Einstellungen nur gebuchte Flüge, erhält der Pilot Ihre Fehlermeldung.

1. `POST /api/pireps/prefile` – `airline_id`, `aircraft_id`, `flight_id`, `flight_number`, `route_code`, `route_leg`,
   `dpt_airport_id`, `arr_airport_id`, `level`, `route`, `planned_distance`, `planned_flight_time`, `block_off_time`,
   `source_name = "Flarelog"`
2. `POST /api/pireps/{id}/acars/position` – Positionen alle 10 s, rund um Start und Landung jede Sekunde:
   `lat`, `lon`, `altitude`/`altitude_msl` (ft), `altitude_agl` (ft, bezogen auf Abflug- bzw. Aufsetzhöhe),
   `heading`, `vs` (fpm), `gs` (kt), `sim_time` und `created_at` (Zeitpunkt der Messung).
   Die Position im Moment des Aufsetzens trägt als `vs` die gemessene Landerate.
3. `POST /api/pireps/{id}/fields` – Zusatzfelder (Text):

   | Feld | Inhalt |
   |---|---|
   | `Simulator` | `Aerofly FS 4` |
   | `Network` | `offline` |
   | `Tracker` | `Flarelog <Version>` |
   | `Sim Aircraft` | in Aerofly gewähltes Flugzeug (z. B. `c172`), sofern lesbar |
   | `Landing Rate (fpm)` | Landerate, negativ |
   | `Touchdown Speed (kt)` | Groundspeed beim Aufsetzen |
   | `Bounces` | Anzahl erneuten Steigens nach dem Aufsetzen |
   | `Approach VS Mean (fpm)`, `Approach VS Sigma (fpm)` | Sinkrate 1000–50 ft über Aufsetzhöhe: Mittel und Streuung |
   | `Rollout (m)` | Aufsetzen bis < 30 kt |
   | `Departure Runway`, `Arrival Runway` | erkannt aus Position/Kurs (OurAirports) |
   | `Taxi Out`, `Taxi In`, `Pause Time` | `hh:mm:ss` |
   | `Sim Rate Max` | höchster erkannter Zeitraffer (1 = Echtzeit) |
   | `Slew Count` | erkannte Positionssprünge |
   | `Daylight Takeoff`, `Daylight Landing` | Tag / Dämmerung / Nacht |

4. `POST /api/pireps/{id}/file` – `distance` (NM), `flight_time` (min), `block_time` (min), `landing_rate` (fpm),
   `block_off_time`, `block_on_time`, `level`, `route`, `notes`, `source_name`.
   `notes` enthält eine lesbare Zusammenfassung der Auswertung.

Schlägt ein Schritt fehl, wird der PIREP mit `PUT /api/pireps/{id}/cancel` zurückgezogen.

## Grenzen

Aerofly FS 4 liefert über den UDP-Broadcast nur Position, GPS-Höhe, Track, Groundspeed, Steuerkurs, Pitch und Roll.
Daher **fehlen**: Treibstoff (Block Fuel, Fuel Used, Nachtanken), Gewichte, Fahrwerk, Klappen, Lichter,
Wind, ein echtes „am Boden“-Signal und G-Kraft. Prüfregeln, die darauf beruhen, sollten Aerofly-PIREPs
(erkennbar an `source_name = "Flarelog"` bzw. dem Feld `Simulator`) ausnehmen.

Die Landerate wird aus der GPS-Höhe berechnet (Auflösung 0,1 m, ein Messpunkt pro Sekunde) und ist eine gute
Schätzung, aber kein Messwert des Simulators.

## Kontakt

Fragen und Wünsche bitte als Issue im GitHub-Repository des Projekts.
