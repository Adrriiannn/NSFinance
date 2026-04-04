using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Modules.Transactions.Services;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public class TransactionSemanticResolverTests
{
    [Fact]
    public void ResolveDisplaySemantic_DeterministicSavingsWithoutRoundupReason_UsesManualMoveVariant()
    {
        var semantic = TransactionSemanticResolver.ResolveDisplaySemantic(
            DeterministicClassificationStatus.ClassifiedMatchedRule,
            deterministicRelationshipType: "savings_transfer",
            deterministicReasonCode: DeterministicClassificationReasonCodes.SavingsProviderStructuralSignal);

        Assert.Equal("savings_manual_move", semantic);
    }

    [Fact]
    public void ResolveDisplaySemantic_DeterministicSavingsRepeatedPattern_UsesManualMoveVariant()
    {
        var semantic = TransactionSemanticResolver.ResolveDisplaySemantic(
            DeterministicClassificationStatus.ClassifiedMatchedRule,
            deterministicRelationshipType: "savings_transfer",
            deterministicReasonCode: DeterministicClassificationReasonCodes.SavingsRepeatedAuxiliaryPattern);

        Assert.Equal("savings_manual_move", semantic);
    }

    [Fact]
    public void ResolveDisplaySemantic_DeterministicSavingsWithContextReason_UsesRoundupVariant()
    {
        var semantic = TransactionSemanticResolver.ResolveDisplaySemantic(
            DeterministicClassificationStatus.ClassifiedMatchedRule,
            deterministicRelationshipType: "savings_transfer",
            deterministicReasonCode: DeterministicClassificationReasonCodes.SavingsContextNearbySpend);

        Assert.Equal("savings_roundup", semantic);
    }

    [Fact]
    public void ResolveDisplaySemantic_DeterministicInternalTransfer_UsesTransferVariant()
    {
        var semantic = TransactionSemanticResolver.ResolveDisplaySemantic(
            DeterministicClassificationStatus.ClassifiedMatchedRule,
            deterministicRelationshipType: "internal_transfer",
            deterministicReasonCode: DeterministicClassificationReasonCodes.TransferPairStrictMatch);

        Assert.Equal("internal_transfer", semantic);
    }
}
