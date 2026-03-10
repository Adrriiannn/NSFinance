using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Users.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Modules.Users.Validators;

namespace NSFinance.Api.Modules.Users.Endpoints;

public static class UpdateUserPreferencesEndpoint
{
    public static async Task<IResult> HandleAsync(
        UpdateUserPreferenceRequest request,
        UserService userService,
        CancellationToken cancellationToken)
    {
        var errors = UpdateUserPreferenceValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await userService.UpdatePreferencesAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
