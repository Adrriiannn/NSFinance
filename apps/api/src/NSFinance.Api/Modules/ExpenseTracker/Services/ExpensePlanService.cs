using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Models;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.ExpenseTracker.Services;

public sealed class ExpensePlanService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    ExpenseTaxonomyService expenseTaxonomyService)
{
    public async Task<IReadOnlyList<ExpensePlanDto>> GetPlansAsync(
        string? status,
        bool templatesOnly,
        int? take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ExpensePlans
            .AsNoTracking()
            .Include(x => x.LineItems)
            .Where(x => x.UserId == currentUserProvider.UserId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = ExpensePlanLifecycleService.NormalizeStatus(status);
            query = query.Where(x => x.Status == normalizedStatus);
        }

        if (templatesOnly)
        {
            query = query.Where(x => x.IsTemplate);
        }

        query = query
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ThenByDescending(x => x.CreatedAtUtc);

        if (take.HasValue)
        {
            query = query.Take(Math.Clamp(take.Value, 1, 50));
        }

        var plans = await query.ToListAsync(cancellationToken);
        var entries = await LoadComparableEntriesAsync(cancellationToken);
        var utcNow = DateTime.UtcNow;

        return plans.Select(plan => ToDto(plan, entries, utcNow)).ToList();
    }

    public Task<IReadOnlyList<ExpensePlanDto>> GetActivePlansAsync(CancellationToken cancellationToken)
    {
        return GetPlansAsync(ExpensePlanStatuses.Active, false, null, cancellationToken);
    }

    public Task<IReadOnlyList<ExpensePlanDto>> GetRecentPlansAsync(int take, CancellationToken cancellationToken)
    {
        return GetPlansAsync(null, false, take, cancellationToken);
    }

    public async Task<ExpensePlanDto?> GetPlanByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await dbContext.ExpensePlans
            .AsNoTracking()
            .Include(x => x.LineItems.OrderBy(item => item.SortOrder))
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == currentUserProvider.UserId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var entries = await LoadComparableEntriesAsync(cancellationToken);
        return ToDto(plan, entries, DateTime.UtcNow);
    }

    public async Task<ExpensePlanDto> CreatePlanAsync(CreateExpensePlanRequest request, CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var creator = await GetCreatorSnapshotsAsync(cancellationToken);
        var normalizedStatus = ExpensePlanLifecycleService.NormalizeStatus(request.Status);
        var normalizedPlanType = ExpensePlanLifecycleService.NormalizePlanType(request.PlanType);

        var plan = new ExpensePlan
        {
            Id = Guid.NewGuid(),
            UserId = currentUserProvider.UserId,
            CreatorDisplayNameSnapshot = creator.DisplayName,
            CreatorTagSnapshot = creator.Tag,
            Title = request.Title.Trim(),
            Description = NormalizeOptionalText(request.Description),
            Notes = NormalizeOptionalText(request.Notes),
            Status = normalizedStatus,
            PlanType = normalizedPlanType,
            PlanOriginType = ExpensePlanLifecycleService.NormalizePlanOriginType(request.PlanOriginType),
            PlanVersion = 1,
            StartDateUtc = request.StartDateUtc.Date,
            EndDateUtc = request.EndDateUtc.Date,
            CurrencyCode = NormalizeCurrency(request.CurrencyCode),
            ExpectedIncomeTotal = Round(request.ExpectedIncomeTotal),
            TagsJson = SerializeTags(request.Tags),
            StatusReason = NormalizeOptionalText(request.StatusReason),
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
            ActivatedAtUtc = normalizedStatus == ExpensePlanStatuses.Active ? utcNow : null,
            IsTemplate = request.IsTemplate,
            IsRecurring = request.IsRecurring,
            RecurrenceRuleJson = SerializeRecurrence(request.Recurrence),
            IsShared = request.IsShared,
            SharingMode = ExpensePlanLifecycleService.NormalizeSharingMode(request.SharingMode, request.IsShared),
            SharedIdentity = request.IsShared ? BuildSharedIdentity() : null,
            LastCalculatedAtUtc = utcNow
        };

        ApplyLineItems(plan, request.LineItems, utcNow);
        RecalculatePlanTotals(plan);

        dbContext.ExpensePlans.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);

        var entries = await LoadComparableEntriesAsync(cancellationToken);
        return ToDto(plan, entries, utcNow);
    }

    public async Task<ExpensePlanDto?> UpdatePlanAsync(Guid id, UpdateExpensePlanRequest request, CancellationToken cancellationToken)
    {
        var plan = await dbContext.ExpensePlans
            .Include(x => x.LineItems)
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == currentUserProvider.UserId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        if (!ExpensePlanLifecycleService.CanEdit(plan))
        {
            throw new InvalidOperationException("Only drafted or scheduled plans can be edited.");
        }

        var utcNow = DateTime.UtcNow;
        plan.Title = request.Title.Trim();
        plan.Description = NormalizeOptionalText(request.Description);
        plan.Notes = NormalizeOptionalText(request.Notes);
        plan.PlanType = ExpensePlanLifecycleService.NormalizePlanType(request.PlanType);
        plan.StartDateUtc = request.StartDateUtc.Date;
        plan.EndDateUtc = request.EndDateUtc.Date;
        plan.CurrencyCode = NormalizeCurrency(request.CurrencyCode);
        plan.ExpectedIncomeTotal = Round(request.ExpectedIncomeTotal);
        plan.TagsJson = SerializeTags(request.Tags);
        plan.StatusReason = NormalizeOptionalText(request.StatusReason);
        plan.IsTemplate = request.IsTemplate;
        plan.IsRecurring = request.IsRecurring;
        plan.RecurrenceRuleJson = SerializeRecurrence(request.Recurrence);
        plan.IsShared = request.IsShared;
        plan.SharingMode = ExpensePlanLifecycleService.NormalizeSharingMode(request.SharingMode, request.IsShared);
        plan.SharedIdentity = request.IsShared ? plan.SharedIdentity ?? BuildSharedIdentity() : null;
        plan.UpdatedAtUtc = utcNow;
        plan.LastCalculatedAtUtc = utcNow;
        plan.PlanVersion += 1;

        ReplaceLineItems(plan, request.LineItems, utcNow);
        RecalculatePlanTotals(plan);

        await dbContext.SaveChangesAsync(cancellationToken);
        var entries = await LoadComparableEntriesAsync(cancellationToken);
        return ToDto(plan, entries, utcNow);
    }

    public async Task<ExpensePlanDto?> TransitionPlanAsync(
        Guid id,
        TransitionExpensePlanRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.ExpensePlans
            .Include(x => x.LineItems)
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == currentUserProvider.UserId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var utcNow = DateTime.UtcNow;
        ExpensePlanLifecycleService.ApplyTransition(plan, request.TargetStatus, utcNow, request.StatusReason);
        plan.LastCalculatedAtUtc = utcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var entries = await LoadComparableEntriesAsync(cancellationToken);
        return ToDto(plan, entries, utcNow);
    }

    public async Task<ExpensePlanDto?> DuplicatePlanAsync(Guid sourcePlanId, CancellationToken cancellationToken)
    {
        var source = await dbContext.ExpensePlans
            .AsNoTracking()
            .Include(x => x.LineItems)
            .SingleOrDefaultAsync(x => x.Id == sourcePlanId && x.UserId == currentUserProvider.UserId, cancellationToken);

        if (source is null)
        {
            return null;
        }

        var creator = await GetCreatorSnapshotsAsync(cancellationToken);
        var utcNow = DateTime.UtcNow;
        var plan = new ExpensePlan
        {
            Id = Guid.NewGuid(),
            UserId = currentUserProvider.UserId,
            CreatorDisplayNameSnapshot = creator.DisplayName,
            CreatorTagSnapshot = creator.Tag,
            Title = $"{source.Title} copy",
            Description = source.Description,
            Notes = source.Notes,
            Status = ExpensePlanStatuses.Drafted,
            PlanType = source.PlanType,
            PlanOriginType = ExpensePlanOriginTypes.Duplicated,
            PlanVersion = 1,
            StartDateUtc = source.StartDateUtc,
            EndDateUtc = source.EndDateUtc,
            CurrencyCode = source.CurrencyCode,
            ExpectedIncomeTotal = source.ExpectedIncomeTotal,
            ExpectedSpendTotal = source.ExpectedSpendTotal,
            ExpectedRemainingTotal = source.ExpectedRemainingTotal,
            TagsJson = source.TagsJson,
            StatusReason = $"Duplicated from {source.Id}",
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
            LastCalculatedAtUtc = utcNow,
            SourcePlanId = source.Id,
            IsTemplate = false,
            IsRecurring = source.IsRecurring,
            RecurrenceRuleJson = source.RecurrenceRuleJson,
            IsShared = false,
            SharingMode = null,
            SharedIdentity = null
        };

        plan.LineItems = source.LineItems
            .OrderBy(item => item.SortOrder)
            .Select(item => new ExpensePlanLineItem
            {
                Id = Guid.NewGuid(),
                PlanId = plan.Id,
                TaxonomyDomainId = item.TaxonomyDomainId,
                TaxonomyCategoryId = item.TaxonomyCategoryId,
                TaxonomySubcategoryId = item.TaxonomySubcategoryId,
                DisplayNameSnapshot = item.DisplayNameSnapshot,
                HierarchyPathSnapshot = item.HierarchyPathSnapshot,
                ExpectedAmount = item.ExpectedAmount,
                Notes = item.Notes,
                SortOrder = item.SortOrder,
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow
            })
            .ToList();

        dbContext.ExpensePlans.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);

        var entries = await LoadComparableEntriesAsync(cancellationToken);
        return ToDto(plan, entries, utcNow);
    }

    private void ApplyLineItems(ExpensePlan plan, IReadOnlyList<ExpensePlanLineItemRequest> lineItems, DateTime utcNow)
    {
        plan.LineItems = lineItems
            .OrderBy(item => item.SortOrder)
            .Select(item => BuildLineItem(plan.Id, item, utcNow))
            .ToList();
    }

    private void ReplaceLineItems(ExpensePlan plan, IReadOnlyList<ExpensePlanLineItemRequest> lineItems, DateTime utcNow)
    {
        dbContext.ExpensePlanLineItems.RemoveRange(plan.LineItems);
        plan.LineItems.Clear();
        foreach (var lineItem in lineItems.OrderBy(item => item.SortOrder))
        {
            plan.LineItems.Add(BuildLineItem(plan.Id, lineItem, utcNow));
        }
    }

    private ExpensePlanLineItem BuildLineItem(Guid planId, ExpensePlanLineItemRequest request, DateTime utcNow)
    {
        var subcategory = request.TaxonomySubcategoryId.HasValue
            ? expenseTaxonomyService.GetUserSelectableSubcategory(request.TaxonomySubcategoryId.Value)
            : null;

        if (subcategory is null)
        {
            throw new InvalidOperationException("Each plan line item must reference a valid user-visible taxonomy sub-category.");
        }

        var categoryName = expenseTaxonomyService.GetCategoryName(subcategory.CategoryId) ?? subcategory.Name;
        var domainName = expenseTaxonomyService.GetDomainName(subcategory.DomainId) ?? categoryName;
        return new ExpensePlanLineItem
        {
            Id = Guid.NewGuid(),
            PlanId = planId,
            TaxonomyDomainId = subcategory.DomainId,
            TaxonomyCategoryId = subcategory.CategoryId,
            TaxonomySubcategoryId = subcategory.Id,
            DisplayNameSnapshot = subcategory.Name,
            HierarchyPathSnapshot = $"{domainName} > {categoryName} > {subcategory.Name}",
            ExpectedAmount = Round(request.ExpectedAmount),
            Notes = NormalizeOptionalText(request.Notes),
            SortOrder = request.SortOrder,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };
    }

    private void RecalculatePlanTotals(ExpensePlan plan)
    {
        plan.ExpectedSpendTotal = Round(plan.LineItems.Sum(item => item.ExpectedAmount));
        plan.ExpectedRemainingTotal = Round(plan.ExpectedIncomeTotal - plan.ExpectedSpendTotal);
    }

    private ExpensePlanDto ToDto(ExpensePlan plan, IReadOnlyList<ExpenseTrackerEntry> entries, DateTime utcNow)
    {
        var comparison = ExpensePlanComparisonService.BuildComparison(plan, entries, expenseTaxonomyService, utcNow);
        var lineItems = plan.LineItems
            .OrderBy(item => item.SortOrder)
            .Select(item => new ExpensePlanLineItemDto(
                item.Id,
                item.PlanId,
                item.TaxonomyDomainId,
                expenseTaxonomyService.GetDomainName(item.TaxonomyDomainId) ?? "Unknown",
                item.TaxonomyCategoryId,
                expenseTaxonomyService.GetCategoryName(item.TaxonomyCategoryId) ?? "Unknown",
                item.TaxonomySubcategoryId,
                expenseTaxonomyService.GetSubcategoryName(item.TaxonomySubcategoryId),
                item.DisplayNameSnapshot,
                item.HierarchyPathSnapshot,
                item.ExpectedAmount,
                item.Notes,
                item.SortOrder,
                item.CreatedAtUtc,
                item.UpdatedAtUtc))
            .ToList();

        return new ExpensePlanDto(
            plan.Id,
            plan.UserId,
            plan.CreatorDisplayNameSnapshot,
            plan.CreatorTagSnapshot,
            plan.Title,
            plan.Description,
            plan.Notes,
            plan.Status,
            plan.PlanType,
            plan.PlanOriginType,
            plan.PlanVersion,
            plan.StartDateUtc,
            plan.EndDateUtc,
            plan.CurrencyCode,
            plan.ExpectedIncomeTotal,
            plan.ExpectedSpendTotal,
            plan.ExpectedRemainingTotal,
            DeserializeTags(plan.TagsJson),
            plan.StatusReason,
            plan.CreatedAtUtc,
            plan.UpdatedAtUtc,
            plan.ActivatedAtUtc,
            plan.CompletedAtUtc,
            plan.LockedAtUtc,
            plan.ArchivedAtUtc,
            plan.CancelledAtUtc,
            plan.LastCalculatedAtUtc,
            plan.SourcePlanId,
            plan.ImportedFromPublicPlanId,
            plan.IsTemplate,
            plan.IsRecurring,
            DeserializeRecurrence(plan.RecurrenceRuleJson),
            plan.IsShared,
            plan.SharingMode,
            plan.SharedIdentity,
            comparison,
            lineItems);
    }

    private async Task<(string DisplayName, string Tag)> GetCreatorSnapshotsAsync(CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == currentUserProvider.UserId, cancellationToken);

        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.FullName : user.DisplayName;
        var tag = !string.IsNullOrWhiteSpace(user.Handle)
            ? $"@{user.Handle.Trim()}"
            : $"@{displayName.Trim().ToLowerInvariant().Replace(' ', '_')}";

        return (displayName.Trim(), tag);
    }

    private async Task<IReadOnlyList<ExpenseTrackerEntry>> LoadComparableEntriesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ExpenseTrackerEntries
            .AsNoTracking()
            .Include(x => x.LinkedOriginalEntry)
            .Where(x => x.UserId == currentUserProvider.UserId)
            .ToListAsync(cancellationToken);
    }

    private static string NormalizeCurrency(string currencyCode)
    {
        return string.IsNullOrWhiteSpace(currencyCode)
            ? "EUR"
            : currencyCode.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static decimal Round(decimal value)
    {
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string SerializeTags(IReadOnlyList<string>? tags)
    {
        var normalized = (tags ?? [])
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();

        return JsonSerializer.Serialize(normalized);
    }

    private static IReadOnlyList<string> DeserializeTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(tagsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string? SerializeRecurrence(ExpensePlanRecurrenceDto? recurrence)
    {
        return recurrence is null ? null : JsonSerializer.Serialize(recurrence);
    }

    private static ExpensePlanRecurrenceDto? DeserializeRecurrence(string? recurrenceJson)
    {
        if (string.IsNullOrWhiteSpace(recurrenceJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ExpensePlanRecurrenceDto>(recurrenceJson);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSharedIdentity()
    {
        return $"plan_{Guid.NewGuid():N}";
    }
}
