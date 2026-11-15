'use strict';
/* Klaude RP Launcher: Oberfläche. Alles, was den PC betrifft (Dateien, Programme starten, Internet), macht das Programm (C#).
   Die Seite fragt es über Bruecke.rufen('<aktion>', daten). Ohne Programm (Vorschau im Browser) antwortet eine Attrappe. */

// ---------------------------------------------------------------- Brücke
const Bruecke = (() => {
  const wv = window.chrome && window.chrome.webview;
  let zaehler = 0;
  const offen = new Map();
  const hoerer = [];
  if (wv) {
    wv.addEventListener('message', (e) => {
      const d = e.data || {};
      if (d.id && offen.has(d.id)) {
        const { ok, nein } = offen.get(d.id);
        offen.delete(d.id);
        if (d.fehler) nein(new Error(d.fehler)); else ok(d.ergebnis);
      } else if (d.ereignis) {
        hoerer.forEach((h) => h(d));
      }
    });
  }
  function rufen(aktion, daten = {}) {
    if (!wv) return Attrappe.rufen(aktion, daten, hoerer);
    const id = 'a' + (++zaehler);
    return new Promise((ok, nein) => {
      offen.set(id, { ok, nein });
      wv.postMessage({ id, aktion, daten });
    });
  }
  return { rufen, bei: (fn) => hoerer.push(fn), echt: !!wv };
})();

// ---------------------------------------------------------------- Hilfen
const $ = (s) => document.querySelector(s);
const el = (tag, eigenschaften = {}, kinder = []) => {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(eigenschaften)) {
    if (k === 'text') e.textContent = v;
    else if (k === 'klasse') e.className = v;
    else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
    else e.setAttribute(k, v);
  }
  for (const k of [].concat(kinder)) if (k) e.append(k);
  return e;
};
const zahl = (n) => Number(n).toLocaleString('de-DE');
const groesse = (b) => {
  if (b >= 1e9) return (b / 1e9).toLocaleString('de-DE', { maximumFractionDigits: 1 }) + ' GB';
  if (b >= 1e6) return (b / 1e6).toLocaleString('de-DE', { maximumFractionDigits: 0 }) + ' MB';
  return Math.max(1, Math.round(b / 1e3)) + ' KB';
};

function meldung(text, art = '') {
  const m = el('div', { klasse: 'meldung ' + art, text });
  $('#meldungen').append(m);
  setTimeout(() => { m.classList.add('weg'); setTimeout(() => m.remove(), 320); }, art === 'fehler' ? 7000 : 4500);
}

function frage(titel, text, ja = 'Ja') {
  return new Promise((fertig) => {
    $('#dialog-titel').textContent = titel;
    $('#dialog-text').textContent = text;
    $('#dialog-ja').textContent = ja;
    const d = $('#dialog');
    d.classList.remove('versteckt');
    $('#dialog-nein').focus();
    const schluss = (wert) => {
      d.classList.add('versteckt');
      $('#dialog-ja').onclick = $('#dialog-nein').onclick = null;
      document.removeEventListener('keydown', taste, true);
      fertig(wert);
    };
    const taste = (e) => { if (e.key === 'Escape') { e.preventDefault(); schluss(false); } };
    document.addEventListener('keydown', taste, true);
    $('#dialog-ja').onclick = () => schluss(true);
    $('#dialog-nein').onclick = () => schluss(false);
  });
}

function eingabe(titel, vorschlag = '') {
  return new Promise((fertig) => {
    $('#eingabe-titel').textContent = titel;
    const f = $('#eingabe-feld');
    f.value = vorschlag;
    const d = $('#eingabe');
    d.classList.remove('versteckt');
    setTimeout(() => f.select(), 30);
    const schluss = (wert) => {
      d.classList.add('versteckt');
      document.removeEventListener('keydown', taste, true);
      fertig(wert);
    };
    const taste = (e) => {
      if (e.key === 'Escape') { e.preventDefault(); schluss(null); }
      if (e.key === 'Enter') { e.preventDefault(); schluss(f.value.trim() || null); }
    };
    document.addEventListener('keydown', taste, true);
    $('#eingabe-ja').onclick = () => schluss(f.value.trim() || null);
    $('#eingabe-nein').onclick = () => schluss(null);
  });
}

// ---------------------------------------------------------------- Fenster
function skalieren() {
  const s = Math.min(window.innerWidth / 1280, window.innerHeight / 760);
  if (s > 0) document.documentElement.style.setProperty('--s', s);
}
window.addEventListener('resize', skalieren);
skalieren();

$('#titelleiste').addEventListener('mousedown', (e) => {
  if (e.button !== 0 || e.target.closest('button')) return;
  Bruecke.rufen('fenster', { aktion: 'ziehen' });
});
$('#knopf-min').addEventListener('click', () => Bruecke.rufen('fenster', { aktion: 'min' }));
$('#knopf-zu').addEventListener('click', () => Bruecke.rufen('fenster', { aktion: 'zu' }));
document.addEventListener('contextmenu', (e) => { if (!e.target.closest('input')) e.preventDefault(); });

// Reiter
const geladen = {};
document.querySelectorAll('.reiter button').forEach((b) => b.addEventListener('click', () => zeigen(b.dataset.ziel)));
function zeigen(ziel) {
  document.body.dataset.ansicht = ziel;
  document.querySelectorAll('.reiter button').forEach((b) => b.classList.toggle('aktiv', b.dataset.ziel === ziel));
  if (ziel === 'grafik' && !geladen.grafik) { geladen.grafik = true; Grafik.laden(); }
  if (ziel === 'tasten' && !geladen.tasten) { geladen.tasten = true; Tasten.laden(); }
}

// ---------------------------------------------------------------- Start
const Start = {
  info: null,
  async laden() {
    try {
      this.info = await Bruecke.rufen('info');
    } catch (e) {
      this.info = null;
    }
    this.neuigkeiten();
    this.status();
    setInterval(() => this.status(), 20000);
  },

  neuigkeiten() {
    const liste = $('#neuigkeiten');
    liste.textContent = '';
    const n = (this.info && this.info.neuigkeiten) || [];
    if (!n.length) {
      liste.append(el('li', { klasse: 'leer', text: this.info ? 'Gerade nichts Neues.' : 'Server nicht erreichbar.' }));
      return;
    }
    for (const eintrag of n.slice(0, 6)) {
      const [j, m, t] = String(eintrag.datum || '').split('-');
      liste.append(el('li', {}, [
        el('span', { klasse: 'datum', text: t && m ? `${t}.${m}` : '' }),
        el('span', {}, [el('span', { klasse: 'titel', text: eintrag.titel || '' }), eintrag.text ? el('span', { klasse: 'text', text: eintrag.text }) : null]),
      ]));
    }
  },

  async status() {
    let s;
    try { s = await Bruecke.rufen('status'); } catch (e) { return; }
    // Server
    const pS = $('#punkt-server');
    pS.className = 'punkt ' + (s.server.online ? 'an' : 'aus');
    $('#text-server').textContent = s.server.online ? 'Server online' : 'Server nicht erreichbar';
    const z = $('#zahl-server');
    z.textContent = '';
    if (s.server.online) {
      z.append(String(s.server.spieler), el('small', { text: ' / ' + s.server.max }));
      z.title = `Ping ${s.server.ping} ms`;
    }
    // TeamSpeak
    const pT = $('#punkt-ts');
    const tsText = $('#text-ts');
    const tsZahl = $('#zahl-ts');
    if (!s.teamspeak.installiert) {
      pT.className = 'punkt aus'; tsText.textContent = 'TeamSpeak fehlt'; tsZahl.textContent = '';
    } else if (!s.teamspeak.yaca) {
      pT.className = 'punkt warn'; tsText.textContent = 'TeamSpeak bereit'; tsZahl.textContent = 'YaCA fehlt';
    } else {
      pT.className = 'punkt an'; tsText.textContent = 'TeamSpeak bereit'; tsZahl.textContent = 'YaCA ✓';
    }
    // Spielen-Knopf
    const hinweis = $('#spielen-hinweis');
    if (!s.fivem.installiert) hinweis.textContent = 'FiveM ist nicht installiert. Ein Klick auf Spielen öffnet die Download-Seite.';
    else if (!s.teamspeak.installiert) hinweis.textContent = 'TeamSpeak 3 fehlt, ohne TeamSpeak gibt es keine Sprache im Spiel.';
    else if (!s.teamspeak.yaca) hinweis.textContent = 'Das Sprach-Plugin YaCA fehlt in TeamSpeak. Ohne es hörst du niemanden.';
    else hinweis.textContent = 'Verbindet Server und TeamSpeak mit einem Klick';
    Start.fivemLaeuft = s.fivem.laeuft;
  },

  async spielen() {
    const k = $('#knopf-spielen');
    k.disabled = true;
    k.querySelector('span').textContent = 'STARTET …';
    try {
      const r = await Bruecke.rufen('spielen');
      if (r && r.hinweis) meldung(r.hinweis, r.ok ? '' : 'warnung');
    } catch (e) {
      meldung(e.message, 'fehler');
    }
    setTimeout(() => { k.disabled = false; k.querySelector('span').textContent = 'SPIELEN'; }, 6000);
  },
};
$('#knopf-spielen').addEventListener('click', () => Start.spielen());
$('#knopf-ts').addEventListener('click', async () => {
  try {
    const r = await Bruecke.rufen('teamspeak');
    if (r && r.hinweis) meldung(r.hinweis, r.ok ? '' : 'warnung');
  } catch (e) { meldung(e.message, 'fehler'); }
});

// ---------------------------------------------------------------- Grafik
const Grafik = {
  daten: null, aktuell: null, entwurf: null, vorhanden: false,

  async laden() {
    try {
      this.daten = await (await fetch('daten/grafik.json')).json();
      const r = await Bruecke.rufen('grafikLesen');
      this.vorhanden = r.vorhanden;
      this.aktuell = {};
      for (const e of this.daten.einstellungen) {
        const roh = r.werte ? r.werte[e.schluessel] : undefined;
        this.aktuell[e.schluessel] = this.wandeln(e, roh);
      }
      this.entwurf = { ...this.aktuell };
      this.zeichnen();
    } catch (e) {
      $('#grafik-stand').textContent = 'Konnte nicht gelesen werden: ' + e.message;
    }
  },

  wandeln(e, roh) {
    if (roh === undefined || roh === null) return undefined;
    if (e.art === 'schalter') return String(roh).toLowerCase() === 'true';
    const n = parseFloat(roh);
    return Number.isFinite(n) ? n : undefined;
  },

  gleich(a, b) {
    if (typeof a === 'number' && typeof b === 'number') return Math.abs(a - b) < 0.011;
    return a === b;
  },

  passendesPreset(werte) {
    return this.daten.presets.find((p) => Object.entries(p.werte).every(([k, v]) => this.gleich(werte[k], v)));
  },

  geaendert() {
    return this.daten.einstellungen.some((e) => this.entwurf[e.schluessel] !== undefined && !this.gleich(this.entwurf[e.schluessel], this.aktuell[e.schluessel]));
  },

  zeichnen() {
    const stand = $('#grafik-stand');
    if (!this.vorhanden) {
      stand.textContent = 'Keine FiveM-Grafikdatei gefunden. Starte FiveM einmal, danach kannst du hier einstellen.';
    } else {
      const p = this.passendesPreset(this.aktuell);
      stand.textContent = '';
      stand.append('Jetzt eingestellt: ', el('b', { text: p ? p.name : 'Eigene Einstellung' }));
    }
    // Presets
    const box = $('#grafik-presets');
    box.textContent = '';
    const passt = this.passendesPreset(this.entwurf);
    for (const p of this.daten.presets) {
      box.append(el('button', {
        type: 'button', klasse: 'preset' + (passt && passt.id === p.id ? ' aktiv' : ''),
        onclick: () => { this.entwurf = { ...this.entwurf, ...p.werte }; this.zeichnen(); },
      }, [el('b', { text: p.name }), el('span', { text: p.beschreibung })]));
    }
    // Feineinstellung
    const raster = $('#grafik-regler');
    raster.textContent = '';
    for (const e of this.daten.einstellungen) {
      const wert = this.entwurf[e.schluessel];
      const anders = wert !== undefined && !this.gleich(wert, this.aktuell[e.schluessel]);
      const zeile = el('div', { klasse: 'regel' + (anders ? ' geaendert' : '') }, [el('span', { text: e.name })]);
      if (e.art === 'stufe') {
        const st = el('div', { klasse: 'stufen', role: 'group', 'aria-label': e.name });
        for (const [w, name] of e.werte) {
          st.append(el('button', {
            type: 'button', klasse: this.gleich(wert, w) ? 'an' : '', text: name, 'aria-pressed': String(this.gleich(wert, w)),
            onclick: () => { this.entwurf[e.schluessel] = w; this.zeichnen(); },
          }));
        }
        zeile.append(st);
      } else if (e.art === 'schalter') {
        zeile.append(el('button', {
          type: 'button', klasse: 'schalter' + (wert ? ' an' : ''), role: 'switch', 'aria-checked': String(!!wert), 'aria-label': e.name,
          onclick: () => { this.entwurf[e.schluessel] = !wert; this.zeichnen(); },
        }));
      } else {
        const id = 'r-' + e.schluessel;
        const aus = el('output', { for: id, text: Math.round((wert ?? 0) * 100) + ' %' });
        const eingabe = el('input', { type: 'range', id, min: e.min, max: e.max, step: '0.05', value: String(wert ?? 0), 'aria-label': e.name });
        eingabe.addEventListener('input', () => { aus.textContent = Math.round(eingabe.value * 100) + ' %'; });
        eingabe.addEventListener('change', () => { this.entwurf[e.schluessel] = parseFloat(eingabe.value); this.zeichnen(); });
        zeile.append(el('div', { klasse: 'regler' }, [eingabe, aus]));
      }
      raster.append(zeile);
    }
    $('#grafik-uebernehmen').disabled = !this.vorhanden || !this.geaendert();
  },

  async uebernehmen() {
    const werte = {};
    for (const e of this.daten.einstellungen) {
      const w = this.entwurf[e.schluessel];
      if (w === undefined) continue;
      werte[e.schluessel] = e.art === 'schalter' ? (w ? 'true' : 'false') : (e.art === 'regler' ? Number(w).toFixed(6) : String(Math.round(w)));
    }
    try {
      await Bruecke.rufen('grafikSchreiben', { werte });
      this.aktuell = { ...this.entwurf };
      this.zeichnen();
      meldung('Grafik gespeichert. Gilt ab dem nächsten Start von FiveM.', 'erfolg');
    } catch (e) {
      meldung(e.message, 'fehler');
    }
  },
};
$('#grafik-uebernehmen').addEventListener('click', () => Grafik.uebernehmen());

// ---------------------------------------------------------------- Tasten
// FiveM-Tastennamen (docs.fivem.net, Input Mapper „keyboard“) zu KeyboardEvent.code (Position der Taste).
const CODE_ZU_FIVEM = {
  Backspace: 'BACK', Tab: 'TAB', Enter: 'RETURN', Pause: 'PAUSE', CapsLock: 'CAPITAL', Space: 'SPACE', PageUp: 'PAGEUP', PageDown: 'PAGEDOWN',
  End: 'END', Home: 'HOME', ArrowLeft: 'LEFT', ArrowUp: 'UP', ArrowRight: 'RIGHT', ArrowDown: 'DOWN', Insert: 'INSERT', Delete: 'DELETE',
  Numpad0: 'NUMPAD0', Numpad1: 'NUMPAD1', Numpad2: 'NUMPAD2', Numpad3: 'NUMPAD3', Numpad4: 'NUMPAD4', Numpad5: 'NUMPAD5', Numpad6: 'NUMPAD6',
  Numpad7: 'NUMPAD7', Numpad8: 'NUMPAD8', Numpad9: 'NUMPAD9', NumpadMultiply: 'MULTIPLY', NumpadAdd: 'ADD', NumpadSubtract: 'SUBTRACT',
  NumpadDecimal: 'DECIMAL', NumpadDivide: 'DIVIDE', NumpadEnter: 'NUMPADENTER', NumLock: 'NUMLOCK', ScrollLock: 'SCROLL',
  ShiftLeft: 'LSHIFT', ShiftRight: 'RSHIFT', ControlLeft: 'LCONTROL', ControlRight: 'RCONTROL', AltLeft: 'LMENU', AltRight: 'RMENU',
  Semicolon: 'SEMICOLON', Equal: 'EQUALS', Comma: 'COMMA', Minus: 'MINUS', Period: 'PERIOD', Slash: 'SLASH', Backquote: 'GRAVE',
  BracketLeft: 'LBRACKET', Backslash: 'BACKSLASH', BracketRight: 'RBRACKET', Quote: 'APOSTROPHE', IntlBackslash: 'OEM_102',
};
for (let i = 0; i <= 9; i++) CODE_ZU_FIVEM['Digit' + i] = String(i);
for (let i = 65; i <= 90; i++) CODE_ZU_FIVEM['Key' + String.fromCharCode(i)] = String.fromCharCode(i);
for (let i = 1; i <= 12; i++) CODE_ZU_FIVEM['F' + i] = 'F' + i;
const FIVEM_ZU_CODE = Object.fromEntries(Object.entries(CODE_ZU_FIVEM).map(([c, f]) => [f, c]));
Object.assign(FIVEM_ZU_CODE, { PRIOR: 'PageUp', NEXT: 'PageDown', OEM_1: 'Semicolon', OEM_2: 'Slash', OEM_3: 'Backquote', OEM_4: 'BracketLeft', OEM_5: 'Backslash', OEM_6: 'BracketRight', OEM_7: 'Quote', PLUS: 'Equal' });

// Anzeige: die Beschriftung auf der eigenen Tastatur (deutsches Layout als Rückfall)
const NAMEN = {
  BACK: 'Rücktaste', TAB: 'Tab', RETURN: 'Enter', PAUSE: 'Pause', CAPITAL: 'Feststell', SPACE: 'Leertaste', PAGEUP: 'Bild ↑', PAGEDOWN: 'Bild ↓',
  END: 'Ende', HOME: 'Pos1', LEFT: 'Pfeil ←', UP: 'Pfeil ↑', RIGHT: 'Pfeil →', DOWN: 'Pfeil ↓', INSERT: 'Einfg', DELETE: 'Entf',
  MULTIPLY: 'Num *', ADD: 'Num +', SUBTRACT: 'Num −', DECIMAL: 'Num ,', DIVIDE: 'Num /', NUMPADENTER: 'Num Enter', NUMLOCK: 'Num', SCROLL: 'Rollen',
  LSHIFT: 'Shift links', RSHIFT: 'Shift rechts', LCONTROL: 'Strg links', RCONTROL: 'Strg rechts', LMENU: 'Alt', RMENU: 'Alt Gr',
};
const DEUTSCH = { Z: 'Y', Y: 'Z', GRAVE: '^', MINUS: 'ß', EQUALS: '´', LBRACKET: 'Ü', RBRACKET: '+', SEMICOLON: 'Ö', APOSTROPHE: 'Ä', BACKSLASH: '#', SLASH: '-', COMMA: ',', PERIOD: '.', OEM_102: '<' };
let layout = null;
if (navigator.keyboard && navigator.keyboard.getLayoutMap) {
  navigator.keyboard.getLayoutMap().then((m) => { layout = m; if (geladen.tasten) Tasten.zeichnen(); }).catch(() => {});
}
function tastenName(id) {
  if (!id || id === 'NULL') return 'keine';
  if (NAMEN[id]) return NAMEN[id];
  if (/^NUMPAD\d$/.test(id)) return 'Num ' + id.slice(6);
  const code = FIVEM_ZU_CODE[id];
  if (layout && code && layout.get(code)) return layout.get(code).toUpperCase();
  return DEUTSCH[id] || id;
}

const Tasten = {
  daten: null, eintraege: [], aktuell: {}, entwurf: {}, profile: [], vorhanden: false, wartet: null,

  schluessel: (t) => t.ressource + '|' + t.befehl,

  async laden() {
    try {
      this.daten = await (await fetch('daten/tasten.json')).json();
      this.eintraege = this.daten.gruppen.flatMap((g) => g.tasten);
      const r = await Bruecke.rufen('tastenLesen', { eintraege: this.eintraege.map((t) => ({ ressource: t.ressource, befehl: t.befehl })) });
      this.vorhanden = r.vorhanden;
      this.aktuell = {};
      for (const t of this.eintraege) {
        const k = this.schluessel(t);
        this.aktuell[k] = (r.belegung && r.belegung[k]) || t.standard;
      }
      this.entwurf = { ...this.aktuell };
      const p = await Bruecke.rufen('profileLesen');
      this.profile = (p && p.profile) || [];
      this.profilListe('aktuell');
      this.zeichnen();
    } catch (e) {
      $('#tasten-stand').textContent = 'Konnte nicht gelesen werden: ' + e.message;
    }
  },

  profilListe(auswahl) {
    const s = $('#tasten-profil');
    s.textContent = '';
    s.append(el('option', { value: 'aktuell', text: 'Aktuelle Belegung' }));
    s.append(el('option', { value: 'standard', text: 'Server-Standard' }));
    this.profile.forEach((p, i) => s.append(el('option', { value: 'p' + i, text: 'Profil: ' + p.name })));
    s.value = auswahl;
  },

  profilWaehlen(wert) {
    if (wert === 'aktuell') this.entwurf = { ...this.aktuell };
    else if (wert === 'standard') this.entwurf = Object.fromEntries(this.eintraege.map((t) => [this.schluessel(t), t.standard]));
    else {
      const p = this.profile[Number(wert.slice(1))];
      if (p) this.entwurf = { ...this.entwurf, ...p.belegung };
    }
    this.zeichnen();
  },

  doppelte() {
    const zaehl = {};
    for (const t of this.eintraege) {
      const k = this.entwurf[this.schluessel(t)];
      if (k && k !== 'NULL') zaehl[k] = (zaehl[k] || 0) + 1;
    }
    return zaehl;
  },

  zeichnen() {
    const stand = $('#tasten-stand');
    const anzahlAnders = this.eintraege.filter((t) => this.entwurf[this.schluessel(t)] !== this.aktuell[this.schluessel(t)]).length;
    if (!this.vorhanden) stand.textContent = 'Keine FiveM-Einstellungen gefunden. Starte FiveM einmal, danach kannst du hier einstellen.';
    else if (anzahlAnders) { stand.textContent = ''; stand.append(el('b', { text: anzahlAnders + (anzahlAnders === 1 ? ' Änderung' : ' Änderungen') }), ' noch nicht übernommen'); }
    else stand.textContent = 'So ist es gerade in FiveM eingestellt.';

    const zaehl = this.doppelte();
    const liste = $('#tasten-liste');
    liste.textContent = '';
    for (const g of this.daten.gruppen) {
      const gruppe = el('div', { klasse: 'gruppe' }, [el('h3', { text: g.name })]);
      for (const t of g.tasten) {
        const k = this.schluessel(t);
        const wert = this.entwurf[k];
        // Doppelt zählt nur, wenn beide Funktionen sich wirklich stören können (z. B. Alt fürs Auge und fürs Autoradio ist gewollt).
        const doppelt = wert && wert !== 'NULL' && zaehl[wert] > 1 && !this.erlaubtDoppelt(t, wert);
        const wartet = this.wartet === k;
        const knopf = el('button', {
          type: 'button', klasse: 'taste' + (wartet ? ' wartet' : '') + (!wert || wert === 'NULL' ? ' leer' : ''), text: wartet ? 'Taste drücken …' : tastenName(wert),
          'aria-label': `${t.name}: ${tastenName(wert)}. Ändern`, onclick: () => { this.wartet = wartet ? null : k; this.zeichnen(); },
        });
        // Rechtsklick: Taste entfernen (keine Belegung)
        knopf.addEventListener('contextmenu', (e) => { e.preventDefault(); this.wartet = null; this.entwurf[k] = 'NULL'; this.zeichnen(); });
        const zurueck = el('button', { type: 'button', klasse: 'zuruecksetzen', title: 'Server-Standard: ' + tastenName(t.standard), 'aria-label': 'Auf Server-Standard setzen',
          onclick: () => { this.entwurf[k] = t.standard; this.zeichnen(); } });
        zurueck.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 12a8 8 0 1 0 2.3-5.6"/><path d="M4 4v5h5"/></svg>';
        if (wert === t.standard) zurueck.style.visibility = 'hidden';
        gruppe.append(el('div', { klasse: 'taste-zeile' + (wert !== this.aktuell[k] ? ' geaendert' : '') + (doppelt ? ' doppelt' : '') }, [
          el('span', { klasse: 'name', text: t.name }), knopf, zurueck,
        ]));
      }
      liste.append(gruppe);
    }
    $('#tasten-uebernehmen').disabled = !this.vorhanden || !anzahlAnders;
  },

  // Gleiche Taste in getrennten Lagen (zu Fuß / im Fahrzeug / Beruf) ist in Ordnung, wenn der Server es so vorgibt.
  erlaubtDoppelt(t, wert) {
    return this.eintraege.filter((x) => this.entwurf[this.schluessel(x)] === wert).every((x) => x.standard === wert);
  },

  tasteGedrueckt(e) {
    if (!this.wartet) return false;
    e.preventDefault();
    e.stopPropagation();
    if (e.code === 'Escape') { this.wartet = null; this.zeichnen(); return true; }
    const id = CODE_ZU_FIVEM[e.code];
    if (!id) { meldung('Diese Taste kann FiveM nicht belegen.', 'warnung'); return true; }
    this.entwurf[this.wartet] = id;
    this.wartet = null;
    $('#tasten-profil').value = 'aktuell';
    this.zeichnen();
    return true;
  },

  async uebernehmen() {
    const belegung = {};
    for (const t of this.eintraege) belegung[this.schluessel(t)] = this.entwurf[this.schluessel(t)];
    try {
      await Bruecke.rufen('tastenSchreiben', { belegung, standard: Object.fromEntries(this.eintraege.map((t) => [this.schluessel(t), t.standard])) });
      this.aktuell = { ...this.entwurf };
      this.profilListe('aktuell');
      this.zeichnen();
      meldung('Tasten gespeichert. Gilt ab dem nächsten Start von FiveM.', 'erfolg');
    } catch (e) {
      meldung(e.message, 'fehler');
    }
  },

  async alsProfil() {
    const name = await eingabe('Belegung als Profil speichern', '');
    if (!name) return;
    const belegung = {};
    for (const t of this.eintraege) belegung[this.schluessel(t)] = this.entwurf[this.schluessel(t)];
    const vorhanden = this.profile.findIndex((p) => p.name.toLowerCase() === name.toLowerCase());
    if (vorhanden >= 0) this.profile[vorhanden] = { name, belegung }; else this.profile.push({ name, belegung });
    try {
      await Bruecke.rufen('profileSpeichern', { profile: this.profile });
      this.profilListe('p' + (vorhanden >= 0 ? vorhanden : this.profile.length - 1));
      meldung(`Profil „${name}“ gespeichert.`, 'erfolg');
    } catch (e) { meldung(e.message, 'fehler'); }
  },
};
document.addEventListener('keydown', (e) => Tasten.tasteGedrueckt(e), true);
$('#tasten-profil').addEventListener('change', (e) => Tasten.profilWaehlen(e.target.value));
$('#tasten-uebernehmen').addEventListener('click', () => Tasten.uebernehmen());
$('#tasten-speichern-als').addEventListener('click', () => Tasten.alsProfil());

// ---------------------------------------------------------------- Wartung
const Wartung = {
  cache: null,
  async cachePruefen() {
    const wert = $('#cache-wert');
    const loeschen = $('#cache-loeschen');
    loeschen.disabled = true;
    wert.textContent = 'Wird geprüft …';
    try {
      const r = await Bruecke.rufen('cachePruefen');
      this.cache = r;
      if (!r.listeBereit) wert.textContent = 'Der Server hat die Liste gerade nicht. Versuch es in ein paar Minuten noch einmal.';
      else if (!r.anzahl) wert.textContent = 'Kein Cache von Klaude RP gefunden. Alles sauber.';
      else {
        wert.textContent = `${zahl(r.anzahl)} Dateien · ${groesse(r.bytes)} von Klaude RP`;
        loeschen.disabled = false;
      }
    } catch (e) {
      wert.textContent = e.message;
    }
  },

  async cacheLoeschen() {
    if (!this.cache || !this.cache.anzahl) return;
    const ok = await frage('Cache von Klaude RP löschen?',
      `${zahl(this.cache.anzahl)} Dateien (${groesse(this.cache.bytes)}) werden gelöscht. Nur von Klaude RP, andere Server bleiben unberührt.\n\nBeim nächsten Verbinden lädt FiveM sie neu, das dauert einen Moment. FiveM muss dafür geschlossen sein.`,
      'Ja, löschen');
    if (!ok) return;
    $('#cache-loeschen').disabled = true;
    $('#cache-wert').textContent = 'Wird gelöscht …';
    try {
      const r = await Bruecke.rufen('cacheLoeschen', { token: this.cache.token });
      $('#cache-wert').textContent = `Gelöscht: ${zahl(r.geloescht)} Dateien · ${groesse(r.bytes)}`;
      meldung('Cache von Klaude RP gelöscht.', 'erfolg');
      this.cache = null;
    } catch (e) {
      $('#cache-wert').textContent = e.message;
      meldung(e.message, 'fehler');
    }
  },

  update: null,
  async updatePruefen(still) {
    const wert = $('#update-wert');
    if (!still) wert.textContent = 'Wird geprüft …';
    try {
      const r = await Bruecke.rufen('updatePruefen');
      this.update = r;
      $('#version').textContent = `v${r.version}` + (r.neu ? ' · Update verfügbar' : ' · aktuell');
      $('#version').classList.toggle('neu', !!r.neu);
      if (r.neu) {
        wert.textContent = `Version ${r.neueVersion} ist da (${groesse(r.bytes)})`;
        $('#update-installieren').classList.remove('versteckt');
      } else {
        wert.textContent = r.fehler ? `Version ${r.version} · ${r.fehler}` : `Version ${r.version} ist aktuell`;
        $('#update-installieren').classList.add('versteckt');
      }
      return r;
    } catch (e) {
      wert.textContent = e.message;
      return null;
    }
  },

  async updateInstallieren() {
    $('#update-installieren').disabled = true;
    $('#update-pruefen').disabled = true;
    $('#update-balken').classList.remove('versteckt');
    $('#version').textContent = 'Update wird geladen …';
    try {
      await Bruecke.rufen('updateInstallieren');
      // Das Programm startet sich danach selbst neu.
    } catch (e) {
      meldung('Update fehlgeschlagen: ' + e.message, 'fehler');
      $('#update-balken').classList.add('versteckt');
      $('#update-installieren').disabled = false;
      $('#update-pruefen').disabled = false;
      this.updatePruefen(true);
    }
  },

  async sicherung(art) {
    const name = art === 'grafik' ? 'Grafik' : 'Tasten';
    const ok = await frage(`${name} zurückholen?`, `Die ${name}-Einstellungen von vor der ersten Änderung durch den Launcher werden wiederhergestellt. FiveM muss geschlossen sein.`, 'Zurückholen');
    if (!ok) return;
    try {
      await Bruecke.rufen('sicherungZurueck', { art });
      meldung(`${name} zurückgeholt.`, 'erfolg');
      if (art === 'grafik' && geladen.grafik) Grafik.laden();
      if (art === 'tasten' && geladen.tasten) Tasten.laden();
    } catch (e) { meldung(e.message, 'fehler'); }
  },
};
$('#cache-pruefen').addEventListener('click', () => Wartung.cachePruefen());
$('#cache-loeschen').addEventListener('click', () => Wartung.cacheLoeschen());
$('#update-pruefen').addEventListener('click', () => Wartung.updatePruefen(false));
$('#update-installieren').addEventListener('click', () => Wartung.updateInstallieren());
$('#sicherung-grafik').addEventListener('click', () => Wartung.sicherung('grafik'));
$('#sicherung-tasten').addEventListener('click', () => Wartung.sicherung('tasten'));

Bruecke.bei((d) => {
  if (d.ereignis === 'fortschritt') {
    $('#update-fuellung').style.width = Math.round(d.anteil * 100) + '%';
    $('#version').textContent = `Update wird geladen … ${Math.round(d.anteil * 100)} %`;
  }
  if (d.ereignis === 'meldung') meldung(d.text, d.art || '');
});

// ---------------------------------------------------------------- Attrappe (nur Vorschau im Browser)
const Attrappe = {
  warte: (ms) => new Promise((r) => setTimeout(r, ms)),
  profile: [],
  async rufen(aktion, daten, hoerer) {
    await this.warte(250);
    switch (aktion) {
      case 'info': return { neuigkeiten: [
        { datum: '2026-11-15', titel: 'Der Klaude RP Launcher ist da', text: 'Ein Klick verbindet Spiel und TeamSpeak.' },
        { datum: '2026-11-14', titel: 'Vorschau-Eintrag', text: 'Echte Neuigkeiten kommen vom Server.' },
      ] };
      case 'status': return { fivem: { installiert: true, laeuft: false }, teamspeak: { installiert: true, yaca: true }, server: { online: true, spieler: 42, max: 64, ping: 18 } };
      case 'spielen': return { ok: true, hinweis: 'Vorschau: hier würden TeamSpeak und FiveM starten.' };
      case 'teamspeak': return { ok: true, hinweis: 'Vorschau: hier würde TeamSpeak starten.' };
      case 'grafikLesen': {
        const g = await (await fetch('daten/grafik.json')).json();
        const werte = {};
        for (const [k, v] of Object.entries(g.presets[1].werte)) werte[k] = String(v);
        werte.TextureQuality = '2';
        return { vorhanden: true, werte };
      }
      case 'grafikSchreiben': return { ok: true };
      case 'tastenLesen': return { vorhanden: true, belegung: { 'rp-job-taxi|taxameter': 'J', 'ox_inventory|+inv': 'TAB' } };
      case 'tastenSchreiben': return { ok: true };
      case 'profileLesen': return { profile: this.profile };
      case 'profileSpeichern': this.profile = daten.profile; return { ok: true };
      case 'cachePruefen': await this.warte(600); return { listeBereit: true, anzahl: 5470, bytes: 3.4e9, token: 'x' };
      case 'cacheLoeschen': await this.warte(800); return { geloescht: 5470, bytes: 3.4e9 };
      case 'updatePruefen': return { version: '1.0.0', neu: false };
      case 'updateInstallieren':
        for (let i = 1; i <= 10; i++) { await this.warte(150); hoerer.forEach((h) => h({ ereignis: 'fortschritt', anteil: i / 10 })); }
        throw new Error('Vorschau: kein echtes Update.');
      case 'sicherungZurueck': return { ok: true };
      default: return {};
    }
  },
};

// ---------------------------------------------------------------- Los geht's
(async () => {
  Start.laden();
  // Vorschau: index.html?standbild#grafik öffnet direkt diesen Bereich, ohne Überblendungen
  if (location.search.includes('standbild')) document.documentElement.classList.add('standbild');
  const ziel = location.hash.slice(1);
  if (['grafik', 'tasten', 'wartung'].includes(ziel)) zeigen(ziel);
  if (ziel === 'wartung' && !Bruecke.echt) Wartung.cachePruefen();
  const u = await Wartung.updatePruefen(true);
  // Updates holt der Launcher selbst, die Spieler müssen nichts tun.
  if (u && u.neu && u.automatisch) {
    meldung(`Update auf Version ${u.neueVersion} wird geladen …`);
    Wartung.updateInstallieren();
  }
})();
