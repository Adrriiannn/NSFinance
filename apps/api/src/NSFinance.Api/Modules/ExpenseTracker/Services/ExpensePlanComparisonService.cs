using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Models;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.ExpenseTracker.Services;

public static class ExpensePlanComparisonService
{
    public static ExpensePlanComparisonDto BuildComparison(
        ExpensePlan plan,
        IReadOnlyList<ExpenseTrackerEntry> sourceEntries,
        ExpenseTaxonomyService taxonomyService,
        DateTime utcNow)
    {
        if (plan.IsTemplate)
        {
            return new ExpensePlanComparisonDto(
                plan.Id,
                plan.ExpectedSpendTotal,
                0m,
                -plan.ExpectedSpendTotal,
                plan.ExpectedSpendTotal == 0 ? 0m : -100m,
                plan.ExpectedSpendTotal,
                0m,
                0,
                0,
                0m,
                "template",
                false,
                utcNow,
                [],
                []);
        }

        var comparisonFacts = BuildComparableEntryFacts(sourceEntries, taxonomyService)
            .Where(fact => fact.EffectiveOccurredAtUtc.Date >= plan.StartDateUtc.Date && fact.EffectiveOccurredAtUtc.Date <= plan.EndDateUtc.Date)
            .ToList();

        var lineItemComparisons = plan.LineItems
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.DisplayNameSnapshot)
            .Select(item => BuildLineItemComparison(item, comparisonFacts, taxonomyService))
            .ToList();

        var matchedIds = new HashSet<Guid>(lineItemComparisons.SelectMany(item => item.MatchingTransactionIds));
        var unexpected = comparisonFacts
            .Where(fact => !matchedIds.Contains(fact.EntryId))
            .GroupBy(fact => new
            {
                fact.TaxonomyDomainId,
                fact.DomainName,
                fact.TaxonomyCategoryId,
                fact.CategoryName,
                fact.TaxonomySubcategoryId,
                fact.SubcategoryName
            })
            .Select(group => new ExpensePlanUnexpectedSpendDto(
                group.Key.TaxonomyDomainId,
                group.Key.DomainName,
                group.Key.TaxonomyCategoryId,
                group.Key.CategoryName,
                group.Key.TaxonomySubcategoryId,
                group.Key.SubcategoryName,
                Round(group.Sum(x => x.EffectiveAmount)),
                group.Count(),
                group.Select(x => x.EntryId).ToList()))
            .Where(item => item.ActualAmount != 0)
            .OrderByDescending(item => Math.Abs(item.ActualAmount))
            .ToList();

        var actualSpendTotal = Round(comparisonFacts.Sum(fact => fact.EffectiveAmount));
        var varianceAmount = Round(actualSpendTotal - plan.ExpectedSpendTotal);
        var variancePercent = plan.ExpectedSpendTotal == 0m ? 0m : Round(varianceAmount / plan.ExpectedSpendTotal * 100m);
        var remainingPlannedAmount = Round(plan.ExpectedSpendTotal - actualSpendTotal);
        var percentOfPlanUsed = plan.ExpectedSpendTotal == 0m ? 0m : Round(actualSpendTotal / plan.ExpectedSpendTotal * 100m);
        var periodProgress = ExpensePlanLifecycleService.EvaluatePeriodProgress(plan, utcNow);

        return new ExpensePlanComparisonDto(
            plan.Id,
            plan.ExpectedSpendTotal,
            actualSpendTotal,
            varianceAmount,
            variancePercent,
            remainingPlannedAmount,
            percentOfPlanUsed,
            lineItemComparisons.Sum(item => item.Dto.MatchedTransactionCount),
            unexpected.Sum(item => item.MatchingTransactionCount),
            periodProgress.PercentElapsed,
            periodProgress.PeriodState,
            periodProgress.ShouldAutoComplete,
            utcNow,
            lineItemComparisons.Select(item => item.Dto).ToList(),
            unexpected);
    }

    private static (ExpensePlanComparisonLineItemDto Dto, IReadOnlyList<Guid> MatchingTransactionIds) BuildLineItemComparison(
        ExpensePlanLineItem lineItem,
        IReadOnlyList<ExpensePlanComparableEntryFact> comparisonFacts,
        ExpenseTaxonomyService taxonomyService)
    {
        var matching = comparisonFacts.Where(fact => MatchesLineItem(lineItem, fact)).ToList();
        var actualAmount = Round(matching.Sum(item => item.EffectiveAmount));
        var varianceAmount = Round(actualAmount - lineItem.ExpectedAmount);
        var variancePercent = lineItem.ExpectedAmount == 0m ? 0m : Round(varianceAmount / lineItem.ExpectedAmount * 100m);
        var remainingPlannedAmount = Round(lineItem.ExpectedAmount - actualAmount);
        var percentOfPlanUsed = lineItem.ExpectedAmount == 0m ? 0m : Round(actualAmount / lineItem.ExpectedAmount * 100m);

        var dto = new ExpensePlanComparisonLineItemDto(
            lineItem.Id,
            lineItem.TaxonomyDomainId,
            taxonomyService.GetDomainName(lineItem.TaxonomyDomainId) ?? "Unknown",
            lineItem.TaxonomyCategoryId,
            taxonomyService.GetCategoryName(lineItem.TaxonomyCategoryId) ?? "Unknown",
            lineItem.TaxonomySubcategoryId,
            taxonomyService.GetSubcategoryName(lineItem.TaxonomySubcategoryId),
            lineItem.DisplayNameSnapshot,
            lineItem.ExpectedAmount,
            actualAmount,
            varianceAmount,
            variancePercent,
            remainingPlannedAmount,
            percentOfPlanUsed,
            matching.Count);

        return (dto, matching.Select(item => item.EntryId).ToList());
    }

    private static bool MatchesLineItem(ExpensePlanLineItem lineItem, ExpensePlanComparableEntryFact fact)
    {
        if (lineItem.TaxonomySubcategoryId.HasValue)
        {
            return lineItem.TaxonomySubcategoryId == fact.TaxonomySubcategoryId;
        }

        return lineItem.TaxonomyCategoryId == fact.TaxonomyCategoryId;
    }

    internal static IReadOnlyList<ExpensePlanComparableEntryFact> BuildComparableEntryFacts(
        IReadOnlyList<ExpenseTrackerEntry> sourceEntries,
        ExpenseTaxonomyService taxonomyService)
    {
        return sourceEntries
            .Where(entry => string.Equals(entry.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                var effectiveOccurredAtUtc = entry.LinkedOriginalEntry?.OccurredAtUtc ?? entry.OccurredAtUtc;
                var effectiveAmount = entry.LinkedOriginalEntryId.HasValue && entry.LinkedOriginalOffsetAmount.HasValue
                    ? entry.LinkedOriginalOffsetAmount.Value
                    : entry.Amount;
                var domainId = entry.LinkedOriginalEntry?.TaxonomyDomainId ?? entry.TaxonomyDomainId;
                var categoryId = entry.LinkedOriginalEntry?.TaxonomyCategoryId ?? entry.TaxonomyCategoryId;
                var subcategoryId = entry.LinkedOriginalEntry?.TaxonomySubcategoryId ?? entry.TaxonomySubcategoryId;
                var fallbackCategory = entry.LinkedOriginalEntry?.Category ?? entry.Category;

                return new ExpensePlanComparableEntryFact(
                    entry.Id,
                    effectiveOccurredAtUtc,
                    Round(effectiveAmount),
                    domainId,
                    categoryId,
                    subcategoryId,
                    taxonomyService.GetDomainName(domainId) ?? "Unknown",
                    taxonomyService.GetCategoryName(categoryId) ?? fallbackCategory,
                    taxonomyService.GetSubcategoryName(subcategoryId) ?? fallbackCategory);
            })
            .Where(fact => fact.EffectiveAmount != 0m)
            .ToList();
    }

    private static decimal Round(decimal value)
    {
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}

