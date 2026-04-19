using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class GooglePlacesOptionsValidatorTests
{
    private readonly GooglePlacesOptionsValidator sut = new();

    [Fact]
    public void Validate_FailsWhenEnabledWithoutApiKey()
    {
        var result = sut.Validate(
            name: null,
            new GooglePlacesOptions
            {
                Enabled = true,
                ApiKey = ""
            });

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures, failure => failure.Contains("ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SucceedsWhenDisabledWithoutApiKey()
    {
        var result = sut.Validate(
            name: null,
            new GooglePlacesOptions
            {
                Enabled = false,
                ApiKey = ""
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_FailsForInvalidBaseUrl()
    {
        var result = sut.Validate(
            name: null,
            new GooglePlacesOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                BaseUrl = "not-a-url"
            });

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures, failure => failure.Contains("BaseUrl", StringComparison.Ordinal));
    }
}
