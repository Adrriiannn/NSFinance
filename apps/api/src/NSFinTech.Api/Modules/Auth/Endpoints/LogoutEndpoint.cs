namespace NSFinTech.Api.Modules.Auth.Endpoints;

public static class LogoutEndpoint
{
    public static IResult HandleAsync()
    {
        return Results.NoContent();
    }
}
