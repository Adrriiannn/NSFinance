using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NSFinance.Shared.Configuration;

namespace NSFinance.Api.Persistence.DesignTime;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true);

        var configuration = configurationBuilder
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration[EnvironmentVariableNames.DatabaseConnectionString]
            ?? configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Database connection string is missing. Set {EnvironmentVariableNames.DatabaseConnectionString}, " +
                "or ConnectionStrings:DefaultConnection.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
