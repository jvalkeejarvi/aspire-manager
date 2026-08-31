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
