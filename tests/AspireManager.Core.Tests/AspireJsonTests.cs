using AspireManager.Core;
using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

/// <summary>Payloads are captured verbatim from a real AppHost, trimmed only of fields the model ignores.</summary>
public class AspireJsonTests
{
    private const string ProjectResource = """
        {"name":"recipes-api-nfgpzdyc","displayName":"recipes-api","resourceType":"Project","state":"Running",
         "startTimestamp":"2026-08-31T10:13:59.913+00:00","healthStatus":"Healthy",
         "commands":{"rebuild":{"displayName":"Rebuild","description":"Stop the resource, rebuild.","state":"Enabled","sortOrder":3},
                     "restart":{"displayName":"Restart","description":"Restart resource.","state":"Enabled","sortOrder":2},
                     "stop":{"displayName":"Stop","description":"Stop resource","state":"Enabled","sortOrder":1}}}
        """;

    [Fact]
    public void ParsesResourceAndSeparatesNameFromDisplayName()
    {
        AspireResource? resource = AspireJson.ParseResource(ProjectResource);

        resource.Should().NotBeNull();
        resource!.Name.Should().Be("recipes-api-nfgpzdyc");
        resource.DisplayName.Should().Be("recipes-api");
        resource.ResourceType.Should().Be("Project");
        resource.State.Should().Be("Running");
        resource.HealthStatus.Should().Be("Healthy");
        resource.Commands.Should().ContainKeys("rebuild", "restart", "stop");
    }

    [Fact]
    public void OmittedArgumentInputsParseAsNullRatherThanThrowing()
    {
        AspireResource resource = AspireJson.ParseResource(ProjectResource)!;

        resource.Commands!["restart"].ArgumentInputs.Should().BeNull();
    }

    [Fact]
    public void ParsesArgumentInputsWhenPresent()
    {
        const string withArgs = """
            {"name":"sqlPass","displayName":"sqlPass","resourceType":"Parameter","state":"Running",
             "commands":{"set-parameter":{"displayName":"Set parameter","state":"Enabled","sortOrder":0,
               "argumentInputs":[{"name":"Value","label":"sqlPass","inputType":"Text","required":true},
                                 {"name":"SaveToUserSecrets","label":"Save to user secrets"}]}}}
            """;

        AspireResource resource = AspireJson.ParseResource(withArgs)!;

        resource.Commands!["set-parameter"].ArgumentInputs.Should().HaveCount(2);
        resource.Commands["set-parameter"].ArgumentInputs![0].Name.Should().Be("Value");
    }

    [Fact]
    public void ParsesLogLineKeyedByDisplayName()
    {
        const string line = """
            {"resourceName":"ticker","timestamp":"2026-08-31T11:23:47.219Z","content":"Mon Aug 31 14:23:47 EEST 2026","isError":false}
            """;

        LogLine? log = AspireJson.ParseLogLine(line);

        log.Should().NotBeNull();
        log!.ResourceName.Should().Be("ticker");
        log.Content.Should().Be("Mon Aug 31 14:23:47 EEST 2026");
        log.IsError.Should().BeFalse();
        log.Timestamp.Should().Be(DateTimeOffset.Parse("2026-08-31T11:23:47.219Z"));
    }

    [Fact]
    public void ParsesAppHostList()
    {
        const string json = """
            [{"appHostPath":"/Users/juuso/git/g6-single-repo/src/host/Shop.AppHost/Shop.AppHost.csproj",
              "appHostPid":45548,"status":"running","sdkVersion":"13.5.1","cliPid":45070,
              "dashboardUrl":"https://localhost:17228/login?t=abc"}]
            """;

        IReadOnlyList<AppHost> hosts = AspireJson.ParseAppHosts(json);

        hosts.Should().HaveCount(1);
        hosts[0].AppHostPid.Should().Be(45548);
        hosts[0].Status.Should().Be("running");
    }

    /// <summary>`describe` wraps its resources without --follow but emits them bare with it.</summary>
    [Fact]
    public void ParsesWrappedSnapshotForm()
    {
        string json = $"{{\"resources\":[{ProjectResource}]}}";

        AspireJson.ParseSnapshot(json).Should().HaveCount(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"name\":\"half-written")]
    [InlineData("not json at all")]
    public void ReturnsNullForBlankOrTruncatedLines(string line) =>
        AspireJson.ParseResource(line).Should().BeNull();
}

public class LogDocumentTests
{
    /// <summary>
    /// `aspire logs` without --follow wraps everything in one pretty-printed document, unlike the NDJSON it
    /// streams with --follow. Captured verbatim.
    /// </summary>
    private const string Document = """
        {
          "logs": [
            {
              "resourceName": "cosmos",
              "timestamp": "2026-08-31T10:13:36.910Z",
              "content": "Release Version: EN20260810",
              "isError": false
            },
            {
              "resourceName": "cosmos",
              "timestamp": "2026-08-31T10:13:37.100Z",
              "content": "started",
              "isError": true
            }
          ]
        }
        """;

    [Fact]
    public void ParsesTheWrappedDocument()
    {
        IReadOnlyList<LogLine> logs = AspireJson.ParseLogDocument(Document);

        logs.Should().HaveCount(2);
        logs[0].Content.Should().Be("Release Version: EN20260810");
        logs[0].ResourceName.Should().Be("cosmos");
        logs[1].IsError.Should().BeTrue();
    }

    [Fact]
    public void EmptyDocumentIsNoLines() =>
        AspireJson.ParseLogDocument("""{"logs":[]}""").Should().BeEmpty();

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void UnusableInputIsNoLines(string json) =>
        AspireJson.ParseLogDocument(json).Should().BeEmpty();
}

/// <summary>
/// Regression: `describe --follow` emits a resource with no `state` while it is restarting, and the record
/// declares State non-nullable, so `StateMark` dereferenced null and took the application down.
/// </summary>
public class MissingFieldTests
{
    [Fact]
    public void ResourceWithNoStateIsUsable()
    {
        AspireResource resource = AspireJson.ParseResource(
            """{"name":"cosmos-abc","displayName":"cosmos","resourceType":"Container"}""")!;

        resource.State.Should().NotBeNull();
        ShellModel.StateMark(resource).Should().Be("?");
        ShellModel.Row(resource).Should().NotBeNull();
        ShellModel.Tone(new ResourceItem(resource)).Should().Be(RowTone.Inactive);
    }

    [Fact]
    public void ExplicitNullStateIsAlsoHandled() =>
        AspireJson.ParseResource(
                """{"name":"c-1","displayName":"c","resourceType":"Container","state":null}""")!
            .State.Should().NotBeNull();

    [Fact]
    public void MissingResourceTypeGetsAPlaceholder() =>
        AspireJson.ParseResource("""{"name":"c-1","displayName":"c","state":"Running"}""")!
            .ResourceType.Should().Be("Unknown");

    /// <summary>Display name is what the log stream and commands key on; fall back rather than crash.</summary>
    [Fact]
    public void MissingDisplayNameFallsBackToTheName() =>
        AspireJson.ParseResource("""{"name":"c-1","resourceType":"Container","state":"Running"}""")!
            .DisplayName.Should().Be("c-1");

    /// <summary>Without a name there is nothing to key on, so the line is dropped rather than stored.</summary>
    [Theory]
    [InlineData("""{"displayName":"c","state":"Running"}""")]
    [InlineData("""{"name":null,"displayName":"c"}""")]
    [InlineData("""{"name":""}""")]
    public void ResourceWithoutANameIsDropped(string json) =>
        AspireJson.ParseResource(json).Should().BeNull();

    [Fact]
    public void RowsRenderForAResourceMissingEverythingOptional()
    {
        AspireResource resource = AspireJson.ParseResource("""{"name":"x-1"}""")!;

        IReadOnlyList<ResourceRow> rows = ShellModel.Rows([resource]);

        rows.Select(ShellModel.RowText).Should().NotBeEmpty();
        ShellModel.AvailableCommands(resource).Should().BeEmpty();
    }

    [Fact]
    public void LogLineWithNoContentIsUsable() =>
        AspireJson.ParseLogLine("""{"resourceName":"cosmos","timestamp":"2026-08-31T10:00:00Z"}""")!
            .Content.Should().BeEmpty();

    /// <summary>A log line with no resource cannot be filed against one.</summary>
    [Fact]
    public void LogLineWithNoResourceIsDropped() =>
        AspireJson.ParseLogLine("""{"content":"orphan","timestamp":"2026-08-31T10:00:00Z"}""")
            .Should().BeNull();

    [Fact]
    public void AppHostWithNoPathIsDropped() =>
        AspireJson.ParseAppHosts("""[{"appHostPid":1,"status":"running"},{"appHostPath":"/x/A.csproj"}]""")
            .Should().ContainSingle().Which.AppHostPath.Should().Be("/x/A.csproj");

    [Fact]
    public void AppHostWithNoStatusIsNotRunning() =>
        AppHostSelection.Select(AspireJson.ParseAppHosts("""[{"appHostPath":"/x/A.csproj"}]"""), null)
            .Should().BeOfType<NoAppHost>();
}
