using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class MerchantCategoryJudgmentServiceTests
{
    [Fact]
    public async Task Judge_ParsesValidAssignment()
    {
        var client = new CannedAIClient("""
            {"decision":"assign","definitionKey":"cat:13010","confidence":0.88,"rationale":"Grocery retailer, outflow.","abstainReason":null}
            """);
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.True(judgment.Assigned);
        Assert.Equal("cat:13010", judgment.DefinitionKey);
        Assert.Equal(0.88, judgment.Confidence);
        Assert.Null(judgment.AbstainReason);
    }

    [Fact]
    public async Task Judge_UnknownDefinitionKey_BecomesAbstention()
    {
        var client = new CannedAIClient("""
            {"decision":"assign","definitionKey":"cat:99999","confidence":0.9,"rationale":"Made up.","abstainReason":null}
            """);
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.False(judgment.Assigned);
        Assert.Equal("unknown_definition_key", judgment.AbstainReason);
    }

    [Fact]
    public async Task Judge_MalformedJson_BecomesAbstention()
    {
        var client = new CannedAIClient("this is not json");
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.False(judgment.Assigned);
        Assert.Equal("malformed_json", judgment.AbstainReason);
    }

    [Fact]
    public async Task Judge_ExplicitAbstention_IsPreserved()
    {
        var client = new CannedAIClient("""
            {"decision":"abstain","definitionKey":null,"confidence":0.4,"rationale":"Fits several definitions.","abstainReason":"mixed_use"}
            """);
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.False(judgment.Assigned);
        Assert.Equal("mixed_use", judgment.AbstainReason);
        Assert.Equal(0.4, judgment.Confidence);
    }

    [Fact]
    public async Task Judge_PromptOffersOnlyFlooredDefinitions()
    {
        var client = new CannedAIClient("""
            {"decision":"abstain","definitionKey":null,"confidence":0,"rationale":"n/a","abstainReason":"n/a"}
            """);
        await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        var prompt = client.LastRequest!.Messages.Single().Content;
        Assert.Contains("cat:13010", prompt);
        // Internal Transfers is deterministic-only (null floor) and must
        // never be offered to the AI.
        Assert.DoesNotContain("cat:92010", prompt);
    }

    private static MerchantCategoryJudgmentService CreateService(CannedAIClient client)
    {
        return new MerchantCategoryJudgmentService(
            new FakeRouter(),
            client,
            NullLogger<MerchantCategoryJudgmentService>.Instance);
    }

    private static MerchantCategoryJudgmentInput Input()
    {
        return new MerchantCategoryJudgmentInput(
            NormalizedDescriptor: "NEWSHOP MAIN ST",
            CanonicalName: "NewShop",
            BusinessSummary: "A local shop.",
            OfficialWebsite: "https://newshop.ie",
            MerchantType: "Merchant",
            MerchantUsageType: "NarrowUse",
            PrimaryCountryCode: "IE",
            ObservedDirection: "outflow",
            ObservedOccurrences: 3,
            TypicalAbsAmount: 24.5m);
    }

    private sealed class FakeRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(AITaskType taskType, AIModelClass preferredModelClass, string? complexityHint = null)
        {
            return new AIModelRoute(taskType, preferredModelClass, "test-model", "test-deployment", false, "resolved", []);
        }
    }

    private sealed class CannedAIClient(string payload) : IAIClient
    {
        public AIRequest? LastRequest { get; private set; }

        public Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new AIResponse(
                Content: null,
                StructuredPayloadJson: payload,
                FinishReason: "stop",
                Provider: "test",
                Model: route.Model,
                Deployment: route.Deployment,
                InputTokenEstimate: null,
                OutputTokenEstimate: null,
                LatencyMs: 1,
                WasMocked: true,
                RawDiagnostics: null,
                Succeeded: true,
                FailureReason: null));
        }
    }
}
