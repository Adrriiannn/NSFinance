namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public enum MerchantResolutionFinalState
{
    DeterministicTerminal = 0,
    RegistryResolvedTerminal = 1,
    AIResolvedTerminal = 2,
    AIEnrichedSuggestionOnly = 3,
    NeedsUserConfirmation = 4,
    Unresolved = 5
}

