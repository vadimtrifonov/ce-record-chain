using Xunit;
using static Skyrim.RecordChain.Tests.RecordChainTestDriver;

namespace Skyrim.RecordChain.Tests;

public sealed class RecordChainBatchTests(RecordChainFixture fixture) : IClassFixture<RecordChainFixture>
{
    private readonly RecordChainTestDriver _driver = new(fixture);

    [Fact]
    public void OneKeyBatchMatchesSingularOutput()
    {
        Assert.Equal(
            _driver.Run(fixture.MultipleOverrides),
            _driver.RunBatch($"{fixture.MultipleOverrides}\n"));
    }

    [Fact]
    public void PreservesInputAndChainOrderAcrossRecordKinds()
    {
        var formKeys = new[]
        {
            fixture.MultipleOverrides,
            fixture.Cell,
            fixture.PlacedObject,
            fixture.DeletedWinner,
            fixture.Injected
        };
        var result = _driver.RunBatch(string.Join('\n', formKeys));
        var rows = ParseRows(result);

        Assert.Equal(
        [
            fixture.MultipleOverrides.ToString(),
            fixture.MultipleOverrides.ToString(),
            fixture.MultipleOverrides.ToString(),
            fixture.Cell.ToString(),
            fixture.Cell.ToString(),
            fixture.PlacedObject.ToString(),
            fixture.PlacedObject.ToString(),
            fixture.DeletedWinner.ToString(),
            fixture.DeletedWinner.ToString(),
            fixture.Injected.ToString(),
            fixture.Injected.ToString()
        ],
            rows.Select(row => row.GetProperty("formKey").GetString()));

        foreach (var formKey in formKeys)
        {
            var chain = rows
                .Where(row => row.GetProperty("formKey").GetString() == formKey.ToString())
                .ToArray();
            Assert.True(chain[0].GetProperty("origin").GetBoolean());
            Assert.True(chain[^1].GetProperty("winner").GetBoolean());
        }

        Assert.True(rows
            .Single(row => row.GetProperty("formKey").GetString() == fixture.DeletedWinner.ToString()
                           && row.GetProperty("winner").GetBoolean())
            .GetProperty("deleted").GetBoolean());
        Assert.Equal(
            new[] { "Injector.esp", "InjectionPatch.esp" },
            rows.Where(row => row.GetProperty("formKey").GetString() == fixture.Injected.ToString())
                .Select(row => row.GetProperty("plugin").GetString()));
    }

    [Fact]
    public void FileAndStandardInputProduceIdenticalOutput()
    {
        var input = string.Join('\n', fixture.NewRecord, fixture.LightRecord) + "\n";
        var inputPath = Path.Combine(fixture.Root, "formkeys.txt");
        File.WriteAllText(inputPath, input);

        var standardInputResult = _driver.RunBatch(input);
        var fileResult = _driver.RunBatch(string.Empty, inputPath);

        Assert.Equal(standardInputResult, fileResult);
    }

    [Fact]
    public void ValidatesMo2RootBeforeReadingStandardInput()
    {
        var stdin = new TrackingTextReader();
        var result = Invoke(
        [
            "--game", "SkyrimSE",
            "--mo2-root", Path.Combine(fixture.Root, "Missing MO2"),
            "--profile", fixture.ProfileName,
            "--formkeys-from", "-"
        ],
            stdin);

        Assert.False(stdin.WasRead);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("MO2 instance directory does not exist", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatesProfileBeforeReadingStandardInput()
    {
        var stdin = new TrackingTextReader();
        var result = Invoke(
        [
            "--game", "SkyrimSE",
            "--mo2-root", fixture.Mo2Root,
            "--profile", fixture.MissingProfileFilesProfile,
            "--formkeys-from", "-"
        ],
            stdin);

        Assert.False(stdin.WasRead);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("plugins.txt", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsDuplicateFormKeyWithoutOutput()
    {
        var result = _driver.RunBatch($"{fixture.MultipleOverrides}\n{fixture.MultipleOverrides}\n");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("duplicate FormKey", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("line 2", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMalformedFormKeyWithLineNumberWithoutOutput()
    {
        var result = _driver.RunBatch($"{fixture.MultipleOverrides}\nnot-a-form-key\n");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("line 2", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not-a-form-key", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsEmptyInputWithoutOutput()
    {
        var result = _driver.RunBatch(string.Empty);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("empty", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsEmptyLineWithoutOutput()
    {
        var result = _driver.RunBatch($"{fixture.MultipleOverrides}\n\n");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("line 2", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyChainProducesNoRowsInBatch()
    {
        var rows = ParseRows(_driver.RunBatch(
            $"{fixture.NewRecord}\n00FFFF:Skyrim.esm\n{fixture.InactiveOverride}\n"));

        Assert.Equal(
            [fixture.NewRecord.ToString(), fixture.InactiveOverride.ToString()],
            rows.Select(row => row.GetProperty("formKey").GetString()));
    }

    [Fact]
    public void RejectsPositionalAndBatchInputsTogether()
    {
        var result = Invoke(
        [
            "--game", "SkyrimSE",
            "--mo2-root", fixture.Mo2Root,
            "--profile", fixture.ProfileName,
            "--formkeys-from", "-",
            fixture.MultipleOverrides.ToString()
        ],
            fixture.DeletedWinner.ToString());

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("exactly one", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TrackingTextReader : TextReader
    {
        public bool WasRead { get; private set; }

        public override string? ReadLine()
        {
            WasRead = true;
            return null;
        }
    }
}
