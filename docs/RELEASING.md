# Releasing

What a release of this fork involves, and what is deliberately not done yet.

## What a release is, right now

Source only. There is no installer you can hand to someone and no binary anybody should run,
because:

- No Authenticode certificate. A Windows installer built from this tree warns under SmartScreen.
- No update signing key. `LanternUpdatePublicKey` in `Directory.Build.props` is empty, so the
  updater accepts only a same-release checksum match.
- The macOS and Windows-ARM64 package jobs do not pass in CI (see below).

A tag is still worth making: it gives the CHANGELOG a version to point at, it gives people a
snapshot to build from, and it makes the download badge tell the truth instead of saying "no
releases found".

## Tagging 1.0.0

```bash
git tag -a v1.0.0 -m "v1.0.0 — source only"
git push origin v1.0.0
```

Then draft the release on GitHub. `.github/release.yml` already groups pull requests by label, so
the notes fill themselves in above the section you paste from `CHANGELOG.md`.

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

`build (win-arm64)`, `portable / portable (macos-15, osx-arm64)` and `portable / portable
(macos-15-intel, osx-x64)` do not pass. Two causes, both from this fork not owning upstream's
infrastructure:

- **win-arm64 and macOS** fetch prebuilt engine binaries from a download mirror that belongs to the
  upstream project. This fork has none. `fetch-engines.ps1` now warns and skips the two ARM64-only
  engines instead of failing outright, which is why the job gets further than it used to, but the
  build still cannot complete without those files.
- **macOS** additionally needs `Magick.Native-Q8-*.dll.dylib` built for macOS 13, hosted somewhere
  reachable. `build/fetch-magick-native.ps1` says so and refuses rather than downloading from
  somebody else's bucket.

Fixing either means publishing those artifacts under this project's own name — a GitHub release
works. That is a piece of work in its own right, listed on `ROADMAP.md`.

**This is why there is no CI badge in the README.** A badge that reads "failing" on the front page
of a project is worse than no badge. It goes back when the jobs pass.

## What is needed before a real binary release

1. An Authenticode code-signing certificate, and `build/sign-windows.ps1` pointed at it.
2. A key pair from `build/new-update-key.ps1`, with the public half set as
   `LanternUpdatePublicKey` in `Directory.Build.props`. Without it, an installed copy cannot verify
   a later release and will refuse to update.
3. Engine artifacts published somewhere this project controls, so the ARM64 and macOS jobs can
   complete.
4. A decision about who signs for macOS, which needs an Apple Developer account for notarisation.
