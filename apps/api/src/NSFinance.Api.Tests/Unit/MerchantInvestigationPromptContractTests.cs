using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class MerchantInvestigationPromptContractTests
{
    // The parser enforces exact enum spellings; the prompt must therefore
    // teach every allowed value. A live model cannot guess internal enum
    // names - omitting these lines caused 100% parse rejection in production.
    [Fact]
    public void Prompt_EnumeratesEveryEnumValue_TheParserEnforces()
    {
        var prompt = new MerchantInvestigationPromptBuilder()
            .BuildPrompt(new MerchantInvestigationPromptInput(
                RawDescriptor: "CURRYS SWORDS",
                NormalizedDescriptor: "CURRYS SWORDS",
                TriggerSource: "unit",
                CorrelationId: "unit-correlation"))
            .Messages
            .Single()
            .Content;

        string[] requiredEnumValues =
        [
            "\"Merchant\"", "\"Institution\"", "\"Marketplace\"", "\"Government\"", "\"Utility\"", "\"Insurer\"", "\"Unknown\"",
            "\"NarrowUse\"", "\"MixedUse\"", "\"Intermediary\"",
            "\"AI\"", "\"Deterministic\"", "\"TransactionObservation\"", "\"OfficialSource\"", "\"Manual\"",
            "\"OfficialDomain\"", "\"AuthoritativeListing\"", "\"PublicDirectory\"", "\"WeakWebMention\"", "\"AIInferenceOnly\"", "\"NoSource\"",
            "\"BillingDescriptor\"", "\"MerchantName\"", "\"Domain\"", "\"Abbreviation\"", "\"ProcessorDescriptor\""
        ];

        Assert.All(requiredEnumValues, value => Assert.Contains(value, prompt));
        Assert.Contains("unknown properties cause outright rejection", prompt);
    }
}
