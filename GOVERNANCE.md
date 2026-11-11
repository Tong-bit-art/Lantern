# Governance

Lantern is maintained by a single person ([@Tong-bit-art](https://github.com/Tong-bit-art)). This
is a benevolent-maintainer model, written down so that contributors know what to expect rather than
having to guess.

## Decisions

- Direction is set by the maintainer. [ROADMAP.md](ROADMAP.md) states intent, not a commitment.
- Anything touching `src/Lantern.Core/Policy/` — the rule language, the conflict-resolution order,
  the on-disk format — is the one part of this repository that is original work, so changes to it
  start as an issue explaining the reasoning. A pull request that changes rule semantics without a
  prior issue is likely to be asked to open one first.
- Everything else: send the pull request, small or large.

## Pull requests

- A pull request must keep the build and tests green. `dotnet build Lantern.slnx -c Release` and
  `dotnet test Lantern.slnx` are what CI runs.
- New behaviour in the policy engine needs tests. The existing suite is the specification; if a
  change makes a test wrong rather than failing, say so in the pull request and explain why.
- Do not strip or rewrite the upstream MIT notice. `LICENSE` carries both copyright lines on
  purpose.

## Upstream

Upstream Filee is tracked as a version snapshot, not as a live git remote. The policy and the
replay procedure are in [docs/UPSTREAM.md](docs/UPSTREAM.md). Upstream code that looks better than
what is here is still the maintainer's call to merge, because merging it means replaying the
identity replacements afterwards.

## Becoming a maintainer

Sustained, useful contribution is the path: a track record of pull requests that were merged, and
review of other people's work. When that happens the person is added as a collaborator and
`.github/CODEOWNERS` is updated.

## Conduct

[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Reports go through a private security advisory rather
than a public issue.
