using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Transactions.Services;

public static class TransactionSemanticResolver
{
    public static string ResolveDisplaySemantic(
        DeterministicClassificationStatus deterministicClassificationStatus,
        string? deterministicRelationshipType,
        string? deterministicReasonCode)
    {
        if (deterministicClassificationStatus != DeterministicClassificationStatus.ClassifiedMatchedRule)
        {
            return "real_transaction";
        }

        if (string.Equals(deterministicRelationshipType, "internal_transfer", StringComparison.Ordinal))
        {
            return "internal_transfer";
        }

        if (string.Equals(deterministicRelationshipType, "savings_transfer", StringComparison.Ordinal))
        {
            return string.Equals(
                deterministicReasonCode,
                DeterministicClassificationReasonCodes.SavingsContextNearbySpend,
                StringComparison.Ordinal)
                ? "savings_roundup"
                : "savings_manual_move";
        }

        return "real_transaction";
    }
}
