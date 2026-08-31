using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class UrlTests
{
    private static AspireResource Resource(params AspireUrl[] urls) =>
        new("recipes-api-abc", "recipes-api", "Project", "Running", "Healthy", null, null, urls);

    [Fact]
    public void UrlsAreKeptInTheOrderTheAppHostReportedThem() =>
        ShellModel.Urls(Resource(
                new AspireUrl("http://localhost:5134/scalar", "http"),
                new AspireUrl("https://localhost:7246/scalar", "https")))
            .Select(static u => u.Url)
            .Should().ContainInOrder("http://localhost:5134/scalar", "https://localhost:7246/scalar");

    [Fact]
    public void ResourceWithNoUrlsHasNone()
    {
        ShellModel.Urls(Resource()).Should().BeEmpty();
        ShellModel.PrimaryUrl(Resource()).Should().BeNull();
    }

    [Fact]
    public void PrimaryIsTheFirstUrl() =>
        ShellModel.PrimaryUrl(Resource(
                new AspireUrl("http://localhost:5134/scalar", "http"),
                new AspireUrl("https://localhost:7246/scalar", "https")))!
            .Url.Should().Be("http://localhost:5134/scalar");

    /// <summary>An emulator health probe is listed but is not what a bare "open" should launch.</summary>
    [Fact]
    public void PrimarySkipsInternalEndpoints()
    {
        AspireResource resource = Resource(
            new AspireUrl("http://localhost:27654", "emulatorhealth", IsInternal: true),
            new AspireUrl("http://localhost:8081", "explorer"));

        ShellModel.PrimaryUrl(resource)!.Url.Should().Be("http://localhost:8081");
        ShellModel.Urls(resource).Should().HaveCount(2, "the picker still lists it");
    }

    [Fact]
    public void OnlyInternalUrlsMeansNoPrimary() =>
        ShellModel.PrimaryUrl(Resource(new AspireUrl("http://localhost:27654", IsInternal: true)))
            .Should().BeNull();

    /// <summary>
    /// The URL is handed to the OS shell, so anything that is not http(s) is refused rather than launched
    /// on an AppHost's say-so.
    /// </summary>
    [Theory]
    [InlineData("http://localhost:5134", true)]
    [InlineData("https://localhost:7246/scalar", true)]
    [InlineData("HTTP://localhost:5134", true)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("tcp://localhost:1433", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyHttpSchemesAreOpenable(string? url, bool expected) =>
        ShellModel.IsOpenable(url).Should().Be(expected);

    [Fact]
    public void UnopenableUrlsAreFilteredOut() =>
        ShellModel.Urls(Resource(
                new AspireUrl("tcp://localhost:1433", "tcp"),
                new AspireUrl("http://localhost:8081", "explorer")))
            .Should().ContainSingle().Which.Url.Should().Be("http://localhost:8081");

    [Fact]
    public void LabelShowsTheEndpointNameAndUrl() =>
        ShellModel.UrlLabel(new AspireUrl("http://localhost:5134/scalar", "http"))
            .Should().Be("http  http://localhost:5134/scalar");

    [Fact]
    public void LabelPrefersDisplayNameWhenPresent() =>
        ShellModel.UrlLabel(new AspireUrl("http://localhost:8081", "explorer", "Data Explorer"))
            .Should().Be("Data Explorer  http://localhost:8081");

    [Fact]
    public void LabelCopesWithNoName() =>
        ShellModel.UrlLabel(new AspireUrl("http://localhost:8081")).Should().Be("http://localhost:8081");

    [Fact]
    public void LabelMarksInternalEndpoints() =>
        ShellModel.UrlLabel(new AspireUrl("http://localhost:27654", "emulatorhealth", IsInternal: true))
            .Should().Be("emulatorhealth  http://localhost:27654  (internal)");
}
