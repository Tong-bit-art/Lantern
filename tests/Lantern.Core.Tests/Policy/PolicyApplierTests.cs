// Tests for the bridge between the policy engine and the conversion path.
//
// These are the tests that stop the engine from drifting back into being a library nobody calls.
// They exercise the translation in both directions: a job and a preset become a fact table, and a
// decision becomes a preset again.

using Lantern.Core.Policy;
using Lantern.Core.Presets;

namespace Lantern.Core.Tests.Policy;

public class PolicyApplierTests
{
    private static Preset Preset(string id = "to-webp", string target = "webp", int quality = 90)
    {
        var preset = new Preset { Id = id, Name = id, TargetFormat = target };
        preset.Image.Quality = quality;
        return preset;
    }

    private static Func<string, Preset?> NoPresets => _ => null;

    [Fact]
    public void An_empty_policy_returns_the_preset_untouched()
    {
        var original = Preset();

        var result = PolicyApplier.Apply(new ConversionPolicy(), ["a.png"], original, NoPresets);

        Assert.Same(original, result.Preset);
        Assert.False(result.Changed);
        Assert.Null(result.RefusedBecause);
    }

    [Fact]
    public void A_rule_can_lower_the_quality_without_touching_the_target_format()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule
                {
                    Id = "cap-quality",
                    Then = new PolicyAction { Quality = 60 },
                },
            ],
        };

        var result = PolicyApplier.Apply(policy, ["a.png"], Preset(target: "webp", quality: 90), NoPresets);

        Assert.True(result.Changed);
        Assert.Equal(60, result.Preset.Image.Quality);
        // The field the rule did not mention survives.
        Assert.Equal("webp", result.Preset.TargetFormat);
    }

    [Fact]
    public void The_input_preset_is_never_mutated()
    {
        var original = Preset(quality: 90);
        var policy = new ConversionPolicy
        {
            Rules = [new PolicyRule { Id = "cap", Then = new PolicyAction { Quality = 30 } }],
        };

        PolicyApplier.Apply(policy, ["a.png"], original, NoPresets);

        Assert.Equal(90, original.Image.Quality);
    }

    [Fact]
    public void A_rule_can_set_the_longest_edge()
    {
        var policy = new ConversionPolicy
        {
            Rules = [new PolicyRule { Id = "cap-edge", Then = new PolicyAction { MaxEdgePixels = 2048 } }],
        };

        var result = PolicyApplier.Apply(policy, ["a.png"], Preset(), NoPresets);

        Assert.True(result.Changed);
        Assert.Equal(ResizeMode.LongEdge, result.Preset.Image.Resize);
        Assert.Equal(2048, result.Preset.Image.LongEdge);
    }

    [Fact]
    public void A_refusing_rule_reports_the_reason_and_leaves_the_preset_alone()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule
                {
                    Id = "no-big-batches",
                    When = new PolicyCondition(CombineMode.All,
                        [new PolicyTest(PolicyApplier.BatchCountFact, PolicyOperator.GreaterThan, PolicyValue.Number(3))], []),
                    Then = new PolicyAction { RejectReason = "policy.refused.too_many" },
                },
            ],
        };

        var refused = PolicyApplier.Apply(policy, ["a.png", "b.png", "c.png", "d.png"], Preset(), NoPresets);

        Assert.Equal("policy.refused.too_many", refused.RefusedBecause);
        Assert.False(refused.Changed);
    }

    [Fact]
    public void A_preset_named_by_a_rule_replaces_the_one_the_user_chose()
    {
        var merge = Preset(id: "merge-pdf", target: "pdf");
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule
                {
                    Id = "bulk",
                    When = new PolicyCondition(CombineMode.All,
                        [new PolicyTest(PolicyApplier.BatchCountFact, PolicyOperator.GreaterOrEqual, PolicyValue.Number(3))], []),
                    Then = new PolicyAction { PresetId = "merge-pdf" },
                },
            ],
        };

        var result = PolicyApplier.Apply(
            policy,
            ["a.docx", "b.docx", "c.docx"],
            Preset(),
            id => id == "merge-pdf" ? merge : null);

        Assert.True(result.Changed);
        Assert.Equal("merge-pdf", result.Preset.Id);
        Assert.Equal("pdf", result.Preset.TargetFormat);
    }

    [Fact]
    public void Facts_describe_the_drop_the_format_and_the_request()
    {
        var facts = PolicyApplier.BuildFacts(["photo.PNG", "second.png"], Preset(id: "mine", target: "jpg"));

        Assert.Equal(2, facts.Values[PolicyApplier.BatchCountFact].AsNumber());
        Assert.Equal("mine", facts.Values["request.preset"].AsText());
        Assert.Equal("jpg", facts.Values["request.target"].AsText());
        Assert.Equal("photo.PNG", facts.Values["source.name"].AsText());
        Assert.Equal("png", facts.Values["source.extension"].AsText());
        Assert.Equal("image", facts.Values["source.category"].AsText());
    }

    [Fact]
    public void Size_is_only_a_fact_for_a_single_file()
    {
        // The aggregate of a batch would be a different fact; inventing one here would make a rule
        // behave differently on a drop of one than it does on a drop of many.
        var many = PolicyApplier.BuildFacts(["a.png", "b.png"], Preset());
        Assert.False(many.Values.ContainsKey("source.size_bytes"));
    }

    [Fact]
    public void A_rule_can_match_the_batch_size_and_the_format_category()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule
                {
                    Id = "bulk-images",
                    When = new PolicyCondition(CombineMode.All,
                    [
                        new PolicyTest(PolicyApplier.BatchCountFact, PolicyOperator.GreaterOrEqual, PolicyValue.Number(10)),
                        new PolicyTest("source.category", PolicyOperator.Equals, PolicyValue.Text("image")),
                    ], []),
                    Then = new PolicyAction { Quality = 75 },
                },
            ],
        };

        var nine = PolicyApplier.Apply(policy, [.. Enumerable.Repeat("x.png", 9)], Preset(), NoPresets);
        Assert.False(nine.Changed);

        var ten = PolicyApplier.Apply(policy, [.. Enumerable.Repeat("x.png", 10)], Preset(), NoPresets);
        Assert.True(ten.Changed);
        Assert.Equal(75, ten.Preset.Image.Quality);
    }

    [Fact]
    public void The_explanation_names_the_rule_that_fired()
    {
        var policy = new ConversionPolicy
        {
            Rules = [new PolicyRule { Id = "cap", Then = new PolicyAction { Quality = 50 } }],
        };

        var result = PolicyApplier.Apply(policy, ["a.png"], Preset(), NoPresets);
        var text = result.Explain();

        Assert.Contains("cap", text);
        Assert.Contains("quality=50", text);
    }
}
