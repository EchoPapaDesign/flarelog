namespace AeroflyAcars.Runtime;

/// <summary>
/// Das Display (Flight Tracking). Leaflet kommt von unpkg, die Kartenkacheln von OpenStreetMap
/// (abgedunkelt per CSS). Alles andere ist hier eingebettet.
/// </summary>
public static class MapPage
{
    public const string Html = """
<!doctype html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Flarelog</title>
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
<style>
  :root {
    --bg: #0c0e11; --panel: #12151a; --panel2: #171b21; --line: #232831;
    --text: #e7e9ec; --muted: #8b93a0; --dim: #5d6573;
    --accent: #3fd68a; --accent-dim: rgba(63, 214, 138, .14);
    --warn: #f2b43c; --bad: #f05a5a; --route: #9aa4b2;
  }
  * { box-sizing: border-box; }
  html, body { height: 100%; margin: 0; background: var(--bg); color: var(--text);
    font: 14px/1.4 "Segoe UI", system-ui, sans-serif; overflow: hidden; }
  .num { font-variant-numeric: tabular-nums; }

  .app { display: grid; grid-template-columns: 60px minmax(0, 1fr) 360px; height: 100vh; }

  /* linke Leiste */
  .rail { background: var(--panel); border-right: 1px solid var(--line); display: flex; flex-direction: column;
    align-items: center; padding-top: 14px; gap: 18px; }
  .logo { width: 34px; height: 34px; display: grid; place-items: center; }
  .logo svg { display: block; }
  .rail .nav { width: 40px; height: 40px; border-radius: 8px; display: grid; place-items: center; color: var(--muted); }
  .rail .nav.active { background: #fff; color: #111; }

  /* Mitte */
  .main { display: grid; grid-template-rows: 44px 48px 64px minmax(0, 1fr); min-width: 0; }
  .topbar { display: flex; align-items: center; gap: 22px; padding: 0 20px; border-bottom: 1px solid var(--line);
    background: var(--panel); }
  .topbar .title { color: var(--muted); }
  .simac { color: var(--text); font-size: 13px; white-space: nowrap; }
  .simac::before { content: "✈"; color: var(--muted); margin-right: 6px; }
  .conn { display: flex; align-items: center; gap: 8px; color: var(--dim); }
  .conn i { width: 8px; height: 8px; border-radius: 50%; background: var(--dim); }
  .conn.ok { color: var(--accent); } .conn.ok i { background: var(--accent); }
  .conn.pause { color: var(--warn); } .conn.pause i { background: var(--warn); }

  .flightbar { display: flex; align-items: center; gap: 16px; padding: 0 20px; border-bottom: 1px solid var(--line);
    white-space: nowrap; overflow: hidden; }
  .tracking { display: flex; align-items: center; gap: 7px; color: var(--muted); }
  .tracking i { width: 8px; height: 8px; border-radius: 50%; background: var(--dim); }
  .tracking.on i { background: var(--accent); }
  .tracking.on { color: var(--text); }
  #fn { font-weight: 700; font-size: 15px; }
  #rt { color: var(--muted); letter-spacing: .02em; }
  .badge { border: 1px solid var(--accent); color: var(--accent); border-radius: 4px; padding: 0 6px; font-size: 12px;
    font-weight: 600; display: none; }
  #ac { color: var(--muted); overflow: hidden; text-overflow: ellipsis; }
  .spacer { flex: 1; }
  #tod { color: var(--muted); }

  .stats { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); border-bottom: 1px solid var(--line); }
  .stat { padding: 10px 20px; position: relative; min-width: 0; }
  .stat label { display: block; font-size: 11px; letter-spacing: .06em; color: var(--muted); text-transform: uppercase; }
  .stat b { display: block; font-size: 18px; font-weight: 600; white-space: nowrap; }
  .stat:first-child::after { content: ""; position: absolute; left: 0; right: 0; bottom: -1px; height: 3px; background: var(--text); }
  .up { color: var(--accent); } .down { color: var(--warn); }

  .content { position: relative; min-height: 0; }
  #map { position: absolute; inset: 0; background: #0b0d10; }
  .leaflet-container { background: #0b0d10; font: inherit; }
  .dark-tiles { filter: invert(1) hue-rotate(180deg) brightness(.82) contrast(.92) saturate(.35); }
  .leaflet-control-attribution { background: rgba(12,14,17,.7) !important; color: var(--dim) !important; }
  .leaflet-control-attribution a { color: var(--muted) !important; }
  .leaflet-bar a { background: var(--panel2) !important; color: var(--text) !important; border-color: var(--line) !important; }
  .wpt-label { background: none; border: none; box-shadow: none; color: var(--muted); font-size: 11px; font-weight: 600; }
  .wpt-label::before { display: none; }

  .follow { position: absolute; right: 12px; bottom: 26px; z-index: 500; background: var(--panel2); border: 1px solid var(--line);
    border-radius: 6px; padding: 6px 10px; color: var(--muted); font-size: 12px; cursor: pointer; user-select: none; }
  .follow.on { color: var(--accent); border-color: var(--accent); }

  .banner { position: absolute; left: 50%; top: 16px; transform: translateX(-50%); z-index: 600;
    background: rgba(18,21,26,.95); border: 1px solid var(--line); border-radius: 10px; padding: 8px 18px;
    color: var(--warn); font-weight: 600; display: none; }

  .toast { position: absolute; left: 50%; top: 28px; transform: translate(-50%, -12px); z-index: 700; opacity: 0;
    transition: opacity .35s, transform .35s; pointer-events: none;
    background: rgba(18,21,26,.96); border: 1px solid var(--line); border-radius: 14px; padding: 16px 30px; text-align: center;
    box-shadow: 0 10px 40px rgba(0,0,0,.5); }
  .toast.show { opacity: 1; transform: translate(-50%, 0); }
  .toast .t1 { font-size: 12px; letter-spacing: .1em; color: var(--muted); text-transform: uppercase; }
  .toast .t2 { font-size: 40px; font-weight: 700; line-height: 1.15; }
  .toast .t3 { font-size: 18px; font-weight: 700; }
  .Good { color: var(--accent); } .Mediocre { color: var(--warn); } .Hard { color: var(--bad); }

  /* rechte Spalte */
  .log { background: var(--panel); border-left: 1px solid var(--line); display: grid;
    grid-template-rows: 52px auto auto minmax(0, 1fr) auto auto; min-height: 0; }
  .conds { border-bottom: 1px solid var(--line); padding: 10px 18px; font-size: 12px; }
  .conds .cond { display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px; }
  .conds .cond > span { color: var(--muted); letter-spacing: .06em; text-transform: uppercase; font-size: 11px; }
  .seg { display: inline-flex; border: 1px solid var(--line); border-radius: 5px; overflow: hidden; }
  .seg button { font: inherit; font-size: 12px; border: 0; background: var(--panel2); color: var(--muted); padding: 3px 10px; cursor: pointer; }
  .seg button + button { border-left: 1px solid var(--line); }
  .seg button.on { background: var(--accent-dim); color: var(--accent); font-weight: 600; }
  .conds .cstat { color: var(--muted); margin-top: 2px; }
  .conds input { width: 92px; font: inherit; font-size: 12px; text-transform: uppercase; background: var(--panel2);
    color: var(--text); border: 1px solid var(--line); border-radius: 5px; padding: 3px 8px; }
  .conds .cinfo { color: var(--dim); font-size: 11px; margin-top: 4px; }
  .routecard { border-bottom: 1px solid var(--line); padding: 10px 18px; font-size: 12px; display: none; }
  .routecard .rc-row { display: flex; align-items: baseline; gap: 8px; }
  .routecard .rc-tag { font-size: 10px; letter-spacing: .08em; color: var(--muted); border: 1px solid var(--line);
    border-radius: 3px; padding: 0 4px; min-width: 28px; text-align: center; }
  .routecard b { font-size: 13px; }
  .routecard .rc-proc { color: var(--muted); }
  .routecard .rc-wpts { color: var(--dim); margin: 5px 0 5px 10px; padding-left: 26px; border-left: 1px dashed var(--line);
    line-height: 1.5; display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden; }
  .routecard .rc-src { color: var(--dim); font-size: 11px; margin-top: 4px; }
  .log h2 { margin: 0; padding: 0 18px; display: flex; align-items: center; font-size: 13px; letter-spacing: .1em; color: var(--muted); border-bottom: 1px solid var(--line); }
  #events { overflow-y: auto; padding: 8px 18px 14px; }
  .ev { position: relative; padding: 8px 0 8px 14px; }
  .ev time { display: block; font-size: 12px; color: var(--dim); }
  .ev.phase::before { content: ""; position: absolute; left: 0; top: 14px; width: 6px; height: 6px; border-radius: 50%; background: var(--text); }
  .ev.note span { color: var(--muted); }
  .empty { color: var(--dim); padding: 14px 0; }

  .landing { border-top: 1px solid var(--line); padding: 12px 18px; display: none; }
  .landing .row { display: flex; align-items: baseline; justify-content: space-between; }
  .landing .big { font-size: 24px; font-weight: 700; }
  .landing .sub { color: var(--muted); font-size: 12px; }

  .footer { border-top: 1px solid var(--line); display: grid; grid-template-columns: 1fr 1fr 1fr; padding: 12px 18px; gap: 10px; }
  .footer label { display: block; font-size: 11px; letter-spacing: .06em; color: var(--muted); text-transform: uppercase; }
  .footer b { font-size: 15px; }
  .footer small { color: var(--muted); font-weight: 400; }

  .rail button.nav { border: 0; background: none; cursor: pointer; }
  .rail button.nav:hover { color: var(--text); background: var(--panel2); }
  .rail .bottom { margin-top: auto; margin-bottom: 14px; display: flex; flex-direction: column; gap: 10px; }
  #notice { margin-left: auto; color: var(--dim); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 55%; }

  .log h2 .actions { margin-left: auto; display: flex; gap: 6px; }
  .btn { font: inherit; font-size: 12px; font-weight: 600; letter-spacing: 0; border-radius: 5px; padding: 5px 10px;
    cursor: pointer; border: 1px solid var(--line); background: var(--panel2); color: var(--text); }
  .btn:hover { border-color: var(--muted); }
  .btn.primary { background: #fff; color: #111; border-color: #fff; }
  .btn.warn { color: var(--warn); border-color: rgba(242,180,60,.5); }
  .btn:disabled { opacity: .45; cursor: default; }

  .overlay { position: fixed; inset: 0; background: rgba(0,0,0,.45); z-index: 2000; display: none; }
  .overlay.show { display: block; }
  .wxraw { margin: 4px 0 6px; padding: 10px 12px; background: var(--panel2); border: 1px solid var(--line); border-radius: 6px;
    font: 13px/1.55 ui-monospace, Consolas, monospace; color: var(--text); white-space: pre-wrap; word-break: break-word; }
  .sheet { position: absolute; left: 60px; top: 0; bottom: 0; width: 420px; background: var(--panel); border-right: 1px solid var(--line);
    padding: 22px 24px; overflow-y: auto; box-shadow: 10px 0 40px rgba(0,0,0,.4); }
  .sheet h3 { margin: 0 0 18px; font-size: 16px; display: flex; align-items: center; }
  .sheet h3 .btn { margin-left: auto; }
  .field { margin-bottom: 18px; }
  .field > label { display: block; font-size: 11px; letter-spacing: .06em; color: var(--muted); text-transform: uppercase; margin-bottom: 6px; }
  .field .row { display: flex; gap: 8px; }
  .field select { appearance: auto; }
  .field input[type=text] { flex: 1; min-width: 0; font: inherit; color: var(--text); background: var(--bg); border: 1px solid var(--line);
    border-radius: 5px; padding: 7px 10px; }
  .field input[type=text]:focus { outline: none; border-color: var(--accent); }
  .check { display: flex; align-items: center; gap: 8px; margin-top: 8px; color: var(--text); cursor: pointer; }
  .hint { color: var(--dim); font-size: 12px; margin-top: 6px; word-break: break-all; }
  .sheet hr { border: 0; border-top: 1px solid var(--line); margin: 20px 0; }

  .snack { position: fixed; left: 50%; bottom: 24px; transform: translate(-50%, 20px); z-index: 3000; opacity: 0;
    transition: opacity .25s, transform .25s; background: var(--panel2); border: 1px solid var(--line); border-radius: 8px;
    padding: 10px 16px; max-width: 70vw; pointer-events: none; }
  .snack.show { opacity: 1; transform: translate(-50%, 0); }
  .snack.bad { border-color: var(--bad); }

  .bookings { display: flex; flex-direction: column; gap: 8px; margin-top: 10px; }
  .booking { border: 1px solid var(--line); border-radius: 8px; padding: 10px 12px; cursor: pointer; background: var(--bg); }
  .booking:hover { border-color: var(--muted); }
  .booking.sel { border-color: var(--accent); background: var(--accent-dim); }
  .booking .l1 { display: flex; justify-content: space-between; font-weight: 600; }
  .booking .l2 { color: var(--muted); font-size: 12px; margin-top: 2px; }
  .field input[type=password] { flex: 1; min-width: 0; font: inherit; color: var(--text); background: var(--bg); border: 1px solid var(--line);
    border-radius: 5px; padding: 7px 10px; }
  .field input[type=password]:focus { outline: none; border-color: var(--accent); }
  .ok-text { color: var(--accent); }
  .field select, .field textarea { flex: 1; min-width: 0; font: inherit; color: var(--text); background: var(--bg);
    border: 1px solid var(--line); border-radius: 5px; padding: 7px 10px; }
  .field textarea { min-height: 70px; resize: vertical; }
  .sum { display: grid; grid-template-columns: 1fr 1fr; gap: 8px 16px; margin-bottom: 18px; }
  .sum div label { display: block; font-size: 11px; letter-spacing: .06em; color: var(--muted); text-transform: uppercase; }
  .sum div b { font-size: 15px; }
  .warnbox { border: 1px solid rgba(242,180,60,.5); color: var(--warn); border-radius: 6px; padding: 8px 10px; font-size: 12px; margin-bottom: 14px; }
  .booking .suffix { color: var(--dim); font-weight: 400; font-size: 12px; margin-left: 6px; }
  #bookingTag { color: var(--accent); font-size: 12px; border: 1px solid var(--accent); border-radius: 4px; padding: 0 6px; display: none; }

  ::-webkit-scrollbar { width: 8px; } ::-webkit-scrollbar-thumb { background: var(--line); border-radius: 4px; }
</style>
</head>
<body>
<div class="app">
  <aside class="rail">
    <div class="logo" title="Flarelog"><svg viewBox="0 0 256 256" width="34" height="34" aria-label="Flarelog"><rect x="8" y="8" width="240" height="240" rx="56" fill="#18212b"/><rect x="40" y="180" width="176" height="22" rx="11" fill="#e9eef3"/><path d="M48 44 L92 88 C122 118 150 166 210 166" fill="none" stroke="#3fd68a" stroke-width="26" stroke-linecap="round" stroke-linejoin="round"/></svg></div>
    <div class="nav active" title="Flight Tracking">
      <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M21 16v-2l-8-5V3.5a1.5 1.5 0 0 0-3 0V9l-8 5v2l8-2.5V19l-2 1.5V22l3.5-1 3.5 1v-1.5L13 19v-5.5z"/></svg>
    </div>
    <div class="bottom">
      <button class="nav" id="navWeather" title="Wetter: METAR und TAF">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M7 18h10a4 4 0 0 0 .6-7.95A6 6 0 0 0 6.2 9.1 4.5 4.5 0 0 0 7 18z"/></svg>
      </button>
      <button class="nav" id="navAirline" title="Airline (Buchungen)">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="7" width="18" height="13" rx="2"/><path d="M9 7V5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2"/></svg>
      </button>
      <button class="nav" id="navReports" title="Berichtsordner öffnen">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></svg>
      </button>
      <button class="nav" id="navSettings" title="Einstellungen">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></svg>
      </button>
    </div>
  </aside>

  <section class="main">
    <header class="topbar">
      <span class="title">Flight Tracking</span>
      <span id="conn" class="conn"><i></i><span>Warte auf Aerofly …</span></span>
      <span id="simAc" class="simac" style="display:none" title="In Aerofly gewähltes Flugzeug"></span>
      <span id="notice"></span>
      <button class="btn primary" id="btnUpdate" style="display:none;margin-left:12px">Update verfügbar</button>
    </header>

    <div class="flightbar">
      <span id="trk" class="tracking"><i></i><span>Bereit</span></span>
      <b id="fn">–</b>
      <span id="rt">–</span>
      <span id="sb" class="badge">SB ✓</span>
      <span id="bookingTag">Buchung</span>
      <span id="liveTag" class="badge" style="display:none" title="Der Flug wird live an die Airline gesendet">LIVE</span>
      <span id="ac"></span>
      <span class="spacer"></span>
      <span id="tod" class="num"></span>
    </div>

    <div class="stats">
      <div class="stat"><label>Höhe</label><b id="alt" class="num">–</b></div>
      <div class="stat"><label>Groundspeed</label><b id="gs" class="num">–</b></div>
      <div class="stat"><label>Vertikal</label><b id="vs" class="num">–</b></div>
      <div class="stat"><label>Kurs (rw)</label><b id="hdg" class="num">–</b></div>
      <div class="stat"><label>Air Time</label><b id="air" class="num">–</b></div>
      <div class="stat"><label>DTG</label><b id="dtg" class="num">–</b></div>
    </div>

    <div class="content">
      <div id="map"></div>
      <div id="banner" class="banner">Simulator pausiert</div>
      <div id="toast" class="toast">
        <div class="t1">Touchdown</div>
        <div id="toastRate" class="t2 num">–</div>
        <div id="toastRating" class="t3">–</div>
      </div>
      <div id="follow" class="follow on">Flugzeug folgen</div>
    </div>
  </section>

  <aside class="log">
    <h2>FLIGHT LOG
      <span class="actions">
        <button class="btn warn" id="btnFinish" style="display:none" title="Aufzeichnung beenden und Bericht speichern">Beenden</button>
        <button class="btn primary" id="btnSubmit" style="display:none" title="Flug als PIREP an die Airline senden">An Airline senden</button>
        <button class="btn" id="btnSubmitted" style="display:none" title="PIREP auf der Airline-Webseite ansehen">PIREP ✓</button>
        <button class="btn" id="btnCopy" style="display:none" title="Flugdaten für ein manuelles PIREP-Formular kopieren">Daten kopieren</button>
        <button class="btn primary" id="btnNew" style="display:none" title="Neuen Flug aufzeichnen">Neuer Flug</button>
      </span>
    </h2>
    <div class="conds">
      <div class="cond"><span>Uhrzeit</span>
        <div class="seg" id="segTime"><button data-v="0">Aerofly</button><button data-v="1">Real</button></div></div>
      <div class="cond"><span>Wetter</span>
        <div class="seg" id="segWx"><button data-v="0">Aerofly</button><button data-v="1">Real (Departure)</button></div></div>
      <div class="cond" id="wxIcaoRow" style="display:none"><span>Departure</span>
        <input type="text" id="wxIcao" maxlength="6" placeholder="ICAO" autocomplete="off" spellcheck="false"></div>
      <div class="cinfo" id="wxAuto"></div>
      <div class="cstat" id="condStatus"></div>
      <div class="cinfo">Wird beim Start von Aerofly übernommen – während des Flugs kann Flarelog Zeit und Wetter nicht ändern.</div>
    </div>
    <div id="routeCard" class="routecard">
      <div class="rc-row"><span class="rc-tag">DEP</span><b id="rcDep"></b><span id="rcSid" class="rc-proc"></span></div>
      <div id="rcWpts" class="rc-wpts"></div>
      <div class="rc-row"><span class="rc-tag">ARR</span><b id="rcArr"></b><span id="rcStar" class="rc-proc"></span></div>
      <div id="rcSrc" class="rc-src"></div>
    </div>
    <div id="events"><div class="empty">Noch keine Einträge – Aerofly starten und losrollen.</div></div>
    <div id="landing" class="landing">
      <div class="row"><span class="sub">LANDUNG</span><span id="ldgApt" class="sub"></span></div>
      <div class="row"><span id="ldgRate" class="big num">–</span><span id="ldgRating" class="big">–</span></div>
      <div id="ldgGs" class="sub num"></div>
      <div id="ldgMore" class="sub num" style="margin-top:6px;line-height:1.6"></div>
    </div>
    <div class="footer">
      <div><label>Phase</label><b id="phase">–</b></div>
      <div><label>Next WPT</label><b id="next">–</b></div>
      <div><label>ETA</label><b id="eta" class="num">–</b></div>
    </div>
  </aside>
</div>

<div id="overlay" class="overlay">
  <div class="sheet">
    <h3>Einstellungen <button class="btn" id="btnClose">Schließen</button></h3>
    <div class="field">
      <label>SimBrief-Benutzer oder Pilot-ID</label>
      <div class="row"><input type="text" id="setSb" placeholder="z. B. meinname" autocomplete="off" spellcheck="false">
        <button class="btn primary" id="btnSbLoad">Plan laden</button></div>
      <label class="check"><input type="checkbox" id="setSbAuto"> Beim Start automatisch laden</label>
      <div class="hint" id="sbHint">Lädt den zuletzt in SimBrief erzeugten Flugplan.</div>
    </div>
    <hr>
    <div class="field">
      <label>Landeansage</label>
      <label class="check"><input type="checkbox" id="setVoice"> „Touchdown with … feet per minute“ ansagen</label>
      <div class="row" style="margin-top:10px"><button class="btn" id="btnVoiceTest">Stimme testen</button></div>
    </div>
    <hr>
    <div class="field">
      <label>Flugberichte</label>
      <div class="hint" id="setFolder"></div>
      <div class="row" style="margin-top:10px"><button class="btn" id="btnFolder">Ordner öffnen</button></div>
    </div>
    <hr>
    <div class="field">
      <label>Aerofly</label>
      <div class="hint" id="setUdp"></div>
    </div>
    <hr>
    <div class="field">
      <label>Über</label>
      <div class="hint" id="setVersion"></div>
    </div>
  </div>
</div>
<div id="airlineOverlay" class="overlay">
  <div class="sheet">
    <h3><span id="alTitle">Airline</span> <button class="btn" id="btnAirlineClose">Schließen</button></h3>
    <div class="field">
      <label>Webseite der Airline (phpVMS 7)</label>
      <div class="row"><input type="text" id="alUrl" placeholder="https://www.deine-va.de" autocomplete="off" spellcheck="false"></div>
    </div>
    <div class="field">
      <label>Persönlicher API-Schlüssel</label>
      <div class="row"><input type="password" id="alKey" placeholder="aus dem Airline-Profil einfügen" autocomplete="off"></div>
      <div class="hint" id="alKeyHint">Wird verschlüsselt auf diesem PC gespeichert und nur an die Airline gesendet.</div>
    </div>
    <div class="row"><button class="btn primary" id="btnAlConnect">Verbinden &amp; Buchungen laden</button></div>
    <label class="check" style="margin-top:12px"><input type="checkbox" id="alLive"> Live-Tracking: Flug ab dem Losrollen an die Airline melden</label>
    <div class="hint">Der Flug erscheint dann während des Flugs auf der Live-Karte der Airline. Dafür braucht es eine Buchung
      oder einen SimBrief-Plan (Ziel, Flugnummer) und ein bestimmbares Flugzeug.</div>
    <div class="hint" id="alLiveInfo"></div>
    <div class="hint" id="alPilot" style="margin-top:12px"></div>
    <hr>
    <div class="field">
      <label style="display:flex;align-items:center">Gebuchte Flüge <span style="margin-left:auto"><button class="btn" id="btnAlRefresh">Aktualisieren</button></span></label>
      <div class="hint" id="alUpdated"></div>
      <div id="alBookings" class="bookings"></div>
      <div class="hint" style="margin-top:10px">Flug anklicken, um ihn für die Aufzeichnung zu verwenden. Nach der Landung
        erscheint im Flight Log „An Airline senden“ – gesendet wird erst, wenn du es dort bestätigst.
        Ohne Buchung wird der Flug als freier Flug eingereicht, sofern die Airline das erlaubt.</div>
    </div>
  </div>
</div>
<div id="submitOverlay" class="overlay">
  <div class="sheet">
    <h3>PIREP einreichen <button class="btn" id="btnSubmitClose">Schließen</button></h3>
    <div class="sum">
      <div><label>Flug</label><b id="suFlight">–</b></div>
      <div><label>Strecke</label><b id="suRoute">–</b></div>
      <div><label>Blockzeit</label><b id="suBlock" class="num">–</b></div>
      <div><label>Flugzeit</label><b id="suAir" class="num">–</b></div>
      <div><label>Landerate</label><b id="suRate" class="num">–</b></div>
      <div><label>Gelandet in</label><b id="suLanded">–</b></div>
    </div>
    <div id="suWarn" class="warnbox" style="display:none"></div>
    <div class="field" id="suFreeField" style="display:none">
      <label>Flugnummer (freier Flug ohne Buchung)</label>
      <div class="row"><input type="text" id="suFlightNo" placeholder="z. B. 1022" autocomplete="off" spellcheck="false"></div>
      <div class="hint">Wird mit deiner Airline eingereicht. Manche Airlines erlauben nur gebuchte Flüge – dann kommt eine Meldung.</div>
    </div>
    <div id="suCheck" class="hint" style="margin:-6px 0 14px"></div>
    <div class="field">
      <label>Flugzeug (Airline-Flotte)</label>
      <div class="row"><select id="suAircraft"><option value="">Flotte wird geladen …</option></select></div>
      <div class="hint">Oben stehen Flugzeuge am Abflugort und passend zum SimBrief-Typ.</div>
      <div class="row" style="margin-top:8px"><input type="text" id="suReg" placeholder="oder Kennzeichen eingeben, z. B. D-EABC" autocomplete="off" spellcheck="false"></div>
      <div class="hint" id="suFleetInfo"></div>
    </div>
    <div class="field">
      <label>Bemerkung (optional)</label>
      <div class="row"><textarea id="suNotes" placeholder="z. B. Testflug mit Flarelog"></textarea></div>
    </div>
    <div class="hint" style="margin-bottom:12px">Gesendet werden: Flugnummer, Flughäfen, Flugzeug, Block- und Flugzeit, geflogene Strecke,
      Landerate, Route und die aufgezeichneten Positionen (alle 10 s). Treibstoff kann Aerofly nicht liefern.</div>
    <div class="row"><button class="btn primary" id="btnSubmitSend">Jetzt einreichen</button></div>
  </div>
</div>
<div id="weatherOverlay" class="overlay">
  <div class="sheet">
    <h3>Wetter <button class="btn" id="btnWxClose">Schließen</button></h3>
    <div class="field">
      <label>Flugplatz (ICAO)</label>
      <div class="row"><input type="text" id="wxStation" maxlength="6" placeholder="z. B. EDDF" autocomplete="off" spellcheck="false"
        style="text-transform:uppercase"><button class="btn primary" id="btnWxFetch">Abrufen</button></div>
      <div class="row" id="wxChips" style="margin-top:8px;gap:6px"></div>
    </div>
    <div id="wxResult" style="display:none">
      <div class="field">
        <label>METAR <span id="wxName" style="text-transform:none;letter-spacing:0;color:var(--dim)"></span></label>
        <pre class="wxraw" id="wxMetar"></pre>
        <div class="hint" id="wxSummary"></div>
      </div>
      <div class="field">
        <label>TAF</label>
        <pre class="wxraw" id="wxTaf"></pre>
      </div>
      <div class="hint" id="wxFetched"></div>
    </div>
    <div class="hint" style="margin-top:12px">Quelle: Aviation Weather Center (aviationweather.gov). Nur zur Information für die Simulation.</div>
  </div>
</div>
<div id="snack" class="snack"></div>

<script>
const $ = id => document.getElementById(id);
const de = (v, d = 0) => Number(v).toLocaleString('de-DE', { maximumFractionDigits: d, minimumFractionDigits: d });

const map = L.map('map', { zoomControl: true, attributionControl: true }).setView([51.0, 10.0], 6);
// OpenStreetMap-Kacheln, per CSS-Filter abgedunkelt (kein API-Schlüssel nötig)
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
  maxZoom: 19, className: 'dark-tiles',
  attribution: '&copy; OpenStreetMap-Mitwirkende'
}).addTo(map);

const routeLine = L.polyline([], { color: '#9aa4b2', weight: 2, opacity: .8, dashArray: '8 8' }).addTo(map);
const trackLine = L.polyline([], { color: '#3fd68a', weight: 3, opacity: .9 }).addTo(map);
let marker = null, routeKey = '', firstFix = true, follow = true;
let lastCalloutId = null, toastTimer = null, eventCount = -1;
let airBase = null; // { sec, at, running }

$('follow').onclick = () => { follow = !follow; $('follow').classList.toggle('on', follow); };
map.on('dragstart', () => { follow = false; $('follow').classList.remove('on'); });

function planeIcon(hdg) {
  return L.divIcon({
    className: '', iconSize: [36, 36], iconAnchor: [18, 18],
    html: `<svg width="36" height="36" viewBox="-18 -18 36 36" style="transform: rotate(${hdg}deg)">
      <path d="M0,-16 L2.4,-6 L15,1.5 L15,4.5 L2.4,1.5 L1.8,10 L6,13 L6,15.5 L0,14 L-6,15.5 L-6,13 L-1.8,10 L-2.4,1.5 L-15,4.5 L-15,1.5 L-2.4,-6 Z"
        fill="#ffffff" stroke="#0c0e11" stroke-width="1.2"/></svg>`
  });
}

function fmtAlt(ft) { return ft >= 10000 ? 'FL' + String(Math.round(ft / 100)).padStart(3, '0') : de(Math.round(ft / 10) * 10) + ' ft'; }
function fmtDur(sec) {
  sec = Math.max(0, Math.floor(sec));
  const h = Math.floor(sec / 3600), m = Math.floor(sec % 3600 / 60), s = sec % 60;
  return [h, m, s].map(x => String(x).padStart(2, '0')).join(':');
}
function fmtEta(min) {
  if (min == null) return '–';
  const h = Math.floor(min / 60), m = Math.round(min % 60);
  return h > 0 ? `${h}h ${String(m).padStart(2, '0')}m` : `${m}m`;
}

function showToast(c) {
  $('toastRate').textContent = `${de(Math.abs(c.rate))} fpm`;
  $('toastRating').textContent = c.rating;
  $('toastRating').className = 't3 ' + c.rating;
  $('toast').classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => $('toast').classList.remove('show'), 12000);
}

function renderEvents(events) {
  if (events.length === eventCount) return;
  eventCount = events.length;
  const box = $('events');
  const atBottom = box.scrollHeight - box.scrollTop - box.clientHeight < 40;
  box.innerHTML = events.length === 0
    ? '<div class="empty">Noch keine Einträge – Aerofly starten und losrollen.</div>'
    : events.map(e => `<div class="ev ${e.phaseChange ? 'phase' : 'note'}"><time>${e.time} UTC</time><span></span></div>`).join('');
  // Text sicher einsetzen (kein HTML aus Daten)
  box.querySelectorAll('.ev span').forEach((el, i) => el.textContent = events[i].text);
  if (atBottom) box.scrollTop = box.scrollHeight;
}

let snackTimer = null, lastState = null;
function snack(text, ok = true) {
  const el = $('snack');
  el.textContent = text; el.className = 'snack show' + (ok ? '' : ' bad');
  clearTimeout(snackTimer); snackTimer = setTimeout(() => el.className = 'snack', 4500);
}
async function post(path, body) {
  try {
    const r = await fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Acars': '1' },
      body: JSON.stringify(body ?? {}) });
    const res = await r.json();
    snack(res.message, res.ok);
    tick();
    return res;
  } catch (e) { snack('Keine Verbindung zu Flarelog', false); return { ok: false }; }
}

function openSettings() {
  const st = lastState && lastState.settings;
  if (st) {
    $('setSb').value = st.simBriefUser ?? '';
    $('setSbAuto').checked = st.loadSimBriefOnStart;
    $('setVoice').checked = st.voice;
    $('setFolder').textContent = st.reportFolder;
    $('setVersion').textContent = `Flarelog ${st.version} · ACARS für Aerofly FS 4 – Daten: OurAirports (gemeinfrei), OpenStreetMap-Mitwirkende, SimBrief.`;
    $('setUdp').textContent = `Empfang auf UDP-Port ${st.udpPort}. In Aerofly: Sonstige Einstellungen → „Sende Fluginformation an IP Addresse“ an, Broadcast IP 127.0.0.1, Port ${st.udpPort}.`;
  }
  $('overlay').classList.add('show');
}
$('navSettings').onclick = openSettings;
$('btnClose').onclick = () => $('overlay').classList.remove('show');
$('overlay').onclick = e => { if (e.target.id === 'overlay') $('overlay').classList.remove('show'); };
$('navReports').onclick = () => post('/api/open-reports');
$('btnFolder').onclick = () => post('/api/open-reports');
$('btnVoiceTest').onclick = () => post('/api/voice-test');
$('setVoice').onchange = () => post('/api/settings', { voice: $('setVoice').checked });
$('setSbAuto').onchange = () => post('/api/settings', { loadSimBriefOnStart: $('setSbAuto').checked });
$('setSb').onchange = () => post('/api/settings', { simBriefUser: $('setSb').value });
$('btnSbLoad').onclick = async () => {
  $('btnSbLoad').disabled = true;
  await post('/api/settings', { simBriefUser: $('setSb').value });
  const res = await post('/api/simbrief', { user: $('setSb').value });
  $('btnSbLoad').disabled = false;
  if (res.ok) { $('overlay').classList.remove('show'); }
};
$('btnFinish').onclick = () => { if (confirm('Aufzeichnung beenden und Bericht jetzt speichern?')) post('/api/finish'); };
$('btnNew').onclick = () => { resetMap(); post('/api/newflight'); };

async function fetchWeather(icao) {
  $('btnWxFetch').disabled = true;
  let res;
  try {
    const r = await fetch('/api/weather', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Acars': '1' },
      body: JSON.stringify({ icao }) });
    res = await r.json();
  } catch (e) { res = { ok: false, message: 'Keine Verbindung zu Flarelog' }; }
  $('btnWxFetch').disabled = false;
  if (!res.ok) { snack(res.message, false); return; }
  const d = res.data;
  $('wxStation').value = d.icao;
  $('wxName').textContent = d.name ? `· ${d.name}` : '';
  $('wxMetar').textContent = d.metar || 'kein METAR verfügbar';
  $('wxSummary').textContent = d.summary ? `Für Aerofly: ${d.summary}` : '';
  $('wxTaf').textContent = d.taf || 'kein TAF verfügbar (nicht jeder Platz hat einen)';
  $('wxFetched').textContent = `Abgerufen um ${d.fetched} Uhr`;
  $('wxResult').style.display = 'block';
}
function openWeather() {
  const st = lastState && lastState.settings;
  const chips = $('wxChips'); chips.innerHTML = '';
  (st && st.weatherStations || []).forEach((icao, i, all) => {
    const b = document.createElement('button');
    b.className = 'btn'; b.textContent = (all.length > 1 ? (i === 0 ? 'DEP ' : 'ARR ') : '') + icao;
    b.onclick = () => fetchWeather(icao);
    chips.appendChild(b);
  });
  $('weatherOverlay').classList.add('show');
  fetchWeather($('wxStation').value.trim() || null);
}
$('navWeather').onclick = openWeather;
$('btnWxClose').onclick = () => $('weatherOverlay').classList.remove('show');
$('weatherOverlay').onclick = e => { if (e.target.id === 'weatherOverlay') $('weatherOverlay').classList.remove('show'); };
$('btnWxFetch').onclick = () => fetchWeather($('wxStation').value.trim());
$('wxStation').onkeydown = e => { if (e.key === 'Enter') fetchWeather($('wxStation').value.trim()); };
function segSet(id, on) { [...$(id).children].forEach(b => b.classList.toggle('on', (b.dataset.v === '1') === !!on)); }
$('alLive').onchange = () => post('/api/airline/live', { on: $('alLive').checked });
$('segTime').onclick = e => { if (e.target.dataset.v) post('/api/conditions', { realTime: e.target.dataset.v === '1' }); };
$('segWx').onclick = e => { if (e.target.dataset.v) post('/api/conditions', { realWeather: e.target.dataset.v === '1' }); };
$('wxIcao').onchange = () => post('/api/conditions', { weatherIcao: $('wxIcao').value });
$('btnCopy').onclick = async () => {
  let res;
  try {
    const r = await fetch('/api/pirep-text', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Acars': '1' }, body: '{}' });
    res = await r.json();
  } catch (e) { snack('Keine Verbindung zu Flarelog', false); return; }
  if (!res.ok) { snack(res.message, false); return; }
  let copied = false;
  try { await navigator.clipboard.writeText(res.text); copied = true; } catch (e) { /* Ersatzweg unten */ }
  if (!copied) {
    const ta = document.createElement('textarea');
    ta.value = res.text; document.body.appendChild(ta); ta.select();
    try { copied = document.execCommand('copy'); } catch (e) { copied = false; }
    ta.remove();
  }
  snack(copied ? res.message : 'Kopieren nicht möglich', copied);
};

// inputs = true nur beim Öffnen: sonst würde die laufende Aktualisierung Eingaben überschreiben.
function fillAirline(inputs) {
  const a = lastState && lastState.airline;
  if (!a) return;
  if (inputs) { $('alUrl').value = a.url || ''; $('alKey').value = ''; $('alLive').checked = !!a.liveTracking; }
  $('alLiveInfo').textContent = a.liveInfo || '';
  $('alTitle').textContent = a.presetName || 'Airline';
  $('alKeyHint').textContent = a.hasKey
    ? 'Ein Schlüssel ist gespeichert. Nur ausfüllen, um ihn zu ersetzen.'
    : 'Wird verschlüsselt auf diesem PC gespeichert und nur an die Airline gesendet.';
  $('alPilot').innerHTML = '';
  if (a.pilot) {
    const sp = document.createElement('span'); sp.className = 'ok-text';
    sp.textContent = `✓ Angemeldet als ${a.pilot}` + (a.pilotAirport ? ` · aktuell in ${a.pilotAirport}` : '');
    $('alPilot').appendChild(sp);
  }
  $('alUpdated').textContent = a.updated ? `Stand ${a.updated} UTC` : '';
  const box = $('alBookings');
  box.innerHTML = '';
  if (a.pilot && a.bookings.length === 0) {
    box.innerHTML = '<div class="hint">Keine gebuchten Flüge. Auf der Webseite einen Flug buchen und „Aktualisieren“ klicken.</div>';
  }
  a.bookings.forEach(b => {
    const el = document.createElement('div');
    el.className = 'booking' + (b.flightId === a.selected ? ' sel' : '');
    const l1 = document.createElement('div'); l1.className = 'l1';
    const n = document.createElement('span'); n.textContent = b.ident;
    if (b.suffix) { const sx = document.createElement('span'); sx.className = 'suffix'; sx.textContent = b.suffix; n.appendChild(sx); }
    const r = document.createElement('span'); r.textContent = `${b.dep} → ${b.arr}`;
    l1.append(n, r);
    const l2 = document.createElement('div'); l2.className = 'l2';
    const parts = [];
    if (b.aircraft) parts.push(b.aircraft);
    if (b.timeMin) parts.push(fmtEta(b.timeMin));
    if (b.distanceNm) parts.push(`${de(b.distanceNm)} nm`);
    if (b.route) parts.push(b.route);
    l2.textContent = parts.join(' · ');
    el.append(l1, l2);
    el.onclick = () => post('/api/airline/select', { flightId: b.flightId === a.selected ? null : b.flightId });
    box.appendChild(el);
  });
}
$('navAirline').onclick = () => { fillAirline(true); $('airlineOverlay').classList.add('show'); };
$('alUrl').onchange = () => post('/api/settings', { airlineUrl: $('alUrl').value });
$('btnAirlineClose').onclick = () => $('airlineOverlay').classList.remove('show');
$('airlineOverlay').onclick = e => { if (e.target.id === 'airlineOverlay') $('airlineOverlay').classList.remove('show'); };
$('btnAlConnect').onclick = async () => {
  $('btnAlConnect').disabled = true;
  await post('/api/airline/connect', { url: $('alUrl').value, apiKey: $('alKey').value });
  $('alKey').value = '';
  $('btnAlConnect').disabled = false;
};
$('btnAlRefresh').onclick = () => post('/api/airline/refresh');

function fmtMin(m) { return m == null ? '–' : fmtDur(m * 60).slice(0, 5); }
function fillSubmit(full) {
  const su = lastState && lastState.submit, a = lastState && lastState.airline;
  if (!su) return;
  $('suFlight').textContent = su.free ? 'Freier Flug' : (su.flight ?? '–');
  $('suFreeField').style.display = su.free ? 'block' : 'none';
  if (full && su.free && !$('suFlightNo').value && su.flightNumberHint) $('suFlightNo').value = su.flightNumberHint;
  $('suRoute').textContent = su.dep ? `${su.dep} → ${su.arr}` : '–';
  $('suBlock').textContent = fmtMin(su.blockMin);
  $('suAir').textContent = fmtMin(su.airMin);
  $('suRate').textContent = su.landingRate != null ? `${de(su.landingRate)} fpm` : '–';
  $('suLanded').textContent = su.landedAt ?? '–';
  const warn = [];
  if (su.problem) warn.push(su.problem);
  if (su.landedAt && su.arr && su.landedAt !== su.arr) warn.push(`Gelandet in ${su.landedAt}, gebucht war ${su.arr}.`);
  const ck = su.aircraftCheck;
  if (ck && ck.ok === false) warn.push('Flugzeug prüfen: ' + ck.text);
  $('suCheck').textContent = ck && ck.ok !== false ? (ck.ok ? '✓ ' : '') + ck.text : '';
  $('suCheck').className = 'hint' + (ck && ck.ok ? ' ok-text' : '');
  $('suWarn').style.display = warn.length ? 'block' : 'none';
  $('suWarn').textContent = warn.join(' ');
  $('btnSubmitSend').disabled = !su.available || su.sending;
  $('btnSubmitSend').textContent = su.sending ? 'Wird gesendet …' : 'Jetzt einreichen';
  if (full && a) {
    const sel = $('suAircraft'), keep = sel.value;
    const hint = (su.aircraftHint || '').toUpperCase();
    const score = f => {
      let p = 0;
      if (su.dep && f.airport === su.dep) p += 2;
      if (hint && [f.icao, f.type].some(v => (v || '').toUpperCase() === hint)) p += 1;
      return p;
    };
    const list = [...a.fleet].sort((x, y) => score(y) - score(x) || String(x.reg).localeCompare(String(y.reg)));
    sel.innerHTML = '';
    $('suFleetInfo').textContent = a.fleetInfo ? `Antwort der Airline: ${a.fleetInfo}` : '';
    if (su.bookedAircraftId) {
      const o = document.createElement('option');
      o.value = su.bookedAircraftId;
      o.textContent = `${su.bookedAircraft} (aus der Buchung)`;
      sel.appendChild(o);
    }
    if (list.length === 0 && !su.bookedAircraftId) { const o = document.createElement('option'); o.value = ''; o.textContent = 'Keine Flugzeuge in der Liste – Kennzeichen eingeben'; sel.appendChild(o); }
    list.filter(f => f.id !== su.bookedAircraftId).forEach(f => {
      const o = document.createElement('option');
      o.value = f.id;
      o.textContent = `${f.reg} · ${f.type ?? f.icao ?? ''}` + (f.airport ? ` · in ${f.airport}` : '');
      sel.appendChild(o);
    });
    if (keep && [...sel.options].some(o => o.value === keep)) sel.value = keep;
  }
}
async function openSubmit() {
  fillSubmit(false);
  $('submitOverlay').classList.add('show');
  await post('/api/airline/fleet');
  fillSubmit(true);
}
$('btnSubmit').onclick = openSubmit;
$('btnSubmitClose').onclick = () => $('submitOverlay').classList.remove('show');
$('submitOverlay').onclick = e => { if (e.target.id === 'submitOverlay') $('submitOverlay').classList.remove('show'); };
$('btnSubmitted').onclick = () => { if (lastState && lastState.submit.url) window.open(lastState.submit.url, '_blank'); };
$('btnSubmitSend').onclick = async () => {
  const reg = $('suReg').value.trim();
  const id = reg ? '' : $('suAircraft').value;
  if (!id && !reg) { snack('Bitte ein Flugzeug auswählen oder ein Kennzeichen eingeben.', false); return; }
  if (!confirm('PIREP jetzt an die Airline senden?')) return;
  $('btnSubmitSend').disabled = true;
  const res = await post('/api/airline/submit', { aircraftId: id, registration: reg, notes: $('suNotes').value,
    flightNumber: $('suFlightNo').value });
  if (res.ok) $('submitOverlay').classList.remove('show');
  fillSubmit(false);
};

const routeMarkers = [];
function clearRoute() { routeLine.setLatLngs([]); routeMarkers.forEach(m => m.remove()); routeMarkers.length = 0; }
function resetMap() {
  clearRoute(); routeKey = ''; trackLine.setLatLngs([]); eventCount = -1;
  $('landing').style.display = 'none'; airBase = null;
}

async function tick() {
  let s;
  try {
    const r = await fetch('/api/state', { cache: 'no-store' });
    s = await r.json();
    lastState = s;
  } catch (e) {
    $('conn').className = 'conn'; $('conn').lastChild.textContent = 'Keine Verbindung zu Flarelog';
    return;
  }

  // Verbindung
  const conn = $('conn');
  if (s.paused) { conn.className = 'conn pause'; conn.lastChild.textContent = 'Simulator pausiert'; }
  else if (s.connected) { conn.className = 'conn ok'; conn.lastChild.textContent = `Verbunden mit ${s.source ?? 'Aerofly'}`; }
  else { conn.className = 'conn'; conn.lastChild.textContent = 'Keine Daten – Aerofly mit „Sende Fluginformation an IP“?'; }
  $('banner').style.display = s.paused ? 'block' : 'none';
  $('notice').textContent = s.pendingPlan ? `Nächster Flug: ${s.pendingPlan}` : (s.notices.length ? s.notices[s.notices.length - 1] : '');
  $('btnFinish').style.display = s.inProgress ? 'inline-block' : 'none';
  $('btnUpdate').style.display = s.update ? 'inline-block' : 'none';
  if (s.update) { $('btnUpdate').textContent = `Version ${s.update.version} verfügbar`; $('btnUpdate').onclick = () => window.open(s.update.url, '_blank'); }
  $('btnNew').style.display = (s.reportSaved || s.finished) ? 'inline-block' : 'none';
  $('btnCopy').style.display = s.landing ? 'inline-block' : 'none';
  const canSubmit = s.airline.hasKey && !!s.airline.pilot && !s.submit.pirepId && (s.reportSaved || s.finished);
  $('btnSubmit').style.display = canSubmit ? 'inline-block' : 'none';
  $('btnSubmitted').style.display = s.submit.pirepId ? 'inline-block' : 'none';
  if ($('submitOverlay').classList.contains('show')) fillSubmit(false);
  if (!s.landing) $('landing').style.display = 'none';

  // Kopfzeile
  const h = s.header;
  const tracking = s.connected && !s.finished;
  $('trk').className = 'tracking' + (tracking ? ' on' : '');
  $('trk').lastChild.textContent = s.finished ? 'Beendet' : tracking ? 'Tracking' : 'Bereit';
  $('fn').textContent = h.flight ?? 'Freier Flug';
  $('rt').textContent = `${h.origin ?? '····'} — ${h.destination ?? '····'}`;
  $('sb').style.display = h.simbrief ? 'inline-block' : 'none';
  $('simAc').style.display = h.simAircraft ? 'inline' : 'none';
  $('simAc').textContent = h.simAircraft || '';
  $('simAc').title = h.simAircraftCode ? `In Aerofly gewählt: ${h.simAircraftCode}` : '';
  if (s.settings) {
    segSet('segTime', s.settings.realTime);
    segSet('segWx', s.settings.realWeather);
    $('condStatus').textContent = s.settings.conditionsStatus || '';
    $('wxIcaoRow').style.display = s.settings.realWeather ? 'flex' : 'none';
    if (document.activeElement !== $('wxIcao')) $('wxIcao').value = s.settings.weatherIcao || '';
    $('wxAuto').style.display = s.settings.realWeather ? 'block' : 'none';
    $('wxAuto').textContent = s.settings.weatherAuto
      ? `Wetter von ${s.settings.weatherAuto}. SimBrief-Plan und Buchung haben Vorrang vor der Eingabe.`
      : 'Kein Abflugplatz bekannt – bitte ICAO eintragen.';
  }
  const ri = s.settings && s.settings.routeInfo;
  $('routeCard').style.display = ri ? 'block' : 'none';
  if (ri) {
    $('rcDep').textContent = [ri.dep, ri.depRwy].filter(Boolean).join(' ');
    $('rcSid').textContent = ri.sid ? `SID ${ri.sid}` : '';
    const w = ri.waypoints || [];
    $('rcWpts').textContent = w.length ? w.join(' · ') : 'direkt';
    $('rcWpts').title = w.join(' ');
    $('rcArr').textContent = [ri.arr, ri.arrRwy].filter(Boolean).join(' ');
    $('rcStar').textContent = ri.star ? `STAR ${ri.star}` : '';
    $('rcSrc').textContent = 'Plan aus SimBrief';
  }
  $('bookingTag').style.display = h.booking ? 'inline-block' : 'none';
  if (h.booking) $('bookingTag').textContent = `Buchung ${h.booking}`;
  $('liveTag').style.display = s.airline && s.airline.live ? 'inline-block' : 'none';
  if (s.airline) $('liveTag').title = s.airline.liveInfo || 'Live an die Airline';
  if ($('airlineOverlay').classList.contains('show')) fillAirline(false);
  $('ac').textContent = h.aircraft ?? '';
  $('tod').textContent = s.progress && s.progress.todNm != null ? `TOD in ${de(s.progress.todNm)} nm` : '';

  // Werte
  const p = s.sample;
  if (p) {
    $('alt').textContent = fmtAlt(p.altFt);
    $('gs').textContent = `${de(p.gsKt)} kts`;
    const vs = Math.round(p.vsFpm / 10) * 10;
    $('vs').innerHTML = '';
    const arrow = document.createElement('span');
    arrow.className = vs > 100 ? 'up' : vs < -100 ? 'down' : '';
    arrow.textContent = (vs > 100 ? '▲ ' : vs < -100 ? '▼ ' : '') + `${de(vs)} fpm`;
    $('vs').appendChild(arrow);
    $('hdg').textContent = `${String(Math.round(p.hdg) % 360).padStart(3, '0')}°`;
  }
  $('dtg').textContent = s.progress ? `${de(s.progress.dtgNm)} nm` : '–';
  airBase = s.airTimeSec == null ? null
    : { sec: s.airTimeSec, at: performance.now(), running: s.connected && !s.paused && !s.landing };
  $('phase').textContent = s.paused ? 'Pause' : s.phase;
  if (s.progress) {
    $('next').innerHTML = '';
    $('next').append(s.progress.next + ' ');
    const sm = document.createElement('small'); sm.textContent = `${de(s.progress.nextNm)} nm`;
    $('next').appendChild(sm);
  } else { $('next').textContent = '–'; }
  $('eta').textContent = fmtEta(s.progress ? s.progress.etaMin : null);

  // Landung
  if (s.landing) {
    $('landing').style.display = 'block';
    $('ldgRate').textContent = `${de(Math.abs(s.landing.rate))} fpm`;
    $('ldgRating').textContent = s.landing.rating;
    $('ldgRating').className = 'big ' + s.landing.rating;
    $('ldgApt').textContent = s.landing.airport ?? '';
    $('ldgGs').textContent = s.landing.gsKt != null ? `bei ${de(s.landing.gsKt)} kt Groundspeed` : '';
    const an = s.landing.analysis, more = [];
    if (an) {
      more.push(`Bounces ${an.bounces}`);
      if (an.approachVsSigmaFpm != null)
        more.push(`Endanflug ${an.approachRating} · σ ${de(an.approachVsSigmaFpm)} fpm · Ø ${de(an.approachVsMeanFpm)} fpm`);
      if (an.rolloutM != null) more.push(`Rollout ${de(an.rolloutM)} m`);
      if (an.arrivalRunway) more.push(`Bahn ${an.arrivalRunway}` + (an.departureRunway ? ` (Start ${an.departureRunway})` : ''));
      if (an.maxSimRate > 1) more.push(`⚠ Zeitraffer bis ${de(an.maxSimRate, 1)}×`);
      if (an.slewEvents > 0) more.push(`⚠ Slew ${an.slewEvents}×`);
    }
    $('ldgMore').textContent = more.join('\n');
    $('ldgMore').style.whiteSpace = 'pre-line';
  }
  if (s.lastCallout && s.lastCallout.id !== lastCalloutId) {
    if (lastCalloutId !== null || Date.now() - s.lastCallout.id < 30000) showToast(s.lastCallout);
    lastCalloutId = s.lastCallout.id;
  }

  // Karte
  // Route neu zeichnen, sobald sich der Plan ändert (neuer Plan geladen oder verworfen).
  const key = s.route ? s.route.map(w => w.ident).join(',') : '';
  if (key !== routeKey) {
    clearRoute();
    routeKey = key;
  }
  if (key && routeMarkers.length === 0 && s.route.length > 1) {
    routeLine.setLatLngs(s.route.map(w => [w.lat, w.lon]));
    s.route.forEach((w, i) => {
      const m = L.circleMarker([w.lat, w.lon], { radius: i === 0 || i === s.route.length - 1 ? 6 : 3,
        color: i === 0 || i === s.route.length - 1 ? '#3fd68a' : '#9aa4b2', fillOpacity: 1, weight: 1 }).addTo(map);
      routeMarkers.push(m);
      m.bindTooltip(w.ident, { permanent: i === 0 || i === s.route.length - 1, direction: 'bottom', className: 'wpt-label' });
    });
    if (!p) map.fitBounds(routeLine.getBounds(), { padding: [50, 50] });
  }
  trackLine.setLatLngs(s.track);
  if (p) {
    const pos = [p.lat, p.lon];
    if (!marker) marker = L.marker(pos, { icon: planeIcon(p.hdg), zIndexOffset: 1000 }).addTo(map);
    else { marker.setLatLng(pos); marker.setIcon(planeIcon(p.hdg)); }
    if (firstFix) { map.setView(pos, 12); firstFix = false; }
    else if (follow) map.panTo(pos, { animate: true, duration: .8 });
  }

  renderEvents(s.events);
}

// Air Time läuft zwischen den Abfragen flüssig weiter
setInterval(() => {
  if (!airBase) { $('air').textContent = '–'; return; }
  const extra = airBase.running ? (performance.now() - airBase.at) / 1000 : 0;
  $('air').textContent = fmtDur(airBase.sec + extra);
}, 250);

setInterval(tick, 1000);
tick();
</script>
</body>
</html>
""";
}
