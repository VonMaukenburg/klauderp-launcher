"""Sammelt ALLE Tastenbelegungen der Server-Scripte für den Launcher (web/daten/tasten.json).

Durchsucht jede Ressource, die live läuft (server.cfg: ensure/stop, ohne [lokal]), nach
- RegisterKeyMapping('befehl', 'beschreibung', 'keyboard', 'TASTE')
- lib.addKeybind({ name = 'x', description = '...', defaultKey = 'TASTE' })   -> Befehl '+x'
und liest die Standardtaste aus Text, Config-Werten (Config.X / config.x), GetConvar(...) und Übersetzungen (locale(...)).

Deutsche Namen und Gruppen stehen in tasten-namen.json (von Hand gepflegt). Neue Belegungen ohne deutschen Namen
zeigt das Skript am Ende an: dort eintragen und das Skript noch einmal laufen lassen.

    python werkzeuge/klauderp-launcher/tasten-liste.py
"""
import json
import os
import re
import sys

HIER = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HIER, '..', '..'))
BASIS = os.path.join(REPO, 'txData', 'Qbox_D53072.base')
RES = os.path.join(BASIS, 'resources')
NAMEN = os.path.join(HIER, 'tasten-namen.json')
ZIEL = os.path.join(HIER, 'web', 'daten', 'tasten.json')

# Läuft live nicht oder ist abgeschaltet (pma-voice: Sprache läuft über YaCA, die Brücke blockiert pma-voice)
NIE = {'pma-voice'}
GRUPPEN_REIHENFOLGE = ['Allgemein', 'Fahrzeug', 'Sprache und Funk', 'Emotes', 'Handy und Tablet', 'Berufe', 'Polizei', 'Rettungsdienst',
                       'Housing und Möbel', 'Sonstiges', 'Team (Admins)']


def lesen(pfad):
    try:
        with open(pfad, encoding='utf-8', errors='replace') as f:
            return f.read()
    except OSError:
        return ''


def server_cfg():
    """Welche Ressourcen und Ordner gestartet bzw. gestoppt werden."""
    gestartet, gestoppt = set(), set()
    for name in ['server.cfg', 'voice.cfg', 'misc.cfg', 'ox.cfg']:
        for zeile in lesen(os.path.join(BASIS, name)).splitlines():
            m = re.match(r'\s*(ensure|start|stop)\s+(\S+)', zeile)
            if not m:
                continue
            (gestartet if m.group(1) != 'stop' else gestoppt).add(m.group(2))
    return gestartet, gestoppt


def convars():
    werte = {}
    for name in os.listdir(BASIS):
        if not name.endswith('.cfg') or 'secret' in name:
            continue
        for zeile in lesen(os.path.join(BASIS, name)).splitlines():
            m = re.match(r'\s*set[rs]?\s+(\S+)\s+"?([^"#]*?)"?\s*(#.*)?$', zeile)
            if m:
                werte[m.group(1)] = m.group(2).strip()
    return werte


def ressourcen():
    """(name, pfad, kategorie) aller Ressourcen mit fxmanifest.lua."""
    for wurzel, ordner, dateien in os.walk(RES):
        ordner[:] = [o for o in ordner if o not in ('node_modules', '.git', 'web', 'html', 'ui', 'stream')]
        if 'fxmanifest.lua' in dateien:
            rel = os.path.relpath(wurzel, RES).replace('\\', '/')
            kategorien = [t for t in rel.split('/') if t.startswith('[')]
            yield os.path.basename(wurzel), wurzel, kategorien
            ordner[:] = [o for o in ordner if o not in ('client', 'server', 'shared', 'modules', 'config', 'locales')] and ordner


def aktiv(name, kategorien, gestartet, gestoppt):
    if name in NIE or name in gestoppt or '[lokal]' in kategorien:
        return False
    return name in gestartet or any(k in gestartet for k in kategorien)


def argumente(text, start):
    """Teilt die Argumente eines Aufrufs ab text[start] (nach der Klammer) an obersten Kommas."""
    tiefe, teile, aktuell, i, in_text = 0, [], '', start, None
    while i < len(text):
        z = text[i]
        if in_text:
            aktuell += z
            if z == '\\':
                aktuell += text[i + 1]
                i += 2
                continue
            if z == in_text:
                in_text = None
        elif z in '\'"`':
            in_text = z
            aktuell += z
        elif z in '([{':
            tiefe += 1
            aktuell += z
        elif z in ')]}':
            if tiefe == 0:
                teile.append(aktuell.strip())
                return teile, i
            tiefe -= 1
            aktuell += z
        elif z == ',' and tiefe == 0:
            teile.append(aktuell.strip())
            aktuell = ''
        else:
            aktuell += z
        i += 1
    return teile, i


class Aufloeser:
    def __init__(self, pfad, cv):
        self.pfad = pfad
        self.cv = cv
        self.config_text = ''
        for wurzel, ordner, dateien in os.walk(pfad):
            ordner[:] = [o for o in ordner if o not in ('node_modules', 'web', 'html', 'stream', 'dist')]
            for d in dateien:
                if d.endswith(('.lua', '.json', '.json5')) and ('config' in d.lower() or 'config' in wurzel.lower() or 'shared' in wurzel.lower()):
                    self.config_text += '\n' + lesen(os.path.join(wurzel, d))
        self.locales = {}
        for sprache in ('de', 'en'):
            for kandidat in (f'locales/{sprache}.json', f'locale/{sprache}.json'):
                t = lesen(os.path.join(pfad, kandidat))
                if t:
                    try:
                        self.locales.setdefault(sprache, json.loads(t))
                    except ValueError:
                        pass

    def locale(self, schluessel):
        for sprache in ('de', 'en'):
            d = self.locales.get(sprache)
            if not d:
                continue
            wert = d.get(schluessel)
            if wert is None and '.' in schluessel:
                wert = d
                for teil in schluessel.split('.'):
                    wert = wert.get(teil) if isinstance(wert, dict) else None
            if isinstance(wert, str):
                return wert
        return None

    def wert(self, ausdruck, kontext=''):
        a = ausdruck.strip()
        m = re.fullmatch(r'''(['"`])(.*)\1''', a, re.S)
        if m:
            return m.group(2)
        m = re.fullmatch(r'''GetConvar\(\s*['"]([^'"]+)['"]\s*,\s*['"]([^'"]*)['"]\s*\)''', a)
        if m:
            return self.cv.get(m.group(1), m.group(2))
        m = re.fullmatch(r'''(?:locale|Translate|_U|L)\(\s*['"]([^'"]+)['"].*\)''', a, re.S)
        if m:
            return self.locale(m.group(1))
        m = re.fullmatch(r'[A-Za-z_][\w.\[\]]*\.(\w+)', a)
        if m:
            # Config.X / config.x / cfg.X: letzter Name, in Config-Dateien oder im selben Skript suchen
            name = m.group(1)
            for quelle in (self.config_text, kontext):
                t = re.search(r'''\b%s\s*[=:]\s*['"]([^'"]*)['"]''' % re.escape(name), quelle)
                if t:
                    return t.group(1)
        return None


def suchen(gestartet, gestoppt, cv):
    funde, offen = [], []
    for name, pfad, kategorien in ressourcen():
        if not aktiv(name, kategorien, gestartet, gestoppt):
            continue
        a = Aufloeser(pfad, cv)
        for wurzel, ordner, dateien in os.walk(pfad):
            ordner[:] = [o for o in ordner if o not in ('node_modules', 'web', 'html', 'stream', 'nurlokal', 'server')]
            for d in dateien:
                if not d.endswith(('.lua', '.js')) or d.startswith('server') or 'imports' in wurzel.replace('\\', '/').split('/'):
                    continue
                text = lesen(os.path.join(wurzel, d))
                ohne_kommentar = re.sub(r'--\[\[.*?\]\]|--[^\n]*', '', text, flags=re.S) if d.endswith('.lua') else text
                for m in re.finditer(r'RegisterKeyMapping\s*\(', ohne_kommentar):
                    teile, _ = argumente(ohne_kommentar, m.end())
                    if len(teile) < 4:
                        continue
                    befehl = a.wert(teile[0], ohne_kommentar)
                    mapper = (a.wert(teile[2], ohne_kommentar) or '').lower()
                    if befehl is None or befehl.startswith('~!'):
                        if befehl is None:
                            offen.append(f'{name}: {d}: Befehl nicht lesbar ({teile[0][:40]})')
                        continue
                    if mapper and mapper != 'keyboard':
                        continue
                    funde.append({'ressource': name, 'befehl': befehl, 'beschreibung': a.wert(teile[1], ohne_kommentar) or '',
                                  'standard': (a.wert(teile[3], ohne_kommentar) or '').upper()})
                for m in re.finditer(r'(?:lib\.)?addKeybind\s*\(\s*\{', ohne_kommentar):
                    teile, ende = argumente(ohne_kommentar, m.end())
                    block = ohne_kommentar[m.end():ende]
                    felder = {}
                    for f in ('name', 'description', 'defaultKey', 'defaultMapper'):
                        t = re.search(r'\b%s\s*=\s*([^,\n]+)' % f, block)
                        if t:
                            felder[f] = a.wert(t.group(1).strip().rstrip(','), ohne_kommentar)
                    if not felder.get('name'):
                        t = re.search(r'\bname\s*=\s*([^,\n]+)', block)
                        offen.append(f'{name}: {d}: addKeybind ohne lesbaren Namen ({t.group(1)[:40] if t else "?"})')
                        continue
                    if felder.get('defaultMapper') and felder['defaultMapper'].lower() != 'keyboard':
                        continue
                    funde.append({'ressource': name, 'befehl': '+' + felder['name'], 'beschreibung': felder.get('description') or '',
                                  'standard': (felder.get('defaultKey') or '').upper()})
    return funde, offen


def main():
    gestartet, gestoppt = server_cfg()
    funde, offen = suchen(gestartet, gestoppt, convars())
    namen = json.load(open(NAMEN, encoding='utf-8')) if os.path.exists(NAMEN) else {}
    ergaenzt = namen.get('ergaenzt', [])

    eintraege, gesehen, ohne_namen = {}, set(), []
    for f in funde + ergaenzt:
        k = f['ressource'] + '|' + f['befehl']
        if k in gesehen:
            continue
        gesehen.add(k)
        n = namen.get('namen', {}).get(k)
        if n is None:
            ohne_namen.append(f)
            n = {'name': f['beschreibung'] or f['befehl'], 'gruppe': 'Sonstiges'}
        if n.get('aus'):
            continue
        standard = (n.get('standard') or f.get('standard') or '').upper()
        if not standard:
            standard = 'NULL'  # so schreibt FiveM selbst „keine Taste“
        gruppe = n.get('gruppe', 'Sonstiges')
        if gruppe == 'Team (Admins)':
            continue  # Admin-Tasten gehören nicht in den Spieler-Launcher; ihre rbind-Zeilen bleiben unangetastet
        eintraege.setdefault(gruppe, []).append({'ressource': f['ressource'], 'befehl': f['befehl'], 'name': n['name'], 'standard': standard})

    gruppen = []
    for g in GRUPPEN_REIHENFOLGE + sorted(set(eintraege) - set(GRUPPEN_REIHENFOLGE)):
        if g in eintraege:
            reihenfolge = namen.get('reihenfolge', {})
            liste = sorted(eintraege[g], key=lambda t: (reihenfolge.get(t['ressource'] + '|' + t['befehl'], 500), t['name'].lower()))
            gruppen.append({'name': g, 'tasten': liste})

    ausgabe = {
        'hinweis': 'Erzeugt von werkzeuge/klauderp-launcher/tasten-liste.py, nicht von Hand ändern. Deutsche Namen: tasten-namen.json.',
        'gruppen': gruppen,
    }
    with open(ZIEL, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(ausgabe, f, ensure_ascii=False, indent=1)
        f.write('\n')

    anzahl = sum(len(g['tasten']) for g in gruppen)
    print(f'{anzahl} Tastenbelegungen in {len(gruppen)} Gruppen -> {ZIEL}')
    if ohne_namen:
        print(f'\nOhne deutschen Namen ({len(ohne_namen)}), bitte in tasten-namen.json eintragen:')
        for f in ohne_namen:
            print(f'  "{f["ressource"]}|{f["befehl"]}": standard {f["standard"] or "-"}, "{f["beschreibung"]}"')
    if offen:
        print('\nNicht lesbar (von Hand prüfen, ggf. unter "ergaenzt" eintragen):')
        for o in offen:
            print('  ' + o)
    return 0


if __name__ == '__main__':
    sys.exit(main())
