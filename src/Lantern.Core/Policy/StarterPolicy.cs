// Lantern original module: conversion policy engine — the starter policy.
//
// A new install gets a small, deliberately boring rule set. Each rule exists because it solves a
// problem a batch converter actually has, and each is commented so the file doubles as the tutorial
// for writing more. Priorities are spaced by ten so a user can slot a rule between two of them
// without renumbering.

namespace Lantern.Core.Policy;

/// <summary>Builds the policy a fresh install starts with.</summary>
public static class StarterPolicy
{
    /// <summary>Fact names the starter rules use. Exposed so a settings page can offer them as suggestions.</summary>
    public static IReadOnlyList<string> KnownFacts { get; } =
    [
        "source.name",
        "source.extension",
        "source.size_bytes",
        "source.size_mb",
        "source.folder",
        "source.category",
        "source.format",
        "request.preset",
        "request.target",
        "request.batch_count",
        "destination.folder",
        "destination.free_mb",
        "user.language",
        "user.tag",
    ];

    /// <summary>The starter policy.</summary>
    public static ConversionPolicy Build() => new()
    {
        Name = "default",
        Rules =
        [
            // A camera or phone original is worth keeping at full quality; a screenshot is not, and
            // crushing it saves nothing a person notices. Priority 50 so a user's own exception can
            // sit above it.
            new PolicyRule
            {
                Id = "photo-keep-quality",
                Description = "Camera originals (HEIC/RAW) go to JPG at high quality rather than the default re-encode.",
                Priority = 50,
                When = new PolicyCondition(CombineMode.All,
                [
                    new PolicyTest("source.extension", PolicyOperator.InSet, PolicyValue.List(["heic", "heif", "dng", "cr2", "cr3", "nef", "arw"])),
                ], []),
                Then = new PolicyAction
                {
                    PresetId = "to-jpg",
                    Quality = 92,
                    TargetFormat = "jpg",
                },
            },

            // A large batch of documents is almost always a paper trail somebody wants in one file,
            // not a pile of PDFs. Merging is the useful default above a dozen files.
            new PolicyRule
            {
                Id = "bulk-documents-to-one-pdf",
                Description = "Twelve or more office documents in one drop become a single merged PDF.",
                Priority = 40,
                When = new PolicyCondition(CombineMode.All,
                [
                    new PolicyTest("source.extension", PolicyOperator.InSet, PolicyValue.List(["docx", "doc", "odt", "rtf", "xlsx", "xls", "pptx", "ppt", "txt", "md"])),
                    new PolicyTest("request.batch_count", PolicyOperator.GreaterOrEqual, PolicyValue.Number(12)),
                ], []),
                Then = new PolicyAction
                {
                    PresetId = "merge-pdf",
                    TargetFormat = "pdf",
                    NamePattern = "{folder}-merged",
                },
            },

            // Upload forms with a hard size limit are the single most common reason people convert at
            // all. Shrinking under 8 MB beats failing the upload and doing it by hand.
            new PolicyRule
            {
                Id = "shrink-for-upload",
                Description = "A picture over 8 MB is re-encoded to WEBP at reduced quality and a bounded edge.",
                Priority = 30,
                When = new PolicyCondition(CombineMode.All,
                [
                    new PolicyTest("source.size_mb", PolicyOperator.GreaterThan, PolicyValue.Number(8)),
                    new PolicyTest("source.category", PolicyOperator.Equals, PolicyValue.Text("image"), CaseSensitive: false),
                ], []),
                Then = new PolicyAction
                {
                    PresetId = "to-webp",
                    TargetFormat = "webp",
                    Quality = 82,
                    MaxEdgePixels = 4096,
                },
            },

            // Names that are already meaningful should survive a conversion. Only apply the pattern
            // when the source name is not an anonymous camera or screenshot name.
            new PolicyRule
            {
                Id = "keep-meaningful-names",
                Description = "Descriptive source names are preserved; anonymous names get the preset appended.",
                Priority = 10,
                When = new PolicyCondition(CombineMode.All,
                [
                    new PolicyTest("source.name", PolicyOperator.NotExists, PolicyValue.Null),
                ], []),
                Then = new PolicyAction { NamePattern = "{name}" },
            },

            new PolicyRule
            {
                Id = "name-anonymous-files",
                Description = "Screenshots, camera defaults and download-temp names get the preset appended so they stay distinguishable.",
                Priority = 20,
                When = new PolicyCondition(CombineMode.All,
                [
                    new PolicyTest("source.name", PolicyOperator.Matches, PolicyValue.Text(
                        @"^(img_|dsc_|screenshot|screen shot|untitled|无标题|未命名|新建|download|下载)[ _\-]?\d*")),
                ], []),
                Then = new PolicyAction { NamePattern = "{name}-{preset}" },
            },

            // A conversion whose output would land on the source is refused outright. The path resolver
            // already dodges this by renaming to "name (2)", but silently — and a batch tool that quietly
            // renames somebody's file because the preset asked for the same name is doing something the
            // user did not ask for. Refusing says so.
            new PolicyRule
            {
                Id = "never-overwrite-source",
                Description = "Refuse a job whose output naming points at the source file itself, instead of silently renaming it.",
                Priority = 1000,
                When = new PolicyCondition(CombineMode.All,
                [
                    new PolicyTest("destination.would_overwrite_source", PolicyOperator.Equals, PolicyValue.Boolean(true)),
                ], []),
                Then = new PolicyAction
                {
                    RejectReason = "policy.refused.overwrite_source",
                    Sealed = true,
                },
            },
        ],
    };
}
