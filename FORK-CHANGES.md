# Fork changes — Lantern vs. Filee

Baseline: `47e24cd3311b00b7fae75e36a8c3e98b0fe72b24` (Filee 2.4.0). See `UPSTREAM-BASELINE.txt`.

This document exists so that anyone — a platform reviewer, a lawyer, a contributor — can tell
apart three kinds of change that are easy to conflate: mechanical renames, identity replacements,
and actual new work. Only the third kind is original authorship. The first two are necessary for
the fork to be legally distributable and operationally separate, but they are not a claim of
creativity and this document does not present them as one.

## 1. Mechanical rename (no logic change)

Applied uniformly across the tree. A reviewer can reproduce every one of these with `sed` and
confirm that no control flow, data structure or algorithm differs.

| Category | From | To |
| --- | --- | --- |
| Root namespace | `Filee` | `Lantern` |
| Assemblies | `Filee.App`, `Filee.Core`, `Filee.Engines`, … | `Lantern.App`, `Lantern.Core`, `Lantern.Engines`, … |
| Project folders | `src/Filee.*`, `tests/Filee.*` | `src/Lantern.*`, `tests/Lantern.*` |
| Solution | `Filee.slnx` | `Lantern.slnx` |
| Entry executable | `Filee.exe` | `Lantern.exe` |
| CLI executable | `filee-cli` | `lantern-cli` |
| CLI command | `filee` | `lantern` |
| Icon assets | `filee.ico`, `filee.png`, `filee.svg` | `lantern.ico`, `lantern.png`, `lantern.svg` |
| Avalonia resource root | `avares://Filee/…` | `avares://Lantern/…` |
| Environment variables | `FILEE_DATA_DIR`, `FILEE_UPDATE_KEY`, … (20 names) | `LANTERN_DATA_DIR`, `LANTERN_UPDATE_KEY`, … |
| MSBuild properties | `FileeRepositoryUrl`, `FileeRelease`, … | `LanternRepositoryUrl`, `LanternRelease`, … |
| Linux desktop files | `filee-donut@filee.sh` (GNOME/KWin/Hyprland) | `lantern-donut@lantern.sh` |
| macOS bundle strings | `Filee` in `Info.plist`, `ServicesMenu.strings` | `Lantern` |

Renaming was done with byte-level substitution over the tracked text files. It produced seven
classes of collateral damage in identifiers that happened to contain the substring `File`
(`ZipFileExtensions`, `MoveFileExW`, `SHCreateStreamOnFileEx`, `FileExists`, `FileExtension`,
`FileExplorerContextMenus`, `UsesFilesCopy`, `AddEntry`, `StartFile`). Each was found by compiling
and fixed by hand. This is recorded because it is evidence about method: the rename was driven by
the compiler, not by a search-and-replace that was trusted. Commit history shows the correction
steps.

## 2. Identity replacement (separation, not authorship)

An installation of this fork and an installation of upstream Filee must coexist without touching
each other. These changes are the minimum required for that, and each one is a place where a
copy-paste fork would have caused real damage to users.

| Item | Upstream value | Lantern value |
| --- | --- | --- |
| Windows installer GUID | `{C8686E06-8327-40A2-B0F7-3DEDD61D9248}` | `{891211E8-307E-4D05-BF33-98BDC7D7383C}` |
| Uninstall registry key | `…\Uninstall\{C8686E06-…}_is1` | `…\Uninstall\{891211E8-…}_is1` |
| Startup entry name | `Filee` | `Lantern` |
| Explorer context menu | `Software\Classes\*\shell\Filee` | `Software\Classes\*\shell\Lantern` |
| Settings folder | `%APPDATA%\Filee` | `%APPDATA%\Lantern` |
| Engine download folder | `%LocalAppData%\Filee\engines` | `%LocalAppData%\Lantern\engines` |
| Work folder | `%TEMP%\Filee` | `%TEMP%\Lantern` |
| Unix cache folder | `~/.cache/filee`, `~/Library/Caches/Filee` | `~/.cache/lantern`, `~/Library/Caches/Lantern` |
| Single-instance mutex | `Filee-<sha256 prefix>` | `Lantern-<sha256 prefix>` |
| Update API endpoint | `api.github.com/repos/KnifeLemon/Filee` | `api.github.com/repos/Tong-bit-art/Lantern` |
| Update signing key | upstream's P-256 public key | **emptied** |
| Expected Authenticode publisher | `CN=Open Source Developer MYEONGGU KIM, …` | **emptied** |
| Expected Authenticode issuer | `CN=Certum Code Signing 2021 CA, …` | **emptied** |
| Version number | 2.4.0 | 1.0.0 |
| Vendor / copyright | Filee contributors | Tong-bit-art, with the upstream notice retained |

Two of these deserve emphasis.

**The installer GUID had to change.** Keeping the upstream GUID would have made this installer
register as an upgrade of Filee: it would have overwritten the user's existing installation and
its uninstaller would have deleted Filee's files. That is user-visible data loss, not a naming
quibble.

**The update key and publisher had to be emptied, not replaced with wishful values.** The upstream
build embeds a public key and refuses any update whose checksum manifest is not signed by the
matching private half, which only the upstream maintainer holds. Leaving them in place would have
produced a fork that permanently reports "update available" and permanently refuses to install it.
Emptying them makes the updater fall back to same-release checksum verification, which is the
correct behaviour for a project that does not yet publish signed releases. Populating them requires
generating a fresh key pair with `build/new-update-key.ps1` and buying an Authenticode certificate;
that is a release-engineering step, documented in the file itself, and deliberately not faked.

## 3. Original work

Everything in this section has no upstream counterpart.

| Path | Lines | What it is |
| --- | --- | --- |
| `src/Lantern.Core/Policy/PolicyLanguage.cs` | 373 | Rule-language primitives: typed values, twelve comparison operators, a nestable condition tree with four combine modes, and a trace-producing evaluator. |
| `src/Lantern.Core/Policy/ConversionPolicy.cs` | 267 | Rule and action models, fact table with derived-fact expansion, the conflict-resolution order (priority → specificity → declaration), decision merging with sealing, and the audit record. |
| `src/Lantern.Core/Policy/PolicyStore.cs` | 364 | JSON persistence with a deliberately lenient reader: unknown operators drop one test, rules without an id drop one rule, and corrupt JSON falls back to the starter policy instead of throwing. Round-trip serialiser for the whole condition tree. |
| `src/Lantern.Core/Policy/StarterPolicy.cs` | 145 | The seven rules a new install starts with, each written to solve a concrete batch-conversion problem, plus the list of fact names the UI offers as suggestions. |
| `tests/Lantern.Core.Tests/Policy/PolicyEngineTests.cs` | 318 | 19 tests pinning merge semantics, tie-breaks, operator edge cases, sealing, rejection, and the behaviour of the shipped starter rules. |
| `tests/Lantern.Core.Tests/Policy/PolicyStoreTests.cs` | 242 | 8 tests pinning the on-disk JSON vocabulary as a contract, including lenient-read behaviour and full round-trip fidelity. |
| `EVIDENCE/*.md`, `UPSTREAM-BASELINE.txt`, `NOTICE` | — | Provenance documentation. |

Total original code: 1,709 lines across 6 source files. 27 tests, all passing.

### Why this is a real feature and not filler

The baseline app already decides *what* a file becomes: the user drops files on a slice and a
preset names a target format. It has no mechanism for deciding *how a whole batch* should be
handled under conditions the user describes once and reuses. The concrete gaps:

1. **No conditional defaults.** A photographer converting a folder wants HEIC files kept at
   quality 92 and PNG screenshots crushed to WEBP; the baseline requires two separate manual
   passes over two separate selections.
2. **No batch-aware rules.** "If this drop has more than a dozen documents, merge them instead of
   converting each one" is not expressible in a preset, because a preset sees one file at a time
   and has no access to the size of the drop.
3. **No explainability.** When a preset produces an unexpected format, nothing in the baseline
   tells the user which setting won. The policy engine's decision record answers that question
   line by line.
4. **No guard rails.** The baseline will happily let a misconfigured output pattern write over a
   source. The starter policy refuses that case with a sealed high-priority rule.

The engine is deliberately pure: it takes a fact table in and returns a decision out, with no file
system access, no clock, no network. That design is what makes the 27 tests meaningful and what
makes the module reviewable in isolation from the 150,000 lines it sits inside.

### How to verify the originality claim mechanically

```bash
# 1. Get the exact baseline.
git clone https://github.com/KnifeLemon/Filee.git upstream
git -C upstream checkout 47e24cd3311b00b7fae75e36a8c3e98b0fe72b24

# 2. Confirm the policy module does not exist upstream.
ls upstream/src/Filee.Core/Policy 2>&1   # expect: No such file or directory

# 3. Confirm no upstream file contains the module's public types.
grep -rn "ConversionPolicy\|PolicyRule\|PolicyTest" upstream/src --include=*.cs | wc -l   # expect: 0

# 4. Confirm the module is wired into the fork and compiles.
grep -rn "namespace Lantern.Core.Policy" src/
dotnet build src/Lantern.Core/Lantern.Core.csproj -c Release

# 5. Confirm the tests actually exercise it.
dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj -c Release --filter "FullyQualifiedName~Policy"
```

## 4. Verification log

| Check | Result |
| --- | --- |
| `dotnet build Lantern.slnx -c Release` | success, 0 errors, 16 warnings (all inherited from upstream) |
| `Lantern.exe` produced | yes, `src/Lantern.App/bin/Release/net10.0/Lantern.exe` |
| `dotnet test tests/Lantern.Core.Tests --filter Policy` | 27 passed, 0 failed |
| `dotnet test tests/Lantern.Core.Tests` (whole project) | 1238 passed, 2 failed, 8 skipped |
| The 2 failures, run against the untouched upstream tree | also fail there (network-dependent `WebDownloaderTests`) |
| Upstream baseline build, before any change | success, 0 errors |

The two failing tests are `WebDownloaderTests.Never_follows_a_redirect_into_the_local_network` and
`WebDownloaderTests.Connects_only_to_the_checked_addresses_of_a_name`. Both attempt real DNS
resolution and HTTP requests against test endpoints, and both fail identically on the unmodified
upstream checkout in this environment. They are not caused by any change in this fork. This is
stated plainly rather than omitted, because a verification log that only reports green is not a
verification log.

## 5. What this fork is not

It is not a clean-room reimplementation. The bulk of the source remains upstream's MIT-licensed
work, and the LICENSE file says so with both copyright lines intact. Anyone claiming this
repository is entirely original would be wrong, and `UPSTREAM-BASELINE.txt` makes that claim
impossible to sustain by accident.

What can be claimed accurately:

- The policy engine, its tests and this documentation are original work by Tong-bit-art.
- The fork is a lawful MIT derivative; it may be distributed, including in closed form, provided
  the LICENSE text is retained. It is.
- The fork is operationally independent of upstream: separate installer identity, separate data
  directories, separate update channel, no shared signing material.
