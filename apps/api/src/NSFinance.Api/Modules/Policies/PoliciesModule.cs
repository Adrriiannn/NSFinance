using NSFinance.Api.Modules.Policies.Endpoints;
using NSFinance.Api.Modules.Policies.Services;

namespace NSFinance.Api.Modules.Policies;

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

        legal.MapGet("/open-banking", (PolicyService service, CancellationToken ct) =>
            GetPolicyByTypeEndpoint.HandleAsync("open_banking_disclosure", service, ct))
            .WithName("GetOpenBankingDisclosure");

        legal.MapGet("/ai-disclosure", (PolicyService service, CancellationToken ct) =>
            GetPolicyByTypeEndpoint.HandleAsync("ai_disclosure", service, ct))
            .WithName("GetAiDisclosure");

        legal.MapGet("/data-rights", (PolicyService service, CancellationToken ct) =>
            GetPolicyByTypeEndpoint.HandleAsync("data_rights_gdpr_summary", service, ct))
            .WithName("GetDataRightsSummary");

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
