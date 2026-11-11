// Tests for policy persistence (Lantern original module).
//
// The point of these tests is that the on-disk format is a contract. A user hand-edits policy.json;
// a later version of the app must read what they wrote. These tests pin the JSON vocabulary so a
// refactor of the domain model cannot silently change it.

using Lantern.Core.Policy;

namespace Lantern.Core.Tests.Policy;

public class PolicyStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lantern-policy-tests-" + Guid.NewGuid().ToString("N"));

    public PolicyStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not a test failure.
        }
    }

    private string PolicyPath => Path.Combine(_directory, PolicyStore.FileName);

    [Fact]
    public void A_missing_file_yields_the_starter_policy()
    {
        var store = new PolicyStore(_directory);

        Assert.NotEmpty(store.Current.Rules);
        Assert.Contains(store.Current.Rules, rule => rule.Id == "never-overwrite-source");
    }

    [Fact]
    public void Save_then_reload_round_trips_the_rule_structure()
    {
        var original = new ConversionPolicy
        {
            Name = "mine",
            Rules =
            [
                new PolicyRule
                {
                    Id = "nested",
                    Description = "a rule with a nested group",
                    Priority = 42,
                    When = new PolicyCondition(CombineMode.Any,
                    [
                        new PolicyTest("source.extension", PolicyOperator.Equals, PolicyValue.Text("png")),
                    ],
                    [
                        new PolicyCondition(CombineMode.All,
                        [
                            new PolicyTest("source.size_mb", PolicyOperator.GreaterThan, PolicyValue.Number(2)),
                            new PolicyTest("source.name", PolicyOperator.Matches, PolicyValue.Text(@"^shot_\d+")),
                        ], []),
                    ]),
                    Then = new PolicyAction
                    {
                        PresetId = "to-webp",
                        TargetFormat = "webp",
                        Quality = 77,
                        MaxEdgePixels = 2048,
                        NamePattern = "{name}-small",
                        AllowedEngines = ["magick"],
                        Sealed = true,
                    },
                },
            ],
        };

        Assert.True(new PolicyStore(_directory).Save(original));

        var reloaded = new PolicyStore(_directory).Current;
        var rule = Assert.Single(reloaded.Rules);

        Assert.Equal("mine", reloaded.Name);
        Assert.Equal("nested", rule.Id);
        Assert.Equal("a rule with a nested group", rule.Description);
        Assert.Equal(42, rule.Priority);
        Assert.Equal(CombineMode.Any, rule.When.Mode);
        Assert.Single(rule.When.Tests);
        Assert.Single(rule.When.Groups);
        Assert.Equal(CombineMode.All, rule.When.Groups[0].Mode);
        Assert.Equal(2, rule.When.Groups[0].Tests.Count);
        Assert.Equal("to-webp", rule.Then.PresetId);
        Assert.Equal(77, rule.Then.Quality);
        Assert.Equal(2048, rule.Then.MaxEdgePixels);
        Assert.Equal("{name}-small", rule.Then.NamePattern);
        Assert.Equal(["magick"], rule.Then.AllowedEngines);
        Assert.True(rule.Then.Sealed);
    }

    [Fact]
    public void A_reloaded_policy_still_evaluates_to_the_same_decision()
    {
        var facts = new PolicyFacts()
            .Set("source.name", "shot_001.png")
            .Set("source.extension", "png")
            .Set("source.size_mb", 5)
            .WithDerived();

        var original = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule
                {
                    Id = "big-png",
                    Priority = 10,
                    When = new PolicyCondition(CombineMode.All,
                    [
                        new PolicyTest("source.extension", PolicyOperator.Equals, PolicyValue.Text("png")),
                        new PolicyTest("source.size_mb", PolicyOperator.GreaterThan, PolicyValue.Number(2)),
                    ], []),
                    Then = new PolicyAction { Quality = 66, TargetFormat = "webp" },
                },
            ],
        };

        new PolicyStore(_directory).Save(original);

        var before = original.Evaluate(facts);
        var after = new PolicyStore(_directory).Current.Evaluate(facts);

        Assert.Equal(before.Result.TargetFormat, after.Result.TargetFormat);
        Assert.Equal(before.Result.Quality, after.Result.Quality);
        Assert.Equal(before.Fired.Count, after.Fired.Count);
    }

    [Fact]
    public void A_rule_without_an_id_is_skipped_but_the_others_still_load()
    {
        File.WriteAllText(PolicyPath, """
            {
              "schemaVersion": 1,
              "name": "handwritten",
              "rules": [
                { "description": "no id here", "then": { "quality": 50 } },
                { "id": "keeper", "priority": 1, "then": { "targetFormat": "png" } }
              ]
            }
            """);

        var policy = new PolicyStore(_directory).Current;

        Assert.Single(policy.Rules);
        Assert.Equal("keeper", policy.Rules[0].Id);
    }

    [Fact]
    public void An_unknown_operator_drops_only_that_test()
    {
        File.WriteAllText(PolicyPath, """
            {
              "rules": [
                {
                  "id": "partial",
                  "priority": 1,
                  "when": {
                    "mode": "all",
                    "tests": [
                      { "fact": "source.extension", "operator": "equals", "operand": "png" },
                      { "fact": "source.size_mb", "operator": "teleports", "operand": 3 }
                    ]
                  },
                  "then": { "targetFormat": "webp" }
                }
              ]
            }
            """);

        var policy = new PolicyStore(_directory).Current;
        var rule = Assert.Single(policy.Rules);

        Assert.Single(rule.When.Tests);
        Assert.Equal("source.extension", rule.When.Tests[0].Fact);
    }

    [Fact]
    public void Corrupt_json_falls_back_to_the_starter_policy_rather_than_throwing()
    {
        File.WriteAllText(PolicyPath, "{ this is not json ");

        var store = new PolicyStore(_directory);

        Assert.NotEmpty(store.Current.Rules);
    }

    [Fact]
    public void Operand_kinds_survive_the_round_trip()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule
                {
                    Id = "kinds",
                    When = new PolicyCondition(CombineMode.All,
                    [
                        new PolicyTest("a", PolicyOperator.GreaterThan, PolicyValue.Number(3.5)),
                        new PolicyTest("b", PolicyOperator.Equals, PolicyValue.Boolean(true)),
                        new PolicyTest("c", PolicyOperator.InSet, PolicyValue.List(["x", "y"])),
                        new PolicyTest("d", PolicyOperator.Equals, PolicyValue.Text("text")),
                    ], []),
                    Then = new PolicyAction { TargetFormat = "png" },
                },
            ],
        };

        new PolicyStore(_directory).Save(policy);
        var reloaded = new PolicyStore(_directory).Current.Rules[0].When.Tests;

        Assert.Equal(4, reloaded.Count);
        Assert.True(reloaded[0].Operand.IsNumber);
        Assert.Equal(3.5, reloaded[0].Operand.AsNumber());
        Assert.True(reloaded[1].Operand.IsBoolean);
        Assert.True(reloaded[1].Operand.AsBoolean());
        Assert.True(reloaded[2].Operand.IsList);
        Assert.Equal(["x", "y"], reloaded[2].Operand.AsList());
        Assert.True(reloaded[3].Operand.IsText);
        Assert.Equal("text", reloaded[3].Operand.AsText());
    }

    [Fact]
    public void Saving_creates_the_data_directory_when_it_is_absent()
    {
        var nested = Path.Combine(_directory, "a", "b", "c");

        var saved = new PolicyStore(nested).Save(new ConversionPolicy { Name = "deep" });

        Assert.True(saved);
        Assert.True(File.Exists(Path.Combine(nested, PolicyStore.FileName)));
    }
}
