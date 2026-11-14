# Änderungen

## 0.9.0-beta.6 – Mac-App mit eigenem Fenster, Kabinenansagen, Feedback aus dem Mac-Test

- **Eigenständige Mac-App:** Flarelog öffnet auf dem Mac ein eigenes Programmfenster mit Symbol im Dock – kein
  Browser mehr nötig. Funktionen, Erkennung, Berichte und Einstellungen bleiben unverändert.
- Fenster schließen während eines Flugs fragt nach und speichert den bisherigen Flug als Teilbericht
- Externe Links (Update, PIREP auf der VA-Seite, Kartenquellen) öffnen im normalen Browser
- Lässt sich das Fenster nicht öffnen, startet Flarelog wie bisher im Browser (Hinweis in `~/Library/Logs/Flarelog/Flarelog.log`)
- **Kabinenansagen:** Begrüßung am Gate, Pushback und Sicherheitseinweisung, Steigflug (ab FL100), Reiseflughöhe, Sinkflug und
  nach der Landung – automatisch passend zur Flugphase, jede Ansage einmal pro Flug. Soundpack von AeRodri.
- Einstellungen → Kabinenansagen: an/aus, „Ansage testen“, „Eigene Ansagen …“ (gleichnamige WAV-Dateien in
  `Dokumente/Flarelog/Sounds` ersetzen die mitgelieferten)
- **Was ist neu:** Nach einem Update zeigt Flarelog einmal die Änderungen an; später über Einstellungen → Über →
  „Was ist neu?“
- **Landung sofort erkannt, auch auf unebenen Bahnen:** Fiel die Bahn hinter dem Aufsetzpunkt ab (z. B. LEAS 29),
  hielt Flarelog das für Ausschweben und erkannte die Landung erst beim Ausrollen – mit falscher Landerate,
  Groundspeed und Ausrollstrecke und einer Ansage 10–15 s zu spät. Jetzt zählt das Aufsetzen, die Ansage kommt
  nach wenigen Sekunden.
- **Stehen auf der Bahn ist kein Block on mehr:** Die Aufzeichnung läuft weiter, bis an der Parkposition gehalten wird
- **Ruhigere Rollspur:** Aerofly meldet die Position nur auf etwa 10 m genau; am Boden wird die Linie jetzt geglättet
- **Übersichtlichere Oberfläche:** Jedes Symbol links öffnet eine eigene Ansicht statt eines Fensters über der
  Karte; das Flight Log bleibt sichtbar. Beim Verkleinern des Fensters überdeckt nichts mehr das Flight Log.
- **Einstellungen an einem Ort:** Uhrzeit und Wetter für Aerofly stehen jetzt in den Einstellungen
- **Update-Hinweis dezent:** Roter Punkt am Zahnrad und kurzer Hinweis statt eines festen Knopfs oben
- **Flight Log in Vergangenheitsform mit Höhe:** z. B. „Sinkflug ab FL150 begonnen“, „Stufensteigflug ab FL350“
- **Wegpunkte auf der Karte:** Name beim Drüberfahren als gut lesbares helles Kärtchen

## 0.9.0-beta.4 – Feedback der ersten Mac-Tests

- **Landerate mit Vorzeichen:** Hinweis nach der Landung und Landekarte zeigen die Landerate jetzt wie der Flugbericht
  mit Minus (z. B. −77 fpm) statt als positiven Wert
- **Besser sichtbare SimBrief-Route:** Abflug- und Zielflughafen rot, Wegpunkte und Routenlinie orange
- **Englische Anleitung:** `README.en.md` und `docs/PILOTS.md`
- **Flughafennamen im Bericht:** Code und Name passen immer zusammen (vorher konnte z. B. „EDDL“ mit dem Namen des
  tatsächlichen Abflugplatzes erscheinen)
- **In die Luft versetzt:** Wird das Flugzeug vom Boden in die Luft versetzt (z. B. in den Anflug), gilt das nicht mehr
  als Start, sondern als „Aufzeichnung im Flug begonnen“

## 0.9.0-beta.3 – English

- **Englische Oberfläche:** Flarelog spricht Deutsch oder Englisch – automatisch nach der Systemsprache oder
  umschaltbar unter Einstellungen → Sprache. Gilt auch für Flight Log, Meldungen und Flugberichte.

## 0.9.0-beta.2 – Mac-Version

- **Mac-Version:** Flarelog.app für macOS 12+ (Apple Silicon und Intel). Läuft im Hintergrund, die Oberfläche
  öffnet sich im Browser; API-Schlüssel im Schlüsselbund, Landeansage mit der Mac-Stimme

## 0.9.0-beta.1 – erste öffentliche Beta

- Live-Tracking über den UDP-Broadcast von Aerofly FS 4, Flugphasen und Pausen-Erkennung
- Landerate mit Sprachansage, Bounces, Stabilität im Endanflug, Rollout, Start- und Landebahn
  (Bahnneigung wird berücksichtigt, Ausschweben erkannt)
- Zeitraffer- und Slew-Erkennung, Abgleich des in Aerofly gewählten Flugzeugs mit der Buchung
- SimBrief: Plan laden, Route auf der Karte, Routenübersicht (SID, Strecke, STAR), Soll-Ist-Vergleich
- Virtuelle Airlines mit phpVMS 7: Buchungen laden, PIREP einreichen (auch freie Flüge),
  Airline-Edition über `airline.json`
- Live-Tracking: Flug ab dem Losrollen auf der Live-Karte der Airline
- „Daten kopieren“ für manuelle PIREP-Formulare
- Wetter-Fenster mit METAR und TAF; auf Wunsch echte Zeit und Abflug-Wetter beim Start von Aerofly
- Flugberichte als Text und JSON
- Steile Bahnen wie Courchevel: Start bergab und Landung bergauf werden erkannt, Landerate gegenüber der Bahnneigung
- Bounces: Gewertet wird der erste Bodenkontakt; Abfangen im Flare zählt nicht als Bounce
- Verstellter Höhenmesser (Aerofly sendet die angezeigte Höhe) wird ausgeglichen
- Platzrunden und Stop-and-go: kein falsches Durchstarten, erneuter Start wird erkannt
