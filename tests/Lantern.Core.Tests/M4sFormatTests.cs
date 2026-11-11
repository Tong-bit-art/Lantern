// Fragmented MP4 pieces (.m4s, #354): a read-only video source on the video donut, learned by existing libraries.

using Lantern.Core.Formats;
using Lantern.Core.Presets;
using Lantern.Core.Profiles;
using Lantern.Core.Settings;

namespace Lantern.Core.Tests;

public class M4sFormatTests
{
    [Fact]
    public void M4s_is_a_video_lantern_reads_but_never_writes()
    {
        var m4s = FormatRegistry.Detect("video-1.M4S");
        Assert.NotNull(m4s);
        Assert.Equal(("m4s", FormatCategory.Video, false), (m4s.Id, m4s.Category, m4s.Writable));
        Assert.Equal("video", ProfileSelector.Select(BuiltInData.CreateProfiles(), ["movie.m4s"])?.Id);
    }

    [Fact]
    public void Version_47_libraries_learn_m4s_once()
    {
        var presets = BuiltInData.CreatePresets();
        var profiles = BuiltInData.CreateProfiles();
        var video = profiles.Single(p => p.Id == "video");
        video.Extensions.Remove("m4s");

        Assert.True(SettingsMigrations.ApplyToLibrary(47, presets, profiles));

        Assert.Contains("m4s", video.Extensions);
        Assert.False(SettingsMigrations.ApplyToLibrary(47, presets, profiles));
    }
}
