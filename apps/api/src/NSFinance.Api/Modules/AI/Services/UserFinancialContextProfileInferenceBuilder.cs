using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.AI.Services;

public interface IUserFinancialProfileInferenceBuilder
{
    Task<IReadOnlyList<UserFinancialProfileInferredSignalCandidate>> BuildCandidatesAsync(
        Guid userId,
        UserFinancialContextProfileData currentState,
        CancellationToken cancellationToken);
}

public sealed class UserFinancialProfileInferenceBuilder(
    AppDbContext dbContext,
    IUserFinancialSummaryService summaryService,
    IRecurringObligationsService recurringObligationsService,
    IBudgetStatusService budgetStatusService,
    ISpendingAnalysisService spendingAnalysisService,
    IOptions<CompanionProfileLifecycleOptions> options,
    ILogger<UserFinancialProfileInferenceBuilder> logger) : IUserFinancialProfileInferenceBuilder
{
    private static readonly JsonSerializerOptions InferencePayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly CompanionProfileLifecycleOptions _options = options.Value;

    public async Task<IReadOnlyList<UserFinancialProfileInferredSignalCandidate>> BuildCandidatesAsync(
        Guid userId,
        UserFinancialContextProfileData currentState,
        CancellationToken cancellationToken)
    {
        var candidates = new List<UserFinancialProfileInferredSignalCandidate>(8);
        UserFinancialSummary? summary = null;
        RecurringObligationsResult? recurring = null;
        BudgetStatusResult? budget = null;
        SpendingAnalysisResult? spending = null;

        try
        {
            summary = await summaryService.GetSummaryAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Financial summary unavailable for profile inference. userId={UserId}", userId);
        }

        try
        {
            recurring = await recurringObligationsService.GetRecurringAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Recurring obligations unavailable for profile inference. userId={UserId}", userId);
        }

        try
        {
            budget = await budgetStatusService.GetBudgetStatusAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Budget status unavailable for profile inference. userId={UserId}", userId);
        }

        try
        {
            spending = await spendingAnalysisService.AnalyzeAsync(
                userId,
                Math.Clamp(_options.SpendingAnalysisLookbackDays, 14, 180),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Spending analysis unavailable for profile inference. userId={UserId}", userId);
        }

        if (summary is not null)
        {
            var incomeRange = UserFinancialProfileValueNormalizer.DeriveIncomeRange(summary.IncomeLast30Days);
            if (!string.IsNullOrWhiteSpace(incomeRange))
            {
                candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                    Key: UserFinancialProfileSignalKey.MonthlyIncomeRange,
                    Value: incomeRange,
                    Source: UserFinancialProfileSignalSource.InferredFromSummary,
                    Strength: summary.IncomeLast30Days >= 4_000m
                        ? UserFinancialProfileSignalStrength.Strong
                        : UserFinancialProfileSignalStrength.Acceptable));
            }

            if (!currentState.ExplicitSignals.ContainsKey(UserFinancialProfileSignalKey.Currency))
            {
                candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                    Key: UserFinancialProfileSignalKey.Currency,
                    Value: UserFinancialProfileValueNormalizer.NormalizeCurrency(summary.Currency),
                    Source: UserFinancialProfileSignalSource.InferredFromSummary,
                    Strength: UserFinancialProfileSignalStrength.Acceptable));
            }
        }

        if (recurring is not null && recurring.Items.Count > 0)
        {
            var recurringItems = recurring.Items
                .Take(Math.Clamp(_options.MaxRecurringObligations, 1, 32))
                .ToArray();
            candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                Key: UserFinancialProfileSignalKey.KnownObligations,
                Value: JsonSerializer.Serialize(recurringItems, InferencePayloadJsonOptions),
                Source: UserFinancialProfileSignalSource.InferredFromRecurringObligations,
                Strength: recurring.Items.Count >= 2
                          || recurring.EstimatedMonthlyTotal >= _options.StrongRecurringMonthlyTotalThreshold
                    ? UserFinancialProfileSignalStrength.Strong
                    : UserFinancialProfileSignalStrength.Acceptable));
        }

        if (budget is not null && (budget.HasBudgetPlan || budget.MonthToDateSpend > 0m))
        {
            candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                Key: UserFinancialProfileSignalKey.BudgetStructure,
                Value: JsonSerializer.Serialize(new
                {
                    budget.HasBudgetPlan,
                    budget.MonthlyBudget,
                    budget.MonthToDateSpend,
                    budget.RemainingBudget
                }, InferencePayloadJsonOptions),
                Source: UserFinancialProfileSignalSource.InferredFromBudget,
                Strength: budget.HasBudgetPlan || budget.MonthToDateSpend >= _options.StrongMonthToDateSpendThreshold
                    ? UserFinancialProfileSignalStrength.Strong
                    : UserFinancialProfileSignalStrength.Acceptable));
        }

        var activePlans = await dbContext.ExpensePlans
            .AsNoTracking()
            .Where(x => x.UserId == userId && (x.Status == "active" || x.ActivatedAtUtc.HasValue))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(_options.MaxActivePlans, 1, 20))
            .Select(x => new { x.Id, x.Title, x.Status, x.ExpectedSpendTotal, x.CurrencyCode })
            .ToListAsync(cancellationToken);
        if (activePlans.Count > 0)
        {
            candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                Key: UserFinancialProfileSignalKey.ActivePlans,
                Value: JsonSerializer.Serialize(activePlans, InferencePayloadJsonOptions),
                Source: UserFinancialProfileSignalSource.InferredFromPlanData,
                Strength: UserFinancialProfileSignalStrength.Strong));
        }

        if (spending is not null && spending.SpendByDomain.Count > 0 && spending.AverageDailySpend > 0m)
        {
            var nonZeroDomainCount = spending.SpendByDomain.Count(x => x.Value > 0m);
            candidates.Add(new UserFinancialProfileInferredSignalCandidate(
                Key: UserFinancialProfileSignalKey.SpendingTendencies,
                Value: JsonSerializer.Serialize(new
                {
                    spending.AverageDailySpend,
                    spending.LargestExpense,
                    spendByDomain = spending.SpendByDomain
                }, InferencePayloadJsonOptions),
                Source: UserFinancialProfileSignalSource.InferredFromSpendingPattern,
                Strength: nonZeroDomainCount >= Math.Max(1, _options.StrongSpendingDomainCountThreshold)
                          && spending.AverageDailySpend >= _options.StrongAverageDailySpendThreshold
                          && spending.LargestExpense >= _options.StrongLargestExpenseThreshold
                    ? UserFinancialProfileSignalStrength.Strong
                    : UserFinancialProfileSignalStrength.Acceptable));
        }

        return candidates;
    }
}
