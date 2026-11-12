# Roadmap

Intent, not a promise. Items move off this list or change shape as they get done, and the
maintainer may close one without replacing it.

## Now

- **Make CI trustworthy.** The workflow inherited from upstream has never fully passed on this
  repository. Two jobs fail for a reason that is not code: `build (win-arm64)` fails at *Fetch engines
  for engine tests* and `portable / Linux native linux-x64` at *Build dependencies*, because both fetch
  engine binaries from `dl.lantern.sh` — a domain this fork does not own, whose host is currently a
  parked page. Fixing that means either hosting the pinned engine artifacts somewhere real, or making
  those jobs skip the fetch and run a reduced test set. Until one of those lands, CI will stay red on
  those two jobs and there will be no status badge on the README.
- **Ship `v1.0.0`.** A source-only release with checksums, so the download and version badges stop
  being lies. No installer until there is something meaningful to install.
- **Keep the format count honest.** The README and the repository description say 280, derived from
  `FormatRegistry.Known`. Derive it at build time instead of restating it by hand in three places,
  where it will drift again.

## Next

- **Per-file dispatch in the policy engine.** A decision currently applies to the whole drop, because
  a `ConversionJob` carries one preset for all its files. That covers the case rules were written
  for ("this drop is a paperwork stack, merge it") but not "convert the HEIC at 92 and the PNG at 82
  in one pass", which needs a preset per file in the job model. This is the largest piece of work
  left and the reason the README describes batch-level behaviour rather than per-file.
- **A settings page for rules.** Editing `policy.json` by hand is fine for the first version and
  hostile to everyone else. The decision trace should be reachable from the UI.
- **Enforce `allowedEngines`.** The field is parsed and merged, and it appears in the decision trace,
  but the route planner has no per-job allow-list so it changes nothing. Either teach the planner to
  take one, or drop the field — a rule that silently does nothing is worse than one that does not
  exist. `ConversionPolicy.UnsupportedActionFields` marks it until then.
- **Replay script for upstream snapshots.** `build/sync-upstream.ps1`, following the three categories
  in `FORK-CHANGES.md`. VSCodium does this with shell scripts and it is the single strongest signal
  that a fork intends to survive.

## Someday

- **Code signing and an update key.** Both are empty by design right now. Filling them means buying
  an Authenticode certificate and generating a key pair with `build/new-update-key.ps1`; until then
  Windows will warn about installers built from this tree and the updater does nothing.
- **Translated READMEs**, written from the current English one rather than inherited. The 18 upstream
  translations were removed: they described upstream's product, carried upstream's dead links and
  named upstream's author. A translation submitted here should translate this README.
- **More facts and operators** in the policy language, if real use shows which ones are missing.
  Adding them speculatively is how a rule language becomes unreadable.

## Not planned

- **Becoming a general fork of upstream.** Changes outside the policy engine and the identity split
  are considered only when they fix something a user of this fork actually hit.
- **A format matrix of our own.** Upstream maintains it; duplicating it here would go stale
  immediately and mislead people about what this repository is responsible for.
