using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class AzureOpenAIProviderTransportTests
{
    [Fact]
    public async Task Send_UsesMaxCompletionTokens_NeverMaxTokens()
    {
        var handler = new SequenceHandler([Ok("""{"choices":[{"message":{"content":"{\"ok\":true}"},"finish_reason":"stop"}]}""")]);
        var transport = CreateTransport(handler);

        var response = await transport.SendAsync(Request(), Route(), CancellationToken.None);

        Assert.True(response.Succeeded);
        var body = Assert.Single(handler.CapturedBodies);
        Assert.Contains("\"max_completion_tokens\":400", body);
        Assert.DoesNotContain("\"max_tokens\"", body);
    }

    [Fact]
    public async Task Send_RetriesOnceWithoutTemperature_WhenDeploymentRejectsIt()
    {
        // First call: the reasoning-class rejection observed live in
        // production. Second call must omit temperature and succeed.
        var handler = new SequenceHandler(
        [
            BadRequest("""{"error":{"message":"Unsupported value: 'temperature' does not support 0.1 with this model.","param":"temperature","code":"unsupported_value"}}"""),
            Ok("""{"choices":[{"message":{"content":"{\"ok\":true}"},"finish_reason":"stop"}]}""")
        ]);
        var transport = CreateTransport(handler);

        var response = await transport.SendAsync(Request(), Route(), CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal(2, handler.CapturedBodies.Count);
        Assert.Contains("\"temperature\":0.1", handler.CapturedBodies[0]);
        Assert.DoesNotContain("\"temperature\"", handler.CapturedBodies[1]);
    }

    [Fact]
    public async Task Send_DoesNotRetry_ForUnrelatedBadRequests()
    {
        var handler = new SequenceHandler(
        [
            BadRequest("""{"error":{"message":"Invalid request.","param":"messages","code":"invalid_value"}}""")
        ]);
        var transport = CreateTransport(handler);

        var response = await transport.SendAsync(Request(), Route(), CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("Azure OpenAI HTTP 400", response.FailureReason);
        Assert.Single(handler.CapturedBodies);
    }

    private static AzureOpenAIProviderTransport CreateTransport(SequenceHandler handler)
    {
        var options = Options.Create(new AIIntegrationOptions
        {
            AzureOpenAI = new AzureOpenAIOptions
            {
                Enabled = true,
                Endpoint = "https://unit.test.openai.azure.com",
                ApiKey = "unit-test-key",
                ApiVersion = "2024-10-21"
            }
        });

        return new AzureOpenAIProviderTransport(
            new StubHttpClientFactory(handler),
            options,
            new AzureOpenAIApiKeyAuthStrategy(options),
            new AzureOpenAIManagedIdentityAuthStrategy(NullLogger<AzureOpenAIManagedIdentityAuthStrategy>.Instance),
            NullLogger<AzureOpenAIProviderTransport>.Instance);
    }

    private static AIRequest Request()
    {
        return AIRequest.Create(
            taskType: AITaskType.MerchantInvestigation,
            preferredModelClass: AIModelClass.HeavyReasoning,
            messages: [AIMessage.User("Investigate.")],
            correlationId: "unit-correlation",
            systemInstructions: "Be strict.",
            structuredOutputSchemaName: "merchant_investigation_v1",
            temperature: 0.1d,
            maxOutputTokens: 400);
    }

    private static AIModelRoute Route()
    {
        return new AIModelRoute(
            AITaskType.MerchantInvestigation,
            AIModelClass.HeavyReasoning,
            "gpt-5-chat",
            "gpt-5-chat",
            false,
            "resolved",
            []);
    }

    private static HttpResponseMessage Ok(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
    }

    private static HttpResponseMessage BadRequest(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(body)
        };
    }

    private sealed class SequenceHandler(IReadOnlyList<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private int _index;

        public List<string> CapturedBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var response = responses[Math.Min(_index, responses.Count - 1)];
            _index += 1;
            return response;
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
