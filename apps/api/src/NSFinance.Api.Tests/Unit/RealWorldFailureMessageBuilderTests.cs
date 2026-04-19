using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class RealWorldFailureMessageBuilderTests
{
    private readonly RealWorldFailureMessageBuilder sut = new();

    [Fact]
    public void Build_MissingLocation_ProducesLocationPrompt()
    {
        var message = sut.Build(RealWorldFailureScenario.MissingLocation, exploratory: false);

        Assert.Contains("location permission", message.ReplyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fallback_missing_location", message.Warnings);
    }

    [Fact]
    public void Build_ProviderUnavailable_DiffersFromNoMatches()
    {
        var provider = sut.Build(RealWorldFailureScenario.ProviderUnavailable, exploratory: false);
        var noMatches = sut.Build(RealWorldFailureScenario.NoMatchesFound, exploratory: false);

        Assert.NotEqual(provider.ReplyText, noMatches.ReplyText);
    }

    [Fact]
    public void Build_ClarificationNeeded_UsesCustomPromptWhenProvided()
    {
        const string clarification = "Do you want nearby places or financial guidance?";
        var message = sut.Build(
            RealWorldFailureScenario.ClarificationNeeded,
            exploratory: false,
            clarificationPrompt: clarification);

        Assert.Equal(clarification, message.ReplyText);
    }
}
