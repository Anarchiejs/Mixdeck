# Génère le logo Mixdeck (direction D « Vumètre M ») : SVG maîtres, icônes de l'appli, PNG.
# Usage : python design/logo/build.py   (rendu par le script render_png.py du skill logo-design, Chrome).
import io
import struct
import subprocess
import sys
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
OUT = HERE / "final"
ASSETS = HERE.parents[1] / "src" / "Mixdeck.App" / "Assets"
RENDER = Path.home() / ".claude/plugins/cache/logo-design-skill/logo-design/1.4.5/skills/logo-design/scripts/render_png.py"

BLUE = ("#5A61EB", "#28A7CA")      # dégradé de la tuile, repris de l'ancien logo
RED = ("#F0555A", "#C4314B")       # micro coupé (MixdeckMutedColor clair/sombre)
INK = "#14152B"

# Cinq barres suspendues à une même ligne : leurs hauteurs dessinent un M.
XS = [48, 88, 128, 168, 208]
HS = [168, 124, 80, 124, 168]
W, TOP = 28, 44


def bars(dx=0.0, dy=0.0, s=1.0):
    # Haut plat (la ligne commune), bas arrondi.
    out = []
    for x, h in zip(XS, HS):
        l, r, t, b = (x - W / 2) * s + dx, (x + W / 2) * s + dx, TOP * s + dy, (TOP + h) * s + dy
        rad = W / 2 * s
        out.append(f"M{l:.2f} {t:.2f}H{r:.2f}V{b - rad:.2f}A{rad:.2f} {rad:.2f} 0 0 1 {l:.2f} {b - rad:.2f}Z")
    return "".join(out)


def svg(body, w=256, h=256, defs=""):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}">{defs}{body}</svg>\n'


def tile(colors, small=False):
    a, b = colors
    defs = (f'<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">'
            f'<stop offset="0" stop-color="{a}"/><stop offset="1" stop-color="{b}"/></linearGradient></defs>')
    if small:
        # Grille de 16 px : barres de 2 px, espaces de 1 px.
        rects = "".join(f'<rect x="{1 + 3 * i}" y="3" width="2" height="{h}" rx="1"/>'
                        for i, h in enumerate([10, 7, 4, 7, 10]))
        return svg(f'<rect width="16" height="16" rx="3.5" fill="url(#g)"/><g fill="#fff">{rects}</g>', 16, 16, defs)
    s = 0.66
    dx = 128 - 128 * s
    dy = 128 - (TOP + 84) * s  # centre optique : barres centrées sur leur hauteur visible
    return svg(f'<rect x="8" y="8" width="240" height="240" rx="56" fill="url(#g)"/>'
               f'<path fill="#fff" d="{bars(dx, dy, s)}"/>', defs=defs)


# Mot « mixdeck » dessiné au trait arrondi (lignes et arcs), sans police : pas de licence à gérer.
WORD = [
    (0, "M0 100V200M0 130A30 30 0 0 1 60 130V200M60 130A30 30 0 0 1 120 130V200"),
    (166, "M0 104V200"),
    (208, "M0 100L66 200M66 100L0 200"),
    (316, "M100 50V200M100 150A50 50 0 1 1 100 149.99"),
    (462, "M2 150H100A50 50 0 1 0 91 178.7"),
    (604, "M91 121.3A50 50 0 1 0 91 178.7"),
    (738, "M0 50V200M64 100L4 158M28 136L68 200"),
]
DOT = (166, 64)  # point du i


def wordmark(color, dx, dy, s, stroke=22):
    paths = "".join(f'<path transform="translate({x} 0)" d="{d}"/>' for x, d in WORD)
    return (f'<g transform="translate({dx} {dy}) scale({s})" fill="none" stroke="{color}" stroke-width="{stroke}" '
            f'stroke-linecap="round" stroke-linejoin="round">{paths}'
            f'<circle cx="{DOT[0]}" cy="{DOT[1]}" r="14" fill="{color}" stroke="none"/></g>')


def lockup(mark_color, word_color):
    # Haut des hampes (d, k) sur la ligne des barres, minuscules centrées sur le milieu du symbole.
    s = 0.84
    return svg(f'<path fill="{mark_color}" d="{bars()}"/>' + wordmark(word_color, 262, 44 - 50 * s, s), 980, 256)


def render(svg_path, size, out):
    subprocess.run([sys.executable, str(RENDER), str(svg_path), "--size", str(size), "-o", str(out)],
                   check=True, capture_output=True)
    img = Image.open(out).convert("RGBA")
    if img.size != (size, size):
        img = img.resize((size, size), Image.LANCZOS)
    return img


def ico(frames, path):
    # Images 16 à 64 en BMP 32 bits (lues partout, y compris par la zone de notification), 256 en PNG.
    entries, blobs = [], []
    for img in frames:
        w, h = img.size
        if w >= 256:
            buf = io.BytesIO()
            img.save(buf, "PNG")
            data = buf.getvalue()
        else:
            px = img.tobytes("raw", "BGRA")
            rows = [px[y * w * 4:(y + 1) * w * 4] for y in range(h)][::-1]
            mask = b"\0" * (((w + 31) // 32) * 4 * h)
            data = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0) + b"".join(rows) + mask
        entries.append((w, h, data))
    offset = 6 + 16 * len(entries)
    head = struct.pack("<HHH", 0, 1, len(entries))
    for w, h, data in entries:
        head += struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    path.write_bytes(head + b"".join(d for _, _, d in entries))


def main():
    OUT.mkdir(exist_ok=True)
    files = {
        "mixdeck-symbol.svg": svg(f'<path fill="{INK}" d="{bars()}"/>'),
        "mixdeck-symbol-black.svg": svg(f'<path fill="#000" d="{bars()}"/>'),
        "mixdeck-symbol-white.svg": svg(f'<path fill="#fff" d="{bars()}"/>'),
        "mixdeck-app.svg": tile(BLUE),
        "mixdeck-app-small.svg": tile(BLUE, small=True),
        "mixdeck-app-muted.svg": tile(RED),
        "mixdeck-app-muted-small.svg": tile(RED, small=True),
        "mixdeck-lockup.svg": lockup(BLUE[0], INK),
        "mixdeck-lockup-white.svg": lockup("#fff", "#fff"),
    }
    for name, text in files.items():
        (OUT / name).write_text(text, encoding="utf-8")

    tmp = OUT / "png"
    tmp.mkdir(exist_ok=True)
    for kind, out_name in (("app", "Mixdeck.ico"), ("app-muted", "MixdeckMuted.ico")):
        frames = []
        for size in (16, 20, 24, 32, 40, 48, 64, 256):
            src = OUT / (f"mixdeck-{kind}-small.svg" if size <= 24 else f"mixdeck-{kind}.svg")
            frames.append(render(src, size, tmp / f"{kind}-{size}.png"))
        ico(frames, ASSETS / out_name)
    render(OUT / "mixdeck-app.svg", 256, ASSETS / "Mixdeck-256.png")
    print("ok :", ", ".join(sorted(files)), "+ Mixdeck.ico, MixdeckMuted.ico, Mixdeck-256.png")


if __name__ == "__main__":
    main()
