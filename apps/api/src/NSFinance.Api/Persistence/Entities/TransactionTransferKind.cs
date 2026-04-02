namespace NSFinance.Api.Persistence.Entities;

public enum TransactionTransferKind
{
    Manual = 1,
    LinkedInternal = 2,
    SavingsRoundup = 3,
    SavingsManualDeposit = 4,
    SavingsManualWithdrawal = 5
}
