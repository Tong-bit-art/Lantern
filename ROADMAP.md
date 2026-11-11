# Roadmap

Intent, not a promise. Items move off this list or change shape as they get done, and the
maintainer may close one without replacing it.

## Now

- **Make CI trustworthy.** The workflow inherited from upstream has never passed on this
  repository. Get one green run on `main`, then put the status badge back in the README.
- **Ship `v1.0.0`.** A source-only release with checksums, so the download and version badges stop
  being lies. No installer until there is something meaningful to install.
- **Keep the format count honest.** The README and the repository description say 280, derived from
  `FormatRegistry.Known`. Derive it at build time instead of restating it by hand in three places,
  where it will drift again.

## Next

- **Wire the policy engine into the conversion path.** The engine is complete and tested, but it is
  not yet consulted by `ConversionService` — today it is a library with a test suite. This is the
  single largest piece of work left, and the reason the feature is described in the README as
  designed rather than as shipped.
- **A settings page for rules.** Editing `policy.json` by hand is fine for the first version and
  hostile to everyone else. The decision trace should be reachable from the UI.
- **Replay script for upstream snapshots.** `build/sync-upstream.ps1`, following the three categories
  in `FORK-CHANGES.md`. VSCodium does this with shell scripts and it is the single strongest signal
  that a fork intends to survive.

## Someday

- **Code signing and an update key.** Both are empty by design right now. Filling them means buying
  an Authenticode certificate and generating a key pair with `build/new-update-key.ps1`; until then
  Windows will warn about installers built from this tree and the updater does nothing.
- **Translated READMEs**, written from the current English one rather than inherited. The 18 files
  under `docs/_upstream-readme-translations/` describe upstream's product and are kept only as
  reference.
- **More facts and operators** in the policy language, if real use shows which ones are missing.
  Adding them speculatively is how a rule language becomes unreadable.

## Not planned

- **Becoming a general fork of upstream.** Changes outside the policy engine and the identity split
  are considered only when they fix something a user of this fork actually hit.
- **A format matrix of our own.** Upstream maintains it; duplicating it here would go stale
  immediately and mislead people about what this repository is responsible for.
