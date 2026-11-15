"""Erzeugt das Programmsymbol (Taskleiste, Desktop, Startmenü) als klauderp.ico mit allen Größen 16 bis 256.

Stahl-K wie im Logo (gebürstetes Metall, Kupferkante) auf dunkler, achteckiger Plakette wie das Logo-Schild.
Schrift: Saira Semi Condensed 800 aus ki/ui-master/fonts (woff2, wird dafür kurz in TTF umgewandelt).

    python werkzeuge/klauderp-launcher/symbol-erzeugen.py
"""
import io
import os

from fontTools.ttLib import TTFont
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HIER = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HIER, '..', '..'))
SCHRIFT = os.path.join(REPO, 'ki', 'ui-master', 'fonts', 'saira-semi-condensed-latin-800-normal.woff2')
ZIEL_ICO = os.path.join(HIER, 'programm', 'klauderp.ico')
ZIEL_PNG = os.path.join(HIER, 'web', 'bilder', 'symbol.png')

S = 1024  # Arbeitsgröße, am Ende verkleinert


def schrift(groesse):
    f = TTFont(SCHRIFT)
    f.flavor = None
    puffer = io.BytesIO()
    f.save(puffer)
    puffer.seek(0)
    return ImageFont.truetype(puffer, groesse)


def achteck(rand, ecke):
    a, b = rand, S - rand
    return [(a + ecke, a), (b - ecke, a), (b, a + ecke), (b, b - ecke), (b - ecke, b), (a + ecke, b), (a, b - ecke), (a, a + ecke)]


def verlauf(oben, unten):
    v = Image.new('RGBA', (1, S))
    for y in range(S):
        t = y / (S - 1)
        v.putpixel((0, y), tuple(int(oben[i] + (unten[i] - oben[i]) * t) for i in range(4)))
    return v.resize((S, S))


def erzeugen():
    bild = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    # Plakette: dunkles Stahlblech mit Kupferrand
    maske = Image.new('L', (S, S), 0)
    ImageDraw.Draw(maske).polygon(achteck(24, 150), fill=255)
    rand = verlauf((255, 190, 120, 255), (150, 70, 20, 255))
    bild.paste(rand, (0, 0), maske)
    innen = Image.new('L', (S, S), 0)
    ImageDraw.Draw(innen).polygon(achteck(58, 136), fill=255)
    blech = verlauf((46, 48, 52, 255), (14, 15, 17, 255))
    # gebürstete Streifen
    streifen = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(streifen)
    for x in range(0, S, 6):
        d.line([(x, 0), (x, S)], fill=(255, 255, 255, 7 if (x // 6) % 2 else 0), width=2)
    blech = Image.alpha_composite(blech, streifen)
    bild.paste(blech, (0, 0), innen)

    # Buchstabe K: Stahlverlauf, Kupferglut außen
    f = schrift(860)
    k_maske = Image.new('L', (S, S), 0)
    dk = ImageDraw.Draw(k_maske)
    box = dk.textbbox((0, 0), 'K', font=f)
    bx = (S - (box[2] - box[0])) // 2 - box[0] + 6
    by = (S - (box[3] - box[1])) // 2 - box[1]
    dk.text((bx, by), 'K', font=f, fill=255)

    glut = k_maske.filter(ImageFilter.GaussianBlur(26))
    glut_farbe = Image.new('RGBA', (S, S), (239, 138, 44, 255))
    glut_bild = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    glut_bild.paste(glut_farbe, (0, 0), glut.point(lambda v: int(v * 0.85)))
    bild = Image.alpha_composite(bild, glut_bild)

    kante = k_maske.filter(ImageFilter.MaxFilter(17))
    kante_bild = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    kante_bild.paste(verlauf((255, 214, 160, 255), (190, 96, 30, 255)), (0, 0), kante)
    bild = Image.alpha_composite(bild, kante_bild)

    stahl = verlauf((250, 251, 252, 255), (120, 126, 134, 255))
    glanz = Image.new('L', (S, S), 0)
    ImageDraw.Draw(glanz).polygon([(0, 0), (S, 0), (S, int(S * 0.38)), (0, int(S * 0.58))], fill=60)
    stahl = Image.composite(Image.new('RGBA', (S, S), (255, 255, 255, 255)), stahl, glanz)
    k_bild = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    k_bild.paste(stahl, (0, 0), k_maske)
    bild = Image.alpha_composite(bild, k_bild)

    # Nieten (nur bei großen Größen sichtbar)
    d = ImageDraw.Draw(bild)
    for x, y in [(150, 150), (S - 150, 150), (150, S - 150), (S - 150, S - 150)]:
        d.ellipse([x - 16, y - 16, x + 16, y + 16], fill=(150, 156, 164, 255))
        d.ellipse([x - 9, y - 11, x + 5, y + 3], fill=(215, 220, 226, 255))
    return bild


def main():
    gross = erzeugen()
    os.makedirs(os.path.dirname(ZIEL_ICO), exist_ok=True)
    os.makedirs(os.path.dirname(ZIEL_PNG), exist_ok=True)
    groessen = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    gross.resize((256, 256), Image.LANCZOS).save(ZIEL_ICO, sizes=[(g, g) for g in groessen])
    gross.resize((256, 256), Image.LANCZOS).save(ZIEL_PNG)
    vorschau = Image.new('RGBA', (sum(groessen) + 10 * len(groessen), 270), (30, 30, 30, 255))
    x = 5
    for g in groessen:
        vorschau.paste(gross.resize((g, g), Image.LANCZOS), (x, 260 - g), gross.resize((g, g), Image.LANCZOS))
        x += g + 10
    vorschau.save(os.path.join(HIER, 'symbol-vorschau.png'))
    print('Symbol geschrieben:', ZIEL_ICO)


if __name__ == '__main__':
    main()
