namespace NSFinance.Api.Persistence.Entities;

public enum TransactionRelationshipType
{
    InternalAccountTransfer = 1,
    SavingsRoundup = 2,
    SavingsManualDeposit = 3,
    SavingsManualWithdrawal = 4,
    PossibleTransferSuggestion = 5,
    PossibleSavingsSuggestion = 6
}

