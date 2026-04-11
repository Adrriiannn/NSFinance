namespace NSFinance.Api.Modules.AI.Services;

public enum AIProviderKind
{
    Mock = 0,
    AzureOpenAI = 1
}

public enum AITaskType
{
    MerchantInvestigation = 0,
    UserChatSimple = 1,
    UserChatComplex = 2,
    FinancialReasoning = 3,
    Other = 4
}

public enum AIModelClass
{
    Fast = 0,
    HeavyReasoning = 1,
    Any = 2
}

public enum AIMessageRole
{
    System = 0,
    Developer = 1,
    User = 2,
    Assistant = 3,
    Tool = 4
}

public enum HeavyModelFallbackPolicy
{
    FailFast = 0,
    UseFastModel = 1
}

public enum UserChatComplexity
{
    Simple = 0,
    Complex = 1
}

public enum MockAIScenario
{
    MerchantStrongCandidate = 0,
    MerchantAmbiguousCandidates = 1,
    MerchantInsufficientEvidence = 2,
    MerchantConflictingCandidates = 3,
    MerchantMalformedOutput = 4,
    MerchantDangerousAliasProposal = 5,
    MerchantNarrowUseWeakOfficial = 6,
    MerchantIntermediaryMarketplace = 7,
    UserChatSimple = 8,
    UserChatComplex = 9
}
