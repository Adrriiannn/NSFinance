using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class OperationalFailureRecorder(
    AppDbContext dbContext,
    ILogger<OperationalFailureRecorder> logger) : IOperationalFailureRecorder
{
    public async Task RecordAsync(OperationalFailureRecordInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.FailureType) || string.IsNullOrWhiteSpace(input.Fingerprint))
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var failureType = TrimRequired(input.FailureType, 120);
        var fingerprint = TrimRequired(input.Fingerprint, 320);
        var persistenceToken = cancellationToken.IsCancellationRequested
            ? CancellationToken.None
            : cancellationToken;

        try
        {
            var existing = await dbContext.OperationalFailureRecords
                .SingleOrDefaultAsync(
                    x => x.Area == input.Area
                         && x.FailureType == failureType
                         && x.Fingerprint == fingerprint,
                    persistenceToken);

            if (existing is null)
            {
                dbContext.OperationalFailureRecords.Add(new OperationalFailureRecord
                {
                    Id = Guid.NewGuid(),
                    Area = input.Area,
                    Severity = input.Severity,
                    FailureType = failureType,
                    Fingerprint = fingerprint,
                    CorrelationId = Trim(input.CorrelationId, 128),
                    SubjectKey = Trim(input.SubjectKey, 320),
                    FailureMessage = Trim(input.FailureMessage, 1200),
                    DetailsJson = Trim(input.DetailsJson, 4000),
                    OccurrenceCount = 1,
                    FirstOccurredUtc = nowUtc,
                    LastOccurredUtc = nowUtc
                });
            }
            else
            {
                existing.LastOccurredUtc = nowUtc;
                existing.OccurrenceCount += 1;
                existing.Severity = MaxSeverity(existing.Severity, input.Severity);
                existing.CorrelationId = Trim(input.CorrelationId, 128) ?? existing.CorrelationId;
                existing.SubjectKey = Trim(input.SubjectKey, 320) ?? existing.SubjectKey;
                existing.FailureMessage = Trim(input.FailureMessage, 1200) ?? existing.FailureMessage;
                existing.DetailsJson = Trim(input.DetailsJson, 4000) ?? existing.DetailsJson;
            }

            await dbContext.SaveChangesAsync(persistenceToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Operational failure record persistence failed area={Area} failureType={FailureType} fingerprint={Fingerprint}",
                input.Area,
                failureType,
                fingerprint);
        }
    }

    private static OperationalFailureSeverity MaxSeverity(OperationalFailureSeverity left, OperationalFailureSeverity right)
        => left > right ? left : right;

    private static string TrimRequired(string value, int maxLength)
    {
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
