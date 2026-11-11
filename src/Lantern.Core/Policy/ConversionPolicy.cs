// Lantern original module: conversion policy engine — rules and evaluation.
//
// A policy is an ordered list of rules. Each rule says: when these facts hold, apply these settings,
// with this priority. The engine resolves conflicts by priority first, then by specificity (a rule
// with more tests beats a rule with fewer), then by declaration order. Specificity as a tie-break is
// what makes a policy readable: the general rule can sit at the top of the file and the exceptions
// below it, without the author having to hand-number every priority.

namespace Lantern.Core.Policy;

/// <summary>
/// What a matching rule contributes. Absent members leave the previous value alone, so rules layer
/// over one another instead of replacing wholesale.
/// </summary>
public sealed class PolicyAction
{
    /// <summary>Preset id to convert with, e.g. <c>to-webp</c>.</summary>
    public string? PresetId { get; set; }

    /// <summary>Target format id, used when no preset is named.</summary>
    public string? TargetFormat { get; set; }

    /// <summary>Encoder quality, 1–100.</summary>
    public int? Quality { get; set; }

    /// <summary>Longest edge in pixels; the other edge follows the aspect ratio.</summary>
    public int? MaxEdgePixels { get; set; }

    /// <summary>Output folder, absolute or relative to the source.</summary>
    public string? OutputFolder { get; set; }

    /// <summary>Output name pattern, e.g. <c>{name}-{preset}</c>.</summary>
    public string? NamePattern { get; set; }

    /// <summary>Engine ids allowed to run this file. Empty means "no restriction".</summary>
    public List<string> AllowedEngines { get; set; } = [];

    /// <summary>Stop the file from being converted, with this reason shown to the user.</summary>
    public string? RejectReason { get; set; }

    /// <summary>Marks the action as a veto: no later rule may override the settings it set.</summary>
    public bool Sealed { get; set; }

    /// <summary>Copies this action so a merge never mutates a rule stored in the catalog.</summary>
    public PolicyAction Clone() => new()
    {
        PresetId = PresetId,
        TargetFormat = TargetFormat,
        Quality = Quality,
        MaxEdgePixels = MaxEdgePixels,
        OutputFolder = OutputFolder,
        NamePattern = NamePattern,
        AllowedEngines = [.. AllowedEngines],
        RejectReason = RejectReason,
        Sealed = Sealed,
    };

    /// <summary>
    /// Layers <paramref name="next"/> over this action. Values the next action leaves null survive;
    /// a sealed action ignores everything.
    /// </summary>
    public void Absorb(PolicyAction next)
    {
        if (Sealed)
            return;

        PresetId = next.PresetId ?? PresetId;
        TargetFormat = next.TargetFormat ?? TargetFormat;
        Quality = next.Quality ?? Quality;
        MaxEdgePixels = next.MaxEdgePixels ?? MaxEdgePixels;
        OutputFolder = next.OutputFolder ?? OutputFolder;
        NamePattern = next.NamePattern ?? NamePattern;
        RejectReason = next.RejectReason ?? RejectReason;

        if (next.AllowedEngines.Count > 0)
        {
            AllowedEngines = AllowedEngines.Count == 0
                ? [.. next.AllowedEngines]
                : [.. AllowedEngines.Intersect(next.AllowedEngines, StringComparer.OrdinalIgnoreCase)];
        }

        Sealed = next.Sealed;
    }
}

/// <summary>
/// One rule: a condition, an action, and the metadata needed to explain a decision later.
/// </summary>
public sealed class PolicyRule
{
    public string Id { get; set; } = "";

    /// <summary>Shown in the audit record and in Settings.</summary>
    public string Description { get; set; } = "";

    /// <summary>When the condition holds.</summary>
    public PolicyCondition When { get; set; } = PolicyCondition.Always;

    /// <summary>What to do.</summary>
    public PolicyAction Then { get; set; } = new();

    /// <summary>Higher wins. Ties break on specificity, then on declaration order.</summary>
    public int Priority { get; set; }

    /// <summary>Turned off rules stay in the file but take no part.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many tests the rule carries, counting nested ones. Used as the specificity tie-break, so
    /// a rule that names an extension and a size outranks one that only names the extension.
    /// </summary>
    public int Specificity => CountTests(When);

    private static int CountTests(PolicyCondition condition) =>
        condition.Tests.Count + condition.Groups.Sum(CountTests);
}

/// <summary>Facts about one file, as the caller declares them before evaluation.</summary>
public sealed class PolicyFacts
{
    private readonly Dictionary<string, PolicyValue> _values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, PolicyValue> Values => _values;

    public PolicyFacts Set(string name, PolicyValue value)
    {
        _values[name] = value;
        return this;
    }

    public PolicyFacts Set(string name, string value) => Set(name, PolicyValue.Text(value));

    public PolicyFacts Set(string name, double value) => Set(name, PolicyValue.Number(value));

    public PolicyFacts Set(string name, bool value) => Set(name, PolicyValue.Boolean(value));

    /// <summary>
    /// Adds the derived facts a policy author expects to exist without having to declare them:
    /// <c>source.size_mb</c> from <c>source.size_bytes</c>, and <c>source.extension</c> lowercased
    /// from <c>source.name</c>.
    /// </summary>
    public PolicyFacts WithDerived()
    {
        if (_values.TryGetValue("source.size_bytes", out var bytes) && bytes.IsNumber
            && !_values.ContainsKey("source.size_mb"))
        {
            _values["source.size_mb"] = PolicyValue.Number(Math.Round(bytes.AsNumber() / (1024 * 1024), 3));
        }

        if (_values.TryGetValue("source.name", out var name) && name.IsText
            && !_values.ContainsKey("source.extension"))
        {
            var text = name.AsText();
            var dot = text.LastIndexOf('.');
            _values["source.extension"] = PolicyValue.Text(dot >= 0 && dot < text.Length - 1 ? text[(dot + 1)..].ToLowerInvariant() : "");
        }

        return this;
    }
}

/// <summary>
/// One rule's contribution to the final decision, kept so the decision can be explained.
/// </summary>
/// <param name="RuleId">Which rule.</param>
/// <param name="Priority">Its priority.</param>
/// <param name="Specificity">How many tests it carried.</param>
/// <param name="Trace">The evaluation trace of its condition.</param>
public sealed record RuleHit(string RuleId, int Priority, int Specificity, IReadOnlyList<string> Trace);

/// <summary>
/// The outcome of evaluating a policy for one file: the merged action, which rules fired, and a
/// plain-language account of the order they were applied in.
/// </summary>
public sealed class PolicyDecision
{
    public string Subject { get; init; } = "";

    public PolicyAction Result { get; init; } = new();

    public IReadOnlyList<RuleHit> Fired { get; init; } = [];

    /// <summary>True when a rule rejected the file: <see cref="PolicyAction.RejectReason"/> says why.</summary>
    public bool Rejected => !string.IsNullOrEmpty(Result.RejectReason);

    /// <summary>The audit record, one line per step, suitable for a log file or a UI detail pane.</summary>
    public string Explain() => Explain(Environment.NewLine);

    public string Explain(string newline)
    {
        var lines = new List<string> { $"policy {Subject}: {Fired.Count} rule(s) fired" };
        foreach (var hit in Fired)
        {
            lines.Add($"  [{hit.Priority}/{hit.Specificity}] {hit.RuleId}");
            lines.AddRange(hit.Trace.Select(line => "    " + line));
        }

        lines.Add($"  result: preset={Result.PresetId ?? "-"} format={Result.TargetFormat ?? "-"} "
            + $"quality={Result.Quality?.ToString() ?? "-"} maxEdge={Result.MaxEdgePixels?.ToString() ?? "-"}"
            + (Rejected ? $" REJECTED ({Result.RejectReason})" : ""));
        return string.Join(newline, lines);
    }
}

/// <summary>
/// The policy: an ordered rule set plus the evaluation order rules obey.
/// </summary>
public sealed class ConversionPolicy
{
    public string Name { get; set; } = "default";

    public List<PolicyRule> Rules { get; set; } = [];

    /// <summary>
    /// Evaluates every enabled rule against <paramref name="facts"/> and merges the winners.
    /// </summary>
    /// <remarks>
    /// Order matters and is the whole point of the design. Rules are applied lowest priority first,
    /// so a higher-priority rule overwrites a lower one. Within one priority, the least specific rule
    /// applies first, so a specific exception overwrites the general rule it was written to except.
    /// Declaration order settles what remains, later winning.
    /// </remarks>
    public PolicyDecision Evaluate(PolicyFacts facts, string subject = "")
    {
        var evaluated = facts.WithDerived().Values;
        var fired = new List<RuleHit>();
        var ordered = new List<(PolicyRule Rule, RuleHit Hit)>();

        for (var index = 0; index < Rules.Count; index++)
        {
            var rule = Rules[index];
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Id))
                continue;

            var trace = new List<string>();
            if (!rule.When.Evaluate(evaluated, trace))
                continue;

            var hit = new RuleHit(rule.Id, rule.Priority, rule.Specificity, trace);
            fired.Add(hit);
            ordered.Add((rule, hit));
        }

        // Lowest priority first, then least specific, then earliest declared.
        ordered.Sort(static (a, b) =>
        {
            var byPriority = a.Rule.Priority.CompareTo(b.Rule.Priority);
            if (byPriority != 0)
                return byPriority;
            var bySpecificity = a.Rule.Specificity.CompareTo(b.Rule.Specificity);
            if (bySpecificity != 0)
                return bySpecificity;
            return string.CompareOrdinal(a.Rule.Id, b.Rule.Id);
        });

        var result = new PolicyAction();
        foreach (var (rule, _) in ordered)
            result.Absorb(rule.Then);

        return new PolicyDecision
        {
            Subject = string.IsNullOrEmpty(subject) ? "(unnamed)" : subject,
            Result = result,
            Fired = fired,
        };
    }
}
