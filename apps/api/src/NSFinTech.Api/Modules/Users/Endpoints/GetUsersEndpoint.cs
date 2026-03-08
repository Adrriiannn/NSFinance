using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Modules.Users.DTOs;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Persistence;

namespace NSFinTech.Api.Modules.Users.Endpoints;

public static class GetUsersEndpoint
{
    public static async Task<IResult> HandleAsync(
        AppDbContext dbContext,
        ICurrentUserProvider currentUserProvider,
        CancellationToken cancellationToken)
    {
        var users = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == currentUserProvider.UserId)
            .Select(x => new UserListItemDto(
                x.Id,
                x.Email,
                x.FirstName,
                x.LastName,
                x.CreatedUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(users);
    }
}
