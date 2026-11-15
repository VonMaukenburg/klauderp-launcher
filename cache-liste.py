"""Schreibt die Fingerabdrücke (SHA1) aller Dateien, die Spieler vom Server laden, nach rp-launcher/daten/cache.txt.

FiveM legt jede geladene Server-Datei im Spieler-PC unter
%LOCALAPPDATA%\\FiveM\\FiveM.app\\data\\server-cache-priv\\cache_<SHA1 der Datei> ab.
Mit dieser Liste löscht der Launcher dort nur Dateien von Klaude RP, nie die von anderen Servern.

Quelle ist der Ordner cache/files des Servers:
- <ressource>/resource.rpf usw.: SHA1 des Inhalts (wird zwischengespeichert, nur geänderte Dateien neu gerechnet)
- <ressource>/stream_cache/z<SHA1>: der Name ist schon der Fingerabdruck

Live läuft das alle 15 Minuten über den Timer rp-launcher-cache (deploy/INSTALL.md), lokal von Hand:
    python werkzeuge/klauderp-launcher/cache-liste.py
"""
import argparse
import hashlib
import json
import os
import re
import sys
import time

HIER = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HIER, '..', '..'))
BASIS = os.path.join(REPO, 'txData', 'Qbox_D53072.base')
MUSTER = re.compile(r'^z?([0-9a-fA-F]{40})$')


def sha1_datei(pfad):
    h = hashlib.sha1()
    with open(pfad, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--basis', default=BASIS, help='Server-Daten-Ordner (mit cache/ und resources/)')
    args = ap.parse_args()

    cache_ordner = os.path.join(args.basis, 'cache', 'files')
    ziel_ordner = os.path.join(args.basis, 'resources', '[rp]', 'rp-launcher', 'daten')
    if not os.path.isdir(cache_ordner):
        print(f'Kein Cache-Ordner: {cache_ordner}', file=sys.stderr)
        return 1
    os.makedirs(ziel_ordner, exist_ok=True)

    merk_datei = os.path.join(ziel_ordner, 'merker.json')
    try:
        with open(merk_datei, encoding='utf-8') as f:
            merker = json.load(f)
    except (OSError, ValueError):
        merker = {}
    neuer_merker = {}

    hashes = set()
    gerechnet = 0
    for wurzel, _, dateien in os.walk(cache_ordner):
        im_stream = os.path.basename(wurzel) == 'stream_cache'
        for name in dateien:
            if im_stream:
                m = MUSTER.match(name)
                if m:
                    hashes.add(m.group(1).lower())
                continue
            pfad = os.path.join(wurzel, name)
            try:
                st = os.stat(pfad)
            except OSError:
                continue
            schluessel = f'{st.st_size}:{int(st.st_mtime)}'
            alt = merker.get(pfad)
            if alt and alt[0] == schluessel:
                h = alt[1]
            else:
                try:
                    h = sha1_datei(pfad)
                except OSError:
                    continue
                gerechnet += 1
            neuer_merker[pfad] = [schluessel, h]
            hashes.add(h)

    ziel = os.path.join(ziel_ordner, 'cache.txt')
    tmp = ziel + '.neu'
    with open(tmp, 'w', encoding='ascii', newline='\n') as f:
        f.write(f'# stand {int(time.time())}\n')
        for h in sorted(hashes):
            f.write(h + '\n')
    os.replace(tmp, ziel)
    with open(merk_datei + '.neu', 'w', encoding='utf-8') as f:
        json.dump(neuer_merker, f)
    os.replace(merk_datei + '.neu', merk_datei)
    print(f'{len(hashes)} Fingerabdrücke geschrieben ({gerechnet} Dateien neu gerechnet) -> {ziel}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
