# Änderungen

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
