using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Policies.DTOs;
using NSFinance.Api.Modules.Policies.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public class PolicyServiceTests
{
    [Fact]
    public async Task AcceptPolicy_CreatesAcceptanceRecord()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        SeedUserAndPolicy(dbContext, userId, out var version);

        var audit = new InMemoryAuditService();
        var service = new PolicyService(dbContext, new FixedCurrentUserProviderForTests(userId), audit);

        var result = await service.AcceptPolicyAsync(
            new AcceptPolicyRequest("terms_of_service", version, "test_context", "mobile", "1.0.0"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(dbContext.PolicyAcceptances);
        Assert.Contains(audit.Events, x => x.EventName == "legal_policy_accepted");
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"policy-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static void SeedUserAndPolicy(AppDbContext dbContext, Guid userId, out string version)
    {
        version = "1.0.0";
        var documentId = Guid.NewGuid();
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = "policy@test.local",
            NormalizedEmail = "policy@test.local",
            DisplayName = "Policy User",
            Status = "active",
            OnboardingStatus = "completed",
            Role = "user",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            EmailVerified = true,
            Timezone = "UTC",
            Locale = "en-US",
            PreferredCurrency = "EUR",
            PlanTier = "standard"
        });
        dbContext.PolicyDocuments.Add(new PolicyDocument
        {
            Id = documentId,
            PolicyType = "terms_of_service",
            Name = "Terms",
            CreatedUtc = DateTime.UtcNow
        });
        dbContext.PolicyVersions.Add(new PolicyVersion
        {
            Id = Guid.NewGuid(),
            PolicyDocumentId = documentId,
            Version = version,
            EffectiveUtc = DateTime.UtcNow,
            ContentReference = "legal/terms/v1",
            IsActive = true,
            CreatedUtc = DateTime.UtcNow
        });
        dbContext.SaveChanges();
    }

    private sealed class InMemoryAuditService : IAuditService
    {
        public List<(string Category, string EventName)> Events { get; } = [];

        public Task WriteEventAsync(string category, string eventName, string targetEntityType, string? targetEntityId, Guid? actorId, string actorType, object? metadata, CancellationToken cancellationToken)
        {
            Events.Add((category, eventName));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedCurrentUserProviderForTests(Guid userId) : ICurrentUserProvider
    {
        public Guid UserId => userId;

        public bool TryGetUserId(out Guid result)
        {
            result = userId;
            return true;
        }

        public bool TryGetSessionId(out Guid sessionId)
        {
            sessionId = Guid.NewGuid();
            return true;
        }
    }
}
