using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Infrastructure.Seeding;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Infrastructure.Startup;

public static class ApplicationInitializationExtensions
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitialization");
        var dbContext = serviceProvider.GetRequiredService<AppDbContext>();
        var seeder = serviceProvider.GetRequiredService<DevelopmentDataSeeder>();

        var startupMigrationsEnabled = configuration.GetValue("Database:ApplyMigrationsOnStartup", true);
        var applyMigrations = app.Environment.IsDevelopment() && startupMigrationsEnabled;
        var seedDemoData = app.Environment.IsDevelopment() && configuration.GetValue("Database:SeedDemoDataOnStartup", true);
        var seedPolicyData = configuration.GetValue("Database:SeedPolicyDataOnStartup", true);

        if (!app.Environment.IsDevelopment() && startupMigrationsEnabled)
        {
            logger.LogWarning(
                "Database:ApplyMigrationsOnStartup is ignored outside Development. Apply schema changes through the CI/CD migration bundle workflow.");
        }

        if (applyMigrations)
        {
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied during Development startup.");
        }

        if (seedPolicyData)
        {
            await seeder.SeedPolicyDataAsync(dbContext, CancellationToken.None);
        }

        if (seedDemoData)
        {
            await seeder.SeedAsync(dbContext, CancellationToken.None);
        }
    }
}
