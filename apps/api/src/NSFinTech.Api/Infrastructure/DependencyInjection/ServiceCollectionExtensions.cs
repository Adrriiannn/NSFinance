using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NSFinTech.Api.Infrastructure.Seeding;
using NSFinTech.Api.Modules.Accounts.Services;
using NSFinTech.Api.Modules.Auth.Services;
using NSFinTech.Api.Modules.Categories.Services;
using NSFinTech.Api.Modules.Insights.Services;
using NSFinTech.Api.Modules.Transactions.Services;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Persistence;
using NSFinTech.Shared.Configuration;
using System.Text;

namespace NSFinTech.Api.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT access token."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Id = "Bearer",
                            Type = ReferenceType.SecurityScheme
                        }
                    },
                    []
                }
            });
        });
        services.AddHttpContextAccessor();
        services.AddCors(options =>
        {
            options.AddPolicy("DevCors", policy =>
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            });
        });

        services.Configure<JwtOptions>(options =>
        {
            configuration.GetSection(JwtOptions.SectionName).Bind(options);
            var signingKeyOverride = configuration[EnvironmentVariableNames.JwtSigningKey];
            if (!string.IsNullOrWhiteSpace(signingKeyOverride))
            {
                options.SigningKey = signingKeyOverride;
            }
        });

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var signingKeyFromEnv = configuration[EnvironmentVariableNames.JwtSigningKey];
        if (!string.IsNullOrWhiteSpace(signingKeyFromEnv))
        {
            jwtOptions.SigningKey = signingKeyFromEnv;
        }
        var signingKey = Encoding.UTF8.GetBytes(jwtOptions.SigningKey);
        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters for development safety.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "email",
                    RoleClaimType = "role"
                };
            });

        services.AddAuthorization();

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(GetConnectionString(configuration));
        });

        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<JwtTokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<ICurrentUserProvider, HttpContextCurrentUserProvider>();
        services.AddScoped<AccountService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<DevelopmentDataSeeder>();

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
