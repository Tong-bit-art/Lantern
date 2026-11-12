# Releasing

What a release of this fork involves, and what is deliberately not done yet.

## What a release is, right now

Source only. There is no installer you can hand to someone and no binary anybody should run,
because:

- No Authenticode certificate. A Windows installer built from this tree warns under SmartScreen.
- No update signing key. `LanternUpdatePublicKey` in `Directory.Build.props` is empty, so the
  updater accepts only a same-release checksum match.
- The Windows-ARM64 job's missing engines are now published by this repository, so it has what it
  fetches; the remaining CI failure is the arm64 macOS dylib (see below).

A tag is still worth making: it gives the CHANGELOG a version to point at, it gives people a
snapshot to build from, and it makes the download badge tell the truth instead of saying "no
releases found".

## Tagging 1.0.0

`v1.0.0` is **published as a prerelease**, not as the latest release. That is deliberate and it is
what makes the updater behave:

- GitHub's `/releases/latest` answers with the newest *non-prerelease* release. A prerelease is
  skipped, so an installed copy does not offer an update it cannot install.
- `UpdateService.RefreshAsync` additionally refuses to offer a release with no package for the
  current platform, so a source-only tag cannot prompt-and-fail even if it were marked latest.
- The README's release badge passes `include_prereleases` so it shows the tag rather than "no
  releases found". A badge that says nothing exists while the project has a published tag is its own
  kind of lie.

Mark the release latest only once it carries a signed installer.

```bash
git tag -a v1.0.0 -m "v1.0.0 — source only"
git push origin v1.0.0
gh release create v1.0.0 --title "v1.0.0 — source only" --notes-file <(sed -n '/## \[1.0.0\]/,/^---$/p' CHANGELOG.md) --prerelease
```

Release body: the `## [1.0.0]` section from `CHANGELOG.md`, verbatim. It states what is new, what
changed, what was removed, and — importantly — that the format conversion itself is upstream's work.

Attach nothing but `SHA256SUMS` if you attach anything at all. A source-only release that ships an
unsigned installer is worse than one that ships nothing, because somebody will run it.

## What each CI job covers

| Job | What it verifies |
| --- | --- |
| `changes` | Which parts of the tree changed, so the rest can skip |
| `build` | Format check, full test suite on Windows, installer round trip |
| `metadata` | The `.desktop`/AppStream metadata is valid |
| `portable / Linux native` | Native dependencies build on Ubuntu x64 and ARM64 |
| `portable / portable` | The self-contained package builds per platform |
| `build (win-arm64)` | The Windows ARM64 build |

### Known failing jobs

`build (win-arm64)` and `portable / portable (macos-15, osx-arm64)` / `(macos-15-intel, osx-x64)` do
not pass. The original cause — this fork owning none of upstream's download infrastructure — is now
half fixed, and what is left is different in each case.

- **win-arm64: fixed.** `windows-arm64-native.yml` builds rhwp 0.8.7 and Unhwp 0.13.0 from their pinned
  sources and, with `publish` set, puts them on the `native-win-arm64` release of this repository.
  `fetch-engines.ps1` pins those URLs and their SHA-256; both were downloaded back and checked, and both
  are ARM64. The job has what it needs.
- **macOS x64: fixed.** `magick-native-macos.yml` built `Magick.Native-Q8-x64.dll.dylib` for macOS 13
  and published it to `native-magick-macos`; `fetch-magick-native.ps1` pins it. Verified by downloading
  and reading the Mach-O header.
- **macOS arm64: blocked upstream.** Dependencies' `macos-arm64/install.sh` — fetched from upstream at
  build time, not present in this repository — uninstalls zstd and then asserts `zstd should not be
  installed`, while the formulas it installs are linked against that library. The leg dies resolving its
  own tools. The library is not missing and not the wrong architecture; the log shows a valid arm64
  Mach-O at exactly the path dyld reports as missing. It is marked `continue-on-error` for arm64 only so
  the failure is visible without hiding the x64 result. Fixing it means changing that upstream script,
  or pinning an older Magick.Native whose dependencies do not have the contradiction.

**This is why there is no CI badge in the README.** A badge that reads "failing" on the front page
of a project is worse than no badge. It goes back when the jobs pass.

## What is needed before a real binary release

1. An Authenticode code-signing certificate, and `build/sign-windows.ps1` pointed at it.
2. A key pair from `build/new-update-key.ps1`, with the public half set as
   `LanternUpdatePublicKey` in `Directory.Build.props`. Without it, an installed copy cannot verify
   a later release and will refuse to update.
3. The arm64 macOS dylib, which is blocked on an upstream script that removes the zstd its own formulas
   need — see `docs/RELEASING.md`. Everything else those jobs needed is now published by this repository.
4. A decision about who signs for macOS, which needs an Apple Developer account for notarisation.
