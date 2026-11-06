<img src="docs/flarelog.png" width="96" alt="Flarelog-Logo">

# Flarelog

ACARS für **Aerofly FS 4**: Live-Tracking, Landeauswertung mit Sprachansage, SimBrief-Flugpläne und
PIREPs an virtuelle Airlines mit **phpVMS 7**.

Aerofly FS 4 bietet keine SimConnect-Schnittstelle, deshalb unterstützen die gängigen ACARS-Programme
Aerofly nicht. Flarelog liest stattdessen den eingebauten UDP-Broadcast von Aerofly mit.

## Funktionen

- **Display** im eigenen Fenster: Karte mit geflogener und geplanter Route, Höhe, Groundspeed, Vertikalgeschwindigkeit,
  Air Time, Distanz zum Ziel, nächster Wegpunkt, ETA, Flight Log
- **Flugphasen** automatisch: Rollen, Startlauf, Steig-/Reise-/Sinkflug, Anflug, Landung, Block on;
  Pausen werden erkannt
- **Landung:** Landerate mit Sprachansage („Touchdown with 180 feet per minute. Good.“), Bounces,
  Stabilität im Endanflug, Rollout, Start- und Landebahn
- **Uhrzeit und Wetter:** auf Wunsch echte Zeit und METAR-Wetter des Abflugplatzes beim Start von Aerofly
- **Kontrolle:** Zeitraffer- und Slew-Erkennung, Abgleich des in Aerofly gewählten Flugzeugs mit der Buchung
- **SimBrief:** letzten Flugplan laden, Route auf der Karte, Routenübersicht (SID, Strecke, STAR), Soll-Ist-Vergleich im Bericht
- **Virtuelle Airlines (phpVMS 7):** gebuchte Flüge laden, nach dem Flug PIREP einreichen – inklusive
  Positionen und Auswertung
- **Flugbericht** als Text und JSON in `Dokumente\Flarelog\Fluege`

> **Beta:** Flarelog ist neu. Fehler und Wünsche bitte unter [Issues](https://github.com/EchoPapaDesign/flarelog/issues) melden.

## Download und Start

1. Unter [**Releases**](https://github.com/EchoPapaDesign/flarelog/releases) die neueste `Flarelog.exe` herunterladen.
2. An einen beliebigen Ort legen (z. B. Desktop) und starten. Eine Installation ist nicht nötig, .NET ist enthalten.
3. Windows zeigt beim ersten Start eventuell „Der Computer wurde durch Windows geschützt“, weil das Programm
   (noch) nicht signiert ist: **Weitere Informationen → Trotzdem ausführen**.
4. Fragt die Windows-Firewall nach: Zugriff für **private Netzwerke** erlauben.

Voraussetzung: Windows 10 oder 11 (64 Bit), Aerofly FS 4.

Ausführliche Anleitung: **[docs/PILOTEN.md](docs/PILOTEN.md)**

## Kurz eingerichtet

1. **Aerofly:** Einstellungen → Sonstige Einstellungen → „Sende Fluginformation an IP Addresse“ **an**,
   Broadcast IP Addresse **127.000.000.001**, Broadcast IP port **49002**.
2. **SimBrief (optional):** Zahnrad → SimBrief-Benutzer eintragen → „Plan laden“.
3. **Airline (optional):** Koffer-Symbol → Webseite der VA und persönlichen API-Schlüssel eintragen →
   „Verbinden & Buchungen laden“ → gebuchten Flug anklicken.
4. Fliegen. Nach dem Block on im Flight Log **„An Airline senden“**.

## Für VA-Betreiber

Flarelog nutzt die normale ACARS-Schnittstelle von phpVMS 7 – auf Seiten der Airline muss nichts installiert
werden. Was genau ankommt und was Aerofly nicht liefern kann: **[docs/VA-BETREIBER.md](docs/VA-BETREIBER.md)**

## Was der Aerofly-Broadcast liefert – und was nicht

Aerofly sendet Position, GPS-Höhe, Track, Groundspeed sowie Steuerkurs, Pitch und Roll. Daraus berechnet
Flarelog alles Weitere. **Nicht verfügbar** sind Treibstoff, Gewichte, Fahrwerk, Klappen, Wind,
Höhe über Grund und ein „am Boden“-Signal. Die Landerate wird deshalb aus der GPS-Höhe berechnet.
Hubschrauber werden noch nicht unterstützt (Schwebeflug, Landungen ohne Ausrollen).

## Bekannte Grenzen

- Nur Windows 10/11 (64 Bit). Aerofly für macOS wird nicht unterstützt.
- **Hubschrauber** werden noch nicht unterstützt (Schwebeflug, Landungen ohne Ausrollen).
- Treibstoff, Fahrwerk, Klappen, Lichter und G-Kraft liefert Aerofly nicht – diese Werte fehlen im PIREP.
- Zeit und Wetter lassen sich nur vor dem Start von Aerofly setzen, nicht während des Flugs.
- Updates werden angezeigt, aber nicht automatisch installiert.
- Das Programm ist nicht digital signiert; Windows warnt beim ersten Start.

## Datenschutz

- Alles läuft lokal. Es gibt keine Telemetrie und keinen eigenen Server.
- Der API-Schlüssel der Airline wird mit der Windows-Datenschutz-API verschlüsselt gespeichert
  (`%APPDATA%\Flarelog\settings.json`) und nur an die eingetragene Airline gesendet.
- Netzwerkzugriffe: SimBrief (Flugplan), die eingetragene Airline (Buchungen, PIREP), OurAirports
  (Flughafen- und Bahnenliste, alle 30 Tage), OpenStreetMap (Kartenkacheln), Aviation Weather Center (METAR, nur mit „Wetter: Real“),
  GitHub (Update-Prüfung, falls eingerichtet).

## Für Entwickler

```
AeroflyAcars.sln
src/AeroflyAcars.Desktop    Programm: Flarelog.exe (WinForms + WebView2)
src/AeroflyAcars.Runtime    Programmlogik: Empfang, Auswertung, Berichte, Sprachansage, Display-Server
src/AeroflyAcars.Core       Bibliothek: UDP-Format, Phasen, Auswertung, SimBrief, Flughäfen/Bahnen, phpVMS
src/AeroflyAcars.App        Konsolenversion (Fehlersuche)
src/AeroflyAcars.SimDummy   Aerofly-Ersatz: sendet einen künstlichen 13-Minuten-Flug
tests/AeroflyAcars.SelfTest Selbsttest (ohne Testframework)
publish.cmd                 Erzeugt lokal publish\Flarelog.exe
.github/workflows           Build bei jedem Push, Release mit .exe bei Tag v*
```

Die Projektordner heißen noch `AeroflyAcars.*` (Name bis Version 0.9); das Programm selbst heißt Flarelog.

Voraussetzung: .NET 8 SDK.

```
dotnet build
dotnet run --project tests\AeroflyAcars.SelfTest      # erwartet: "Alle 35 Tests bestanden."
dotnet run --project src\AeroflyAcars.Desktop
dotnet run --project src\AeroflyAcars.SimDummy         # Testflug ohne Aerofly
```

**Neue Version veröffentlichen:** Version in `Directory.Build.props` anpassen, Eintrag in `CHANGELOG.md` ergänzen, dann

```
git tag v0.9.0-beta.1
git push origin v0.9.0-beta.1
```

GitHub baut, testet und legt ein Release mit `Flarelog.exe` an (Versionen mit `-` als Vorabversion).
Das Programm meldet neue Versionen über `GitHubRepository` in `src/AeroflyAcars.Core/AppInfo.cs`.

### Wie die Erkennung funktioniert

| Ereignis | Regel |
|---|---|
| Block off | Groundspeed > 3 kt |
| Startlauf | Groundspeed ≥ 40 kt |
| Abheben | Höhe > Bodenhöhe beim Rollen + 15 ft |
| Steig-/Sinkflug | berechnete Vertikalgeschwindigkeit > +300 / < −300 fpm (5 s bestätigt) |
| Reiseflug | Höhe hält sich; mit SimBrief: ab geplanter Höhe − 1000 ft |
| Anflug | Sinken unter 3000 ft über Platzhöhe |
| Aufsetzen | Sinken endet, Position läuft weiter, Höhe bleibt stehen; bestätigt bei < 50 kt und mind. 8 kt langsamer |
| Landerate | Vertikalgeschwindigkeit der letzten 2 s vor dem Aufsetzen |
| Pause | Position steht ≥ 3 s, obwohl das Flugzeug laut Groundspeed > 50 m weit gekommen wäre |
| Zeitraffer | gefahrene Strecke / (Groundspeed × Zeit), Median über 5 s |
| Slew | Sprung > 0,3 NM schneller als max(250 kt, 4,5 × Groundspeed) oder > 2000 ft Höhe |
| Block on | 30 s Stillstand nach dem Rollen |

## Daten und Danksagung

- Flughäfen und Bahnen: [OurAirports](https://ourairports.com/data/) (gemeinfrei)
- Karte: © [OpenStreetMap](https://www.openstreetmap.org/copyright)-Mitwirkende, [Leaflet](https://leafletjs.com)
- Flugpläne: [SimBrief](https://www.simbrief.com)
- Aerofly FS ist eine Marke der IPACS. Dieses Projekt ist kein offizielles Produkt von IPACS.

## Lizenz

MIT – siehe [LICENSE](LICENSE). © 2026 EP Design
