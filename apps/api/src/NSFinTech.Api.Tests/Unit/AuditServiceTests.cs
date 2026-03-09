using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinTech.Api.Infrastructure.RequestContext;
using NSFinTech.Api.Modules.Audit.Services;
using NSFinTech.Api.Persistence;

namespace NSFinTech.Api.Tests.Unit;

public class AuditServiceTests
{
    [Fact]
    public async Task WriteEventAsync_PersistsAuditRecord()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuditService(
            dbContext,
            new TestRequestContextAccessor(),
            NullLogger<AuditService>.Instance);

        await service.WriteEventAsync(
            category: "auth",
            eventName: "login_success",
            targetEntityType: "session",
            targetEntityId: Guid.NewGuid().ToString(),
            actorId: Guid.NewGuid(),
            actorType: "user",
            metadata: new { source = "tests" },
            CancellationToken.None);

        Assert.Single(dbContext.AuditEvents);
        Assert.Equal("auth", dbContext.AuditEvents.Single().EventCategory);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"audit-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private sealed class TestRequestContextAccessor : IRequestContextAccessor
    {
        public string CorrelationId => "audit-test-correlation";
        public string SourceChannel => "api";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "test-agent";
        public string? Platform => "ios";
        public string? AppVersion => "1.0.0";
    }
}
