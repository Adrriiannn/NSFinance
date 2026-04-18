namespace NSFinance.Api.Modules.AI.Services;

public interface IUserFinancialProfileInferencePersistencePolicy
{
    bool CanPersist(UserFinancialProfileInferredSignalCandidate candidate);
    bool CanReplace(UserFinancialProfileSignal existingSignal, UserFinancialProfileInferredSignalCandidate candidate);
}

public sealed class UserFinancialProfileInferencePersistencePolicy : IUserFinancialProfileInferencePersistencePolicy
{
    public bool CanPersist(UserFinancialProfileInferredSignalCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Value))
        {
            return false;
        }

        return ToTier(candidate.Strength) >= UserFinancialProfileInferenceStrengthTier.Moderate;
    }

    public bool CanReplace(UserFinancialProfileSignal existingSignal, UserFinancialProfileInferredSignalCandidate candidate)
    {
        if (existingSignal.Metadata.IsExplicit)
        {
            return false;
        }

        var incomingTier = ToTier(candidate.Strength);
        if (incomingTier < UserFinancialProfileInferenceStrengthTier.Moderate)
        {
            return false;
        }

        var existingTier = ToTier(existingSignal.Metadata.Strength);
        if (incomingTier < existingTier)
        {
            return false;
        }

        return incomingTier > existingTier
               || !string.Equals(existingSignal.Value, candidate.Value, StringComparison.Ordinal);
    }

    private static UserFinancialProfileInferenceStrengthTier ToTier(UserFinancialProfileSignalStrength strength)
    {
        return strength switch
        {
            UserFinancialProfileSignalStrength.Strong => UserFinancialProfileInferenceStrengthTier.Strong,
            UserFinancialProfileSignalStrength.Acceptable => UserFinancialProfileInferenceStrengthTier.Moderate,
            UserFinancialProfileSignalStrength.Explicit => UserFinancialProfileInferenceStrengthTier.Strong,
            _ => UserFinancialProfileInferenceStrengthTier.Weak
        };
    }
}
