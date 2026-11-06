# Änderungen

## In Arbeit

- Keine Landeansage mehr in der Luft, wenn ohne Bahndaten nur die Platzhöhe bekannt ist (Abfangen knapp unter Platzhöhe)
- Steile Bahnen (z. B. Courchevel): Landung wird auch erkannt, wenn die Höhe beim Ausrollen bergauf steigt;
  die Landerate wird gegenüber der Bahnneigung berechnet, auch wenn mit Leistung bergauf gerollt wird
- Stop-and-go / Platzrunden mit Ausrollen: erneuter Start wird erkannt, gewertet wird die letzte Landung
- Durchstarten nur, wenn der Anflug unter 1000 ft über Platz ging – Manöver in der Platzrunde zählen nicht mehr
- Flight Log: Landungs-Box und Fußzeile überlappen nicht mehr
- Wetter: Ein eingetragener Flugplatz wird verworfen, sobald Aerofly an einem anderen Platz steht
- Höhenmesser verstellt: Aerofly sendet die angezeigte Höhe. Wird am Höhenmesser gedreht (z. B. auf 0 am
  Abflugplatz), gleicht Flarelog den Sprung aus – Abheben, Bodenhöhe am Ziel und Positionen für die Airline stimmen wieder
- Landung mit Bounces: Gewertet wird der erste Bodenkontakt (auf 1,5 ft an der Rollhöhe), nicht erst das
  spätere sanfte Ausrollen. Ein Wiederansteigen im Flare ohne Bodenkontakt zählt weiterhin nicht als Bounce.

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
