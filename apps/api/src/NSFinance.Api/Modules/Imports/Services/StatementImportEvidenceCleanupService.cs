using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Imports.Services;

public sealed class StatementImportEvidenceCleanupService(
    AppDbContext dbContext,
    TimeProvider timeProvider)
{
    internal const int ReadBatchSize = 500;
    internal const int MaximumRowsPerRun = 5_000;

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var purgedCount = 0;

        while (purgedCount < MaximumRowsPerRun)
        {
            var remaining = MaximumRowsPerRun - purgedCount;
            var rows = await dbContext.StatementImportRows
                .Where(row => row.SourceEvidenceJson != null
                    && row.EvidenceExpiresUtc != null
                    && row.EvidenceExpiresUtc <= utcNow)
                .OrderBy(row => row.EvidenceExpiresUtc)
                .ThenBy(row => row.Id)
                .Take(Math.Min(ReadBatchSize, remaining))
                .ToListAsync(cancellationToken);
            if (rows.Count == 0)
            {
                break;
            }

            foreach (var row in rows)
            {
                row.SourceEvidenceJson = null;
                row.EvidenceExpiresUtc = null;
                row.UpdatedUtc = utcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            purgedCount += rows.Count;
            dbContext.ChangeTracker.Clear();
        }

        return purgedCount;
    }
}
