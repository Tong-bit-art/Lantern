# Tracking upstream Filee

Baseline: Filee 2.4.0 = `47e24cd3311b00b7fae75e36a8c3e98b0fe72b24`, recorded in
[`UPSTREAM-BASELINE.txt`](../UPSTREAM-BASELINE.txt).

## Policy

Upstream is a **reference implementation, not a git remote**. There is no `upstream` remote and no
continuous merge. Snapshots are taken deliberately.

The reason is the identity split: the moment upstream code lands here, every replacement listed in
section 2 of [`FORK-CHANGES.md`](../FORK-CHANGES.md) has to be replayed against it, or the build
starts writing to Filee's registry keys and data directories again. A blind `git merge` would do
exactly that, silently. A snapshot is a review, not a merge.

This is also why the fork does not describe itself as a drop-in replacement. Keeping a copy of a
fast-moving project current is the failure mode that has killed more forks than bad features have —
AvaloniaILSpy's archival notice says it outright.

## Taking a snapshot

```bash
# 1. Bring upstream down at the commit you intend to track.
git clone https://github.com/KnifeLemon/Filee.git /tmp/filee-upstream
git -C /tmp/filee-upstream checkout <sha>

# 2. Diff it against the baseline to see what actually changed upstream.
git -C /tmp/filee-upstream diff 47e24cd3311b00b7fae75e36a8c3e98b0fe72b24..<sha> --stat

# 3. Copy the changed files in, then replay the renames and identity replacements.
#    build/sync-upstream.ps1 is planned for this (see ROADMAP.md); until it exists, the
#    checklist in FORK-CHANGES.md sections 1 and 2 is the procedure.

# 4. Never overwrite these with upstream's version:
#      src/Lantern.Core/Policy/        original work
#      EVIDENCE/                       original documentation
#      LICENSE  NOTICE                 dual copyright
#      FORK-CHANGES.md  UPSTREAM-BASELINE.txt

# 5. Update UPSTREAM-BASELINE.txt to the new commit and rebuild.
dotnet build Lantern.slnx -c Release
```

## Versioning

Lantern has its own semantic version series starting at `1.0.0`. Upstream's version number appears
only in `UPSTREAM-BASELINE.txt`, in `FORK-CHANGES.md` and in the historical section of
`CHANGELOG.md`. A Lantern release number is not comparable to a Filee release number, and the
CHANGELOG says so at the top so nobody has to work it out from the gap.

## Known divergences

| Surface | Upstream Filee | Lantern |
| --- | --- | --- |
| Version | 2.4.0 | 1.0.0 |
| Installer identity | Filee GUID, registry keys, mutex | All replaced; coexists with Filee |
| Data directory | `%APPDATA%\Filee` | `%APPDATA%\Lantern`, overridable with `LANTERN_DATA_DIR` |
| Update signing key | upstream's P-256 key | empty — no signed releases yet |
| Authenticode publisher | upstream's Certum certificate | empty |
| Update endpoint | `KnifeLemon/Filee` | `Tong-bit-art/Lantern` |
| Conversion logic | — | unchanged, upstream's |
| Policy engine | does not exist | original work in `src/Lantern.Core/Policy/` |
| Brand | Filee name, logo, site | not used; see `NOTICE` |
