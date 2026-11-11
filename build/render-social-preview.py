"""Render the social preview card GitHub shows when the repository is linked.

Spec from GitHub: PNG/JPG, under 1 MB, at least 640x320, 1280x640 recommended. The card deliberately
leads with the policy file rather than with drag-and-drop, because the policy engine is the only
thing about this repository that is not upstream's.

Run from the repository root:
    python build/render-social-preview.py
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

OUT = Path("docs/media/social-preview.png")
ICON = Path("src/Lantern.App/Assets/Icons/lantern.png")

WIDTH, HEIGHT = 1280, 640
SS = 2  # draw at 2x, downsample for crisp text edges

BG_TOP = (28, 26, 44)
BG_BOTTOM = (18, 17, 30)
AMBER = (245, 166, 35)
CREAM = (255, 246, 231)
MUTED = (168, 163, 200)
CODE_BG = (34, 31, 54)
CODE_KEY = (255, 198, 107)
CODE_STR = (150, 220, 170)
CODE_PUNCT = (140, 134, 175)


def find_font(candidates: list[str], size: int) -> ImageFont.FreeTypeFont:
    for name in candidates:
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default(size)


def main() -> None:
    w, h = WIDTH * SS, HEIGHT * SS
    canvas = Image.new("RGB", (w, h), BG_BOTTOM)

    # Vertical gradient background.
    strip = Image.new("RGB", (1, h))
    px = strip.load()
    for y in range(h):
        t = y / max(1, h - 1)
        px[0, y] = tuple(round(a + (b - a) * t) for a, b in zip(BG_TOP, BG_BOTTOM))
    canvas = strip.resize((w, h), Image.BILINEAR)

    draw = ImageDraw.Draw(canvas)

    # A warm wash behind the icon, echoing the halo in the mark.
    glow = Image.new("RGB", (w, h), BG_BOTTOM)
    gd = ImageDraw.Draw(glow)
    for i in range(40, 0, -1):
        t = i / 40
        r = 300 * SS * t
        shade = tuple(
            round(b + (a - b) * (1 - t) ** 2 * 0.20)
            for a, b in zip(AMBER, BG_BOTTOM)
        )
        gd.ellipse([200 * SS - r, 250 * SS - r, 200 * SS + r, 250 * SS + r], fill=shade)
    canvas = Image.blend(canvas, glow, 0.55)
    draw = ImageDraw.Draw(canvas)

    # --- icon --------------------------------------------------------------------------------
    if ICON.exists():
        icon = Image.open(ICON).convert("RGBA")
        icon_size = 230 * SS
        icon = icon.resize((icon_size, icon_size), Image.LANCZOS)
        canvas.paste(icon, (86 * SS, 74 * SS), icon)

    # --- wordmark ----------------------------------------------------------------------------
    bold = find_font(["segoeuib.ttf", "arialbd.ttf", "DejaVuSans-Bold.ttf"], 92 * SS)
    draw.text((88 * SS, 318 * SS), "Lantern", font=bold, fill=CREAM)

    tagline = find_font(["segoeui.ttf", "arial.ttf", "DejaVuSans.ttf"], 27 * SS)
    draw.text((92 * SS, 440 * SS), "Describe the policy once.", font=tagline, fill=MUTED)
    draw.text((92 * SS, 478 * SS), "Drop the files.", font=tagline, fill=MUTED)

    # --- policy.json card ----------------------------------------------------------------------
    card_x, card_y = 636 * SS, 86 * SS
    card_w, card_h = 546 * SS, 462 * SS
    draw.rounded_rectangle(
        [card_x, card_y, card_x + card_w, card_y + card_h],
        radius=18 * SS,
        fill=CODE_BG,
        outline=(58, 53, 88),
        width=2 * SS,
    )

    # window dots
    for i, colour in enumerate([(255, 108, 92), (255, 191, 92), (76, 209, 122)]):
        cx = card_x + (30 + i * 26) * SS
        cy = card_y + 30 * SS
        draw.ellipse([cx - 6 * SS, cy - 6 * SS, cx + 6 * SS, cy + 6 * SS], fill=colour)

    mono = find_font(["consola.ttf", "cour.ttf", "DejaVuSansMono.ttf"], 22 * SS)
    code_x = card_x + 30 * SS
    line_y = card_y + 74 * SS
    line_gap = 30 * SS

    lines: list[list[tuple[str, tuple[int, int, int]]]] = [
        [("{", CODE_PUNCT)],
        [('  "id"', CODE_KEY), (": ", CODE_PUNCT), ('"shrink-for-upload"', CODE_STR), (",", CODE_PUNCT)],
        [('  "priority"', CODE_KEY), (": ", CODE_PUNCT), ("30", AMBER), (",", CODE_PUNCT)],
        [('  "when"', CODE_KEY), (": ", CODE_PUNCT), ("{ ", CODE_PUNCT), ('"size_mb"', CODE_KEY),
         (": ", CODE_PUNCT), ("> ", CODE_STR), ("8", AMBER), (" }", CODE_PUNCT), (",", CODE_PUNCT)],
        [('  "then"', CODE_KEY), (": ", CODE_PUNCT), ("{ ", CODE_PUNCT), ('"to"', CODE_KEY),
         (": ", CODE_PUNCT), ('"webp"', CODE_STR), (", ", CODE_PUNCT), ('"q"', CODE_KEY),
         (": ", CODE_PUNCT), ("82", AMBER), (" }", CODE_PUNCT)],
        [("}", CODE_PUNCT)],
    ]

    for row in lines:
        x = code_x
        for text, colour in row:
            draw.text((x, line_y), text, font=mono, fill=colour)
            x += draw.textlength(text, font=mono)
        line_y += line_gap

    # --- footer strip ---------------------------------------------------------------------------
    foot = find_font(["segoeui.ttf", "arial.ttf", "DejaVuSans.ttf"], 24 * SS)
    draw.text((92 * SS, 566 * SS), "github.com/Tong-bit-art/Lantern", font=foot, fill=(120, 114, 155))
    draw.text((card_x, 556 * SS), "fork of Filee (MIT)", font=foot, fill=(120, 114, 155))

    OUT.parent.mkdir(parents=True, exist_ok=True)
    canvas.resize((WIDTH, HEIGHT), Image.LANCZOS).save(OUT, optimize=True)
    print(f"wrote {OUT} ({WIDTH}x{HEIGHT})")


if __name__ == "__main__":
    main()
