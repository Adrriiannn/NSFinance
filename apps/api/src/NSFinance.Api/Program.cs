using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.DependencyInjection;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

var configurationBuilder = builder.Configuration;

if (builder.Environment.IsDevelopment())
{
    configurationBuilder.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}

configurationBuilder.AddEnvironmentVariables();

builder.Services
    .AddProblemDetails()
    .AddApiFoundation(builder.Configuration, builder.Environment);

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy", DateTime.UtcNow)))
    .WithName("HealthCheck")
    .WithTags("System");

app.MapModules();

app.Run();

public partial class Program;
