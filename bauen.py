"""Baut den Klaude RP Launcher und das Update-Paket.

    python werkzeuge/klauderp-launcher/bauen.py            # baut ausgabe/KlaudeRP.exe und ausgabe/update/
    python werkzeuge/klauderp-launcher/bauen.py --test     # zusätzlich ins Testserver-Verzeichnis kopieren (lokal)

Ablauf:
1. Version aus version.txt (bei jeder Änderung für Spieler erhöhen, sonst sieht der Launcher kein Update).
2. Signatur-Schlüssel: privat.pem liegt NICHT in Git (Standard launcher-schluessel/privat.pem neben dem Repo,
   anders mit der Umgebungsvariable KLAUDERP_SCHLUESSEL). Der öffentliche Teil steht in programm/Schluessel.cs.
   Geht privat.pem verloren, können alte Launcher keine Updates mehr annehmen (Spieler müssten neu herunterladen).
3. Oberfläche (web/) als web.zip und die WebView2-Dateien werden in die exe eingebettet (zwei Bau-Durchgänge).
4. Update-Paket: manifest.json (Version, jede Datei mit SHA-256), manifest.sig (RSA-SHA256), objekte/<sha256>,
   alles als Base64-Text gespeichert (so wie rp-launcher es ausliefert).
   Hochladen auf Live: deploy/launcher-hochladen.ps1
"""
import argparse
import base64
import hashlib
import json
import os
import shutil
import subprocess
import sys
import zipfile

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa

HIER = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HIER, '..', '..'))
PROGRAMM = os.path.join(HIER, 'programm')
WEB = os.path.join(HIER, 'web')
EINGEBETTET = os.path.join(PROGRAMM, 'eingebettet')
AUSGABE = os.path.join(HIER, 'ausgabe')
UPDATE = os.path.join(AUSGABE, 'update')
SCHLUESSEL = os.environ.get('KLAUDERP_SCHLUESSEL') or os.path.abspath(os.path.join(REPO, '..', 'launcher-schluessel', 'privat.pem'))
SCHLUESSEL_CS = os.path.join(PROGRAMM, 'Schluessel.cs')
TEST_ZIEL = os.path.abspath(os.path.join(REPO, '..', 'launcher-test', 'resources', 'rp-launcher', 'dateien'))
BIBLIOTHEKEN = ['Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll']
FESTE_ZEIT = (2026, 1, 1, 0, 0, 0)


def b64(zahl):
    roh = zahl.to_bytes((zahl.bit_length() + 7) // 8, 'big')
    return base64.b64encode(roh).decode()


def schluessel_laden():
    hat_echten = os.path.exists(SCHLUESSEL_CS) and 'PLATZHALTER' not in open(SCHLUESSEL_CS, encoding='utf-8').read()
    if os.path.exists(SCHLUESSEL):
        with open(SCHLUESSEL, 'rb') as f:
            privat = serialization.load_pem_private_key(f.read(), password=None)
    elif hat_echten:
        sys.exit(f'Der private Schlüssel fehlt: {SCHLUESSEL}\nOhne ihn können vorhandene Launcher keine Updates annehmen. '
                 'Schlüssel vom PC holen, auf dem er erzeugt wurde (siehe README).')
    else:
        print('Erzeuge neuen Signatur-Schlüssel (einmalig) ->', SCHLUESSEL)
        os.makedirs(os.path.dirname(SCHLUESSEL), exist_ok=True)
        privat = rsa.generate_private_key(public_exponent=65537, key_size=3072)
        with open(SCHLUESSEL, 'wb') as f:
            f.write(privat.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
    zahlen = privat.public_key().public_numbers()
    inhalt = ('namespace KlaudeRP\n{\n'
              '    /// <summary>Öffentlicher Teil des Update-Schlüssels (erzeugt von bauen.py). Der private Teil liegt nicht in Git.</summary>\n'
              '    internal static class Schluessel\n    {\n'
              f'        public const string Modulus = "{b64(zahlen.n)}";\n'
              f'        public const string Exponent = "{b64(zahlen.e)}";\n'
              '    }\n}\n')
    if not os.path.exists(SCHLUESSEL_CS) or open(SCHLUESSEL_CS, encoding='utf-8').read() != inhalt:
        with open(SCHLUESSEL_CS, 'w', encoding='utf-8', newline='\n') as f:
            f.write(inhalt)
    return privat


def web_zip():
    ziel = os.path.join(EINGEBETTET, 'web.zip')
    with zipfile.ZipFile(ziel, 'w', zipfile.ZIP_DEFLATED) as z:
        for wurzel, ordner, dateien in os.walk(WEB):
            ordner.sort()
            for name in sorted(dateien):
                pfad = os.path.join(wurzel, name)
                rel = os.path.relpath(pfad, WEB).replace('\\', '/')
                info = zipfile.ZipInfo(rel, FESTE_ZEIT)
                info.compress_type = zipfile.ZIP_DEFLATED
                with open(pfad, 'rb') as f:
                    z.writestr(info, f.read())


def dotnet_bauen(version):
    befehl = ['dotnet', 'build', os.path.join(PROGRAMM, 'KlaudeRP.csproj'), '-c', 'Release', '-nologo', '-v', 'q',
              f'-p:Version={version}', '-p:Deterministic=true']
    r = subprocess.run(befehl, cwd=PROGRAMM)
    if r.returncode:
        sys.exit('dotnet build fehlgeschlagen')
    return os.path.join(PROGRAMM, 'bin', 'Release', 'net48')


def bibliotheken_finden(ausgabe):
    gefunden = {}
    for wurzel, _, dateien in os.walk(ausgabe):
        for name in dateien:
            if name in BIBLIOTHEKEN and (name not in gefunden or 'win-x64' in wurzel):
                gefunden[name] = os.path.join(wurzel, name)
    fehlt = [b for b in BIBLIOTHEKEN if b not in gefunden]
    if fehlt:
        sys.exit('WebView2-Dateien fehlen in der Bau-Ausgabe: ' + ', '.join(fehlt))
    return gefunden


def sha256(daten):
    return hashlib.sha256(daten).hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--test', action='store_true', help='Update-Paket auch in den lokalen Testserver kopieren')
    args = ap.parse_args()

    version = open(os.path.join(HIER, 'version.txt'), encoding='utf-8').read().strip()
    privat = schluessel_laden()
    os.makedirs(EINGEBETTET, exist_ok=True)
    web_zip()

    # Durchgang 1: WebView2-Dateien holen, Durchgang 2: mit ihnen eingebettet bauen
    bib = bibliotheken_finden(dotnet_bauen(version))
    for name, pfad in bib.items():
        shutil.copyfile(pfad, os.path.join(EINGEBETTET, name))
    shutil.copyfile(os.path.join(PROGRAMM, 'klauderp.ico'), os.path.join(EINGEBETTET, 'klauderp.ico'))
    ausgabe = dotnet_bauen(version)
    exe = os.path.join(ausgabe, 'KlaudeRP.exe')

    os.makedirs(AUSGABE, exist_ok=True)
    shutil.copyfile(exe, os.path.join(AUSGABE, 'KlaudeRP.exe'))

    # Update-Paket
    if os.path.isdir(UPDATE):
        shutil.rmtree(UPDATE)
    os.makedirs(os.path.join(UPDATE, 'objekte'))
    eintraege = []

    def aufnehmen(pfad_im_launcher, quelle):
        with open(quelle, 'rb') as f:
            daten = f.read()
        h = sha256(daten)
        with open(os.path.join(UPDATE, 'objekte', h), 'wb') as f:
            f.write(base64.b64encode(daten))
        eintraege.append({'pfad': pfad_im_launcher, 'sha256': h, 'groesse': len(daten)})

    aufnehmen('KlaudeRP.exe', exe)
    for name in BIBLIOTHEKEN + ['klauderp.ico']:
        aufnehmen(name, os.path.join(EINGEBETTET, name))
    for wurzel, ordner, dateien in os.walk(WEB):
        ordner.sort()
        for name in sorted(dateien):
            pfad = os.path.join(wurzel, name)
            aufnehmen('app/' + os.path.relpath(pfad, WEB).replace('\\', '/'), pfad)

    manifest = json.dumps({'version': version, 'dateien': eintraege}, ensure_ascii=False, indent=1).encode('utf-8')
    signatur = privat.sign(manifest, padding.PKCS1v15(), hashes.SHA256())
    # Alles als Base64-Text: der Server schickt die Dateien so, wie sie sind (kein Umrechnen auf dem Spielserver)
    with open(os.path.join(UPDATE, 'manifest.json'), 'wb') as f:
        f.write(base64.b64encode(manifest))
    with open(os.path.join(UPDATE, 'manifest.sig'), 'wb') as f:
        f.write(base64.b64encode(signatur))
    with open(os.path.join(UPDATE, 'version.txt'), 'w', encoding='ascii') as f:
        f.write(version)

    groesse = os.path.getsize(os.path.join(AUSGABE, 'KlaudeRP.exe'))
    print(f'Fertig: Version {version}, KlaudeRP.exe {groesse / 1e6:.1f} MB, {len(eintraege)} Dateien im Update-Paket')
    print('  ', os.path.join(AUSGABE, 'KlaudeRP.exe'))
    print('  ', UPDATE)

    if args.test:
        if os.path.isdir(TEST_ZIEL):
            shutil.rmtree(TEST_ZIEL)
        shutil.copytree(UPDATE, TEST_ZIEL)
        print('   Testserver:', TEST_ZIEL)


if __name__ == '__main__':
    main()
