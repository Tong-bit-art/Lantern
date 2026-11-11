# Lantern

A file converter. Drag files onto a format; the result appears next to the original. 270 formats,
all conversion on your own machine, nothing uploaded.

This is a fork of [Filee](https://github.com/KnifeLemon/Filee) (MIT), maintained by
[Tong-bit-art](https://github.com/Tong-bit-art). See `NOTICE`, `UPSTREAM-BASELINE.txt` and
`FORK-CHANGES.md` for exactly what is upstream's work and what is this fork's.

## What this fork adds

A **conversion policy engine** — a declarative way to decide how a whole batch should be handled,
instead of setting each file up by hand.

```jsonc
// %APPDATA%\Lantern\policy.json
{
  "rules": [
    {
      "id": "photo-quality",
      "description": "Camera originals stay high quality",
      "priority": 50,
      "when": { "tests": [
        { "fact": "source.extension", "operator": "inset", "operand": ["heic", "dng", "cr2", "nef"] }
      ]},
      "then": { "targetFormat": "jpg", "quality": 92 }
    },
    {
      "id": "merge-paperwork",
      "description": "A dozen documents in one drop is a stack, not a pile",
      "priority": 40,
      "when": { "tests": [
        { "fact": "source.extension", "operator": "inset", "operand": ["docx", "xlsx", "pptx", "pdf"] },
        { "fact": "request.batch_count", "operator": "gte", "operand": 12 }
      ]},
      "then": { "presetId": "merge-pdf" }
    }
  ]
}
```

Rules merge rather than replace: each one contributes only the settings it names, and the rest
survive from lower-priority rules. Conflicts resolve by priority, then by specificity, then by
declaration order, so a general rule can sit at the top of the file and the exceptions below it. A
rule can `seal` itself and become immune to anything ranked lower.

Every decision can be explained line by line — which rules fired, what each test compared, which
value won. That is the difference between a batch tool you trust and one you re-check by hand.

The engine is pure: facts in, decision out, no file system and no network. 27 tests cover it.

## Building

Requires the .NET 10 SDK.

```bash
git clone https://github.com/Tong-bit-art/Lantern.git
cd Lantern
dotnet build Lantern.slnx -c Release
dotnet run --project src/Lantern.App
```

Optional engines (FFmpeg, LibreOffice, calibre, Ghostscript, Pandoc, ExifTool, Tesseract) are
downloaded on first use. Bundled binaries are fetched by:

```bash
pwsh build/fetch-engines.ps1
```

## Tests

```bash
dotnet test Lantern.slnx
```

Expect two failures in `WebDownloaderTests`: both attempt real DNS resolution and HTTP requests
against test endpoints, and both fail identically on the unmodified upstream checkout in a
restricted network. See `EVIDENCE/build-log.txt`.

## Documentation

| File | What it covers |
| --- | --- |
| `FORK-CHANGES.md` | Every change relative to upstream, split into renames, identity replacements and original work |
| `UPSTREAM-BASELINE.txt` | The exact upstream commit, and how to reproduce the diff |
| `EVIDENCE/ARCHITECTURE-REPORT.md` | How the system is put together: pipeline, routing, formats, presets, identity surfaces |
| `EVIDENCE/ORIGINALITY.md` | What is original here, and the commands to verify it |
| `EVIDENCE/build-log.txt` | Machine output of the build and test runs |
| `docs/` | Upstream's own documentation, unchanged |

## Licence

MIT. Upstream's copyright line and this fork's are both retained in `LICENSE`, as the licence
requires. Third-party components keep their own terms — `THIRD-PARTY-NOTICES.md`.
