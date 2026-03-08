namespace NSFinTech.Api.Common.Contracts;

public sealed record ApiErrorResponse(string Message, string? Code = null);
