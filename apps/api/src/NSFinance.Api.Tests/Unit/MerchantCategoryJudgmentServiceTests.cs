using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

// Two-stage hierarchical judgment: stage one selects a taxonomy category,
// stage two selects a definition from inside that category only.
public sealed class MerchantCategoryJudgmentServiceTests
{
    private const string SelectGroceries = """
        {"decision":"select","categoryId":13010,"confidence":0.9,"rationale":"Grocery retailer.","abstainReason":null}
        """;

    [Fact]
    public async Task Judge_TwoStages_ParsesValidAssignment()
    {
        var client = new SequencedAIClient(SelectGroceries, """
            {"decision":"assign","definitionKey":"cat:13010","confidence":0.88,"rationale":"Grocery retailer, outflow.","abstainReason":null}
            """);
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.True(judgment.Assigned);
        Assert.Equal("cat:13010", judgment.DefinitionKey);
        // The chained confidence is the weaker stage's: min(0.9, 0.88).
        Assert.Equal(0.88, judgment.Confidence);
        Assert.Contains("category 13010", judgment.Rationale);
        Assert.Null(judgment.AbstainReason);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task Judge_StageOneWeaker_CapsFinalConfidence()
    {
        var client = new SequencedAIClient("""
            {"decision":"select","categoryId":13010,"confidence":0.6,"rationale":"Probably groceries.","abstainReason":null}
            """, """
            {"decision":"assign","definitionKey":"cat:13010","confidence":0.95,"rationale":"Clear grocer.","abstainReason":null}
            """);
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.True(judgment.Assigned);
        Assert.Equal(0.6, judgment.Confidence);
    }

    [Fact]
    public async Task Judge_StageOneAbstention_SkipsStageTwo()
    {
        var client = new SequencedAIClient("""
            {"decision":"abstain","categoryId":null,"confidence":0.3,"rationale":"Could be several categories.","abstainReason":"mixed_use"}
            """);
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.False(judgment.Assigned);
        Assert.Equal("stage1_mixed_use", judgment.AbstainReason);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task Judge_UnknownCategoryId_BecomesAbstentionWithoutStageTwo()
    {
        var client = new SequencedAIClient("""
            {"decision":"select","categoryId":99999,"confidence":0.9,"rationale":"Made up.","abstainReason":null}
            """);
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.False(judgment.Assigned);
        Assert.Equal("unknown_category_id", judgment.AbstainReason);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task Judge_KeyOutsideSelectedCategory_BecomesAbstention()
    {
        // cat:12020 (Fuel) is a real eligible definition, but it does not
        // live inside the selected Groceries category - the hierarchy must
        // reject it even though a flat catalog lookup would accept it.
        var client = new SequencedAIClient(SelectGroceries, """
            {"decision":"assign","definitionKey":"cat:12020","confidence":0.9,"rationale":"Wrong branch.","abstainReason":null}
            """);
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.False(judgment.Assigned);
        Assert.Equal("unknown_definition_key", judgment.AbstainReason);
    }

    [Fact]
    public async Task Judge_StageTwoMalformedJson_BecomesAbstention()
    {
        var client = new SequencedAIClient(SelectGroceries, "this is not json");
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.False(judgment.Assigned);
        Assert.Equal("malformed_json", judgment.AbstainReason);
    }

    [Fact]
    public async Task Judge_StageOneMalformedJson_BecomesAbstention()
    {
        var client = new SequencedAIClient("not json either");
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.False(judgment.Assigned);
        Assert.Equal("stage1_malformed_json", judgment.AbstainReason);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task Judge_StageTwoExplicitAbstention_IsPreserved()
    {
        var client = new SequencedAIClient(SelectGroceries, """
            {"decision":"abstain","definitionKey":null,"confidence":0.4,"rationale":"Fits several definitions.","abstainReason":"mixed_use"}
            """);
        var judgment = await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        Assert.False(judgment.Assigned);
        Assert.Equal("mixed_use", judgment.AbstainReason);
        Assert.Equal(0.4, judgment.Confidence);
    }

    [Fact]
    public async Task Judge_StageOnePrompt_OffersOnlyAiAssignableCategories()
    {
        var client = new SequencedAIClient("""
            {"decision":"abstain","categoryId":null,"confidence":0,"rationale":"n/a","abstainReason":"n/a"}
            """);
        await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        var prompt = client.Requests[0].Messages.Single().Content;
        // Categories with AI-eligible definitions appear...
        Assert.Contains("13010", prompt);
        Assert.Contains("23010", prompt);
        // ...but the transfer categories, whose every definition is
        // deterministic-only, must never be offered to the AI.
        Assert.DoesNotContain("92010", prompt);
        Assert.DoesNotContain("92020", prompt);
        // Stage one carries no definition keys at all.
        Assert.DoesNotContain("cat:13010", prompt);
    }

    [Fact]
    public async Task Judge_StageTwoPrompt_OffersOnlySelectedCategoryDefinitions()
    {
        var client = new SequencedAIClient(SelectGroceries, """
            {"decision":"abstain","definitionKey":null,"confidence":0,"rationale":"n/a","abstainReason":"n/a"}
            """);
        await CreateService(client).JudgeAsync(Input(), CancellationToken.None);

        var prompt = client.Requests[1].Messages.Single().Content;
        Assert.Contains("cat:13010", prompt);
        // A definition from a different category never enters stage two.
        Assert.DoesNotContain("cat:12020", prompt);
        // Deterministic-only definitions stay hidden at both stages.
        Assert.DoesNotContain("cat:92010", prompt);
    }

    private static MerchantCategoryJudgmentService CreateService(SequencedAIClient client)
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

    private sealed class SequencedAIClient(params string[] payloads) : IAIClient
    {
        public List<AIRequest> Requests { get; } = [];

        public Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var payload = Requests.Count <= payloads.Length
                ? payloads[Requests.Count - 1]
                : throw new InvalidOperationException("Unexpected extra AI call.");
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
