using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Users.DTOs;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Modules.Users.Validators;

namespace NSFinTech.Api.Modules.Users.Endpoints;

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
