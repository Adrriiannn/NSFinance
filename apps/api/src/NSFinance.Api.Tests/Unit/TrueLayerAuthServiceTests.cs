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
            "https://api.nsfinance.local/api/banking/truelayer/callback",
            "state-xyz",
            TrueLayerAuthService.BuildScopes(),
            providers);

        var uri = new Uri(url);
        var query = ParseQuery(uri.Query);

        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-123", query["client_id"]);
        Assert.Equal("https://api.nsfinance.local/api/banking/truelayer/callback", query["redirect_uri"]);
        Assert.Equal("state-xyz", query["state"]);
        Assert.Equal("accounts balance transactions offline_access", query["scope"]);
        Assert.Equal("uk-cs-mock", query["providers"]);
    }

    [Fact]
    public void BuildAuthorizationLink_SandboxHasProviders_AndNoRawSpacesInQuery()
    {
        var url = TrueLayerAuthService.BuildAuthorizationLink(
            "https://auth.truelayer-sandbox.com",
            "client-123",
            "http://192.168.0.11:5080/api/banking/truelayer/callback",
            "state-xyz",
            TrueLayerAuthService.BuildScopes(),
            TrueLayerAuthService.BuildProviders("sandbox"));

        var uri = new Uri(url);
        var query = ParseQuery(uri.Query);

        Assert.Contains("providers=", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", uri.Query, StringComparison.Ordinal);
        Assert.Equal("uk-cs-mock", query["providers"]);
    }

    [Fact]
    public void BuildScopes_ReturnsPhase2Scopes()
    {
        var scopes = TrueLayerAuthService.BuildScopes();
        Assert.Equal(["accounts", "balance", "transactions", "offline_access"], scopes);
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
