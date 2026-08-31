using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class ConfigParsingTests
{
    [Fact]
    public void ReadsBothTemplates()
    {
        AspireManagerConfig? config = ConfigFile.Parse("""
            {
              "editor": {
                "command": "emacsclient -n +{line} {file}",
                "commandNoLine": "emacsclient -n {file}"
              }
            }
            """);

        config!.Editor!.Command.Should().Be("emacsclient -n +{line} {file}");
        config.Editor.CommandNoLine.Should().Be("emacsclient -n {file}");
    }

    /// <summary>It is hand-edited, so neither should be a parse error.</summary>
    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        AspireManagerConfig? config = ConfigFile.Parse("""
            {
              // which editor to open logs in
              "editor": {
                "command": "code --goto {file}:{line}",
              },
            }
            """);

        config!.Editor!.Command.Should().Be("code --goto {file}:{line}");
    }

    [Fact]
    public void MissingEditorSectionIsNotAnError() =>
        ConfigFile.Parse("{}")!.Editor.Should().BeNull();

    [Fact]
    public void MalformedJsonThrows() =>
        FluentActions.Invoking(static () => ConfigFile.Parse("{ \"editor\": "))
            .Should().Throw<System.Text.Json.JsonException>();

    /// <summary>No config file is "nothing configured", not a failure.</summary>
    [Fact]
    public void MissingFileIsSilent()
    {
        (AspireManagerConfig? config, string? error) = ConfigFile.Load("/nonexistent/aspire-manager.json");

        config.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void BrokenFileReportsWhy()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");

        try
        {
            (AspireManagerConfig? config, string? error) = ConfigFile.Load(path);

            config.Should().BeNull();
            error.Should().NotBeNull().And.Contain(Path.GetFileName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DefaultPathIsInTheHomeDirectory() =>
        ConfigFile.DefaultPath.Should().EndWith(".aspire-manager.json");
}

public class EditorCommandLineTests
{
    private static readonly EditorSettings Both = new(
        "emacsclient -n +{line} {file}",
        "emacsclient -n {file}");

    [Fact]
    public void LineTemplateIsUsedWhenThereIsALine() =>
        EditorCommandLine.Choose(Both, 42).Should().Be((Both.Command, 42));

    [Fact]
    public void NoLineTemplateIsUsedWhenThereIsNot() =>
        EditorCommandLine.Choose(Both, null).Should().Be((Both.CommandNoLine, 1));

    /// <summary>A config with only `command` still has to work without a line; line 1 is where files open.</summary>
    [Fact]
    public void FallsBackToTheLineTemplateAtLineOne() =>
        EditorCommandLine.Choose(new EditorSettings("code --goto {file}:{line}"), null)
            .Should().Be(("code --goto {file}:{line}", 1));

    [Fact]
    public void NothingConfiguredChoosesNothing() =>
        EditorCommandLine.Choose(null, 5).Template.Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTemplateBuildsNothing(string? template) =>
        EditorCommandLine.Build(template, "/tmp/x.log", 1).Should().BeNull();

    [Fact]
    public void FirstTokenIsTheCommandAndTheRestAreArguments()
    {
        (string Command, IReadOnlyList<string> Arguments)? built =
            EditorCommandLine.Build("emacsclient -n +{line} {file}", "/tmp/x.log", 42);

        built!.Value.Command.Should().Be("emacsclient");
        built.Value.Arguments.Should().Equal("-n", "+42", "/tmp/x.log");
    }

    /// <summary>
    /// The whole point of splitting before substituting: a path with spaces must remain one argument.
    /// </summary>
    [Fact]
    public void PathWithSpacesStaysASingleArgument()
    {
        (string, IReadOnlyList<string>)? built =
            EditorCommandLine.Build("code --goto {file}", "/My Logs/tags api.log", 1);

        built!.Value.Item2.Should().Equal("--goto", "/My Logs/tags api.log");
    }

    [Fact]
    public void PlaceholdersCombineInOneToken() =>
        EditorCommandLine.Build("code --goto {file}:{line}", "/tmp/x.log", 7)!.Value.Arguments
            .Should().Equal("--goto", "/tmp/x.log:7");

    [Fact]
    public void RepeatedWhitespaceDoesNotProduceEmptyArguments() =>
        EditorCommandLine.Build("vim   +{line}    {file}", "/tmp/x.log", 3)!.Value.Arguments
            .Should().Equal("+3", "/tmp/x.log");

    [Fact]
    public void CommandWithNoArgumentsWorks() =>
        EditorCommandLine.Build("open", "/tmp/x.log", 1)!.Value.Should()
            .BeEquivalentTo(("open", Array.Empty<string>()));

    /// <summary>No shell: a pipe is just another argument, it does not redirect anything.</summary>
    [Fact]
    public void ShellSyntaxIsNotInterpreted() =>
        EditorCommandLine.Build("code {file} | tee x", "/tmp/x.log", 1)!.Value.Arguments
            .Should().Equal("/tmp/x.log", "|", "tee", "x");
}
