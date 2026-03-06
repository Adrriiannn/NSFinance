using NSFinTech.Api.Modules.Users.DTOs;

namespace NSFinTech.Api.Modules.Users.Endpoints;

public static class GetUsersEndpoint
{
    public static Task<IResult> HandleAsync()
    {
        IReadOnlyList<UserListItemDto> users = [];
        return Task.FromResult(Results.Ok(users) as IResult);
    }
}
