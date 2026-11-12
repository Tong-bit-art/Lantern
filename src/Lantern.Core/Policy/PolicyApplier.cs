// Lantern original module: the bridge between the policy engine and the conversion path.
//
// The engine itself (PolicyLanguage, ConversionPolicy, PolicyStore) is pure: facts in, decision out.
// This is the piece that turns a real job into a fact table, and turns the decision back into a
// preset the job queue understands. Keeping the translation here, rather than inside either side,
// is what lets the engine stay testable without a file system and lets ConversionService stay
// unaware of rules.
//
// Scope, stated plainly because it matters: a ConversionJob carries one preset for the whole batch,
// so a policy decision applies to the batch as a whole. Per-file dispatch would mean changing the
// job model to hold a preset per file — a much larger change, tracked in ROADMAP.md rather than
// pretended here. What this buys today is the batch-level case, which is the one that actually
// needs rules: "this drop is a stack of paperwork, merge it" is a statement about the batch.

using Lantern.Core.Formats;
using Lantern.Core.Presets;

namespace Lantern.Core.Policy;

/// <summary>What the applier did, for the log and for the job's own record.</summary>
public sealed class PolicyApplication
{
    /// <summary>The preset to run with. The same instance as the input when no rule fired.</summary>
    public required Preset Preset { get; init; }

    /// <summary>The decision behind it.</summary>
    public required PolicyDecision Decision { get; init; }

    /// <summary>True when the decision changed the preset.</summary>
    public bool Changed { get; init; }

    /// <summary>
    /// Set when a rule refused the job. The caller must not start it; the string is a localization
    /// key when the rule used one, otherwise the rule's own wording.
    /// </summary>
    public string? RefusedBecause { get; init; }

    /// <summary>The batches's audit record, ready for a log line or a details pane.</summary>
    public string Explain() => Decision.Explain();
}

/// <summary>
/// Builds the fact table for a job and applies the resulting decision to its preset.
/// </summary>
public static class PolicyApplier
{
    /// <summary>Fact name for the number of files in the drop. Named here so the UI can suggest it.</summary>
    public const string BatchCountFact = "request.batch_count";

    /// <summary>
    /// Applies <paramref name="policy"/> to a job of <paramref name="files"/> that the caller intended
    /// to run with <paramref name="preset"/>.
    /// </summary>
    /// <param name="policy">The rules to evaluate.</param>
    /// <param name="files">The sources, as the queue will see them.</param>
    /// <param name="preset">The preset the user chose; not modified.</param>
    /// <param name="lookupPreset">
    /// Resolves a preset id to a preset. Passed in rather than reached for, so this stays pure and a
    /// test can hand it a stub.
    /// </param>
    /// <param name="targetOverride">Set when the caller is converting to a format the preset does not name.</param>
    public static PolicyApplication Apply(
        ConversionPolicy policy,
        IReadOnlyList<string> files,
        Preset preset,
        Func<string, Preset?> lookupPreset,
        string? targetOverride = null)
    {
        var facts = BuildFacts(files, preset, targetOverride);
        var subject = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} files";
        var decision = policy.Evaluate(facts, subject);

        if (!string.IsNullOrEmpty(decision.Result.RejectReason))
        {
            return new PolicyApplication
            {
                Preset = preset,
                Decision = decision,
                Changed = false,
                RefusedBecause = decision.Result.RejectReason,
            };
        }

        var (result, changed) = Adapt(preset, decision.Result, lookupPreset);
        return new PolicyApplication { Preset = result, Decision = decision, Changed = changed };
    }

    /// <summary>
    /// The facts a rule can test. Only what can be known before the work starts: anything that needs
    /// the file opened belongs to the engine, not here.
    /// </summary>
    public static PolicyFacts BuildFacts(IReadOnlyList<string> files, Preset preset, string? targetOverride = null)
    {
        var facts = new PolicyFacts()
            .Set(BatchCountFact, files.Count)
            .Set("request.preset", preset.Id)
            .Set("request.target", targetOverride ?? preset.TargetFormat);

        if (files.Count > 0)
        {
            var first = files[0];
            var extension = Path.GetExtension(first).TrimStart('.').ToLowerInvariant();
            facts.Set("source.name", Path.GetFileName(first))
                 .Set("source.extension", extension)
                 .Set("source.folder", Path.GetDirectoryName(first) ?? "");

            var format = FormatRegistry.FindByExtension(extension);
            if (format is not null)
            {
                facts.Set("source.format", format.Id)
                     .Set("source.category", format.Category.ToString().ToLowerInvariant());
            }

            // Only meaningful for a single file: for a batch, the aggregate would be a different fact
            // name and guessing it here would make rules behave differently on drops of one.
            if (files.Count == 1 && File.Exists(first))
            {
                try
                {
                    facts.Set("source.size_bytes", new FileInfo(first).Length);
                }
                catch (IOException)
                {
                    // A file that vanished between the drop and here simply has no size fact.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return facts;
    }

    /// <summary>
    /// Folds the decision onto the preset. Every field the rule left unset survives, so a rule that
    /// only caps quality does not silently reset the target format.
    /// </summary>
    private static (Preset Preset, bool Changed) Adapt(Preset preset, PolicyAction action, Func<string, Preset?> lookupPreset)
    {
        // A named preset replaces the one the user chose outright, because that is what naming it means.
        var basis = preset;
        var replaced = false;
        if (!string.IsNullOrWhiteSpace(action.PresetId)
            && lookupPreset(action.PresetId!) is { } named
            && named.Id != preset.Id)
        {
            basis = named;
            replaced = true;
        }

        var targetChanged = !string.IsNullOrWhiteSpace(action.TargetFormat)
            && !string.Equals(action.TargetFormat, basis.TargetFormat, StringComparison.OrdinalIgnoreCase);
        var qualityChanged = action.Quality is { } q && q != basis.Image.Quality;
        var edgeChanged = action.MaxEdgePixels is { } e
            && !(basis.Image.Resize == ResizeMode.LongEdge && basis.Image.LongEdge == e);

        if (!replaced && !targetChanged && !qualityChanged && !edgeChanged)
            return (preset, false);

        var result = basis.Clone();
        if (!string.IsNullOrWhiteSpace(action.TargetFormat))
            result.TargetFormat = action.TargetFormat!;
        if (action.Quality is { } quality)
            result.Image.Quality = Math.Clamp(quality, 1, 100);
        if (action.MaxEdgePixels is { } maxEdge)
        {
            // LongEdge is the mode that bounds the longest side without upscaling, which is what a
            // rule saying "cap this at 4096" means.
            result.Image.Resize = ResizeMode.LongEdge;
            result.Image.LongEdge = Math.Max(1, maxEdge);
        }

        return (result, true);
    }
}
