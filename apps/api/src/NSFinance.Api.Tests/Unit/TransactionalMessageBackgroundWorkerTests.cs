using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Auth.Configuration;
using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class TransactionalMessageBackgroundWorkerTests
{
    [Fact]
    public async Task ProcessBatchAsync_WhenEmailProviderIsDisabled_DoesNotOpenDatabaseScope()
    {
        var scopeFactory = new CountingScopeFactory();
        var worker = new TransactionalMessageBackgroundWorker(
            scopeFactory,
            Options.Create(new TransactionalEmailOptions { Enabled = false }),
            new DisabledEmailSender(),
            NullLogger<TransactionalMessageBackgroundWorker>.Instance);

        await worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(0, scopeFactory.CreateCount);
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        public int CreateCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateCount++;
            throw new InvalidOperationException("Disabled email must not open a database scope.");
        }
    }

    private sealed class DisabledEmailSender : ITransactionalEmailSender
    {
        public bool IsConfigured => false;

        public Task<TransactionalEmailSendResult> SendAsync(
            string recipient,
            RenderedIdentityEmail message,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Disabled email must not attempt provider delivery.");
        }
    }
}
