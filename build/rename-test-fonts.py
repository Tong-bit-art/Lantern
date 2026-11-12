"""Rewrite only the name records of the test font fixtures, leaving every other table byte-identical.

An earlier attempt called font.save(), which recompiles the whole font and moved glyph metrics: a
test that requires every glyph to have a non-zero advance width then failed. Recompilation is not
needed to rename a font, so this version edits the name table in place and leaves the rest of the
file alone.

`--restore` copies the upstream fixtures back first, so the operation is repeatable.

Run from the repository root:
    python build/rename-test-fonts.py            # rename Filee -> Lantern
    python build/rename-test-fonts.py --restore  # put the originals back
"""

from __future__ import annotations

import argparse
import shutil
from pathlib import Path

from fontTools.ttLib import TTFont

FONTS = Path("tests/Lantern.Engines.Tests/Fonts")
UPSTREAM = Path("recon/filee-upstream/tests/Filee.Engines.Tests/Fonts")

# Upstream file name -> name used in this fork.
PAIRS = {
    "FileeTest-TrueType.ttf": "LanternTest-TrueType.ttf",
    "FileeTest-CID.otf": "LanternTest-CID.otf",
    "FileeTest-NameKeyed.otf": "LanternTest-NameKeyed.otf",
}


def restore() -> None:
    for source, target in PAIRS.items():
        origin = UPSTREAM / source
        if not origin.exists():
            print(f"{source}: not found in {UPSTREAM}, skipped")
            continue
        shutil.copy2(origin, FONTS / target)
        print(f"{target}: restored from {source}")


def rename(path: Path) -> list[str]:
    font = TTFont(path, recalcTimestamp=False)
    changed: list[str] = []

    for record in font["name"].names:
        try:
            value = record.toUnicode()
        except UnicodeDecodeError:
            continue
        if "Filee" not in value:
            continue
        updated = value.replace("Filee", "Lantern")
        record.string = updated
        changed.append(f"  {record.nameID}: {value!r} -> {updated!r}")

    if changed:
        # Save to a sibling and swap, so a failure cannot leave a half-written fixture.
        temp = path.with_suffix(path.suffix + ".tmp")
        font.save(temp, reorderTables=False)
        font.close()
        shutil.move(temp, path)

    font.close()
    return changed


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--restore", action="store_true")
    args = parser.parse_args()

    if args.restore:
        restore()
        return

    for target in PAIRS.values():
        path = FONTS / target
        if not path.exists():
            print(f"{target}: missing")
            continue
        changed = rename(path)
        print(f"{target}: {'renamed' if changed else 'nothing to do'}")
        for line in changed:
            print(line)


if __name__ == "__main__":
    main()
