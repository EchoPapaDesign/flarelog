# Anleitung für Piloten

*[English version](PILOTS.md)*

## 1. Installieren

1. `Flarelog.exe` aus den **Releases** herunterladen und an einen beliebigen Ort legen, z. B. auf den Desktop.
2. Starten. Beim ersten Mal:
   - **„Der Computer wurde durch Windows geschützt“** → *Weitere Informationen* → *Trotzdem ausführen*.
     Das erscheint, weil das Programm nicht digital signiert ist.
   - **Windows-Firewall** → Zugriff für *private Netzwerke* erlauben. Darüber kommen die Daten von Aerofly.
   - Das Programm lädt einmalig die Flughafen- und Bahnenliste (zusammen ca. 15 MB).

**macOS:** Die passende ZIP-Datei laden (`AppleSilicon` für M1–M4, `Intel` für ältere Macs), entpacken und
`Flarelog.app` in *Programme* ziehen. Beim ersten Öffnen meldet macOS, dass die App nicht geprüft ist →
*Systemeinstellungen → Datenschutz & Sicherheit* → bei Flarelog **Trotzdem öffnen**. Eingehende Verbindungen
**erlauben**. Flarelog öffnet ein eigenes Fenster mit Symbol im Dock; Beenden durch Schließen des Fensters oder
Cmd+Q. In Aerofly dieselben Einstellungen wie unter
Windows (Broadcast an 127.0.0.1, Port 49002).

Es gibt keine Installation. Zum Entfernen die .exe löschen, optional auch
`%APPDATA%\Flarelog` (Einstellungen) und `%LOCALAPPDATA%\Flarelog` (Flughafenlisten).
Unter macOS: `~/Library/Application Support/Flarelog` und `~/Library/Caches/Flarelog`, Protokoll in
`~/Library/Logs/Flarelog.log`.

## 2. Aerofly FS 4 einstellen

Einstellungen → **Sonstige Einstellungen**:

| Einstellung | Wert |
|---|---|
| Sende Fluginformation an IP Addresse | **an** |
| Broadcast IP Addresse | **127.000.000.001** |
| Broadcast IP port | **49002** |

Läuft alles, steht oben im Programm grün **„Verbunden mit Aerofly FS 4“**.

## 3. SimBrief (optional)

Zahnrad unten links → SimBrief-Benutzername oder Pilot-ID eintragen → **Plan laden**.
Geladen wird der zuletzt in SimBrief erzeugte Flugplan. Mit „Beim Start automatisch laden“ geschieht das bei jedem Start.

Die Route erscheint gestrichelt auf der Karte, dazu nächster Wegpunkt, Distanz zum Ziel, ETA und Distanz zum TOD.

Rechts oben zeigt die Routenübersicht Abflug mit Bahn und SID, die Wegpunkte sowie STAR und Ziel.

## Uhrzeit und Wetter (optional)

Rechts oben im Programm: **Uhrzeit** „Aerofly“ oder „Real“, **Wetter** „Aerofly“ oder „Real (Departure)“.
Mit „Real“ trägt Flarelog die echte UTC-Zeit bzw. das aktuelle METAR-Wetter des Abflugplatzes (Wind, Böen als
Turbulenz, Sicht, bis zu drei Wolkenschichten) in Aerofly ein. Abflugplatz: SimBrief-Plan, sonst Buchung, sonst der
im Feld **Departure** eingetragene ICAO-Code, sonst der Platz, an dem das Flugzeug gerade steht bzw. zuletzt stand.

Das geht nur **vor dem Start von Aerofly** – Aerofly liest die Werte beim Start. Läuft Aerofly, holt Flarelog das nach
dem Beenden für den nächsten Start nach. Während des Flugs ändert Flarelog nichts; das Wetter bleibt so, wie es beim
Start war. Niederschlag, Gewitter, Temperatur und Luftdruck kann Aerofly nicht übernehmen. Vor jeder Änderung wird
`main.mcf.flarelog-backup` angelegt.

## 4. Virtuelle Airline (optional, phpVMS 7)

1. **API-Schlüssel besorgen:** Auf der Webseite deiner VA einloggen → Profil. Dort steht der Schlüssel oder es gibt
   einen Knopf „Neuer API Key“. Der Schlüssel ist wie ein Passwort – nicht weitergeben.
   - Achtung: Ein **neuer** Schlüssel macht den alten ungültig. Nutzt du für die VA schon ein anderes ACARS
     (z. B. Stratos oder vmsACARS), muss es denselben Schlüssel verwenden. Zeigt die VA den Schlüssel nicht an,
     frag die VA-Leitung.
   - Hat dir deine VA eine fertige Version mit `airline.json` gegeben, ist die Webseite schon eingetragen.
2. Im Programm auf das **Koffer-Symbol** → Webseite der VA (z. B. `https://www.meine-va.de`) und Schlüssel eintragen →
   **Verbinden & Buchungen laden**. Oben steht dann „✓ Angemeldet als …“.
3. Auf der VA-Webseite einen Flug buchen → im Programm **Aktualisieren** → Flug **anklicken** (grün markiert).
   Ohne Buchung geht es auch als **freier Flug**: Beim Einreichen die Flugnummer eintragen, Abflug und Ziel kommen
   aus der Aufzeichnung. Ob die VA freie Flüge annimmt, entscheidet sie.
   Passt ein geladener SimBrief-Plan nicht zur Buchung, wird er entfernt.

Ob deine VA PIREPs von Flarelog annimmt, entscheidet die VA. Frag im Zweifel vorher nach.

**Live-Tracking:** Ist es im Airline-Fenster eingeschaltet (Standard), meldet Flarelog den Flug beim Losrollen an die
VA – du erscheinst dann auf ihrer Live-Karte, oben steht **LIVE**. Dafür braucht es eine Buchung oder einen
SimBrief-Plan und ein Flugzeug (aus der Buchung oder das SimBrief-Kennzeichen). Klappt das nicht, steht der Grund im
Airline-Fenster und der Flug geht wie bisher nach der Landung raus.

## 5. Fliegen

- Am **Gate oder auf dem Vorfeld stehend** beginnen, nicht in der Luft.
- Losrollen → das Flight Log zeigt „Block off“, danach alle Phasen.
- Nach dem Aufsetzen sagt das Programm die Landerate an: **Good** bis 240 fpm, **Mediocre** bis 400 fpm,
  darüber **Hard**. In der Landekarte stehen außerdem Bounces, Endanflug, Rollout und Bahn.
- An der Parkposition **30 Sekunden stehen bleiben** → „Block on“, der Bericht wird gespeichert.
  Alternativ im Flight Log **Beenden** klicken.
- Pausieren ist erlaubt und wird im Flight Log vermerkt.

## 6. PIREP einreichen

Nach dem Block on erscheint im Flight Log **An Airline senden**:

1. Flug, Strecke, Zeiten und Landerate prüfen.
2. **Flugzeug:** Ist es bei der Buchung festgelegt, ist es schon ausgewählt. Sonst aus der Liste wählen oder
   das Kennzeichen eintippen. Darunter steht der Abgleich mit dem in Aerofly gewählten Flugzeug.
3. Optional eine Bemerkung → **Jetzt einreichen**.

**Airline ohne API-Schlüssel oder ohne phpVMS:** Nach der Landung im Flight Log **Daten kopieren** klicken. Flug, Zeiten,
Distanz, Landerate und Auswertung liegen dann in der Zwischenablage und lassen sich ins manuelle PIREP-Formular einfügen.

Danach führt **PIREP ✓** zum Bericht auf der VA-Webseite. Geht etwas schief, zieht das Programm die Anmeldung
zurück und zeigt die Meldung der VA an.

## 7. Sonstiges

- **Neuer Flug:** Im Flight Log nach einem gespeicherten Bericht.
- **Berichte:** Ordner-Symbol unten links → `Dokumente\Flarelog\Fluege` (Text und JSON).
- **Landeansage:** Zahnrad → an/aus, „Stimme testen“. Eine englische Windows-Stimme klingt am besten
  (Windows-Einstellungen → Zeit und Sprache → Sprache → Englisch mit „Sprachausgabe“ hinzufügen).
  Am Mac wird die englische Stimme „Samantha“ verwendet, falls vorhanden.
- **Zweiter Monitor:** Das Display ist zusätzlich im Browser unter `http://localhost:8080` erreichbar.

## Probleme

| Problem | Lösung |
|---|---|
| „Keine Daten – Aerofly mit Sende Fluginformation …?“ | Aerofly-Einstellungen aus Abschnitt 2 prüfen; Firewall-Freigabe; Aerofly neu starten |
| „UDP-Port 49002 ist belegt“ | Läuft das Programm schon? Ein anderes Tool auf Port 49002? |
| Landung nicht erkannt | Bis zum Stillstand ausrollen; Hubschrauber werden noch nicht unterstützt |
| „API-Schlüssel wurde abgelehnt“ | Schlüssel neu eintragen; wurde inzwischen ein neuer erzeugt? |
| Keine Flugzeuge beim Einreichen | Kennzeichen eintippen; die Diagnosezeile darunter zeigt, was die VA geliefert hat |
| Fehler beim Einreichen | Die Meldung stammt von der VA (z. B. „Flugzeug nicht am Abflugort“) – bei der VA-Leitung nachfragen |
