# Originality statement

This document answers one question precisely: what part of this repository is Tong-bit-art's own
work, and how can a third party verify that without taking anyone's word for it.

## The honest framing

Lantern is a fork of Filee, which is MIT-licensed. Most of the source is upstream's. Saying
otherwise would be both false and trivially disprovable — `UPSTREAM-BASELINE.txt` names the exact
commit, and one `git clone` puts the two trees side by side.

Copyright protects *expression*, not *ideas* or *functionality*. A fork that renames every identifier
and changes nothing else produces a derivative work whose expression is still overwhelmingly the
original author's. That fork is legal under MIT. It is not original, and no amount of renaming makes
it so.

Originality in this repository therefore rests on one thing only: **files that do not exist upstream,
contain logic that does not exist upstream, and are tested.**

## The claim, stated narrowly

Six files, 1,709 lines, 27 tests:

```
src/Lantern.Core/Policy/PolicyLanguage.cs          373 lines
src/Lantern.Core/Policy/ConversionPolicy.cs        267 lines
src/Lantern.Core/Policy/PolicyStore.cs             364 lines
src/Lantern.Core/Policy/StarterPolicy.cs           145 lines
tests/Lantern.Core.Tests/Policy/PolicyEngineTests.cs   318 lines
tests/Lantern.Core.Tests/Policy/PolicyStoreTests.cs    242 lines
```

Everything else in the tree is either upstream code under a rename, or documentation.

## Why the policy engine is a genuine addition

The distinction between "new code" and "code that adds a capability" matters. Since 56% of the
upstream codebase is format handling, it would be easy to add a twelfth converter and call it
original — but that would be filling in an existing pattern the upstream project designed and
documented. The policy engine instead adds something the architecture has no slot for.

What upstream decides | What the policy engine decides
--- | ---
Which formats a file can become | Whether this particular file should be converted at all
Which preset the user picked | Which preset applies when the user has not picked one per file
One file at a time | The whole drop, with the number of files as an input
Fixed behaviour per preset | Conditional behaviour described once and reused

Four capabilities with no upstream equivalent:

1. **Conditional defaults.** "HEIC at quality 92, PNG screenshots to WEBP at 82" is one policy file
   and zero manual passes. Upstream needs two selections and two drags.
2. **Batch-aware rules.** `request.batch_count` is a fact. "More than a dozen office documents in
   one drop become a merged PDF" is expressible; in the upstream preset model it is not, because a
   preset sees one file and has no knowledge of the drop's size.
3. **Explainability.** `PolicyDecision.Explain()` returns a line-by-line account: which rules fired,
   what each test compared, which value won and why. Upstream produces a result with no account of
   how it got there.
4. **Guard rails.** A sealed, highest-priority rule refuses any conversion whose output would land
   on its own source. In a batch tool this is the difference between a mistake and a lost archive.

## Design decisions that constitute the expression

Originality in software lives in the choices, not the line count. The ones here that a different
author would plausibly have made differently:

**Specificity as a tie-break.** Rules sort by priority, then by number of tests, then by declaration
order. The alternative — requiring every rule to carry a unique priority integer — is what most
rule engines do, and it makes policy files fragile: inserting a rule means renumbering its
neighbours. Specificity lets the general rule sit at the top of the file and the exceptions below
it. This is a deliberate, defensible position, and `PolicyEngineTests` pins it so a future refactor
cannot quietly reverse it.

**Layering instead of replacement.** A matching rule contributes only the fields it sets; the rest
survive from lower-priority rules. `PolicyAction.Absorb` merges field by field, and `AllowedEngines`
intersects rather than overwrites, so two rules that each permit a set of engines end up permitting
only what both allow. This is a safety property: adding a restrictive rule can never widen what a
more restrictive one already forbade.

**Sealing.** A rule can set `Sealed` and become immune to later rules regardless of priority. This
exists so a guard rail cannot be accidentally outranked by a rule a user adds later with a large
priority number. Without it, the overwrite guard in the starter policy would be advisory.

**Strict typing.** `PolicyValue` refuses to compare text against a number and records the mismatch
in the trace instead of coercing. Rule engines that coerce are the reason people stop trusting rule
engines. `Numeric_comparisons_refuse_to_compare_a_text_value` tests exactly this.

**Lenient reading, strict writing.** One unknown operator drops one test; one rule without an id
drops one rule; corrupt JSON falls back to the starter policy. A hand-edited configuration file must
never be able to disable the entire system because of one typo. But the writer always emits
camelCase, always round-trips the full tree, and the tests pin that vocabulary as a contract.

**Purity.** The engine takes a `PolicyFacts` dictionary and returns a `PolicyDecision`. No file
system, no clock, no network, no static mutable state. This is why 27 tests cover it thoroughly and
why it can be reviewed in isolation from 150,000 lines of surrounding code.

**A defect found and fixed during development, recorded on purpose.** The first version read JSON
with default serialiser options, which are case-sensitive. A user hand-writing `{"rules": [...]}` in
camelCase would have had the entire file deserialise to an empty rule set — their policy silently
vanishing with no error. Two tests caught it. The fix was `PropertyNameCaseInsensitive` plus an
explicit camelCase read policy. This is in the record because a claim of authorship is more
credible when it comes with a bug that was found by testing rather than a story of code that
worked first time.

## Verification, step by step

```bash
# Establish the baseline.
git clone https://github.com/KnifeLemon/Filee.git upstream
git -C upstream checkout 47e24cd3311b00b7fae75e36a8c3e98b0fe72b24

# 1. The module does not exist upstream.
ls upstream/src/Filee.Core/Policy                    # No such file or directory

# 2. None of its types appear upstream.
grep -rn "ConversionPolicy\|PolicyRule\|PolicyTest\|PolicyStore\|StarterPolicy" \
     upstream/src --include=*.cs | wc -l             # 0

# 3. The same holds for the tests.
ls upstream/tests/Filee.Core.Tests/Policy 2>&1       # No such file or directory

# 4. The module is present, namespaced and compiled here.
grep -rn "namespace Lantern.Core.Policy" src/
dotnet build src/Lantern.Core/Lantern.Core.csproj -c Release

# 5. The tests run and pass.
dotnet test tests/Lantern.Core.Tests/Lantern.Core.Tests.csproj \
    -c Release --filter "FullyQualifiedName~Policy"

# 6. Only the Policy folder differs in kind from upstream's Core project.
diff -r upstream/src/Filee.Core/Policy src/Lantern.Core/Policy 2>&1 | head -1
#    (upstream side absent, so every line here is new)
```

## Timestamps and history

The repository has three commits:

```
dac35df  Add conversion policy engine and its test suite
b6a44ed  Rename Filee to Lantern across the tree
d185564  Import Filee 2.4.0 baseline (upstream 47e24cd3)
```

One caveat, stated plainly because a history document that hides its own imperfections is worth
less than one that names them. The rename and the identity replacements were performed in the
working tree before the repository was initialised, so `d185564` contains the tree *after* those
edits, not a pristine vendoring of upstream. What that commit does establish precisely is the
baseline's contents without the policy engine: `git ls-tree d185564` contains no `Policy`
directory, and `dac35df` is the only commit that adds one, as a self-contained 1,929-line change.

The consequence for a reviewer is that `git diff` between `d185564` and upstream does **not** show
the renames one at a time. To see those, use `FORK-CHANGES.md` sections 1 and 2, which list them
exhaustively, or diff the trees directly against the pinned baseline commit. The policy engine, by
contrast, is fully isolated in the history and can be read as one atomic addition.

The stronger provenance evidence is therefore not the commit order but the diff against upstream at
a known SHA, and that is what the verification section above uses.

## Limits of this statement

This document does not claim the fork as a whole is original. It does not claim the policy engine
is novel in the field — rule engines with priorities and specificity tie-breaks exist, and this one
is not presented as an invention. It claims authorship of this specific implementation, its tests
and its documentation, and it gives the reader the commands to check whether that claim survives
contact with the baseline.

Anyone who wants to reuse the policy engine in their own project may do so under the MIT terms in
LICENSE. That is what the licence is for.
