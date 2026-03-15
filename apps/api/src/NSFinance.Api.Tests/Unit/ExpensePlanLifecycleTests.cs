using NSFinance.Api.Modules.ExpenseTracker.Models;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Persistence.Entities;
using Xunit;

namespace NSFinance.Api.Tests.Unit;

public class ExpensePlanLifecycleTests
{
    [Fact]
    public void CanTransition_RejectsCompletedBackToDrafted()
    {
        var allowed = ExpensePlanLifecycleService.CanTransition(ExpensePlanStatuses.Completed, ExpensePlanStatuses.Drafted);

        Assert.False(allowed);
    }

    [Fact]
    public void EvaluatePeriodProgress_AfterPeriodFlagsAutoCompleteForActivePlan()
    {
        var plan = new ExpensePlan
        {
            Status = ExpensePlanStatuses.Active,
            StartDateUtc = new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 03, 31, 0, 0, 0, DateTimeKind.Utc)
        };

        var progress = ExpensePlanLifecycleService.EvaluatePeriodProgress(plan, new DateTime(2026, 04, 02, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("after_period", progress.PeriodState);
        Assert.True(progress.ShouldAutoComplete);
        Assert.Equal(100m, progress.PercentElapsed);
    }

    [Fact]
    public void TryValidatePeriod_ValidatesWeeklyMonthlyAndSeasonalRanges()
    {
        Assert.True(ExpensePlanLifecycleService.TryValidatePeriod(
            ExpensePlanTypes.Weekly,
            new DateTime(2026, 03, 02, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 03, 08, 0, 0, 0, DateTimeKind.Utc),
            out _));

        Assert.True(ExpensePlanLifecycleService.TryValidatePeriod(
            ExpensePlanTypes.Monthly,
            new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 03, 31, 0, 0, 0, DateTimeKind.Utc),
            out _));

        Assert.True(ExpensePlanLifecycleService.TryValidatePeriod(
            ExpensePlanTypes.Seasonal,
            new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 05, 31, 0, 0, 0, DateTimeKind.Utc),
            out _));

        Assert.False(ExpensePlanLifecycleService.TryValidatePeriod(
            ExpensePlanTypes.Monthly,
            new DateTime(2026, 03, 05, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 03, 31, 0, 0, 0, DateTimeKind.Utc),
            out _));
    }
}
