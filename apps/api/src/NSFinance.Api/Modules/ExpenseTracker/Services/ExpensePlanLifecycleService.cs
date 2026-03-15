using NSFinance.Api.Modules.ExpenseTracker.Models;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.ExpenseTracker.Services;

public static class ExpensePlanLifecycleService
{
    public static string NormalizeStatus(string status)
    {
        var normalized = status.Trim().ToLowerInvariant();
        return ExpensePlanStatuses.All.Contains(normalized) ? normalized : ExpensePlanStatuses.Drafted;
    }

    public static string NormalizePlanType(string planType)
    {
        var normalized = planType.Trim().ToLowerInvariant();
        return ExpensePlanTypes.All.Contains(normalized) ? normalized : ExpensePlanTypes.Monthly;
    }

    public static string NormalizePlanOriginType(string? originType)
    {
        var normalized = originType?.Trim().ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(normalized) && ExpensePlanOriginTypes.All.Contains(normalized)
            ? normalized
            : ExpensePlanOriginTypes.Manual;
    }

    public static string? NormalizeSharingMode(string? sharingMode, bool isShared)
    {
        if (!isShared)
        {
            return null;
        }

        var normalized = sharingMode?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalized) && ExpensePlanSharingModes.All.Contains(normalized))
        {
            return normalized;
        }

        return ExpensePlanSharingModes.DirectShare;
    }

    public static ExpensePlanPeriodProgress EvaluatePeriodProgress(ExpensePlan plan, DateTime utcNow)
    {
        var start = plan.StartDateUtc.Date;
        var end = plan.EndDateUtc.Date;
        var today = utcNow.Date;

        if (today < start)
        {
            return new ExpensePlanPeriodProgress("before_period", 0m, false, false);
        }

        var totalDays = Math.Max((end - start).Days + 1, 1);
        var elapsedDays = Math.Min((today - start).Days + 1, totalDays);
        var percentElapsed = decimal.Round((decimal)elapsedDays / totalDays * 100m, 2, MidpointRounding.AwayFromZero);

        if (today > end)
        {
            return new ExpensePlanPeriodProgress(
                "after_period",
                100m,
                string.Equals(plan.Status, ExpensePlanStatuses.Active, StringComparison.OrdinalIgnoreCase),
                false);
        }

        return new ExpensePlanPeriodProgress(
            "in_period",
            percentElapsed,
            false,
            string.Equals(plan.Status, ExpensePlanStatuses.Active, StringComparison.OrdinalIgnoreCase));
    }

    public static bool CanEdit(ExpensePlan plan)
    {
        return ExpensePlanStatuses.Mutable.Contains(plan.Status)
            && plan.LockedAtUtc is null
            && !string.Equals(plan.Status, ExpensePlanStatuses.Completed, StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanTransition(string currentStatus, string targetStatus)
    {
        var current = NormalizeStatus(currentStatus);
        var target = NormalizeStatus(targetStatus);

        if (current == target)
        {
            return true;
        }

        return current switch
        {
            ExpensePlanStatuses.Drafted => target is ExpensePlanStatuses.Scheduled or ExpensePlanStatuses.Active or ExpensePlanStatuses.Archived,
            ExpensePlanStatuses.Scheduled => target is ExpensePlanStatuses.Active or ExpensePlanStatuses.Cancelled or ExpensePlanStatuses.Archived,
            ExpensePlanStatuses.Active => target is ExpensePlanStatuses.Completed,
            ExpensePlanStatuses.Completed => target is ExpensePlanStatuses.Archived,
            ExpensePlanStatuses.Cancelled => target is ExpensePlanStatuses.Archived,
            _ => false
        };
    }

    public static void EnsureCanTransition(ExpensePlan plan, string targetStatus)
    {
        if (!CanTransition(plan.Status, targetStatus))
        {
            throw new InvalidOperationException($"Plan status cannot transition from {plan.Status} to {targetStatus}.");
        }

        if (string.Equals(plan.Status, ExpensePlanStatuses.Completed, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(targetStatus, ExpensePlanStatuses.Archived, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Completed plans are locked and can only be archived.");
        }
    }

    public static void ApplyTransition(ExpensePlan plan, string targetStatus, DateTime utcNow, string? statusReason)
    {
        var normalizedTarget = NormalizeStatus(targetStatus);
        EnsureCanTransition(plan, normalizedTarget);

        plan.Status = normalizedTarget;
        plan.StatusReason = NormalizeOptionalText(statusReason);
        plan.UpdatedAtUtc = utcNow;

        if (normalizedTarget == ExpensePlanStatuses.Active)
        {
            plan.ActivatedAtUtc ??= utcNow;
            plan.CancelledAtUtc = null;
            plan.ArchivedAtUtc = null;
        }
        else if (normalizedTarget == ExpensePlanStatuses.Completed)
        {
            plan.CompletedAtUtc ??= utcNow;
            plan.LockedAtUtc ??= utcNow;
        }
        else if (normalizedTarget == ExpensePlanStatuses.Cancelled)
        {
            plan.CancelledAtUtc ??= utcNow;
        }
        else if (normalizedTarget == ExpensePlanStatuses.Archived)
        {
            plan.ArchivedAtUtc ??= utcNow;
        }
    }

    public static bool TryValidatePeriod(string planType, DateTime startDateUtc, DateTime endDateUtc, out string? error)
    {
        var normalizedType = NormalizePlanType(planType);
        var start = startDateUtc.Date;
        var end = endDateUtc.Date;

        if (start > end)
        {
            error = "Plan start date must be on or before the end date.";
            return false;
        }

        if (normalizedType == ExpensePlanTypes.Weekly)
        {
            if ((end - start).Days != 6)
            {
                error = "Weekly plans must cover exactly 7 calendar days.";
                return false;
            }
        }
        else if (normalizedType == ExpensePlanTypes.Monthly)
        {
            var expectedStart = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var expectedEnd = new DateTime(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month), 0, 0, 0, DateTimeKind.Utc);
            if (start != expectedStart.Date || end != expectedEnd.Date)
            {
                error = "Monthly plans must span a full calendar month.";
                return false;
            }
        }
        else if (normalizedType == ExpensePlanTypes.Seasonal)
        {
            var expectedStart = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var seasonEndMonth = start.AddMonths(2);
            var expectedEnd = new DateTime(seasonEndMonth.Year, seasonEndMonth.Month, DateTime.DaysInMonth(seasonEndMonth.Year, seasonEndMonth.Month), 0, 0, 0, DateTimeKind.Utc);
            if (start != expectedStart.Date || end != expectedEnd.Date)
            {
                error = "Seasonal plans must span three full calendar months.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
