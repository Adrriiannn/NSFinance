using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Tests.Unit;

public class TrueLayerAuthServiceTests
{
    [Fact]
    public void BuildAuthorizationLink_IncludesExpectedParameters()
    {
        var providers = TrueLayerAuthService.BuildProviders("sandbox");
        var url = TrueLayerAuthService.BuildAuthorizationLink(
            "https://auth.truelayer-sandbox.com",
            "client-123",
            "http://localhost:5080/api/banking/truelayer/callback",
            "state-xyz",
            TrueLayerAuthService.BuildScopes(),
            providers);

        var uri = new Uri(url);
        var query = ParseQuery(uri.Query);

        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-123", query["client_id"]);
        Assert.Equal("http://localhost:5080/api/banking/truelayer/callback", query["redirect_uri"]);
        Assert.Equal("state-xyz", query["state"]);
        Assert.Equal("info accounts cards balance transactions offline_access direct_debits standing_orders", query["scope"]);
        Assert.Equal("uk-cs-mock", query["providers"]);
    }

    [Fact]
    public void BuildAuthorizationLink_SandboxHasProviders_AndNoRawSpacesInQuery()
    {
        var url = TrueLayerAuthService.BuildAuthorizationLink(
            "https://auth.truelayer-sandbox.com",
            "client-123",
            "http://localhost:5080/api/banking/truelayer/callback",
            "state-xyz",
            TrueLayerAuthService.BuildScopes(),
            TrueLayerAuthService.BuildProviders("sandbox"));

        var uri = new Uri(url);
        var query = ParseQuery(uri.Query);

        Assert.Contains("providers=", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", uri.Query, StringComparison.Ordinal);
        Assert.Equal("uk-cs-mock", query["providers"]);
        Assert.False(query.ContainsKey("country_id"));
    }

    [Fact]
    public void BuildAuthorizationLink_LiveTargetsIrelandProvidersAndCountry()
    {
        var url = TrueLayerAuthService.BuildAuthorizationLink(
            "https://auth.truelayer.com",
            "client-123",
            "https://api.finance.nsireland.ie/api/banking/truelayer/callback",
            "state-live",
            TrueLayerAuthService.BuildScopes(),
            TrueLayerAuthService.BuildProviders("live"),
            TrueLayerAuthService.BuildCountryId("live"));

        var uri = new Uri(url);
        var query = ParseQuery(uri.Query);

        Assert.Equal("ie-ob-all", query["providers"]);
        Assert.Equal("IE", query["country_id"]);
    }

    [Fact]
    public void BuildScopes_ReturnsExpandedScopeSet()
    {
        var scopes = TrueLayerAuthService.BuildScopes();
        Assert.Equal(
        [
            "info",
            "accounts",
            "cards",
            "balance",
            "transactions",
            "offline_access",
            "direct_debits",
            "standing_orders"
        ], scopes);
    }

    [Theory]
    [InlineData("nsfinance://accounts/connect-bank?intent=new")]
    [InlineData("nsfinance://modals/add-account?intent=new")]
    [InlineData("exp://192.168.0.11:8081/--/accounts/connect-bank?intent=new")]
    [InlineData("exp://192.168.0.11:8081/--/modals/add-account?intent=new")]
    public void ReturnUriContract_Normalize_AcceptsCurrentAndLegacyRoutes(string input)
    {
        var normalized = TrueLayerReturnUriContract.Normalize(input);
        Assert.NotNull(normalized);
    }

    [Fact]
    public void BuildProviders_LiveUsesIrelandProviderGroup()
    {
        var providers = TrueLayerAuthService.BuildProviders("live");
        Assert.Equal(["ie-ob-all"], providers);
    }

    [Fact]
    public void BuildCountryId_LiveUsesIreland()
    {
        var countryId = TrueLayerAuthService.BuildCountryId("live");
        Assert.Equal("IE", countryId);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
    }
}
