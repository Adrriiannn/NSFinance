using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Transactions.TransferPolicy;

public static class TransferPolicyEngine
{
    private const int TransferDomainId = ExpenseTaxonomyService.TransferDomainId;

    private const int InternalTransfersCategoryId = 92010;
    private const int LiabilityTransfersCategoryId = 92020;
    private const int CashMovementCategoryId = 92030;
    private const int OtherTransfersCategoryId = 92040;

    private const int BankAccountTransferSubcategoryId = 920101;
    private const int SavingsTransferSubcategoryId = 920102;
    private const int InvestmentTransferSubcategoryId = 920103;
    private const int WalletTransferSubcategoryId = 920104;

    private const int CreditCardPaymentTransferSubcategoryId = 920201;
    private const int LoanAccountTransferSubcategoryId = 920202;
    private const int DebtConsolidationTransferSubcategoryId = 920203;

    private const int CashWithdrawalSubcategoryId = 920301;
    private const int CashDepositSubcategoryId = 920302;
    private const int AtmWithdrawalTransferSubcategoryId = 920303;

    private const int BrokerageFundingTransferSubcategoryId = 920401;
    private const int CurrencyTransferSubcategoryId = 920402;
    private const int OtherInternalMoneyMovementSubcategoryId = 920403;

    public static TransferPolicyEvaluation Evaluate(
        int? taxonomyDomainId,
        int? taxonomyCategoryId,
        int? taxonomySubcategoryId,
        TransactionTransferKind? transferKind,
        Guid? linkedTransferTransactionId,
        decimal amount)
    {
        var policyKind = ResolvePolicyKind(taxonomyDomainId, taxonomyCategoryId, taxonomySubcategoryId, transferKind);
        var isTransferLike = policyKind != TransferPolicyKind.None;
        var isDerivedSavingsMovement = transferKind is
            TransactionTransferKind.SavingsRoundup
            or TransactionTransferKind.SavingsManualDeposit
            or TransactionTransferKind.SavingsManualWithdrawal;
        var isManualUnverifiedTransfer = transferKind == TransactionTransferKind.Manual;
        var isVerifiedLinkedTransfer = transferKind == TransactionTransferKind.LinkedInternal
            && linkedTransferTransactionId.HasValue;
        var signedDirection = amount < 0m ? TransferSignedDirection.Outflow : amount > 0m ? TransferSignedDirection.Inflow : TransferSignedDirection.None;

        if (!isTransferLike)
        {
            return CreateEvaluation(
                policyKind,
                reportingBucket: signedDirection == TransferSignedDirection.Outflow ? TransferReportingBucket.Spending : TransferReportingBucket.Income,
                countsTowardExpense: signedDirection == TransferSignedDirection.Outflow,
                countsTowardIncome: signedDirection == TransferSignedDirection.Inflow,
                isGloballyNeutralized: false,
                isVerifiedLinkedTransfer: false,
                isManualUnverifiedTransfer: false,
                allowsAutoLinkedMatching: false,
                allowsManualNeutralization: false,
                requiresLinkedProofForNeutralization: false);
        }

        var allowsAutoLinkedMatching = IsAutoLinkedMatchPolicyEligible(policyKind);
        var allowsManualNeutralization = AllowsManualNeutralization(policyKind);
        var allowsVerifiedLinkedNeutralization = AllowsVerifiedLinkedNeutralization(policyKind);
        var requiresLinkedProofForNeutralization = !allowsManualNeutralization && allowsVerifiedLinkedNeutralization;

        var isGloballyNeutralized = isDerivedSavingsMovement
            || (allowsVerifiedLinkedNeutralization && isVerifiedLinkedTransfer);
        if (!isGloballyNeutralized && allowsManualNeutralization && isManualUnverifiedTransfer)
        {
            isGloballyNeutralized = true;
        }

        if (isGloballyNeutralized)
        {
            return CreateEvaluation(
                policyKind,
                reportingBucket: ResolveNeutralizedBucket(policyKind),
                countsTowardExpense: false,
                countsTowardIncome: false,
                isGloballyNeutralized: true,
                isVerifiedLinkedTransfer: isVerifiedLinkedTransfer,
                isManualUnverifiedTransfer: isManualUnverifiedTransfer,
                allowsAutoLinkedMatching: allowsAutoLinkedMatching,
                allowsManualNeutralization: allowsManualNeutralization,
                requiresLinkedProofForNeutralization: requiresLinkedProofForNeutralization);
        }

        var reportingBucket = ResolveNonNeutralBucket(policyKind, signedDirection);

        var countsTowardExpense = signedDirection == TransferSignedDirection.Outflow;
        var countsTowardIncome = signedDirection == TransferSignedDirection.Inflow;

        if (policyKind == TransferPolicyKind.CashDeposit)
        {
            // Cash deposits are not treated as ordinary income by default.
            countsTowardIncome = false;
        }

        return CreateEvaluation(
            policyKind,
            reportingBucket,
            countsTowardExpense,
            countsTowardIncome,
            isGloballyNeutralized: false,
            isVerifiedLinkedTransfer: isVerifiedLinkedTransfer,
            isManualUnverifiedTransfer: isManualUnverifiedTransfer,
            allowsAutoLinkedMatching: allowsAutoLinkedMatching,
            allowsManualNeutralization: allowsManualNeutralization,
            requiresLinkedProofForNeutralization: requiresLinkedProofForNeutralization);
    }

    public static bool IsAutoLinkedMatchPolicyEligible(
        int? taxonomyDomainId,
        int? taxonomyCategoryId,
        int? taxonomySubcategoryId,
        TransactionTransferKind? transferKind)
    {
        var policyKind = ResolvePolicyKind(taxonomyDomainId, taxonomyCategoryId, taxonomySubcategoryId, transferKind);
        return IsAutoLinkedMatchPolicyEligible(policyKind);
    }

    private static bool IsAutoLinkedMatchPolicyEligible(TransferPolicyKind policyKind)
    {
        return policyKind switch
        {
            TransferPolicyKind.None => true,
            TransferPolicyKind.InternalTransferGeneric => true,
            TransferPolicyKind.BankAccountTransfer => true,
            TransferPolicyKind.SavingsTransfer => true,
            TransferPolicyKind.InvestmentTransfer => true,
            TransferPolicyKind.WalletTransfer => true,
            TransferPolicyKind.CreditCardPaymentTransfer => true,
            TransferPolicyKind.CurrencyTransfer => true,
            _ => false
        };
    }

    private static bool AllowsManualNeutralization(TransferPolicyKind policyKind)
    {
        return policyKind is
            TransferPolicyKind.InternalTransferGeneric
            or TransferPolicyKind.BankAccountTransfer
            or TransferPolicyKind.SavingsTransfer
            or TransferPolicyKind.WalletTransfer
            or TransferPolicyKind.CurrencyTransfer;
    }

    private static bool AllowsVerifiedLinkedNeutralization(TransferPolicyKind policyKind)
    {
        return policyKind is not
            TransferPolicyKind.None
            and not TransferPolicyKind.CashMovementGeneric
            and not TransferPolicyKind.CashWithdrawal
            and not TransferPolicyKind.CashDeposit
            and not TransferPolicyKind.AtmWithdrawalTransfer
            and not TransferPolicyKind.LoanAccountTransfer
            and not TransferPolicyKind.DebtConsolidationTransfer
            and not TransferPolicyKind.OtherInternalMoneyMovement
            and not TransferPolicyKind.OtherTransferGeneric;
    }

    private static TransferReportingBucket ResolveNeutralizedBucket(TransferPolicyKind policyKind)
    {
        return policyKind switch
        {
            TransferPolicyKind.CreditCardPaymentTransfer => TransferReportingBucket.DebtPayment,
            TransferPolicyKind.SavingsTransfer => TransferReportingBucket.SavingsAllocation,
            TransferPolicyKind.InvestmentTransfer or TransferPolicyKind.BrokerageFundingTransfer => TransferReportingBucket.InvestmentAllocation,
            _ => TransferReportingBucket.InternalTransfer
        };
    }

    private static TransferReportingBucket ResolveNonNeutralBucket(TransferPolicyKind policyKind, TransferSignedDirection signedDirection)
    {
        return policyKind switch
        {
            TransferPolicyKind.CashWithdrawal or TransferPolicyKind.AtmWithdrawalTransfer => TransferReportingBucket.CashOut,
            TransferPolicyKind.CashDeposit => TransferReportingBucket.CashIn,
            TransferPolicyKind.CreditCardPaymentTransfer
                or TransferPolicyKind.LoanAccountTransfer
                or TransferPolicyKind.DebtConsolidationTransfer
                or TransferPolicyKind.LiabilityTransferGeneric => TransferReportingBucket.DebtPayment,
            TransferPolicyKind.SavingsTransfer => TransferReportingBucket.SavingsAllocation,
            TransferPolicyKind.InvestmentTransfer or TransferPolicyKind.BrokerageFundingTransfer => TransferReportingBucket.InvestmentAllocation,
            TransferPolicyKind.CurrencyTransfer => TransferReportingBucket.InternalTransfer,
            TransferPolicyKind.InternalTransferGeneric
                or TransferPolicyKind.BankAccountTransfer
                or TransferPolicyKind.WalletTransfer => TransferReportingBucket.InternalTransfer,
            _ => signedDirection == TransferSignedDirection.Outflow ? TransferReportingBucket.Spending : TransferReportingBucket.Income
        };
    }

    private static TransferPolicyEvaluation CreateEvaluation(
        TransferPolicyKind policyKind,
        TransferReportingBucket reportingBucket,
        bool countsTowardExpense,
        bool countsTowardIncome,
        bool isGloballyNeutralized,
        bool isVerifiedLinkedTransfer,
        bool isManualUnverifiedTransfer,
        bool allowsAutoLinkedMatching,
        bool allowsManualNeutralization,
        bool requiresLinkedProofForNeutralization)
    {
        return new TransferPolicyEvaluation(
            policyKind,
            reportingBucket,
            IsTransferTransaction: policyKind != TransferPolicyKind.None,
            countsTowardExpense,
            countsTowardIncome,
            isGloballyNeutralized,
            isVerifiedLinkedTransfer,
            isManualUnverifiedTransfer,
            allowsAutoLinkedMatching,
            allowsManualNeutralization,
            requiresLinkedProofForNeutralization);
    }

    private static TransferPolicyKind ResolvePolicyKind(
        int? taxonomyDomainId,
        int? taxonomyCategoryId,
        int? taxonomySubcategoryId,
        TransactionTransferKind? transferKind)
    {
        if (taxonomySubcategoryId.HasValue)
        {
            return taxonomySubcategoryId.Value switch
            {
                BankAccountTransferSubcategoryId => TransferPolicyKind.BankAccountTransfer,
                SavingsTransferSubcategoryId => TransferPolicyKind.SavingsTransfer,
                InvestmentTransferSubcategoryId => TransferPolicyKind.InvestmentTransfer,
                WalletTransferSubcategoryId => TransferPolicyKind.WalletTransfer,
                CreditCardPaymentTransferSubcategoryId => TransferPolicyKind.CreditCardPaymentTransfer,
                LoanAccountTransferSubcategoryId => TransferPolicyKind.LoanAccountTransfer,
                DebtConsolidationTransferSubcategoryId => TransferPolicyKind.DebtConsolidationTransfer,
                CashWithdrawalSubcategoryId => TransferPolicyKind.CashWithdrawal,
                CashDepositSubcategoryId => TransferPolicyKind.CashDeposit,
                AtmWithdrawalTransferSubcategoryId => TransferPolicyKind.AtmWithdrawalTransfer,
                BrokerageFundingTransferSubcategoryId => TransferPolicyKind.BrokerageFundingTransfer,
                CurrencyTransferSubcategoryId => TransferPolicyKind.CurrencyTransfer,
                OtherInternalMoneyMovementSubcategoryId => TransferPolicyKind.OtherInternalMoneyMovement,
                _ => TransferPolicyKind.None
            };
        }

        if (taxonomyCategoryId.HasValue)
        {
            return taxonomyCategoryId.Value switch
            {
                InternalTransfersCategoryId => TransferPolicyKind.InternalTransferGeneric,
                LiabilityTransfersCategoryId => TransferPolicyKind.LiabilityTransferGeneric,
                CashMovementCategoryId => TransferPolicyKind.CashMovementGeneric,
                OtherTransfersCategoryId => TransferPolicyKind.OtherTransferGeneric,
                _ => TransferPolicyKind.None
            };
        }

        if (transferKind is
            TransactionTransferKind.SavingsRoundup
            or TransactionTransferKind.SavingsManualDeposit
            or TransactionTransferKind.SavingsManualWithdrawal)
        {
            return TransferPolicyKind.SavingsTransfer;
        }

        if (taxonomyDomainId == TransferDomainId || transferKind is TransactionTransferKind.Manual or TransactionTransferKind.LinkedInternal)
        {
            return TransferPolicyKind.InternalTransferGeneric;
        }

        return TransferPolicyKind.None;
    }
}

public sealed record TransferPolicyEvaluation(
    TransferPolicyKind PolicyKind,
    TransferReportingBucket ReportingBucket,
    bool IsTransferTransaction,
    bool CountsTowardExpense,
    bool CountsTowardIncome,
    bool IsGloballyNeutralized,
    bool IsVerifiedLinkedTransfer,
    bool IsManualUnverifiedTransfer,
    bool AllowsAutoLinkedMatching,
    bool AllowsManualNeutralization,
    bool RequiresLinkedProofForNeutralization);

public enum TransferPolicyKind
{
    None = 0,
    InternalTransferGeneric = 1,
    BankAccountTransfer = 2,
    SavingsTransfer = 3,
    InvestmentTransfer = 4,
    WalletTransfer = 5,
    CreditCardPaymentTransfer = 6,
    LoanAccountTransfer = 7,
    DebtConsolidationTransfer = 8,
    CashMovementGeneric = 9,
    CashWithdrawal = 10,
    CashDeposit = 11,
    AtmWithdrawalTransfer = 12,
    LiabilityTransferGeneric = 13,
    BrokerageFundingTransfer = 14,
    CurrencyTransfer = 15,
    OtherInternalMoneyMovement = 16,
    OtherTransferGeneric = 17
}

public enum TransferReportingBucket
{
    Spending = 0,
    Income = 1,
    InternalTransfer = 2,
    SavingsAllocation = 3,
    InvestmentAllocation = 4,
    DebtPayment = 5,
    CashOut = 6,
    CashIn = 7
}

public enum TransferSignedDirection
{
    None = 0,
    Outflow = 1,
    Inflow = 2
}
