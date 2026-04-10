using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public interface IRecurringPatternService
{
    Task<RecurringPatternResult> EvaluateAsync(
        Transaction candidate,
        IReadOnlyList<Transaction> historicalTransactions,
        RecurringPatternOptions options,
        CancellationToken ct);
}

