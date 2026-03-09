using NSFinTech.Api.Modules.Policies.Endpoints;
using NSFinTech.Api.Modules.Policies.Services;

namespace NSFinTech.Api.Modules.Policies;

public static class PoliciesModule
{
    public static IEndpointRouteBuilder MapPoliciesModule(this IEndpointRouteBuilder app)
    {
        var publicPolicies = app.MapGroup("/api/policies")
            .WithTags("Policies");

        publicPolicies.MapGet("/active", GetActivePoliciesEndpoint.HandleAsync)
            .WithName("GetActivePolicies");

        var legal = app.MapGroup("/api/legal")
            .WithTags("Legal");

        legal.MapGet("/terms", (PolicyService service, CancellationToken ct) =>
            GetPolicyByTypeEndpoint.HandleAsync("terms_of_service", service, ct))
            .WithName("GetTerms");

        legal.MapGet("/privacy", (PolicyService service, CancellationToken ct) =>
            GetPolicyByTypeEndpoint.HandleAsync("privacy_policy", service, ct))
            .WithName("GetPrivacy");

        legal.MapGet("/ai-limitations", (PolicyService service, CancellationToken ct) =>
            GetPolicyByTypeEndpoint.HandleAsync("ai_limitations_notice", service, ct))
            .WithName("GetAiLimitations");

        var protectedPolicies = app.MapGroup("/api/policies")
            .WithTags("Policies")
            .RequireAuthorization();

        protectedPolicies.MapGet("/acceptances", GetPolicyAcceptancesEndpoint.HandleAsync)
            .WithName("GetPolicyAcceptances");

        protectedPolicies.MapPost("/accept", AcceptPolicyEndpoint.HandleAsync)
            .WithName("AcceptPolicy");

        protectedPolicies.MapGet("/consents", GetConsentsEndpoint.HandleAsync)
            .WithName("GetConsents");

        protectedPolicies.MapPut("/consents", UpdateConsentEndpoint.HandleAsync)
            .WithName("UpdateConsent");

        return app;
    }
}
