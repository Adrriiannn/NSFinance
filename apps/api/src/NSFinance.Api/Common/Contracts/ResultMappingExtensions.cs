namespace NSFinance.Api.Common.Contracts;

public static class ResultMappingExtensions
{
    public static IResult ToApiError(this ServiceError error)
    {
        return Results.Json(
            new ApiErrorResponse(error.Message, error.Code),
            statusCode: error.StatusCode);
    }
}
