# Repository settings that are not in the tree

A few things about a GitHub repository live only in its Settings pages and cannot be set by
committing a file. They are listed here so they can be reproduced, and so it is obvious which of them
are still outstanding.

## Done

| Setting | Value |
| --- | --- |
| Description | "A 280-format offline file converter with a declarative policy engine. Rules, priorities and sealed guard rails instead of per-file clicking. Fork of Filee (MIT). .NET 10 + Avalonia." |
| Topics | 20, leading with `policy-engine`, `rules-engine`, `declarative` — the words that describe what this fork adds — then the format and platform terms |
| Wiki | Off. It was empty, and an empty wiki is a sign of a template nobody finished. |
| Projects | Off, same reason. |
| Discussions | On, with GitHub's six default categories. Q&A is where the docs point; Issues is for bug reports. |
| License detection | `MIT`, confirmed through the API. This needs a plain LICENSE file: the prose about derivation lives in `NOTICE` because it made licensee report `NOASSERTION`. |
| Release | `v1.0.0`, source only, notes taken from `CHANGELOG.md`. |
| Labels | 24. The eleven this fork added are the ones its own automations reference: `formats`, `engine`, `feature`, `translation`, `build`, `macos`, `website`, `fix`, `ci`, `chore`, `skip-changelog`. |
| Dependabot alerts | On, with security updates. Confirmed through the API. |
| Secret scanning | On, including push protection. This matters more than usual here: the build scripts read tokens and certificates. |
| Branch protection | A ruleset on `main` — no deletion, no non-fast-forward — with a bypass for the repository admin. It deliberately does **not** require the CI status check yet: the workflow still fails on the ARM64 and macOS jobs, and a required check that cannot pass blocks every push, including the maintainer's. Add that rule when those jobs pass. |
| Discussions | On, with GitHub's six default categories. Q&A is where `SUPPORT.md` and the issue template point. |

## Outstanding

### Social preview image

GitHub has no API for this. It must be uploaded by hand:

1. Open <https://github.com/Tong-bit-art/Lantern/settings>.
2. Under **Social preview**, upload `docs/media/social-preview.png`.

The image is 1280×640 PNG at 98 KB, which is inside GitHub's limits (under 1 MB, at least 640×320).
It shows the policy file rather than a screenshot, because the policy engine is the one thing here
that is not upstream's.

Until it is uploaded, `open_graph_image_url` is null and every link to the repository on social
platforms renders as a plain grey card.

### Branch protection

The ruleset `protect main` is configured (`docs/ruleset-protect-main.json`), applied through the API.
It blocks deletion and non-fast-forward pushes to `main`, with the repository admin able to bypass —
otherwise the maintainer could not push at all, which is exactly what happened on the first attempt.

It does **not** require the CI status check. Requiring it while the workflow still fails on the ARM64
and macOS jobs would block every push. That rule goes on when those jobs pass.

### Private vulnerability reporting

`SECURITY.md`, `SUPPORT.md` and `CODE_OF_CONDUCT.md` all point at
`/security/advisories/new`. The setting is not exposed in `security_and_analysis` on this plan, and
the dedicated API endpoints answer 404, so it cannot be turned on from here. If that page 404s for a
reporter, enable it by hand: **Settings → Code security → Private vulnerability reporting**.

### Dependabot

`.github/dependabot.yml` is committed and already producing update pull requests for NuGet and
GitHub Actions. Dependabot *alerts* and secret scanning are separate switches:
**Settings → Code security**.

## What stays off, and why

- **CI badge.** The `build` job passes, but the workflow as a whole fails on the Windows-ARM64 and
  macOS jobs. A badge reading "failing" is worse than no badge.
- **Download badge.** There is nothing to download. `docs/RELEASING.md` explains what has to exist
  first.
- **Third-party rank badges** (Trendshift, HelloGitHub, Product Hunt). Those ranks belong to the
  upstream project's entries. Putting them here would be a false claim of standing this repository
  does not have.
