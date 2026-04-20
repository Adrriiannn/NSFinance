using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Tests.Unit;

public sealed class GooglePlacesIntegrationRegistrationTests
{
    [Fact]
    public void AddAIIntegration_RegistersCompanionAndMerchantPlacesServices()
    {
        using var serviceProvider = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["AI:UseMockProvider"] = "true",
            ["CompanionAI:Places:Enabled"] = "false"
        });

        Assert.IsType<GooglePlacesCompanionSearchService>(
            serviceProvider.GetRequiredService<IPlacesSearchService>());
        Assert.IsType<GooglePlacesPlaceDetailsService>(
            serviceProvider.GetRequiredService<IPlaceDetailsService>());
        Assert.IsType<CompanionPlaceDiscoveryService>(
            serviceProvider.GetRequiredService<ICompanionPlaceDiscoveryService>());
        Assert.IsType<MerchantPlaceLookupService>(
            serviceProvider.GetRequiredService<IMerchantPlaceLookupService>());
        Assert.IsType<RealWorldConversationSearchContextService>(
            serviceProvider.GetRequiredService<IRealWorldConversationSearchContextService>());
        Assert.IsType<RealWorldSearchScopeResolver>(
            serviceProvider.GetRequiredService<IRealWorldSearchScopeResolver>());

        // 3.6 review synthesis remains out of scope.
        Assert.IsType<NullReviewInsightsService>(
            serviceProvider.GetRequiredService<IReviewInsightsService>());
    }

    private static ServiceProvider BuildServiceProvider(IReadOnlyDictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(builder =>
            builder.UseInMemoryDatabase($"google-places-di-{Guid.NewGuid():N}"));
        services.AddAIIntegration(configuration);

        return services.BuildServiceProvider();
    }
}
