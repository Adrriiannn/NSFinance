using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Infrastructure.Seeding;
using NSFinance.Api.Infrastructure.Startup;
using NSFinance.Api.Modules.Accounts.Services;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Auth.Services;
using NSFinance.Api.Modules.Auth.Configuration;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Categories.Services;
using NSFinance.Api.Modules.Insights.Services;
using NSFinance.Api.Modules.Imports.Mapping;
using NSFinance.Api.Modules.Imports.Parsing;
using NSFinance.Api.Modules.Imports.Services;
using NSFinance.Api.Modules.Policies.Services;
using NSFinance.Api.Modules.Support.Services;
using NSFinance.Api.Modules.Transactions.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Shared.Configuration;

namespace NSFinance.Api.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiFoundation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddCheck<BankingOperationJobHealthCheck>(
                "banking_operation_jobs",
                tags: ["ready"]);
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
        ConfigureDataProtection(services, configuration);
        ConfigureCors(services, configuration);
        ConfigureRateLimiting(services);

        services.Configure<JwtOptions>(options =>
        {
            configuration.GetSection(JwtOptions.SectionName).Bind(options);
            var signingKeyOverride = ResolveEnvironmentValue(
                configuration,
                EnvironmentVariableNames.JwtSigningKey);
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
        services.Configure<BankingSyncOptions>(options =>
        {
            configuration.GetSection(BankingSyncOptions.SectionName).Bind(options);
            if (options.ManualCooldownMinutes <= 0)
            {
                options.ManualCooldownMinutes = 10;
            }

            if (options.AutoSyncIntervalMinutes <= 0)
            {
                options.AutoSyncIntervalMinutes = 10;
            }

            if (options.StaleSyncPendingRecoveryMinutes <= 0)
            {
                options.StaleSyncPendingRecoveryMinutes = 10;
            }

            if (options.ProviderRateLimitBackoffMinutes <= 0)
            {
                options.ProviderRateLimitBackoffMinutes = 10;
            }

            if (options.DurableJobMaxAttempts <= 0)
            {
                options.DurableJobMaxAttempts = 5;
            }

            if (options.DurableJobLeaseSeconds <= 0)
            {
                options.DurableJobLeaseSeconds = 120;
            }

            if (options.DurableJobPollMilliseconds <= 0)
            {
                options.DurableJobPollMilliseconds = 500;
            }

            if (options.SyncExecutionLeaseSeconds <= 0)
            {
                options.SyncExecutionLeaseSeconds = 120;
            }

            if (options.UnattendedSyncIntervalMinutes <= 0)
            {
                options.UnattendedSyncIntervalMinutes = 720;
            }

            if (options.UnattendedSyncSweepMinutes <= 0)
            {
                options.UnattendedSyncSweepMinutes = 15;
            }
        });
        services.Configure<BankConnectionAttemptOptions>(options =>
        {
            configuration.GetSection(BankConnectionAttemptOptions.SectionName).Bind(options);
            if (options.SweepIntervalSeconds <= 0)
            {
                options.SweepIntervalSeconds = 60;
            }

            if (options.ExpiryBatchSize <= 0)
            {
                options.ExpiryBatchSize = 64;
            }

            if (options.StaleProcessingExpiryMinutes <= 0)
            {
                options.StaleProcessingExpiryMinutes = 120;
            }
        });
        services.Configure<MerchantOperationalResilienceOptions>(options =>
        {
            configuration.GetSection(MerchantOperationalResilienceOptions.SectionName).Bind(options);
            if (options.UnresolvedBaseCooldownMinutes <= 0)
            {
                options.UnresolvedBaseCooldownMinutes = 30;
            }

            if (options.UnresolvedMaxCooldownMinutes < options.UnresolvedBaseCooldownMinutes)
            {
                options.UnresolvedMaxCooldownMinutes = Math.Max(
                    options.UnresolvedBaseCooldownMinutes,
                    1_440);
            }

            if (options.RejectedCooldownMinutes <= 0)
            {
                options.RejectedCooldownMinutes = 240;
            }

            if (options.HighOccurrenceAccelerationThreshold <= 1)
            {
                options.HighOccurrenceAccelerationThreshold = 10;
            }

            if (options.HighOccurrenceAccelerationMinutes <= 0)
            {
                options.HighOccurrenceAccelerationMinutes = 30;
            }

            if (options.ActiveMerchantValidationDays <= 0)
            {
                options.ActiveMerchantValidationDays = 120;
            }

            if (options.LowConfidenceMerchantValidationDays <= 0)
            {
                options.LowConfidenceMerchantValidationDays = 30;
            }

            if (options.CautiousMerchantValidationDays <= 0)
            {
                options.CautiousMerchantValidationDays = 21;
            }
        });
        services.Configure<MerchantAIGovernanceOptions>(options =>
        {
            configuration.GetSection(MerchantAIGovernanceOptions.SectionName).Bind(options);
            if (options.MaxAICallsPerSyncRun <= 0)
            {
                options.MaxAICallsPerSyncRun = 8;
            }

            if (options.MaxAICallsPerConnectionPerRun <= 0)
            {
                options.MaxAICallsPerConnectionPerRun = 5;
            }

            if (options.MaxAICallsPerUserPer24h <= 0)
            {
                options.MaxAICallsPerUserPer24h = 10;
            }

            if (options.MerchantInvestigationCooldownDays <= 0)
            {
                options.MerchantInvestigationCooldownDays = 7;
            }

            if (options.FailureCooldownHours <= 0)
            {
                options.FailureCooldownHours = 24;
            }

            if (options.LowConfidenceCooldownHours <= 0)
            {
                options.LowConfidenceCooldownHours = 72;
            }

            if (options.MinimumOccurrencesForExpectedValue <= 1)
            {
                options.MinimumOccurrencesForExpectedValue = 2;
            }

            if (options.MeaningfulSpendThreshold <= 0m)
            {
                options.MeaningfulSpendThreshold = 75m;
            }

            if (options.QueueTopMerchantsPerRun <= 0)
            {
                options.QueueTopMerchantsPerRun = Math.Max(1, options.MaxAICallsPerSyncRun);
            }

            if (options.QueueTopMerchantsPerConnectionPerRun <= 0)
            {
                options.QueueTopMerchantsPerConnectionPerRun = Math.Max(1, options.MaxAICallsPerConnectionPerRun);
            }

            if (options.InvestigationLockTimeoutMinutes <= 0)
            {
                options.InvestigationLockTimeoutMinutes = 20;
            }

            if (options.ExpectedValueThreshold <= 0d)
            {
                options.ExpectedValueThreshold = 0.85d;
            }

            if (options.ExpectedValueCountWeight <= 0d)
            {
                options.ExpectedValueCountWeight = 0.38d;
            }

            if (options.ExpectedValueSpendWeight <= 0d)
            {
                options.ExpectedValueSpendWeight = 0.26d;
            }

            if (options.ExpectedValueRecencyWeight <= 0d)
            {
                options.ExpectedValueRecencyWeight = 0.16d;
            }

            if (options.ExpectedValueReusabilityWeight <= 0d)
            {
                options.ExpectedValueReusabilityWeight = 0.20d;
            }

            if (options.ExpectedValueLowConfidencePenaltyWeight < 0d)
            {
                options.ExpectedValueLowConfidencePenaltyWeight = 0.20d;
            }

            if (options.QueuePriorityCountWeight <= 0d)
            {
                options.QueuePriorityCountWeight = 0.33d;
            }

            if (options.QueuePrioritySpendWeight <= 0d)
            {
                options.QueuePrioritySpendWeight = 0.22d;
            }

            if (options.QueuePriorityRecencyWeight <= 0d)
            {
                options.QueuePriorityRecencyWeight = 0.18d;
            }

            if (options.QueuePriorityImpactWeight <= 0d)
            {
                options.QueuePriorityImpactWeight = 0.17d;
            }

            if (options.QueuePriorityDomainWeight <= 0d)
            {
                options.QueuePriorityDomainWeight = 0.10d;
            }

            if (options.SpendNormalizerAmount <= 0m)
            {
                options.SpendNormalizerAmount = 250m;
            }

            if (options.ExpectedValueRecencyHorizonDays <= 0)
            {
                options.ExpectedValueRecencyHorizonDays = 21;
            }
        });
        ValidateTrueLayerConfiguration(configuration);

        services.Configure<GoogleAuthOptions>(options =>
        {
            configuration.GetSection(GoogleAuthOptions.SectionName).Bind(options);
            OverrideIfSet(
                value => options.ClientId = value,
                ResolveEnvironmentValue(
                    configuration,
                    EnvironmentVariableNames.GoogleClientId));
            OverrideIfSet(
                value => options.WebClientId = value,
                ResolveEnvironmentValue(
                    configuration,
                    EnvironmentVariableNames.GoogleWebClientId));
            OverrideIfSet(
                value => options.AndroidClientIdProd = value,
                ResolveEnvironmentValue(
                    configuration,
                    EnvironmentVariableNames.GoogleAndroidClientIdProd));
        });

        services.Configure<IdentitySecurityOptions>(options =>
        {
            configuration.GetSection(IdentitySecurityOptions.SectionName).Bind(options);
            OverrideIfSet(
                value => options.CodePepper = value,
                ResolveEnvironmentValue(configuration, EnvironmentVariableNames.IdentityCodePepper));
        });

        services.Configure<TransactionalEmailOptions>(options =>
        {
            configuration.GetSection(TransactionalEmailOptions.SectionName).Bind(options);
            OverrideIfSet(
                value => options.Endpoint = value,
                ResolveEnvironmentValue(configuration, EnvironmentVariableNames.EmailEndpoint));
            OverrideIfSet(
                value => options.SenderAddress = value,
                ResolveEnvironmentValue(configuration, EnvironmentVariableNames.EmailSenderAddress));
        });

        services.Configure<MicrosoftAuthOptions>(options =>
        {
            configuration.GetSection(MicrosoftAuthOptions.SectionName).Bind(options);
            OverrideIfSet(
                value => options.ClientId = value,
                ResolveEnvironmentValue(configuration, EnvironmentVariableNames.MicrosoftClientId));
        });

        services.Configure<TurnstileOptions>(options =>
        {
            configuration.GetSection(TurnstileOptions.SectionName).Bind(options);
        });

        services.Configure<PasswordPolicyOptions>(options =>
        {
            configuration.GetSection(PasswordPolicyOptions.SectionName).Bind(options);
        });

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var signingKeyFromEnv = ResolveEnvironmentValue(
            configuration,
            EnvironmentVariableNames.JwtSigningKey);
        if (!string.IsNullOrWhiteSpace(signingKeyFromEnv))
        {
            jwtOptions.SigningKey = signingKeyFromEnv;
        }

        var signingKey = Encoding.UTF8.GetBytes(jwtOptions.SigningKey);
        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");
        }

        if (jwtOptions.SigningKey.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"A non-placeholder JWT signing key is required. " +
                $"Set Jwt:SigningKey, Jwt__SigningKey, {EnvironmentVariableNames.JwtSigningKey}, " +
                "and ensure it is not a placeholder value.");
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
        services.AddSingleton<IIdentityCodeService, IdentityCodeService>();
        services.AddSingleton<IdentityPayloadProtector>();
        services.AddSingleton<MfaSecretProtector>();
        services.AddSingleton<IdentityEmailRenderer>();
        services.AddSingleton<ITransactionalEmailSender, AzureCommunicationEmailSender>();
        services.AddScoped<TransactionalMessageService>();
        services.AddScoped<IdentityChallengeService>();
        services.AddScoped<TotpMfaService>();
        services.AddScoped<MfaTrustedDeviceService>();
        services.AddScoped<IGoogleIdTokenVerifier, GoogleIdTokenVerifier>();
        services.AddScoped<GoogleAuthService>();
        services.AddHttpClient<IMicrosoftAccessTokenVerifier, MicrosoftAccessTokenVerifier>();
        services.AddScoped<MicrosoftAuthService>();
        services.AddHttpClient<PwnedPasswordService>((sp, client) =>
        {
            var passwordPolicyOptions = sp.GetRequiredService<IOptions<PasswordPolicyOptions>>().Value;
            client.BaseAddress = new Uri(passwordPolicyOptions.BreachApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(2, passwordPolicyOptions.BreachApiTimeoutSeconds));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NSFinance.Api/1.0");
        });
        services.AddScoped<PasswordPolicyService>();
        services.AddHttpClient<TurnstileVerificationService>();
        services.AddScoped<AuthAbuseService>();
        services.AddScoped<SessionService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();
        services.AddScoped<ICurrentUserProvider, HttpContextCurrentUserProvider>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<UserService>();
        services.AddScoped<PolicyService>();
        services.AddScoped<SupportService>();
        services.AddScoped<AccountBalanceReadService>();
        services.AddScoped<AccountService>();
        services.AddSingleton<IStatementImportMappingEngine, StatementImportMappingEngine>();
        services.AddSingleton<IStatementCsvParser, StatementCsvParser>();
        services.AddScoped<StatementImportBatchService>();
        services.AddScoped<StatementImportLifecycleService>();
        services.AddScoped<StatementImportReviewService>();
        services.AddScoped<StatementImportUploadService>();
        services.AddScoped<StatementImportEvidenceCleanupService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ExpenseTaxonomyService>();
        services.AddScoped<ExpenseTrackerService>();
        services.AddScoped<ExpensePlanService>();
        services.AddScoped<ExpensePlanCommunityService>();
        services.AddAIIntegration(configuration);
        services.AddHttpClient<TrueLayerHttpClient>();
        services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<TrueLayerConfigurationService>();
        services.AddScoped<BankConnectionAttemptService>();
        services.AddScoped<TrueLayerAuthService>();
        services.AddScoped<TrueLayerTokenService>();
        services.AddScoped<TrueLayerDataService>();
        services.AddScoped<BankConnectionService>();
        services.AddScoped<InferredFinancialCommitmentService>();
        services.AddScoped<FinancialCommitmentMergePolicy>();
        services.AddScoped<UserFinancialCommitmentService>();
        services.AddScoped<FinancialCommitmentReadService>();
        services.AddSingleton<DeterministicCategorizationMetrics>();
        services.AddScoped<ProviderCapabilityRegistry>();
        services.AddScoped<NarrativeSignalExtractor>();
        services.AddScoped<TransactionNormalizationService>();
        services.AddSingleton<MerchantDescriptorNormalizer>();
        services.AddScoped<IMerchantRegistryService, MerchantRegistryService>();
        services.AddScoped<IMerchantAcceptancePolicy, MerchantAcceptancePolicy>();
        services.AddScoped<IDomainTriggerPolicyService, DomainTriggerPolicyService>();
        services.AddScoped<IMerchantInvestigationQueueService, MerchantInvestigationQueueService>();
        services.AddScoped<IAITriggerGateService, AITriggerGateService>();
        services.AddScoped<IMerchantResolutionService, MerchantResolutionService>();
        services.AddScoped<TransactionFeatureExtractor>();
        services.AddScoped<IRecurringPatternService, RecurringPatternService>();
        services.AddScoped<TransferPairingEngine>();
        services.AddScoped<SavingsRoutingPolicy>();
        services.AddScoped<SavingsTransferClassifier>();
        services.AddScoped<DeterministicClassificationRetryPlanner>();
        services.AddScoped<DeterministicClassificationPersistenceService>();
        services.AddScoped<DeterministicTransactionCategorizationService>();
        services.AddScoped<DeterministicReclassificationTriggerService>();
        services.AddScoped<BankSyncExecutionLeaseStore>();
        services.AddScoped<BankSyncService>();
        services.AddScoped<BankGlobalSyncService>();
        services.AddScoped<BankingOperationJobStore>();
        services.AddSingleton<BankConnectionAttemptLifecycleBackgroundWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<BankConnectionAttemptLifecycleBackgroundWorker>());
        services.AddSingleton<BankDeterministicEnrichmentBackgroundWorker>();
        services.AddSingleton<IBankDeterministicEnrichmentQueue>(sp => sp.GetRequiredService<BankDeterministicEnrichmentBackgroundWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<BankDeterministicEnrichmentBackgroundWorker>());
        services.AddSingleton<TrueLayerSyncBackgroundWorker>();
        services.AddSingleton<ITrueLayerSyncQueue>(sp => sp.GetRequiredService<TrueLayerSyncBackgroundWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<TrueLayerSyncBackgroundWorker>());
        services.AddSingleton<BankDisconnectBackgroundWorker>();
        services.AddSingleton<IBankDisconnectQueue>(sp => sp.GetRequiredService<BankDisconnectBackgroundWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<BankDisconnectBackgroundWorker>());
        services.AddScoped<PolicyDataSeeder>();
        services.AddHostedService<DatabaseInitializationHostedService>();
        services.AddHostedService<TransactionalMessageBackgroundWorker>();
        services.AddHostedService<StatementImportEvidenceCleanupBackgroundWorker>();

        return services;
    }

    private static string GetConnectionString(IConfiguration configuration)
    {
        var connectionString =
            ResolveEnvironmentValue(
                configuration,
                EnvironmentVariableNames.DatabaseConnectionString)
            ?? configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Database connection string is missing. Set {EnvironmentVariableNames.DatabaseConnectionString} " +
                "or ConnectionStrings:DefaultConnection.");
        }

        return connectionString;
    }

    private static void ConfigureCors(IServiceCollection services, IConfiguration configuration)
    {
        var configuredOrigins =
            ResolveEnvironmentValue(
                configuration,
                EnvironmentVariableNames.AllowedCorsOrigins)
            ?? configuration["Cors:AllowedOrigins"];

        var origins = (configuredOrigins ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        services.AddCors(options =>
        {
            options.AddPolicy("AppCors", policy =>
            {
                if (origins.Length > 0)
                {
                    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
                    return;
                }

                policy.SetIsOriginAllowed(_ => false).AllowAnyHeader().AllowAnyMethod();
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

            options.AddPolicy("password-policy-check", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientPartition(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy("statement-import-upload", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveAuthenticatedPartition(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 6,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    }));

            options.AddPolicy("statement-import-mutation", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveAuthenticatedPartition(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
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

            options.AddPolicy("places-photo", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientPartition(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 120,
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

    private static string ResolveAuthenticatedPartition(HttpContext httpContext)
    {
        var subject = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return string.IsNullOrWhiteSpace(subject)
            ? $"client:{ResolveClientPartition(httpContext)}"
            : $"user:{subject}";
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

    private static string? ResolveEnvironmentValue(
        IConfiguration configuration,
        string key)
    {
        return configuration[key];
    }

    private static void ConfigureDataProtection(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var keysPath = ResolveDataProtectionKeyRingPath(configuration);
        var dataProtectionBuilder = services
            .AddDataProtection()
            .SetApplicationName("NSFinance.Api");

        if (string.IsNullOrWhiteSpace(keysPath))
        {
            return;
        }

        Directory.CreateDirectory(keysPath);
        dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    }

    private static string? ResolveDataProtectionKeyRingPath(IConfiguration configuration)
    {
        var configuredPath =
            ResolveEnvironmentValue(configuration, EnvironmentVariableNames.DataProtectionKeysPath)
            ?? configuration["DataProtection:KeysPath"];

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath.Trim();
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return Path.Combine(home, "ASP.NET", "DataProtection-Keys");
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NSFinance",
                "DataProtection-Keys")
            : "/home/ASP.NET/DataProtection-Keys";
    }

    private static void ValidateTrueLayerConfiguration(IConfiguration configuration)
    {
        var options = new TrueLayerOptions();
        configuration.GetSection(TrueLayerOptions.SectionName).Bind(options);
        OverrideIfSet(value => options.ClientId = value, configuration[EnvironmentVariableNames.TrueLayerClientId]);
        OverrideIfSet(value => options.ClientSecret = value, configuration[EnvironmentVariableNames.TrueLayerClientSecret]);
        OverrideIfSet(value => options.RedirectUri = value, configuration[EnvironmentVariableNames.TrueLayerRedirectUri]);
        OverrideIfSet(value => options.Environment = value, configuration[EnvironmentVariableNames.TrueLayerEnvironment]);
        OverrideIfSet(value => options.AuthBaseUrl = value, configuration[EnvironmentVariableNames.TrueLayerAuthBaseUrl]);
        OverrideIfSet(value => options.ApiBaseUrl = value, configuration[EnvironmentVariableNames.TrueLayerApiBaseUrl]);

        var validation = new TrueLayerConfigurationService(Options.Create(options)).Resolve();
        if (!validation.Succeeded)
        {
            throw new InvalidOperationException(
                $"TrueLayer configuration is invalid: {validation.Error!.Code} - {validation.Error.Message}");
        }
    }
}


