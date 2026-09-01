using AspireManager.Core;
using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class GroupPolicyTests
{
    private static GroupPolicy Policy(GroupSettings settings) => GroupPolicy.From(settings).Policy;

    [Fact]
    public void NothingConfiguredLeavesEverythingUnfoldedAndGrouped()
    {
        GroupPolicy.Default.IsCollapsed("Project").Should().BeFalse();
        GroupPolicy.Default.Mode.Should().Be(GroupMode.Grouped);
        GroupPolicy.From(null).Warning.Should().BeNull();
    }

    [Fact]
    public void ExceptFlipsTheDefault()
    {
        GroupPolicy policy = Policy(new GroupSettings(Default: "collapsed", Except: ["Project"]));

        policy.IsCollapsed("Project").Should().BeFalse();
        policy.IsCollapsed("Container").Should().BeTrue();
    }

    [Fact]
    public void ExceptAlsoFlipsAnExpandedDefault()
    {
        GroupPolicy policy = Policy(new GroupSettings(Default: "expanded", Except: ["Executable"]));

        policy.IsCollapsed("Executable").Should().BeTrue();
        policy.IsCollapsed("Project").Should().BeFalse();
    }

    /// <summary>Type names are long enough that nobody should have to match their casing.</summary>
    [Fact]
    public void MatchingIgnoresCase() =>
        Policy(new GroupSettings(Except: ["pRoJeCt"])).IsCollapsed("Project").Should().BeTrue();

    [Fact]
    public void TrailingStarMatchesByPrefix()
    {
        GroupPolicy policy = Policy(new GroupSettings(Except: ["AzureCosmosDB*"]));

        policy.IsCollapsed("AzureCosmosDBResource").Should().BeTrue();
        policy.IsCollapsed("AzureCosmosDBContainerResource").Should().BeTrue();
        policy.IsCollapsed("AzureStorageResource").Should().BeFalse();
    }

    /// <summary>A star anywhere else is part of the name, not a pattern; nothing is named that way.</summary>
    [Fact]
    public void StarIsOnlyAWildcardAtTheEnd() =>
        Policy(new GroupSettings(Except: ["Azure*Resource"])).IsCollapsed("AzureStorageResource").Should().BeFalse();

    [Fact]
    public void BareStarMatchesEveryType() =>
        Policy(new GroupSettings(Default: "expanded", Except: ["*"])).IsCollapsed("Anything").Should().BeTrue();

    [Fact]
    public void ModeIsRead() =>
        Policy(new GroupSettings(Mode: "typeSuffix")).Mode.Should().Be(GroupMode.TypeSuffix);

    [Fact]
    public void ModeIgnoresCaseAndSurroundingSpace() =>
        Policy(new GroupSettings(Mode: " PLAIN ")).Mode.Should().Be(GroupMode.Plain);

    /// <summary>An unusable value must not stop the rest of the file being honoured.</summary>
    [Fact]
    public void UnrecognisedValuesWarnAndFallBack()
    {
        (GroupPolicy policy, string? warning) = GroupPolicy.From(
            new GroupSettings(Mode: "banana", Default: "folded", Except: ["Project"]));

        policy.Mode.Should().Be(GroupMode.Grouped);
        policy.IsCollapsed("Project").Should().BeTrue();
        warning.Should().Contain("banana").And.Contain("folded");
    }

    [Fact]
    public void OmittedValuesDoNotWarn() =>
        GroupPolicy.From(new GroupSettings(Except: ["Project"])).Warning.Should().BeNull();

    /// <summary>Parsed from JSON, an absent key and an empty string arrive the same way often enough.</summary>
    [Fact]
    public void BlankValuesDoNotWarn() =>
        GroupPolicy.From(new GroupSettings(Mode: "  ", Default: "")).Warning.Should().BeNull();
}

public class GroupConfigParseTests
{
    [Fact]
    public void GroupsBlockIsRead()
    {
        AspireManagerConfig? config = ConfigFile.Parse(
            """
            {
              // both blocks, since the file is hand-edited
              "editor": { "command": "code --goto {file}:{line}" },
              "groups": { "mode": "grouped", "default": "collapsed", "except": ["Project", "Azure*"] },
            }
            """);

        config!.Groups!.Mode.Should().Be("grouped");
        config.Groups.Default.Should().Be("collapsed");
        config.Groups.Except.Should().Equal("Project", "Azure*");
    }

    [Fact]
    public void GroupsBlockIsOptional() =>
        ConfigFile.Parse("""{ "editor": { "command": "vi {file}" } }""")!.Groups.Should().BeNull();
}
