using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Modules.Auth.Services;

public sealed class AuthAbuseService(
    AppDbContext dbContext,
    IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public async Task<(bool LockedOut, DateTime? RetryAfterUtc)> IsLockedOutAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var sinceUtc = now.AddMinutes(-_options.FailedLoginWindowMinutes);

        var recentFailures = await dbContext.AuthAttempts
            .Where(x => x.NormalizedEmail == normalizedEmail && !x.WasSuccessful && x.CreatedUtc >= sinceUtc)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(_options.MaxFailedLoginAttempts)
            .ToListAsync(cancellationToken);

        if (recentFailures.Count < _options.MaxFailedLoginAttempts)
        {
            return (false, null);
        }

        var latestFailureUtc = recentFailures[0].CreatedUtc;
        var retryAfterUtc = latestFailureUtc.AddMinutes(_options.LoginLockoutMinutes);
        if (retryAfterUtc <= now)
        {
            return (false, null);
        }

        return (true, retryAfterUtc);
    }

    public async Task RecordAttemptAsync(
        string normalizedEmail,
        Guid? userId,
        string? ipAddress,
        bool succeeded,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        dbContext.AuthAttempts.Add(new AuthAttempt
        {
            Id = Guid.NewGuid(),
            NormalizedEmail = normalizedEmail,
            UserId = userId,
            IpAddress = ipAddress,
            WasSuccessful = succeeded,
            FailureReason = failureReason,
            CreatedUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
