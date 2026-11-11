// Lantern original module: conversion policy engine — domain model.
//
// This file is part of the Lantern fork's own work, not present upstream. It defines a declarative
// rule language for deciding *how* a file should be converted: which preset applies, which output
// name it gets, which engine may handle it, and what happens when the rules disagree.
//
// The design goal is auditability. Every decision the engine makes is recorded as a Decision
// carrying the rule that produced it, the facts it matched, and the tie-break that settled it.
// A batch conversion can therefore be explained after the fact: "this file became webp@82 because
// rule 'photo-shrink' matched size>4MB and quality was capped by rule 'upload-cap'".

namespace Lantern.Core.Policy;

/// <summary>
/// Where a policy fact comes from. Facts are declared by the caller before evaluation; the engine
/// never inspects the file system itself, which keeps it pure and testable.
/// </summary>
public enum FactSource
{
    /// <summary>Supplied by the caller from the source file (size, extension, dimensions).</summary>
    Source,

    /// <summary>Supplied by the caller from the destination context (folder, drive, free space).</summary>
    Destination,

    /// <summary>Supplied by the user's settings or the invoking command.</summary>
    Request,

    /// <summary>Derived by the engine from other facts during evaluation.</summary>
    Derived,
}

/// <summary>
/// Comparison operators a condition can use. The set is deliberately small: a policy language that
/// grows operators for their own sake stops being readable, and an unreadable rule is an unaudited rule.
/// </summary>
public enum PolicyOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Contains,
    StartsWith,
    EndsWith,
    InSet,
    Matches,
    Exists,
    NotExists,
}

/// <summary>
/// One typed value in the policy language. A wrapper rather than <see cref="object"/> so that a
/// comparison between incompatible types fails loudly at evaluation time instead of silently
/// coercing, which is the usual source of "why did my rule not fire" in rule engines.
/// </summary>
public readonly record struct PolicyValue
{
    private enum Kind : byte
    {
        Null,
        Text,
        Number,
        Boolean,
        List,
    }

    private readonly object? _boxed;
    private readonly Kind _kind;

    private PolicyValue(Kind kind, object? boxed)
    {
        _kind = kind;
        _boxed = boxed;
    }

    /// <summary>An absent value. Never equal to anything, including another absent value, except by <see cref="IsNull"/>.</summary>
    public static PolicyValue Null => default;

    public static PolicyValue Text(string value) => new(Kind.Text, value);

    public static PolicyValue Number(double value) => new(Kind.Number, value);

    public static PolicyValue Boolean(bool value) => new(Kind.Boolean, value);

    public static PolicyValue List(IReadOnlyList<string> values) => new(Kind.List, values);

    public bool IsNull => _kind == Kind.Null;

    public bool IsNumber => _kind == Kind.Number;

    public bool IsText => _kind == Kind.Text;

    public bool IsBoolean => _kind == Kind.Boolean;

    public bool IsList => _kind == Kind.List;

    /// <summary>The text of a text value; empty for anything else.</summary>
    public string AsText() => _boxed as string ?? "";

    /// <summary>The number of a numeric value; 0 for anything else.</summary>
    public double AsNumber() => _boxed is double number ? number : 0;

    /// <summary>The flag of a boolean value; false for anything else.</summary>
    public bool AsBoolean() => _boxed is true;

    /// <summary>The items of a list value; empty for anything else.</summary>
    public IReadOnlyList<string> AsList() => _boxed as IReadOnlyList<string> ?? [];

    /// <summary>
    /// Builds a value from whatever the caller handed in, mapping the shapes a JSON policy file can
    /// express onto the five kinds above. Anything unrecognised becomes <see cref="Null"/>.
    /// </summary>
    public static PolicyValue From(object? raw) => raw switch
    {
        null => Null,
        string text => Text(text),
        bool flag => Boolean(flag),
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
            => Number(System.Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture)),
        IEnumerable<string> items => List([.. items]),
        _ => Null,
    };

    /// <summary>A stable text form, used in audit records and in the settings UI.</summary>
    public override string ToString() => _kind switch
    {
        Kind.Null => "(none)",
        Kind.Number => AsNumber().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        Kind.Boolean => AsBoolean() ? "true" : "false",
        Kind.List => "[" + string.Join(", ", AsList()) + "]",
        _ => AsText(),
    };
}

/// <summary>
/// One test inside a condition. The left side names a fact; the right side is a literal the caller
/// writes in the policy file.
/// </summary>
/// <param name="Fact">Fact name, e.g. <c>source.size_mb</c> or <c>request.preset</c>.</param>
/// <param name="Operator">How the two sides are compared.</param>
/// <param name="Operand">The literal. Ignored by <see cref="PolicyOperator.Exists"/> and <see cref="PolicyOperator.NotExists"/>.</param>
/// <param name="CaseSensitive">Text comparison sensitivity. Defaults to insensitive, which matches how file extensions behave.</param>
public sealed record PolicyTest(
    string Fact,
    PolicyOperator Operator,
    PolicyValue Operand,
    bool CaseSensitive = false)
{
    /// <summary>
    /// Evaluates this test against a fact table. A missing fact is <see cref="PolicyValue.Null"/>, and
    /// <see cref="PolicyOperator.Exists"/> is the only operator that can succeed on it.
    /// </summary>
    public bool Evaluate(IReadOnlyDictionary<string, PolicyValue> facts, out string explanation)
    {
        var found = facts.TryGetValue(Fact, out var actual);
        var left = found ? actual : PolicyValue.Null;
        explanation = "";

        if (Operator is PolicyOperator.Exists)
        {
            explanation = found ? $"{Fact} is present" : $"{Fact} is absent";
            return found;
        }
        if (Operator is PolicyOperator.NotExists)
        {
            explanation = found ? $"{Fact} is present" : $"{Fact} is absent";
            return !found;
        }

        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        switch (Operator)
        {
            case PolicyOperator.Equals:
            case PolicyOperator.NotEquals:
            {
                var equal = ValuesEqual(left, Operand, comparison);
                explanation = $"{Fact}={left} {(equal ? "=" : "!=")} {Operand}";
                return Operator is PolicyOperator.Equals ? equal : !equal;
            }

            case PolicyOperator.GreaterThan:
            case PolicyOperator.GreaterOrEqual:
            case PolicyOperator.LessThan:
            case PolicyOperator.LessOrEqual:
            {
                if (!left.IsNumber || !Operand.IsNumber)
                {
                    explanation = $"{Fact} ({left}) and {Operand} are not both numbers";
                    return false;
                }
                var a = left.AsNumber();
                var b = Operand.AsNumber();
                var result = Operator switch
                {
                    PolicyOperator.GreaterThan => a > b,
                    PolicyOperator.GreaterOrEqual => a >= b,
                    PolicyOperator.LessThan => a < b,
                    _ => a <= b,
                };
                explanation = $"{Fact}={a.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} "
                    + $"{OperatorSymbol()} {b.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
                return result;
            }

            case PolicyOperator.Contains:
            case PolicyOperator.StartsWith:
            case PolicyOperator.EndsWith:
            {
                var haystack = left.IsList
                    ? string.Join("\u001f", left.AsList())
                    : left.AsText();
                var needle = Operand.IsList ? string.Join("\u001f", Operand.AsList()) : Operand.AsText();
                var result = Operator switch
                {
                    PolicyOperator.Contains => haystack.Contains(needle, comparison),
                    PolicyOperator.StartsWith => haystack.StartsWith(needle, comparison),
                    _ => haystack.EndsWith(needle, comparison),
                };
                explanation = $"{Fact}=\"{haystack}\" {Operator} \"{needle}\"";
                return result;
            }

            case PolicyOperator.InSet:
            {
                // Either side may be the set: a fact holding a list is tested against a literal, or a
                // scalar fact is tested against a literal list.
                var candidates = left.IsList ? left.AsList() : [left.AsText()];
                var allowed = Operand.IsList ? Operand.AsList() : [Operand.AsText()];
                var hit = candidates.Any(candidate => allowed.Any(a => string.Equals(a, candidate, comparison)));
                explanation = $"{Fact}={left} in {Operand}";
                return hit;
            }

            case PolicyOperator.Matches:
            {
                if (Operand.AsText() is not { Length: > 0 } pattern)
                {
                    explanation = "empty pattern";
                    return false;
                }
                try
                {
                    var regex = new System.Text.RegularExpressions.Regex(
                        pattern,
                        (CaseSensitive ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(100));
                    var result = regex.IsMatch(left.AsText());
                    explanation = $"\"{left.AsText()}\" {Operator} /{pattern}/";
                    return result;
                }
                catch (ArgumentException)
                {
                    // A malformed pattern is a policy authoring error. Failing the test instead of
                    // throwing keeps one bad rule from halting an entire batch.
                    explanation = $"invalid pattern /{pattern}/";
                    return false;
                }
            }

            default:
                explanation = $"unsupported operator {Operator}";
                return false;
        }
    }

    private string OperatorSymbol() => Operator switch
    {
        PolicyOperator.GreaterThan => ">",
        PolicyOperator.GreaterOrEqual => ">=",
        PolicyOperator.LessThan => "<",
        _ => "<=",
    };

    private static bool ValuesEqual(PolicyValue a, PolicyValue b, StringComparison comparison)
    {
        if (a.IsNull || b.IsNull)
            return a.IsNull && b.IsNull;

        if (a.IsNumber && b.IsNumber)
            return a.AsNumber().Equals(b.AsNumber());
        if (a.IsBoolean && b.IsBoolean)
            return a.AsBoolean() == b.AsBoolean();

        if (a.IsList && b.IsList)
        {
            var left = a.AsList();
            var right = b.AsList();
            return left.Count == right.Count
                && left.Zip(right).All(pair => string.Equals(pair.First, pair.Second, comparison));
        }

        // Mixed shapes compare on their text form, which is what a person writing "x equals jpg"
        // expects even when one side arrived as a list of one.
        var leftText = a.IsList ? string.Join(",", a.AsList()) : a.ToString();
        var rightText = b.IsList ? string.Join(",", b.AsList()) : b.ToString();
        return string.Equals(leftText, rightText, comparison);
    }
}

/// <summary>How the tests of a condition combine.</summary>
public enum CombineMode
{
    All,
    Any,
    None,
    AtLeast,
}

/// <summary>
/// A group of tests combined into one boolean. Groups nest, which is the only structure a policy
/// file needs in practice and the easiest one to explain in an audit log.
/// </summary>
/// <param name="Mode">How <paramref name="Tests"/> and <paramref name="Groups"/> combine.</param>
/// <param name="Tests">Leaf tests.</param>
/// <param name="Groups">Nested conditions.</param>
/// <param name="Minimum">For <see cref="CombineMode.AtLeast"/>: how many children must pass.</param>
public sealed record PolicyCondition(
    CombineMode Mode,
    IReadOnlyList<PolicyTest> Tests,
    IReadOnlyList<PolicyCondition> Groups,
    int Minimum = 1)
{
    /// <summary>An empty condition is true in <see cref="CombineMode.All"/> mode: a rule with no test applies to everything.</summary>
    public static PolicyCondition Always => new(CombineMode.All, [], []);

    /// <summary>Evaluates the whole group, collecting a human-readable trace of why it passed or failed.</summary>
    public bool Evaluate(IReadOnlyDictionary<string, PolicyValue> facts, List<string> trace, int depth = 0)
    {
        // Guards against a policy file that nests itself deeply enough to threaten the stack.
        if (depth > 32)
        {
            trace.Add(new string(' ', depth * 2) + "(nesting too deep, treated as false)");
            return false;
        }

        var results = new List<bool>();
        var lines = new List<string>();

        foreach (var test in Tests)
        {
            var passed = test.Evaluate(facts, out var explanation);
            results.Add(passed);
            lines.Add($"{new string(' ', (depth + 1) * 2)}{(passed ? "✓" : "✗")} {explanation}");
        }

        foreach (var group in Groups)
        {
            var nested = new List<string>();
            var passed = group.Evaluate(facts, nested, depth + 1);
            results.Add(passed);
            lines.Add($"{new string(' ', (depth + 1) * 2)}{(passed ? "✓" : "✗")} group ({group.Mode})");
            lines.AddRange(nested);
        }

        var outcome = Mode switch
        {
            CombineMode.All => results.All(r => r),
            CombineMode.Any => results.Any(r => r),
            CombineMode.None => results.All(r => !r),
            CombineMode.AtLeast => results.Count(r => r) >= Math.Max(1, Minimum),
            _ => false,
        };

        trace.Add($"{new string(' ', depth * 2)}{(outcome ? "PASS" : "FAIL")} {Mode} over {results.Count} child(ren)");
        trace.AddRange(lines);
        return outcome;
    }
}
