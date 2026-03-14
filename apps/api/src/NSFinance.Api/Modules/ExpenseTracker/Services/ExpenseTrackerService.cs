using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.ExpenseTracker.Services;

public sealed class ExpenseTrackerService(AppDbContext dbContext, ICurrentUserProvider currentUserProvider)
{
    public async Task<IReadOnlyList<ExpenseTrackerEntryDto>> GetEntriesAsync(CancellationToken cancellationToken)
    {
        var entries = await dbContext.Set<ExpenseTrackerEntry>()
            .AsNoTracking()
            .Where(x => x.UserId == currentUserProvider.UserId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.UpdatedUtc)
            .ToListAsync(cancellationToken);

        return entries.Select(ToDto).ToList();
    }

    public async Task<ExpenseTrackerEntryDto?> GetEntryByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await dbContext.Set<ExpenseTrackerEntry>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == id && x.UserId == currentUserProvider.UserId,
                cancellationToken);

        return entry is null ? null : ToDto(entry);
    }

    public async Task<ExpenseTrackerEntryDto> CreateEntryAsync(
        CreateExpenseTrackerEntryRequest request,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var entry = new ExpenseTrackerEntry
        {
            Id = Guid.NewGuid(),
            UserId = currentUserProvider.UserId,
            Title = request.Title.Trim(),
            Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
            Currency = NormalizeCurrency(request.Currency),
            Category = NormalizeLabel(request.Category, "Other"),
            PaymentSource = NormalizeLabel(request.PaymentSource, "Other"),
            OccurredAtUtc = request.OccurredAtUtc?.ToUniversalTime() ?? utcNow,
            Notes = NormalizeOptionalText(request.Notes),
            TagsJson = SerializeTags(request.Tags),
            Status = NormalizeStatus(request.Status),
            IsRecurring = request.IsRecurring,
            Merchant = NormalizeOptionalText(request.Merchant),
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow
        };

        dbContext.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entry);
    }

    public async Task<ExpenseTrackerEntryDto?> UpdateEntryAsync(
        Guid id,
        UpdateExpenseTrackerEntryRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.Set<ExpenseTrackerEntry>()
            .SingleOrDefaultAsync(
                x => x.Id == id && x.UserId == currentUserProvider.UserId,
                cancellationToken);

        if (entry is null)
        {
            return null;
        }

        entry.Title = request.Title.Trim();
        entry.Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        entry.Currency = NormalizeCurrency(request.Currency);
        entry.Category = NormalizeLabel(request.Category, "Other");
        entry.PaymentSource = NormalizeLabel(request.PaymentSource, "Other");
        entry.OccurredAtUtc = request.OccurredAtUtc?.ToUniversalTime() ?? entry.OccurredAtUtc;
        entry.Notes = NormalizeOptionalText(request.Notes);
        entry.TagsJson = SerializeTags(request.Tags);
        entry.Status = NormalizeStatus(request.Status);
        entry.IsRecurring = request.IsRecurring;
        entry.Merchant = NormalizeOptionalText(request.Merchant);
        entry.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entry);
    }

    public async Task<bool> DeleteEntryAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await dbContext.Set<ExpenseTrackerEntry>()
            .SingleOrDefaultAsync(
                x => x.Id == id && x.UserId == currentUserProvider.UserId,
                cancellationToken);

        if (entry is null)
        {
            return false;
        }

        dbContext.Remove(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ExpenseTrackerEntryDto ToDto(ExpenseTrackerEntry entry)
    {
        return new ExpenseTrackerEntryDto(
            entry.Id,
            entry.Title,
            entry.Amount,
            entry.Currency,
            entry.Category,
            entry.PaymentSource,
            entry.OccurredAtUtc,
            entry.Notes,
            DeserializeTags(entry.TagsJson),
            entry.Status,
            entry.IsRecurring,
            entry.Merchant,
            entry.CreatedUtc,
            entry.UpdatedUtc);
    }

    private static string NormalizeCurrency(string currency)
    {
        return string.IsNullOrWhiteSpace(currency)
            ? "EUR"
            : currency.Trim().ToUpperInvariant();
    }

    private static string NormalizeStatus(string status)
    {
        return status.Trim().ToLowerInvariant() == "planned" ? "planned" : "completed";
    }

    private static string NormalizeLabel(string value, string fallback)
    {
        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string SerializeTags(IReadOnlyList<string>? tags)
    {
        var normalized = (tags ?? [])
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
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
            var parsed = JsonSerializer.Deserialize<List<string>>(tagsJson);
            return parsed?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
