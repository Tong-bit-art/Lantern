# Lantern

**A file converter that lets you write down how your batch should be handled, once, and doesn't
make you re-decide it every time.**

Drag files onto a format. The conversion happens on your machine. The result lands next to the
original.

Lantern is a fork of [Filee](https://github.com/KnifeLemon/Filee). What is new here is a rules
layer on top of the converter: instead of configuring each file by hand, you describe the policy
once and the engine applies it to everything you drop.

> **Status: source only, v1.0.0.** No release has been published yet, so there is nothing to
> download — build it from source with the steps below. There is also no code-signing certificate
> and no update signing key, which means: if you build a Windows installer yourself, SmartScreen
> will warn about it, and the auto-updater is inert until those are provisioned. Both are stated
> here rather than buried, because a project page that claims more than it has is worse than a
> short one.

---

## Why the rules layer exists

A converter normally decides *what a file becomes*. It does not decide *how a whole batch should
be treated*. Those are different problems, and the second one is where batch work actually hurts.

Four things the preset model cannot express, and this fork can:

**Per-file-type defaults in one pass.** Your camera writes HEIC and your screenshot tool writes
PNG. You want the first kept at quality 92 and the second crushed to WEBP at 82. Without rules
that is two selections, two drags, every single time.

**Decisions that depend on the size of the drop.** "If this drop contains more than a dozen office
documents, they are a paperwork stack — merge them into one PDF instead of converting twelve
separate files." A preset sees one file and has no idea how many siblings it has. A rule sees
`request.batch_count`.

**An answer to "why did it do that".** When a file comes out in a format you did not expect,
something has to tell you which setting won. Every decision here produces a line-by-line account:
which rules matched, what each test compared, which value survived.

**Guard rails that cannot be overridden by accident.** A batch tool's worst failure mode is
overwriting originals. The shipped policy refuses any conversion whose output would land on its own
source, and the rule is *sealed* — a rule you add later, even at priority 99999, cannot outrank it.

## The policy file

Rules live in `policy.json` in the app's data directory. The default is the platform's
application-data folder plus `Lantern` (`%APPDATA%\Lantern` on Windows; on Linux and macOS it
follows `Environment.SpecialFolder.ApplicationData`, which resolves to `$XDG_CONFIG_HOME/Lantern`
or `~/.config/Lantern`). Setting the `LANTERN_DATA_DIR` environment variable overrides it, which is
also how a development copy keeps its policy separate from the one you actually run.

```jsonc
{
  "name": "default",
  "rules": [
    {
      "id": "photo-quality",
      "description": "Camera originals keep their quality",
      "priority": 50,
      "when": {
        "tests": [
          { "fact": "source.extension", "operator": "inset",
            "operand": ["heic", "dng", "cr2", "cr3", "nef", "arw"] }
        ]
      },
      "then": { "targetFormat": "jpg", "quality": 92 }
    },
    {
      "id": "merge-paperwork",
      "description": "A dozen documents is a stack, not a pile",
      "priority": 40,
      "when": {
        "tests": [
          { "fact": "source.extension", "operator": "inset",
            "operand": ["docx", "doc", "odt", "rtf", "xlsx", "pptx", "pdf"] },
          { "fact": "request.batch_count", "operator": "gte", "operand": 12 }
        ]
      },
      "then": { "presetId": "merge-pdf", "namePattern": "{folder}-merged" }
    }
  ]
}
```

### How conflicts are settled

Three rules, applied in order:

1. **Priority** — a higher number overwrites a lower one.
2. **Specificity** — at equal priority, the rule carrying more tests wins. This is why a general
   rule can sit at the top of the file and the exceptions below it, instead of every rule needing
   a hand-maintained unique priority integer.
3. **Declaration order** — later in the file wins what is still tied.

Rules **layer** rather than replace. A rule contributes only the fields it names; everything else
survives from lower-ranked rules. `allowedEngines` intersects instead of overwriting, so two rules
that each permit a set of engines end up permitting only what both allow — adding a restrictive
rule can never widen what a stricter one forbade.

A rule can set `"sealed": true` to become immune to anything lower, regardless of priority.

### Facts you can test

| Fact | Type | Meaning |
| --- | --- | --- |
| `source.name` | text | File name with extension |
| `source.extension` | text | Lowercased extension, derived automatically |
| `source.size_bytes` | number | Exact size |
| `source.size_mb` | number | Size in MB, derived automatically |
| `source.category` | text | Format family: `image`, `pdf`, `document`, `video`, … |
| `source.format` | text | Format id |
| `request.preset` | text | Preset the caller invoked |
| `request.target` | text | Requested target format |
| `request.batch_count` | number | How many files are in this drop |
| `destination.folder` | text | Where the output would go |
| `destination.free_mb` | number | Free space at the destination |
| `destination.would_overwrite_source` | boolean | Set by the caller before evaluation |
| `user.language` | text | UI language code |
| `user.tag` | text | Free-form tag the caller supplies |

### Operators

`equals` · `notequals` · `gt` · `gte` · `lt` · `lte` · `contains` · `startswith` · `endswith` ·
`inset` · `matches` (regex, 100 ms timeout) · `exists` · `notexists`

Conditions nest, and combine with `all`, `any`, `none`, or `atleast` (with a `minimum`).

A comparison between incompatible types **fails and says so in the trace** rather than coercing.
That strictness is deliberate: rule engines that coerce are why people stop trusting rule engines.

### When the file is wrong

The reader is lenient on purpose, because this file gets hand-edited:

- An unknown operator drops that one test, not the rule.
- A rule with no `id` is skipped with a warning, not fatal.
- Corrupt JSON falls back to the starter policy instead of taking the whole app down.

None of that can silently disable the system. That is the point.

## What you get from upstream, unchanged

All of the actual conversion work: 280 recognised formats across images, PDF, Office documents,
HWP/HWPX, e-books, video, audio, subtitles, archives, vector graphics, CAD, fonts, 3D models and
geospatial data. 169 built-in presets. 22 interface languages. A radial menu that opens at your
cursor. Watch folders. A command line.

Switchable format fields mean a user who never touches CAD or 3D models does not see those slices,
while the formats still convert if a file of that kind arrives — "off" hides, it does not disable.

The full capability list is upstream's to document accurately, so it is not restated here at
length. See [Filee's README](https://github.com/KnifeLemon/Filee) for the format matrix and the
engine download behaviour.

## Build

.NET 10 SDK required.

```bash
git clone https://github.com/Tong-bit-art/Lantern.git
cd Lantern
dotnet build Lantern.slnx -c Release
dotnet run --project src/Lantern.App
```

Tests, and what to expect from them:

```bash
dotnet test Lantern.slnx
```

`WebDownloaderTests` contains two cases that resolve real DNS names and make real HTTP requests.
Both fail in a restricted network, and both fail identically on the unmodified upstream checkout —
they are environment-dependent, not a regression. Everything else passes. The recorded run is in
[`EVIDENCE/build-log.txt`](EVIDENCE/build-log.txt).

Optional engines (FFmpeg, LibreOffice, calibre, Ghostscript, Pandoc, ExifTool, Tesseract) download
on first use. Bundled binaries:

```bash
pwsh build/fetch-engines.ps1
```

## How this repo relates to Filee

Lantern is MIT-licensed and derived from Filee, also MIT. The `LICENSE` file carries **both**
copyright lines, which is what the licence requires and what honesty requires.

| File | What it establishes |
| --- | --- |
| [`UPSTREAM-BASELINE.txt`](UPSTREAM-BASELINE.txt) | The exact upstream commit, so the diff is reproducible |
| [`FORK-CHANGES.md`](FORK-CHANGES.md) | Every change, split into mechanical renames, identity replacements, and original work |
| [`EVIDENCE/ARCHITECTURE-REPORT.md`](EVIDENCE/ARCHITECTURE-REPORT.md) | How the system is built: routing graph, format model, identity surfaces |
| [`EVIDENCE/ORIGINALITY.md`](EVIDENCE/ORIGINALITY.md) | Precisely what is original here, with the commands to verify it |
| [`NOTICE`](NOTICE) | Derivation statement and trademark position |

Being blunt about it: **most of this repository is not my code.** The policy engine, its 27 tests
and the documentation are. Section 3 of `FORK-CHANGES.md` draws that line and the verification
commands in `ORIGINALITY.md` let you check whether it holds.

### Installer identity was changed on purpose

This fork cannot coexist with a Filee installation if it keeps upstream's identity. Specifically:

- New installer GUID (`{891211E8-…}` instead of `{C8686E06-…}`). The old one would have registered
  this as an upgrade of Filee, overwriting it and deleting its files on uninstall.
- Separate registry keys, data directories, cache folders and single-instance mutex.
- Upstream's update signing key and expected Authenticode publisher/issuer are **emptied, not
  replaced**. Those values gate update acceptance; left populated, the app would report an update
  as available and then refuse it forever, because the matching private key belongs to the
  upstream maintainer.

### What is not provisioned

- No code-signing certificate. Windows installers built from this tree will trigger SmartScreen.
- No update signing key. The updater falls back to same-release checksum verification.
- No published release and no CI run.

Populating either requires generating a key pair with `build/new-update-key.ps1` and buying an
Authenticode certificate.

## Licence

MIT. Third-party components keep their own terms — see
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

"Filee" is the upstream project's name. This fork does not use its name, logo, website, installer
identity, update key or signing certificate.

## Links

- Upstream project: [KnifeLemon/Filee](https://github.com/KnifeLemon/Filee)
- This fork: [Tong-bit-art/Lantern](https://github.com/Tong-bit-art/Lantern)
