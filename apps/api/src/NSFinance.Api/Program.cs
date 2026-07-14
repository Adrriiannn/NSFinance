using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.DependencyInjection;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services
    .AddProblemDetails()
    .AddApiFoundation(builder.Configuration);

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRateLimiter();
app.UseCors("AppCors");

app.UseSwagger();
app.UseSwaggerUI();
app.UseHsts();
app.UseHttpsRedirection();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy", DateTime.UtcNow)))
    .WithName("HealthCheck")
    .WithTags("System");

app.MapGet("/health/ready", async (
        HealthCheckService healthCheckService,
        CancellationToken cancellationToken) =>
    {
        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("ready"),
            cancellationToken);
        var statusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        return Results.Json(
            new HealthResponse(report.Status.ToString(), DateTime.UtcNow),
            statusCode: statusCode);
    })
    .WithName("ReadinessCheck")
    .WithTags("System");

app.MapModules();

app.Run();

public partial class Program;
