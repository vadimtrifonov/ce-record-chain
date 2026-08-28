using Xunit;
using static Skyrim.RecordChain.Tests.RecordChainTestDriver;

namespace Skyrim.RecordChain.Tests;

public sealed class Mo2ProfileTests(RecordChainFixture fixture) : IClassFixture<RecordChainFixture>
{
    private readonly RecordChainTestDriver _driver = new(fixture);

    [Fact]
    public void LoadsConfiguredPathsIncludingUtf8ByteArrayGamePath()
    {
        var row = Assert.Single(ParseRows(_driver.Run(fixture.CreationClubRecord)));
        var pluginPath = row.GetProperty("pluginPath").GetString();

        Assert.NotNull(pluginPath);
        Assert.StartsWith(
            NormalizePath(fixture.Mo2Root) + "/",
            pluginPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tést-", pluginPath, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMismatchedConfiguredGame()
    {
        var result = _driver.Run(
            fixture.VrRecord,
            game: "SkyrimVR",
            mo2Root: fixture.Mo2Root,
            profile: fixture.ProfileName);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("gameName", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMalformedModlistEntry()
    {
        var result = _driver.Run(
            fixture.MultipleOverrides,
            profile: fixture.MalformedModlistProfile);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("modlist.txt", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("?Broken", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingEnabledModDirectory()
    {
        var result = _driver.Run(
            fixture.MultipleOverrides,
            profile: fixture.MissingEnabledModProfile);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("Missing Mod", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("modlist.txt", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }
}
