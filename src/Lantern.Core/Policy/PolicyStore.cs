// Lantern original module: conversion policy engine — persistence and the built-in starter policy.
//
// Policies live in one JSON file beside settings.json. Reading is lenient on purpose: an unknown
// operator or a rule missing its id is skipped with a warning rather than failing the whole file,
// because a policy file is hand-edited and one typo must not silently disable every other rule.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantern.Core.Policy;

/// <summary>JSON shape of a policy file. Kept separate from the domain model so the on-disk format can stay stable.</summary>
internal sealed class PolicyFile
{
    public int SchemaVersion { get; set; } = 1;

    public string Name { get; set; } = "default";

    public List<RuleFile> Rules { get; set; } = [];
}

internal sealed class RuleFile
{
    public string? Id { get; set; }

    public string? Description { get; set; }

    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    public ConditionFile? When { get; set; }

    public ActionFile? Then { get; set; }
}

internal sealed class ConditionFile
{
    /// <summary>all | any | none | atleast</summary>
    public string? Mode { get; set; }

    public int Minimum { get; set; } = 1;

    public List<TestFile> Tests { get; set; } = [];

    public List<ConditionFile> Groups { get; set; } = [];
}

internal sealed class TestFile
{
    public string? Fact { get; set; }

    /// <summary>equals | notequals | gt | gte | lt | lte | contains | startswith | endswith | inset | matches | exists | notexists</summary>
    public string? Operator { get; set; }

    public JsonElement Operand { get; set; }

    public bool CaseSensitive { get; set; }
}

internal sealed class ActionFile
{
    public string? PresetId { get; set; }

    public string? TargetFormat { get; set; }

    public int? Quality { get; set; }

    public int? MaxEdgePixels { get; set; }

    public string? OutputFolder { get; set; }

    public string? NamePattern { get; set; }

    public List<string>? AllowedEngines { get; set; }

    public string? RejectReason { get; set; }

    public bool Sealed { get; set; }
}

/// <summary>
/// Loads, saves and seeds the user's conversion policy.
/// </summary>
public sealed class PolicyStore
{
    /// <summary>File name inside the data directory.</summary>
    public const string FileName = "policy.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Reading is case-insensitive and accepts camelCase, which is the spelling the write path emits
    /// and the spelling a person types by hand. Without this, a hand-written <c>{"rules": [...]}</c>
    /// deserialises to an empty rule set and the user's whole policy silently disappears.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private readonly ILogger _log;
    private readonly object _lock = new();
    private ConversionPolicy _current;

    public PolicyStore(string dataDirectory, ILogger? log = null)
    {
        _path = Path.Combine(dataDirectory, FileName);
        _log = log ?? NullLogger.Instance;
        _current = Load();
    }

    /// <summary>The policy in force.</summary>
    public ConversionPolicy Current
    {
        get
        {
            lock (_lock)
                return _current;
        }
    }

    /// <summary>Where the file lives, for a settings page to show and a text editor to open.</summary>
    public string Path_ => _path;

    /// <summary>Re-reads the file. Falls back to the policy already in memory when the file is unreadable.</summary>
    public ConversionPolicy Reload()
    {
        lock (_lock)
        {
            _current = Load();
            return _current;
        }
    }

    /// <summary>Replaces the policy and writes it out. Returns false when the write failed (the in-memory copy still changes).</summary>
    public bool Save(ConversionPolicy policy)
    {
        lock (_lock)
        {
            _current = policy;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                var json = JsonSerializer.Serialize(ToFile(policy), WriteOptions);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(ex, "Could not write {Path}", _path);
                return false;
            }
        }
    }

    private ConversionPolicy Load()
    {
        try
        {
            if (!File.Exists(_path))
                return StarterPolicy.Build();

            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize<PolicyFile>(json, ReadOptions);
            if (file is null)
                return StarterPolicy.Build();

            var policy = new ConversionPolicy { Name = file.Name };
            foreach (var entry in file.Rules)
            {
                var rule = ToRule(entry);
                if (rule is null)
                {
                    _log.LogWarning("policy.json: skipping rule {Id}", entry.Id ?? "(no id)");
                    continue;
                }
                policy.Rules.Add(rule);
            }
            return policy;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not read {Path}; using the starter policy", _path);
            return StarterPolicy.Build();
        }
    }

    private static PolicyRule? ToRule(RuleFile file)
    {
        if (string.IsNullOrWhiteSpace(file.Id))
            return null;

        return new PolicyRule
        {
            Id = file.Id!,
            Description = file.Description ?? "",
            Priority = file.Priority,
            Enabled = file.Enabled,
            When = ToCondition(file.When) ?? PolicyCondition.Always,
            Then = ToAction(file.Then),
        };
    }

    private static PolicyCondition? ToCondition(ConditionFile? file)
    {
        if (file is null)
            return null;

        var mode = (file.Mode ?? "all").Trim().ToLowerInvariant() switch
        {
            "any" => CombineMode.Any,
            "none" => CombineMode.None,
            "atleast" or "at-least" => CombineMode.AtLeast,
            _ => CombineMode.All,
        };

        var tests = new List<PolicyTest>();
        foreach (var test in file.Tests)
        {
            if (string.IsNullOrWhiteSpace(test.Fact) || ParseOperator(test.Operator) is not { } op)
                continue;
            tests.Add(new PolicyTest(test.Fact!, op, ToValue(test.Operand), test.CaseSensitive));
        }

        var groups = file.Groups
            .Select(ToCondition)
            .OfType<PolicyCondition>()
            .ToList();

        return new PolicyCondition(mode, tests, groups, Math.Max(1, file.Minimum));
    }

    private static PolicyOperator? ParseOperator(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "equals" or "eq" or "==" => PolicyOperator.Equals,
        "notequals" or "ne" or "!=" => PolicyOperator.NotEquals,
        "gt" or ">" or "greaterthan" => PolicyOperator.GreaterThan,
        "gte" or ">=" or "greaterorequal" => PolicyOperator.GreaterOrEqual,
        "lt" or "<" or "lessthan" => PolicyOperator.LessThan,
        "lte" or "<=" or "lessorequal" => PolicyOperator.LessOrEqual,
        "contains" => PolicyOperator.Contains,
        "startswith" => PolicyOperator.StartsWith,
        "endswith" => PolicyOperator.EndsWith,
        "inset" or "in" => PolicyOperator.InSet,
        "matches" or "regex" => PolicyOperator.Matches,
        "exists" => PolicyOperator.Exists,
        "notexists" or "missing" => PolicyOperator.NotExists,
        _ => null,
    };

    private static PolicyValue ToValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => PolicyValue.Text(element.GetString() ?? ""),
        JsonValueKind.Number => PolicyValue.Number(element.GetDouble()),
        JsonValueKind.True => PolicyValue.Boolean(true),
        JsonValueKind.False => PolicyValue.Boolean(false),
        JsonValueKind.Array => PolicyValue.List([.. element.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? ""
                : item.GetRawText())]),
        _ => PolicyValue.Null,
    };

    private static PolicyAction ToAction(ActionFile? file) => file is null ? new PolicyAction() : new PolicyAction
    {
        PresetId = file.PresetId,
        TargetFormat = file.TargetFormat,
        Quality = file.Quality,
        MaxEdgePixels = file.MaxEdgePixels,
        OutputFolder = file.OutputFolder,
        NamePattern = file.NamePattern,
        AllowedEngines = file.AllowedEngines ?? [],
        RejectReason = file.RejectReason,
        Sealed = file.Sealed,
    };

    private static PolicyFile ToFile(ConversionPolicy policy) => new()
    {
        Name = policy.Name,
        Rules = [.. policy.Rules.Select(rule => new RuleFile
        {
            Id = rule.Id,
            Description = rule.Description,
            Priority = rule.Priority,
            Enabled = rule.Enabled,
            When = FromCondition(rule.When),
            Then = new ActionFile
            {
                PresetId = rule.Then.PresetId,
                TargetFormat = rule.Then.TargetFormat,
                Quality = rule.Then.Quality,
                MaxEdgePixels = rule.Then.MaxEdgePixels,
                OutputFolder = rule.Then.OutputFolder,
                NamePattern = rule.Then.NamePattern,
                AllowedEngines = rule.Then.AllowedEngines.Count > 0 ? rule.Then.AllowedEngines : null,
                RejectReason = rule.Then.RejectReason,
                Sealed = rule.Then.Sealed,
            },
        })],
    };

    private static ConditionFile FromCondition(PolicyCondition condition) => new()
    {
        Mode = condition.Mode switch
        {
            CombineMode.Any => "any",
            CombineMode.None => "none",
            CombineMode.AtLeast => "atleast",
            _ => "all",
        },
        Minimum = condition.Minimum,
        Tests = [.. condition.Tests.Select(test => new TestFile
        {
            Fact = test.Fact,
            Operator = OperatorText(test.Operator),
            Operand = ToElement(test.Operand),
            CaseSensitive = test.CaseSensitive,
        })],
        Groups = [.. condition.Groups.Select(FromCondition)],
    };

    private static string OperatorText(PolicyOperator op) => op switch
    {
        PolicyOperator.Equals => "equals",
        PolicyOperator.NotEquals => "notequals",
        PolicyOperator.GreaterThan => "gt",
        PolicyOperator.GreaterOrEqual => "gte",
        PolicyOperator.LessThan => "lt",
        PolicyOperator.LessOrEqual => "lte",
        PolicyOperator.Contains => "contains",
        PolicyOperator.StartsWith => "startswith",
        PolicyOperator.EndsWith => "endswith",
        PolicyOperator.InSet => "inset",
        PolicyOperator.Matches => "matches",
        PolicyOperator.Exists => "exists",
        _ => "notexists",
    };

    private static JsonElement ToElement(PolicyValue value)
    {
        using var document = JsonDocument.Parse(value.IsNumber
            ? value.AsNumber().ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            : value.IsBoolean
                ? (value.AsBoolean() ? "true" : "false")
                : value.IsList
                    ? JsonSerializer.Serialize(value.AsList())
                    : JsonSerializer.Serialize(value.AsText()));
        return document.RootElement.Clone();
    }
}
