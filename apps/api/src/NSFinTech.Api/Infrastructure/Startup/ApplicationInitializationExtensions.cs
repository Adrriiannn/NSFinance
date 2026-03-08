using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Infrastructure.Seeding;
using NSFinTech.Api.Persistence;

namespace NSFinTech.Api.Infrastructure.Startup;

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

        var ensureCreated = configuration.GetValue("Database:EnsureCreatedOnStartup", true);
        var seedDemoData = configuration.GetValue("Database:SeedDemoDataOnStartup", app.Environment.IsDevelopment());

        if (ensureCreated)
        {
            await dbContext.Database.EnsureCreatedAsync();
            logger.LogInformation("Database schema ensured.");
        }

        await EnsureAuthSchemaCompatibilityAsync(dbContext);

        if (seedDemoData)
        {
            await seeder.SeedAsync(dbContext, CancellationToken.None);
        }
    }

    private static async Task EnsureAuthSchemaCompatibilityAsync(AppDbContext dbContext)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        const string sql = """
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PasswordHash" character varying(512) NOT NULL DEFAULT '';
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LastLoginUtc" timestamp with time zone NULL;
            ALTER TABLE "Users" ALTER COLUMN "FirstName" DROP NOT NULL;
            ALTER TABLE "Users" ALTER COLUMN "LastName" DROP NOT NULL;
            """;

        await dbContext.Database.ExecuteSqlRawAsync(sql);
    }
}
