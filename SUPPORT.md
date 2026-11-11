# Getting help

| I want to… | Where to go |
| --- | --- |
| Ask how to use it, or how to write a rule | [Discussions → Q&A](https://github.com/Tong-bit-art/Lantern/discussions/categories/q-a) |
| Report a bug: wrong output, a crash, a bad conversion | [New issue → Bug report](https://github.com/Tong-bit-art/Lantern/issues/new/choose) |
| Report a problem with a format or a conversion engine | [Filee's tracker](https://github.com/KnifeLemon/Filee/issues) — this fork does not maintain the format matrix |
| Report a security problem | **Not a public issue.** Use [Report a vulnerability](https://github.com/Tong-bit-art/Lantern/security/advisories/new); see [SECURITY.md](SECURITY.md) |
| Build it myself | [README → Build](README.md#build), .NET 10 SDK |
| Find out what this fork changed | [FORK-CHANGES.md](FORK-CHANGES.md) |

## Scope, plainly

Lantern is a fork. The conversion work — 280 formats, the engines, the donut, the presets — is
upstream Filee's, and a bug in it belongs upstream. What is maintained here is:

- The declarative policy engine (`src/Lantern.Core/Policy/`) and its rules.
- The installer identity split, so Lantern and Filee can coexist on one machine.
- This repository's documentation and build.

A useful rule of thumb: if the problem would still happen in Filee, report it to Filee. If it
involves `policy.json`, rule ordering, sealing, decision traces, or the installer, it belongs here.

## Response times

This repository has one maintainer. Expect days, not hours. There is no SLA, and no promise that a
feature request will be accepted — see [GOVERNANCE.md](GOVERNANCE.md).
