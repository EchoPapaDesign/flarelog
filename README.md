<p align="center"><img src="docs/flarelog.png" width="110" alt="Flarelog-Logo"></p>

<h1 align="center">Flarelog</h1>
<p align="center"><b>ACARS für Aerofly FS 4</b> – Live-Tracking, Landeauswertung mit Sprachansage,<br>
SimBrief, Wetter und PIREPs an virtuelle Airlines</p>

> [!IMPORTANT]
> **Flarelog ist eine Beta.** Das Programm ist neu und wird gerade von den ersten Piloten getestet.
> **Dein Feedback ist dringend erbeten:** Was funktioniert, was nicht, was fehlt dir?
> → [Fehler melden oder Wunsch äußern](https://github.com/EchoPapaDesign/flarelog/issues/new/choose)

## Worum geht es?

Aerofly FS 4 hat keine SimConnect-Schnittstelle – deshalb laufen die üblichen ACARS-Programme wie smartCARS,
vmsACARS oder Stratos nicht mit Aerofly. **Flarelog** schließt diese Lücke: Es liest den eingebauten
Daten-Broadcast von Aerofly mit, zeichnet deinen Flug auf, bewertet deine Landung und meldet den Flug auf Wunsch
an deine virtuelle Airline – live während des Flugs und als PIREP danach.

Flarelog ist kostenlos, läuft komplett auf deinem PC und braucht keine Installation.

## Funktionen

**Flug verfolgen**
- Eigenes Fenster mit Karte: geflogene und geplante Route, Höhe, Groundspeed, Vertikalgeschwindigkeit,
  Air Time, Distanz zum Ziel, nächster Wegpunkt, ETA
- Flugphasen automatisch: Block off, Rollen, Start, Steig-, Reise- und Sinkflug, Anflug, Landung, Block on
- Pausen, Zeitraffer und Slew werden erkannt und im Bericht vermerkt
- Anzeige, welches Flugzeug in Aerofly gerade geflogen wird

**Landung bewerten**
- Landerate mit Sprachansage: *„Touchdown with 180 feet per minute. Good.“*
- Bounces, Stabilität im Endanflug, Rollout, Start- und Landebahn, Tag/Nacht
- Berücksichtigt geneigte Bahnen und erkennt das Ausschweben über der Bahn

**Planen und Wetter**
- SimBrief: letzten Flugplan laden, Route auf der Karte, Übersicht mit SID, Strecke und STAR
- Wetter-Fenster mit aktuellem **METAR und TAF** für jeden Flugplatz
- Auf Wunsch **echte Uhrzeit** und **echtes Wetter** des Abflugplatzes beim Start von Aerofly

**Virtuelle Airlines (phpVMS 7)**
- Gebuchte Flüge laden – oder als freier Flug ohne Buchung
- **Live-Tracking:** Ab dem Losrollen erscheinst du auf der Live-Karte deiner Airline
- **PIREP** nach dem Flug: Zeiten, Strecke, Landerate, Positionen und die komplette Auswertung
- Abgleich des gebuchten Flugzeugs mit dem in Aerofly gewählten
- Für Airlines ohne passende Schnittstelle: **„Daten kopieren“** für das manuelle PIREP-Formular

**Berichte**
- Jeder Flug als Text- und JSON-Datei in `Dokumente\Flarelog\Fluege`

## Schnellstart

**1. Herunterladen**
Unter [**Releases**](https://github.com/EchoPapaDesign/flarelog/releases) die neueste `Flarelog.exe`
herunterladen und an einen beliebigen Ort legen, z. B. auf den Desktop.
Voraussetzung: Windows 10 oder 11 (64 Bit) und Aerofly FS 4.

**2. Starten**
Doppelklick auf `Flarelog.exe`. Beim ersten Start:
- „Der Computer wurde durch Windows geschützt“ → **Weitere Informationen** → **Trotzdem ausführen**
  (das Programm ist noch nicht digital signiert)
- Windows-Firewall → Zugriff für **private Netzwerke** erlauben

**3. Aerofly einstellen** – Einstellungen → **Sonstige Einstellungen**:

| Einstellung | Wert |
|---|---|
| Sende Fluginformation an IP Addresse | **an** |
| Broadcast IP Addresse | **127.000.000.001** |
| Broadcast IP port | **49002** |

Oben in Flarelog steht dann grün **„Verbunden mit Aerofly FS 4“**.

**4. Optional einrichten**
- **SimBrief:** Zahnrad → SimBrief-Benutzername → **Plan laden**
- **Airline:** Koffer-Symbol → Webseite der VA und persönlicher API-Schlüssel → **Verbinden & Buchungen laden**
  → gebuchten Flug anklicken
- **Uhrzeit/Wetter:** rechts oben auf **Real** stellen, bevor du Aerofly startest

**5. Fliegen**
Am Gate oder Vorfeld beginnen und losrollen – Flarelog zeichnet alles automatisch auf. Nach der Landung
30 Sekunden an der Parkposition stehen bleiben (oder **Beenden** klicken), dann im Flight Log
**„An Airline senden“**.

📖 Ausführliche Anleitung mit Problemlösungen: **[docs/PILOTEN.md](docs/PILOTEN.md)**

## Feedback

Flarelog wird mit deinen Rückmeldungen besser. Bitte melde:
- **Fehler** – z. B. Landung falsch erkannt, PIREP abgelehnt, Programm hängt
- **Wünsche** – was dir im Flug oder bei deiner Airline fehlt

→ [**Neue Meldung anlegen**](https://github.com/EchoPapaDesign/flarelog/issues/new/choose)
(kostenloses GitHub-Konto nötig). Am meisten hilft der Flugbericht (`.txt` aus `Dokumente\Flarelog\Fluege`),
dazu Flugzeug, Flughafen und was genau passiert ist.

## Für VA-Betreiber

Flarelog nutzt die normale ACARS-Schnittstelle von phpVMS 7 – auf Seiten der Airline muss nichts installiert
werden. Was genau ankommt und was Aerofly nicht liefern kann: **[docs/VA-BETREIBER.md](docs/VA-BETREIBER.md)**

## Was der Aerofly-Broadcast liefert – und was nicht

Aerofly sendet Position, GPS-Höhe, Track, Groundspeed sowie Steuerkurs, Pitch und Roll. Daraus berechnet
Flarelog alles Weitere. **Nicht verfügbar** sind Treibstoff, Gewichte, Fahrwerk, Klappen, Wind,
Höhe über Grund und ein „am Boden“-Signal. Die Landerate wird deshalb aus der GPS-Höhe berechnet.

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
dotnet run --project tests\AeroflyAcars.SelfTest      # erwartet: "Alle 43 Tests bestanden."
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
| Aufsetzen | Sinken endet über der Bahn (Bahnneigung herausgerechnet), weiteres Sinken = Ausschweben; bestätigt bei < 50 kt und mind. 8 kt langsamer |
| Landerate | Vertikalgeschwindigkeit der letzten 2 s vor dem Aufsetzen, gegenüber der Bahn |
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
