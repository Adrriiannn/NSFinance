using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Infrastructure.Seeding;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Infrastructure.Startup;

public sealed class DatabaseInitializationHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostApplicationLifetime hostApplicationLifetime,
    ILogger<DatabaseInitializationHostedService> logger) : IHostedService
{
    private Task? _initializationTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Start DB initialization only after the host is fully started and listening.
        hostApplicationLifetime.ApplicationStarted.Register(() =>
        {
            _initializationTask = Task.Run(
                () => InitializeAsync(hostApplicationLifetime.ApplicationStopping),
                CancellationToken.None);
        });

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_initializationTask is null)
        {
            return;
        }

        try
        {
            await _initializationTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down; no extra action required.
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seeder = scope.ServiceProvider.GetRequiredService<PolicyDataSeeder>();

            var startupMigrationsEnabled = configuration.GetValue("Database:ApplyMigrationsOnStartup", true);
            var applyMigrations = startupMigrationsEnabled;
            var seedPolicyData = configuration.GetValue("Database:SeedPolicyDataOnStartup", true);

            if (applyMigrations)
            {
                await dbContext.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database migrations applied after host startup.");
            }

            if (seedPolicyData)
            {
                await seeder.SeedAsync(dbContext, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Deferred database initialization canceled.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Deferred database initialization failed.");
            hostApplicationLifetime.StopApplication();
        }
    }
}
