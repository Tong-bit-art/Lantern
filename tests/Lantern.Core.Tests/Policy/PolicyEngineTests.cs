// Tests for the Lantern policy engine (Lantern original module).
//
// These tests exist for two reasons. They check the merge semantics, which are the part of the engine
// most likely to drift silently. And they serve as executable documentation of the intended behaviour:
// every assertion below is a statement about how a policy author expects rules to compose.

using Lantern.Core.Policy;

namespace Lantern.Core.Tests.Policy;

public class PolicyEngineTests
{
    private static PolicyFacts ImageFacts(double sizeMb = 1, string extension = "heic") => new PolicyFacts()
        .Set("source.name", $"photo.{extension}")
        .Set("source.extension", extension)
        .Set("source.category", "image")
        .Set("source.size_mb", sizeMb);

    [Fact]
    public void Empty_policy_leaves_every_setting_unset()
    {
        var decision = new ConversionPolicy().Evaluate(ImageFacts());

        Assert.Empty(decision.Fired);
        Assert.Null(decision.Result.PresetId);
        Assert.False(decision.Rejected);
    }

    [Fact]
    public void A_rule_with_no_condition_matches_everything()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule { Id = "always", Then = new PolicyAction { TargetFormat = "png" } },
            ],
        };

        var decision = policy.Evaluate(ImageFacts());

        Assert.Single(decision.Fired);
        Assert.Equal("png", decision.Result.TargetFormat);
    }

    [Fact]
    public void Higher_priority_overwrites_lower()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule { Id = "base", Priority = 0, Then = new PolicyAction { Quality = 70, TargetFormat = "webp" } },
                new PolicyRule { Id = "override", Priority = 10, Then = new PolicyAction { Quality = 95 } },
            ],
        };

        var decision = policy.Evaluate(ImageFacts());

        Assert.Equal(95, decision.Result.Quality);
        // The lower rule's untouched setting survives the merge.
        Assert.Equal("webp", decision.Result.TargetFormat);
    }

    [Fact]
    public void Specificity_breaks_a_priority_tie_in_favour_of_the_narrower_rule()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                // Declared first and equally prioritised, but says less: must lose.
                new PolicyRule
                {
                    Id = "general",
                    Priority = 5,
                    When = new PolicyCondition(CombineMode.All,
                        [new PolicyTest("source.category", PolicyOperator.Equals, PolicyValue.Text("image"))], []),
                    Then = new PolicyAction { Quality = 60 },
                },
                new PolicyRule
                {
                    Id = "specific",
                    Priority = 5,
                    When = new PolicyCondition(CombineMode.All,
                    [
                        new PolicyTest("source.category", PolicyOperator.Equals, PolicyValue.Text("image")),
                        new PolicyTest("source.extension", PolicyOperator.Equals, PolicyValue.Text("heic")),
                    ], []),
                    Then = new PolicyAction { Quality = 92 },
                },
            ],
        };

        var decision = policy.Evaluate(ImageFacts());

        Assert.Equal(92, decision.Result.Quality);
        Assert.Equal(2, decision.Fired.Count);
    }

    [Fact]
    public void Numeric_comparisons_refuse_to_compare_a_text_value()
    {
        var test = new PolicyTest("source.extension", PolicyOperator.GreaterThan, PolicyValue.Number(5));

        var passed = test.Evaluate(ImageFacts().Values, out var explanation);

        Assert.False(passed);
        Assert.Contains("not both numbers", explanation);
    }

    [Fact]
    public void InSet_matches_any_listed_extension_case_insensitively()
    {
        var test = new PolicyTest("source.extension", PolicyOperator.InSet,
            PolicyValue.List(["HEIC", "DNG"]));

        Assert.True(test.Evaluate(ImageFacts().Values, out _));
    }

    [Fact]
    public void A_malformed_regex_fails_the_test_instead_of_throwing()
    {
        var test = new PolicyTest("source.name", PolicyOperator.Matches, PolicyValue.Text("([unclosed"));

        var passed = test.Evaluate(ImageFacts().Values, out var explanation);

        Assert.False(passed);
        Assert.Contains("invalid pattern", explanation);
    }

    [Fact]
    public void Derived_facts_are_computed_from_the_raw_ones()
    {
        var facts = new PolicyFacts()
            .Set("source.name", "Report.FINAL.PDF")
            .Set("source.size_bytes", 3 * 1024 * 1024);

        facts.WithDerived();

        Assert.Equal("pdf", facts.Values["source.extension"].AsText());
        Assert.Equal(3, facts.Values["source.size_mb"].AsNumber());
    }

    [Fact]
    public void AtLeast_needs_the_stated_number_of_children()
    {
        var condition = new PolicyCondition(CombineMode.AtLeast,
        [
            new PolicyTest("source.extension", PolicyOperator.Equals, PolicyValue.Text("heic")),
            new PolicyTest("source.category", PolicyOperator.Equals, PolicyValue.Text("image")),
            new PolicyTest("source.size_mb", PolicyOperator.GreaterThan, PolicyValue.Number(100)),
        ], [], Minimum: 3);

        var trace = new List<string>();
        Assert.False(condition.Evaluate(ImageFacts().Values, trace));

        var relaxed = new PolicyCondition(CombineMode.AtLeast,
            condition.Tests, [], Minimum: 2);
        Assert.True(relaxed.Evaluate(ImageFacts().Values, []));
    }

    [Fact]
    public void A_sealed_action_ignores_later_rules()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule
                {
                    Id = "lock",
                    Priority = 0,
                    Then = new PolicyAction { PresetId = "locked-preset", Sealed = true },
                },
                new PolicyRule
                {
                    Id = "later",
                    Priority = 999,
                    Then = new PolicyAction { PresetId = "should-not-win" },
                },
            ],
        };

        var decision = policy.Evaluate(ImageFacts());

        Assert.Equal("locked-preset", decision.Result.PresetId);
    }

    [Fact]
    public void A_rejecting_rule_marks_the_decision_rejected_and_carries_its_reason()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule
                {
                    Id = "no-huge-files",
                    Priority = 100,
                    When = new PolicyCondition(CombineMode.All,
                        [new PolicyTest("source.size_mb", PolicyOperator.GreaterThan, PolicyValue.Number(500))], []),
                    Then = new PolicyAction { RejectReason = "policy.refused.too_large", Sealed = true },
                },
            ],
        };

        var small = policy.Evaluate(ImageFacts(1));
        Assert.False(small.Rejected);

        var huge = policy.Evaluate(ImageFacts(800));
        Assert.True(huge.Rejected);
        Assert.Equal("policy.refused.too_large", huge.Result.RejectReason);
    }

    [Fact]
    public void Disabled_rules_never_fire()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule { Id = "off", Enabled = false, Then = new PolicyAction { Quality = 99 } },
            ],
        };

        Assert.Empty(policy.Evaluate(ImageFacts()).Fired);
    }

    [Fact]
    public void AllowedEngines_narrows_across_rules_and_empty_means_unrestricted()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule { Id = "a", Priority = 0, Then = new PolicyAction { AllowedEngines = ["magick", "ffmpeg", "pdfium"] } },
                new PolicyRule { Id = "b", Priority = 10, Then = new PolicyAction { AllowedEngines = ["ffmpeg", "pdfium"] } },
            ],
        };

        var decision = policy.Evaluate(ImageFacts());

        Assert.Equal(["ffmpeg", "pdfium"], decision.Result.AllowedEngines);
    }

    [Fact]
    public void Explain_produces_a_readable_audit_trail()
    {
        var policy = new ConversionPolicy
        {
            Rules =
            [
                new PolicyRule
                {
                    Id = "shrink",
                    Priority = 30,
                    When = new PolicyCondition(CombineMode.All,
                        [new PolicyTest("source.size_mb", PolicyOperator.GreaterThan, PolicyValue.Number(8))], []),
                    Then = new PolicyAction { TargetFormat = "webp", Quality = 82 },
                },
            ],
        };

        var text = policy.Evaluate(ImageFacts(12), "huge.heic").Explain();

        Assert.Contains("huge.heic", text);
        Assert.Contains("shrink", text);
        Assert.Contains("webp", text);
        Assert.Contains("quality=82", text);
    }

    [Fact]
    public void Nothing_named_equals_absent()
    {
        // A rule that tests an undeclared fact must not accidentally match: the difference between
        // "no value" and "the value false" is the difference between a rule firing on everything and
        // firing on nothing.
        var test = new PolicyTest("destination.would_overwrite_source", PolicyOperator.Equals, PolicyValue.Boolean(true));

        Assert.False(test.Evaluate(new PolicyFacts().Values, out _));
    }

    [Fact]
    public void Starter_policy_is_coherent_and_orders_its_rules_sensibly()
    {
        var policy = StarterPolicy.Build();

        Assert.NotEmpty(policy.Rules);
        Assert.All(policy.Rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Id)));
        Assert.All(policy.Rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Description)));
        Assert.Equal(policy.Rules.Count, policy.Rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());

        // The overwrite guard must outrank everything a user is likely to add by hand.
        var guard = policy.Rules.Single(r => r.Id == "never-overwrite-source");
        Assert.All(policy.Rules.Where(r => r.Id != guard.Id), rule => Assert.True(guard.Priority > rule.Priority));
    }

    [Fact]
    public void Starter_policy_lets_a_photo_through_at_high_quality()
    {
        var decision = StarterPolicy.Build().Evaluate(ImageFacts(2, "heic"), "IMG_0001.HEIC");

        Assert.False(decision.Rejected);
        Assert.Equal("jpg", decision.Result.TargetFormat);
        Assert.Equal(92, decision.Result.Quality);
    }

    [Fact]
    public void Starter_policy_shrinks_an_oversized_picture()
    {
        var decision = StarterPolicy.Build().Evaluate(ImageFacts(20, "png"), "huge.png");

        Assert.Equal("webp", decision.Result.TargetFormat);
        Assert.Equal(82, decision.Result.Quality);
        Assert.Equal(4096, decision.Result.MaxEdgePixels);
    }
}
