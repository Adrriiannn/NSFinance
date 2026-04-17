using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionContextShaperTests
{
    [Fact]
    public void ShapeTransactionMatches_RespectsRowCapAndEmitsTrimIndicator()
    {
        var sut = new CompanionContextShaper(Options.Create(new CompanionOrchestrationOptions
        {
            MaxTransactionRows = 4
        }));
        var transactions = new TransactionQueryResult(
            Enumerable.Range(0, 12)
                .Select(i => new TransactionQueryItem(DateTime.UtcNow.AddDays(-i), i + 10m, "EUR", $"Transaction {i}", 130, 130100))
                .ToArray());

        var shaped = sut.ShapeTransactionMatches(transactions);

        var output = Assert.IsType<CompanionTransactionMatchesContext>(shaped.Output);
        Assert.Equal(12, output.TotalItemCount);
        Assert.Equal(4, output.Items.Count);
        Assert.Contains(shaped.TrimIndicators, indicator => indicator.Contains("payload_trimmed:transaction_matches_rows", StringComparison.Ordinal));
    }

    [Fact]
    public void TrimToPayloadBudget_RemovesOptionalPayloadsDeterministically()
    {
        var sut = new CompanionContextShaper(Options.Create(new CompanionOrchestrationOptions
        {
            MaxSerializedContextChars = 450
        }));
        var budgetPlanned = new CompanionPlannedTool(CompanionTool.BudgetStatus, true, 20, "required", [FinancialCompanionIntent.BudgetStatus]);
        var spendPlanned = new CompanionPlannedTool(CompanionTool.SpendingAnalysis, false, 30, "optional", [FinancialCompanionIntent.BudgetStatus]);
        var txPlanned = new CompanionPlannedTool(CompanionTool.TransactionQuery, false, 40, "optional", [FinancialCompanionIntent.BudgetStatus]);

        var outputs = new Dictionary<string, object?>
        {
            [CompanionTool.BudgetStatus.ToOutputKey()] = new CompanionBudgetStatusContext(true, 2000m, 900m, 1100m),
            [CompanionTool.SpendingAnalysis.ToOutputKey()] = new CompanionSpendingAnalysisContext(
                [new CompanionDomainSpendContextItem(130, 800m), new CompanionDomainSpendContextItem(220, 300m)],
                2,
                35m,
                260m),
            [CompanionTool.TransactionQuery.ToOutputKey()] = new CompanionTransactionMatchesContext(
                10,
                Enumerable.Range(0, 8)
                    .Select(i => new CompanionTransactionMatchContext(DateTime.UtcNow.AddDays(-i), 20 + i, "EUR", $"Large text {i} {new string('x', 40)}", 130, 130100))
                    .ToArray())
        };
        var records = new[]
        {
            new CompanionToolExecutionRecord(budgetPlanned, CompanionToolExecutionStatus.Success, CompanionTool.BudgetStatus.ToContractName(), CompanionTool.BudgetStatus.ToOutputKey(), outputs[CompanionTool.BudgetStatus.ToOutputKey()], null, [], true),
            new CompanionToolExecutionRecord(spendPlanned, CompanionToolExecutionStatus.Success, CompanionTool.SpendingAnalysis.ToContractName(), CompanionTool.SpendingAnalysis.ToOutputKey(), outputs[CompanionTool.SpendingAnalysis.ToOutputKey()], null, [], true),
            new CompanionToolExecutionRecord(txPlanned, CompanionToolExecutionStatus.Success, CompanionTool.TransactionQuery.ToContractName(), CompanionTool.TransactionQuery.ToOutputKey(), outputs[CompanionTool.TransactionQuery.ToOutputKey()], null, [], true)
        };

        var trimmed = sut.TrimToPayloadBudget(outputs, records);

        Assert.Contains(trimmed.Warnings, warning => warning == "context_payload_trimmed");
        Assert.NotEmpty(trimmed.TrimmedIndicators);
        Assert.Contains(CompanionTool.BudgetStatus.ToOutputKey(), trimmed.Outputs.Keys);
        Assert.Contains(trimmed.AdjustedRecords, record => record.Status == CompanionToolExecutionStatus.TrimmedOut);
    }
}
