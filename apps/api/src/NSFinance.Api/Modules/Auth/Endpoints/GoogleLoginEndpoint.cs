using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Services;
using NSFinance.Api.Modules.Auth.Validators;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class GoogleLoginEndpoint
{
    public static async Task<IResult> HandleAsync(
        GoogleLoginRequest request,
        AuthService authService,
        IHostEnvironment hostEnvironment,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("GoogleLoginEndpoint");
        var tokenSummary = SummarizeToken(request.IdToken);
        if (hostEnvironment.IsDevelopment())
        {
            logger.LogInformation(
                "Google login endpoint hit path=/api/auth/google hasIdToken={HasIdToken} idTokenLength={IdTokenLength} idTokenPrefix={IdTokenPrefix}",
                tokenSummary.HasToken,
                tokenSummary.TokenLength,
                tokenSummary.TokenPrefix);
        }

        var errors = GoogleLoginRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            if (hostEnvironment.IsDevelopment())
            {
                logger.LogWarning("Google login endpoint validation failed path=/api/auth/google errors={ErrorKeys}", string.Join(",", errors.Keys));
            }
            return Results.ValidationProblem(errors);
        }

        var result = await authService.LoginWithGoogleAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            if (hostEnvironment.IsDevelopment())
            {
                logger.LogWarning(
                    "Google login endpoint failed path=/api/auth/google code={Code} statusCode={StatusCode} message={Message}",
                    result.Error?.Code,
                    result.Error?.StatusCode,
                    result.Error?.Message);
            }
            return result.Error!.ToApiError();
        }

        if (hostEnvironment.IsDevelopment())
        {
            logger.LogInformation("Google login endpoint succeeded path=/api/auth/google userId={UserId}", result.Value?.User.Id);
        }

        return Results.Ok(result.Value);
    }

    private static (bool HasToken, int TokenLength, string TokenPrefix) SummarizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, 0, string.Empty);
        }

        var trimmed = token.Trim();
        var prefixLength = Math.Min(10, trimmed.Length);
        return (true, trimmed.Length, trimmed[..prefixLength]);
    }
}
