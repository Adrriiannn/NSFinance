using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Infrastructure.RequestContext;
using NSFinTech.Api.Infrastructure.Seeding;
using NSFinTech.Api.Modules.Accounts.Services;
using NSFinTech.Api.Modules.Audit.Services;
using NSFinTech.Api.Modules.Auth.Services;
using NSFinTech.Api.Modules.Banking.Services;
using NSFinTech.Api.Modules.Categories.Services;
using NSFinTech.Api.Modules.Insights.Services;
using NSFinTech.Api.Modules.Policies.Services;
using NSFinTech.Api.Modules.Support.Services;
using NSFinTech.Api.Modules.Transactions.Services;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Persistence;
using NSFinTech.Shared.Configuration;

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
        services.AddDataProtection();
        ConfigureCors(services, configuration);
        ConfigureRateLimiting(services);

        services.Configure<JwtOptions>(options =>
        {
            configuration.GetSection(JwtOptions.SectionName).Bind(options);
            var signingKeyOverride = configuration[EnvironmentVariableNames.JwtSigningKey];
            if (!string.IsNullOrWhiteSpace(signingKeyOverride))
            {
                options.SigningKey = signingKeyOverride;
            }
        });

        services.Configure<TrueLayerOptions>(options =>
        {
            configuration.GetSection(TrueLayerOptions.SectionName).Bind(options);
            OverrideIfSet(value => options.ClientId = value, configuration[EnvironmentVariableNames.TrueLayerClientId]);
            OverrideIfSet(value => options.ClientSecret = value, configuration[EnvironmentVariableNames.TrueLayerClientSecret]);
            OverrideIfSet(value => options.RedirectUri = value, configuration[EnvironmentVariableNames.TrueLayerRedirectUri]);
            OverrideIfSet(value => options.Environment = value, configuration[EnvironmentVariableNames.TrueLayerEnvironment]);
            OverrideIfSet(value => options.AuthBaseUrl = value, configuration[EnvironmentVariableNames.TrueLayerAuthBaseUrl]);
            OverrideIfSet(value => options.ApiBaseUrl = value, configuration[EnvironmentVariableNames.TrueLayerApiBaseUrl]);
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
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
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
                    RoleClaimType = ClaimTypes.Role
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var subject = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                            ?? context.Principal?.FindFirst("sub")?.Value;
                        var sessionClaim = context.Principal?.FindFirst("sid")?.Value;

                        if (!Guid.TryParse(subject, out var userId) || !Guid.TryParse(sessionClaim, out var sessionId))
                        {
                            context.Fail("Invalid token claims.");
                            return;
                        }

                        var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        var session = await dbContext.Sessions
                            .AsNoTracking()
                            .Include(x => x.User)
                            .SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, context.HttpContext.RequestAborted);

                        if (session is null || session.RevokedUtc is not null || session.ExpiresUtc <= DateTime.UtcNow)
                        {
                            context.Fail("Session is no longer active.");
                            return;
                        }

                        if (session.User is null || session.User.IsDisabled || session.User.IsSuspended)
                        {
                            context.Fail("Account is restricted.");
                        }
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("SupportOrAdmin", policy => policy.RequireRole("support", "admin"));
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
        });

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(GetConnectionString(configuration));
        });

        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<JwtTokenService>();
        services.AddScoped<TokenSecretService>();
        services.AddScoped<AuthAbuseService>();
        services.AddScoped<SessionService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();
        services.AddScoped<ICurrentUserProvider, HttpContextCurrentUserProvider>();
        services.AddScoped<UserService>();
        services.AddScoped<PolicyService>();
        services.AddScoped<SupportService>();
        services.AddScoped<AccountService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<DashboardService>();
        services.AddHttpClient<TrueLayerHttpClient>();
        services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<TrueLayerConfigurationService>();
        services.AddScoped<TrueLayerAuthService>();
        services.AddScoped<TrueLayerTokenService>();
        services.AddScoped<TrueLayerDataService>();
        services.AddScoped<BankConnectionService>();
        services.AddScoped<BankSyncService>();
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

    private static void ConfigureCors(IServiceCollection services, IConfiguration configuration)
    {
        var configuredOrigins =
            configuration[EnvironmentVariableNames.AllowedCorsOrigins]
            ?? configuration["Cors:AllowedOrigins"];

        var defaultOrigins = new[]
        {
            "http://localhost:8081",
            "http://localhost:19006",
            "http://127.0.0.1:19006"
        };

        var origins = (configuredOrigins ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        services.AddCors(options =>
        {
            options.AddPolicy("AppCors", policy =>
            {
                var allowedOrigins = origins.Length > 0 ? origins : defaultOrigins;
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
            });
        });
    }

    private static void ConfigureRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                if (!context.HttpContext.Response.HasStarted)
                {
                    context.HttpContext.Response.ContentType = "application/json";
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new ApiErrorResponse("Too many requests. Please retry later.", "rate_limited"),
                        cancellationToken: token);
                }
            };

            options.AddPolicy("auth-write", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientPartition(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 8,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy("auth-refresh", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientPartition(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy("auth-reset", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientPartition(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 6,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    }));

            options.AddPolicy("provider-callback", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientPartition(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy("support-public", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientPartition(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    }));
        });
    }

    private static string ResolveClientPartition(HttpContext httpContext)
    {
        return httpContext.Connection.RemoteIpAddress?.ToString()
            ?? httpContext.Request.Headers.Host.ToString()
            ?? "unknown";
    }

    private static void OverrideIfSet(
        Action<string> assign,
        string? envValue)
    {
        if (string.IsNullOrWhiteSpace(envValue))
        {
            return;
        }

        assign(envValue.Trim());
    }
}
