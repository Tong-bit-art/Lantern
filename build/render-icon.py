"""Render the Lantern mark to PNG and ICO without a native SVG rasteriser.

The artwork is drawn with Pillow primitives rather than by rasterising lantern.svg, because the
SVG renderer (cairosvg) needs libcairo, which is not present on this machine. Drawing directly also
lets each small size be tuned independently: the 16 and 24 px frames drop the ribs and the tassel,
which turn to mud at that scale, and thicken the body outline instead.

Run from the repository root:
    python build/render-icon.py
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ICON_DIR = Path("src/Lantern.App/Assets/Icons")

# Oversampled canvas: drawing at 4x and downsampling with LANCZOS is what keeps the curved body
# edges and the glow from aliasing.
SS = 4


def _lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))  # type: ignore[return-value]


def _vertical_gradient(size: tuple[int, int], top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    width, height = size
    grad = Image.new("RGB", (1, height))
    pixels = grad.load()
    for y in range(height):
        pixels[0, y] = _lerp(top, bottom, y / max(1, height - 1))
    return grad.resize((width, height), Image.BILINEAR)


def _body_path(cx: float, cy: float, half_width: float, half_height: float) -> list[tuple[float, float]]:
    """The lantern's spindle silhouette, sampled as a polygon.

    A superellipse rather than a circle or an ellipse: the exponent below 1 pinches the top and
    bottom towards the cap while keeping the middle full, which is what makes the shape read as a
    lantern instead of an egg.
    """
    points: list[tuple[float, float]] = []
    exponent = 1.55
    for step in range(720):
        theta = 2 * math.pi * step / 720
        cos_t, sin_t = math.cos(theta), math.sin(theta)
        x = math.copysign(abs(cos_t) ** (2 / exponent), cos_t)
        y = math.copysign(abs(sin_t) ** (2 / exponent), sin_t)
        # Taper the top and bottom a little more than the sides.
        taper = 1.0 - 0.12 * (abs(y) ** 3)
        points.append((cx + x * half_width * taper, cy + y * half_height))
    return points


def draw_mark(size: int, detailed: bool) -> Image.Image:
    """One square frame of the mark at `size` pixels."""
    big = size * SS
    canvas = Image.new("RGBA", (big, big), (0, 0, 0, 0))

    cx = cy = big / 2
    unit = big / 512  # the artwork is authored on a 512 grid

    # --- warm halo -------------------------------------------------------------------------
    halo = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    halo_draw = ImageDraw.Draw(halo)
    halo_r = 150 * unit
    for i in range(28, 0, -1):
        t = i / 28
        r = halo_r * t
        alpha = int(38 * (1 - t) ** 1.8)
        if alpha <= 0:
            continue
        halo_draw.ellipse(
            [cx - r, cy + 12 * unit - r, cx + r, cy + 12 * unit + r],
            fill=(255, 216, 140, alpha),
        )
    canvas = Image.alpha_composite(canvas, halo.filter(ImageFilter.GaussianBlur(big * 0.012)))

    # --- geometry --------------------------------------------------------------------------
    body_cx, body_cy = cx, cy - 2 * unit
    half_w = 84 * unit
    half_h = 132 * unit
    cap_h = 26 * unit
    base_h = 26 * unit

    # --- caps ------------------------------------------------------------------------------
    cap_top = (43, 33, 25, 255)
    cap_bottom = (94, 74, 58, 255)
    cap_img = _vertical_gradient((big, big), cap_top[:3], cap_bottom[:3]).convert("RGBA")

    mask = Image.new("L", (big, big), 0)
    md = ImageDraw.Draw(mask)
    # top finial
    finial_w, finial_h = 44 * unit, 18 * unit
    md.rounded_rectangle(
        [cx - finial_w / 2, body_cy - half_h - cap_h - finial_h + 4 * unit,
         cx + finial_w / 2, body_cy - half_h - cap_h + 4 * unit],
        radius=8 * unit, fill=255,
    )
    # top cap
    cap_w = 132 * unit
    md.rounded_rectangle(
        [cx - cap_w / 2, body_cy - half_h - cap_h, cx + cap_w / 2, body_cy - half_h + 4 * unit],
        radius=10 * unit, fill=255,
    )
    canvas.paste(cap_img, (0, 0), mask)

    # --- body ------------------------------------------------------------------------------
    body_points = _body_path(body_cx, body_cy, half_w, half_h)
    body_mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(body_mask).polygon(body_points, fill=255)

    glass = _vertical_gradient(
        (big, big),
        (255, 243, 214),
        (242, 169, 59),
    ).convert("RGBA")
    canvas.paste(glass, (0, 0), body_mask)

    body_outline = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    outline_draw = ImageDraw.Draw(body_outline)
    outline_draw.line(
        body_points + [body_points[0]],
        fill=(245, 166, 35, 255),
        width=max(2, round(14 * unit)),
        joint="curve",
    )
    # Draw the outline at 2x and downsample: a stroked polygon at 4x supersampling still shows
    # scalloping along a long shallow curve, because each source segment lands on a whole pixel.
    hi = body_outline.resize((big * 2, big * 2), Image.LANCZOS).resize((big, big), Image.LANCZOS)
    canvas = Image.alpha_composite(canvas, hi.filter(ImageFilter.GaussianBlur(0.5 * unit)))

    # --- inner flame -----------------------------------------------------------------------
    flame_w, flame_h = 58 * unit, 100 * unit
    flame = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    flame_draw = ImageDraw.Draw(flame)
    fx, fy = cx, body_cy + 22 * unit
    flame_draw.polygon(
        [
            (fx, fy - flame_h / 2),
            (fx + flame_w * 0.46, fy + flame_h * 0.06),
            (fx + flame_w * 0.30, fy + flame_h / 2),
            (fx - flame_w * 0.30, fy + flame_h / 2),
            (fx - flame_w * 0.46, fy + flame_h * 0.06),
        ],
        fill=(240, 158, 26, 255),
    )
    # The bright core sits low in the flame, the way a candle's does.
    flame_draw.ellipse(
        [fx - flame_w * 0.26, fy + flame_h * 0.06, fx + flame_w * 0.26, fy + flame_h * 0.46],
        fill=(255, 249, 232, 255),
    )
    # Only a light blur, so the flame stays a flame instead of a smudge.
    flame = flame.filter(ImageFilter.GaussianBlur(1.4 * unit))
    canvas = Image.alpha_composite(canvas, Image.composite(flame, Image.new("RGBA", (big, big)), body_mask))

    if detailed:
        # --- ribs --------------------------------------------------------------------------
        # Curved cage wires that follow the body silhouette. They are clipped to the body mask so
        # they can never spill outside it, which is what made an earlier version look like two
        # stray brackets floating beside the lantern.
        ribs = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        rd = ImageDraw.Draw(ribs)
        rib_color = (214, 133, 26, 92)
        rib_width = max(1, round(6 * unit))
        rd.line([(cx, body_cy - half_h), (cx, body_cy + half_h)], fill=rib_color, width=rib_width)
        for offset in (-0.62, -0.32, 0.32, 0.62):
            wire = []
            for step in range(97):
                t = -1.0 + 2.0 * step / 96
                y = body_cy + t * half_h * 0.985
                # Narrow towards the caps the same way the silhouette does.
                x = cx + offset * half_w * (1.0 - 0.12 * (abs(t) ** 3))
                wire.append((x, y))
            rd.line(wire, fill=rib_color, width=rib_width, joint="curve")
        ribs.putalpha(Image.composite(ribs.getchannel("A"), Image.new("L", (big, big), 0), body_mask))
        canvas = Image.alpha_composite(canvas, ribs)

    # --- base and tassel -------------------------------------------------------------------
    base_mask = Image.new("L", (big, big), 0)
    bd = ImageDraw.Draw(base_mask)
    base_w = 128 * unit
    bd.rounded_rectangle(
        [cx - base_w / 2, body_cy + half_h - 14 * unit, cx + base_w / 2, body_cy + half_h + base_h],
        radius=12 * unit, fill=255,
    )
    canvas.paste(cap_img, (0, 0), base_mask)

    if detailed:
        tassel = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        td = ImageDraw.Draw(tassel)
        tassel_top = body_cy + half_h + base_h - 4 * unit
        td.line([(cx, tassel_top), (cx, tassel_top + 24 * unit)],
                fill=(245, 166, 35, 255), width=max(1, round(9 * unit)))
        td.ellipse(
            [cx - 15 * unit, tassel_top + 18 * unit, cx + 15 * unit, tassel_top + 58 * unit],
            fill=(245, 166, 35, 255),
        )
        canvas = Image.alpha_composite(canvas, tassel)

    return canvas.resize((size, size), Image.LANCZOS)


def main() -> None:
    ICON_DIR.mkdir(parents=True, exist_ok=True)

    png = draw_mark(512, detailed=True)
    png.save(ICON_DIR / "lantern.png", optimize=True)
    print(f"wrote {ICON_DIR / 'lantern.png'} (512x512)")

    # LOGO.md requires 256, 64, 48, 32, 24 and 16; 128 is welcome too. The small frames are drawn
    # without ribs and tassel.
    frames = {}
    for size in (256, 128, 64, 48):
        frames[size] = draw_mark(size, detailed=True)
    for size in (32, 24, 16):
        frames[size] = draw_mark(size, detailed=False)

    ico_sizes = [256, 128, 64, 48, 32, 24, 16]
    frames[256].save(
        ICON_DIR / "lantern.ico",
        format="ICO",
        sizes=[(s, s) for s in ico_sizes],
        append_images=[frames[s] for s in ico_sizes[1:]],
    )
    print(f"wrote {ICON_DIR / 'lantern.ico'} ({', '.join(str(s) for s in ico_sizes)} px)")


if __name__ == "__main__":
    main()
