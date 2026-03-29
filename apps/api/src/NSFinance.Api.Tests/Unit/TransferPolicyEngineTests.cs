using NSFinance.Api.Modules.Transactions.TransferPolicy;
using NSFinance.Api.Persistence.Entities;
using Xunit;

namespace NSFinance.Api.Tests.Unit;

public class TransferPolicyEngineTests
{
    [Fact]
    public void LinkedBankAccountTransfer_IsGloballyNeutralized()
    {
        var evaluation = TransferPolicyEngine.Evaluate(
            taxonomyDomainId: 920,
            taxonomyCategoryId: 92010,
            taxonomySubcategoryId: 920101,
            transferKind: TransactionTransferKind.LinkedInternal,
            linkedTransferTransactionId: Guid.NewGuid(),
            amount: -100m);

        Assert.True(evaluation.IsGloballyNeutralized);
        Assert.False(evaluation.CountsTowardExpense);
        Assert.False(evaluation.CountsTowardIncome);
        Assert.True(evaluation.IsVerifiedLinkedTransfer);
    }

    [Fact]
    public void ManualBankAccountTransfer_AllowsExplicitNeutralization()
    {
        var evaluation = TransferPolicyEngine.Evaluate(
            taxonomyDomainId: 920,
            taxonomyCategoryId: 92010,
            taxonomySubcategoryId: 920101,
            transferKind: TransactionTransferKind.Manual,
            linkedTransferTransactionId: null,
            amount: -100m);

        Assert.True(evaluation.IsGloballyNeutralized);
        Assert.True(evaluation.IsManualUnverifiedTransfer);
        Assert.True(evaluation.AllowsManualNeutralization);
    }

    [Fact]
    public void CashWithdrawal_IsNotNeutralizedAndCountsAsExpense()
    {
        var evaluation = TransferPolicyEngine.Evaluate(
            taxonomyDomainId: 920,
            taxonomyCategoryId: 92030,
            taxonomySubcategoryId: 920301,
            transferKind: TransactionTransferKind.Manual,
            linkedTransferTransactionId: null,
            amount: -45m);

        Assert.False(evaluation.IsGloballyNeutralized);
        Assert.True(evaluation.CountsTowardExpense);
        Assert.Equal(TransferReportingBucket.CashOut, evaluation.ReportingBucket);
    }

    [Fact]
    public void CashDeposit_DoesNotCountTowardIncomeByDefault()
    {
        var evaluation = TransferPolicyEngine.Evaluate(
            taxonomyDomainId: 920,
            taxonomyCategoryId: 92030,
            taxonomySubcategoryId: 920302,
            transferKind: TransactionTransferKind.Manual,
            linkedTransferTransactionId: null,
            amount: 250m);

        Assert.False(evaluation.IsGloballyNeutralized);
        Assert.False(evaluation.CountsTowardIncome);
        Assert.Equal(TransferReportingBucket.CashIn, evaluation.ReportingBucket);
    }

    [Fact]
    public void CreditCardPayment_IsNeutralizedOnlyWhenLinkedVerified()
    {
        var unverified = TransferPolicyEngine.Evaluate(
            taxonomyDomainId: 920,
            taxonomyCategoryId: 92020,
            taxonomySubcategoryId: 920201,
            transferKind: TransactionTransferKind.Manual,
            linkedTransferTransactionId: null,
            amount: -120m);
        var verified = TransferPolicyEngine.Evaluate(
            taxonomyDomainId: 920,
            taxonomyCategoryId: 92020,
            taxonomySubcategoryId: 920201,
            transferKind: TransactionTransferKind.LinkedInternal,
            linkedTransferTransactionId: Guid.NewGuid(),
            amount: -120m);

        Assert.False(unverified.IsGloballyNeutralized);
        Assert.True(unverified.CountsTowardExpense);
        Assert.True(verified.IsGloballyNeutralized);
        Assert.False(verified.CountsTowardExpense);
    }

    [Fact]
    public void OtherInternalMoneyMovement_IsNotAutoNeutralizedWhenManual()
    {
        var evaluation = TransferPolicyEngine.Evaluate(
            taxonomyDomainId: 920,
            taxonomyCategoryId: 92040,
            taxonomySubcategoryId: 920403,
            transferKind: TransactionTransferKind.Manual,
            linkedTransferTransactionId: null,
            amount: -80m);

        Assert.False(evaluation.IsGloballyNeutralized);
        Assert.True(evaluation.CountsTowardExpense);
        Assert.False(evaluation.AllowsManualNeutralization);
    }
}
