# Architecture reconstruction

A reverse-engineering report on Filee 2.4.0, produced by reading the source tree at baseline
`47e24cd3` and by building it. This is the analysis the fork was planned from. It is included in
the repository because knowing how the system is put together is what makes a fork maintainable,
and because a change you cannot explain is a change you cannot defend.

## 1. Scale and shape

| Project | C# files | Lines | Role |
| --- | --- | --- | --- |
| `Filee.Engines` | 304 | 84,769 | One class per conversion capability, plus native-tool wrappers |
| `Filee.App` | 173 | 40,920 | Avalonia UI: tray, donut radial menu, settings pages, services |
| `Filee.Core` | 63 | 14,550 | Formats, conversion planning, presets, settings, history, watching |
| `Filee.Setup` | 33 | 5,577 | Its own Windows installer and uninstaller (Avalonia, trimmed, single file) |
| `Filee.Platform.Windows` | 15 | 2,213 | Registry, Recycle Bin, shell integration, backdrop, elevation probe |
| `Filee.Platform.MacOS` | 7 | 1,398 | Dock policy, accessibility permission, services menu |
| `Filee.Cli` | 7 | 1,442 | `filee convert` / `watch` front end |
| `Filee.Platform.Linux` | 8 | 881 | XDG paths, GNOME pointer bridge, Wayland notes |
| **Total** | **990** | **~151,000** | |

The distribution is the important signal: 56% of the code is format handling, 27% is UI. This is a
conversion product, not a UI product with a converter bolted on.

## 2. Layering

```
Filee.App          Avalonia shell, tray, donut, settings
    │  depends on
    ├── Filee.Core        formats · routing · presets · settings · history
    ├── Filee.Engines     IConverter implementations
    └── Filee.Platform.*  OS integration behind IPlatformServices
```

The dependency graph is acyclic and one-directional: `Core` knows nothing about `Engines`, and
`Engines` knows nothing about `App`. Communication happens through interfaces declared in `Core`
(`IConverter`, `IPdfMerger`, `IEnvelopeOpener`, `ISourceFetcher`, …) and assembled once in
`AppHost.Build` with `Microsoft.Extensions.DependencyInjection`. This is a textbook composition
root, and it is the reason a fork can add a capability without touching the UI.

## 3. The conversion pipeline — the part that matters

The central design decision is that **conversions are edges in a weighted graph, and a request is a
shortest-path query**, not a lookup in a table of supported pairs.

```
source format ──┐
                ├─► RoutePlanner (Dijkstra over IConverter edges, max 3 hops)
target format ──┘         │
                          ▼
                      Route = [RouteStep(converter, from, to), …]
                          │
                          ▼
                      JobQueue (per-engine concurrency limits)
                          │
                          ▼
                      IConverter.ConvertAsync(step, progress, ct)
```

`RoutePlanner.cs` (323 lines) is the whole intelligence. Each `ConversionEdge` carries a `Cost`, so
the planner prefers a direct engine over a two-hop chain when both exist, and prefers a cheap chain
over an expensive one. Edges also carry constraint flags that make the graph honest about what a
multi-hop route would actually produce:

| Flag | Meaning |
| --- | --- |
| `ImageSourcesOnly` | Tracing may follow `icns → png` but must never redraw a PDF or a video frame |
| `NotForImageSources` | The inverse: a photo that became a PDF should be traced, not vectorised |
| `DirectOnly` | The edge is a whole route of its own, never a step inside a longer one |
| `FrameSequence` | Writes every video frame as numbered pictures; never part of an ordinary route |
| `Unshaped` | Engine draws text left-to-right without complex-script shaping |
| `MotionPhoto` / `LivePhoto` | Extract or synthesise an iPhone Live Photo |
| `Caption` / `Translate` / `Metadata` / `Upscale` / `Ocr` | Special-purpose edges outside ordinary routing |

There is a second-order mechanism worth naming: `ITextShapingCheck`. When a router would send text
through an `Unshaped` edge, `JobQueue` first asks the engine to scan the source for scripts it
cannot draw (Arabic, Devanagari, Thai …). If shaping is needed, the file is **refused**, or rerouted
around the edge when an alternative exists. A weaker signal, `WithoutWidths`, only reroutes. This is
the kind of detail that separates a working converter from one that silently emits mojibake, and it
is implemented in a 9-line interface plus one implementation.

## 4. Format model

`FormatRegistry.Known` is a flat list of `FileFormat(Id, DisplayName, Category, Extensions, Writable)`
records. 19 categories (`Image`, `Pdf`, `Document`, `Spreadsheet`, `Presentation`, `Hwp`, `Text`,
`Video`, `Audio`, `Ebook`, `Archive`, `Vector`, `Cad`, `Font`, `Subtitle`, `Data`, `Model3D`, `Geo`,
plus two pseudo-formats `folder` and `exe`).

`FormatFields.cs` layers a second axis on top: a **grade** (`Everyday`, `General`, `Specialist`,
`Legacy`) and a **field** (game, design, science, developer, writing …). Fields can be switched off
so that a user who never touches CAD or 3D models does not see those slices. This is a discoverability
mechanism, not a capability switch: a hidden format still converts, it just stops cluttering the
donut. The distinction is made explicit in the source comments, and it matters — a naive fork would
have made "off" mean "unsupported".

## 5. Presets

`Preset` is a 1,782-line model with nested option groups (`Image`, `Pdf`, `Media`, `Archive`,
`Metadata`, `Output`). 169 built-in presets sit in `BuiltInData.cs`, from `to-png` to `merge-pdf` to
`remove-background`. Presets are what the donut's slices bind to.

Two design details that are easy to miss and expensive to get wrong:

- **Lenient enum reading.** `PresetKindJsonConverter` maps an unknown enum name to a safe default
  instead of throwing, so a presets file written by a newer version still loads. Without this, one
  unknown value resets the user's entire preset collection to factory defaults.
- **Serialiser asymmetry.** `ResizeMode.Custom` and `MinShortEdge` are editor-only states; they are
  read but never written, because a preset that persisted them would make an older version reject
  the whole file as corrupt. The comments cite issues #576 and #652, which are the bug reports from
  when this was wrong.

## 6. Identity surfaces

A fork has to find every place the program names itself. The tree contains eight distinct kinds:

| Kind | Count | Where |
| --- | --- | --- |
| Namespaces / assembly names | 9 projects | every file |
| Windows installer GUID | 1 | `SetupNames.AppGuid`, drives the uninstall key |
| Registry keys | 2 (Run, shell verb) | `WindowsPlatformServices`, `SetupRegistry` |
| Data directories | 4 | `UserDataStore.DefaultDirectory`, `SetupPaths`, `WorkFolders`, `EngineEnvironment` |
| Single-instance mutex | 1 | `SingleInstance.NameFor`, SHA-256 of user name |
| Update endpoint + signing key | 2 | `Directory.Build.props` → embedded assembly metadata |
| Code-signing publisher / issuer | 2 | same |
| Asset file names | 3 icons + 3 desktop entries + 1 man page | `Assets/Icons`, `installer/` |

Missing any of the first four produces a fork that corrupts the user's machine: wrong GUID means
overwriting the original installation; shared data directory means two apps fighting over
`settings.json`; shared mutex means the second app never starts because the first one answers.

## 7. Engine inventory

40 classes implement `IConverter` or one of its sibling interfaces. They split into three kinds:

**In-process managed.** `MagickImageConverter` (ImageMagick via Magick.NET), `PdfSharpConverter`,
`PdfiumConverter`, `PdfTextConverter`, `OoxmlConverter`, `SpreadsheetConverter`, `DocxWriterConverter`,
`HwpxConverter`, `EbookConverter`, `DataConverter`, `SubtitleConverter`, `FontConverter`,
`ModelConverter`, `GeoConverter`, `CadConverter`, `VectorConverter`, `IcnsConverter`, `EmlConverter`,
`P7mConverter`, `MarkdownConverter`. These need nothing installed.

**Bundled binaries.** `RhwpConverter` (HWP→PDF, HWP↔HWPX), `UnhwpConverter`, `ArchiveConverter`
(7-Zip), `FolderIsoConverter`. Shipped inside the package.

**Optional, downloaded on demand.** `FfmpegConverter`, `LibreOfficeConverter`, `CalibreConverter`,
`GhostscriptConverter`, `PandocConverter`, `ExifToolConverter`, `TesseractConverter`,
`WhisperConverter`, `TranstranslatorConverter`, `UpscalerConverter`. `engines.json` pins the exact
source URL and checksum for each; `EngineDownloadService` fetches on first use.

The split matters for a fork because only the first kind is pure C#. The second kind is redistributable
only under its own licence (7-Zip is LGPL with an unRAR restriction; rhwp is its own licence), and
the third kind is not redistributed at all. `THIRD-PARTY-NOTICES.md` is the authoritative list.

## 8. What the analysis concluded

Three things shaped the fork's plan:

1. **The composition root is a single function.** `AppHost.Build` is the one place services are wired.
   A new subsystem can be registered there without touching any other file, which is why the policy
   engine could be added as a self-contained module.
2. **`Core` is pure and testable.** No file system, no UI types, no platform calls beyond
   `IPlatformServices`. That is where original work belongs, because it can be tested exhaustively
   and reviewed without a display.
3. **Identity is spread across eight surfaces, not one.** A fork that only renames the assembly and
   the window title is a fork that will overwrite its parent's installation on the user's machine.
   The checklist in section 6 is the reason section 2 of `FORK-CHANGES.md` is as long as it is.
