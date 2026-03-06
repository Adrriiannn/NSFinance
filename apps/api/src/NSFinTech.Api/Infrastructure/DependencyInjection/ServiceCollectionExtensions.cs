using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Persistence;
using NSFinTech.Shared.Configuration;

namespace NSFinTech.Api.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(GetConnectionString(configuration));
        });

        return services;
    }

    private static string GetConnectionString(IConfiguration configuration)
    {
        var connectionString =
            configuration[EnvironmentVariableNames.DatabaseConnectionString]
            ?? configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Database connection string is missing. Set {EnvironmentVariableNames.DatabaseConnectionString} or ConnectionStrings:DefaultConnection.");
        }

        return connectionString;
    }
}
