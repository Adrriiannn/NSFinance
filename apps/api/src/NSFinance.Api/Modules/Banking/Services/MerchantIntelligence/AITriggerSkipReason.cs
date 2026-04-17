namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public enum AITriggerSkipReason
{
    DeterministicTerminal = 0,
    RegistryResolved = 1,
    DomainPolicyDisallowsAI = 2,
    DescriptorNotMerchantLike = 3,
    MerchantRecentlyInvestigated = 4,
    MerchantOnCooldown = 5,
    RunBudgetExceeded = 6,
    DailyBudgetExceeded = 7,
    DuplicateMerchantInRun = 8,
    ExpectedValueTooLow = 9,
    ManualOverridePresent = 10,
    UserConfirmationPreferred = 11
}

