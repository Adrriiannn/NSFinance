using NSFinance.Api.Common.Contracts;

namespace NSFinance.Api.Modules.Imports.Endpoints;

internal static class StatementImportEndpointForm
{
    public static async Task<(IFormCollection? Form, ServiceError? Error)> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType
            || !request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return (null, new ServiceError(
                "A multipart/form-data request is required.",
                "statement_import_multipart_required",
                StatusCodes.Status415UnsupportedMediaType));
        }

        try
        {
            return (await request.ReadFormAsync(cancellationToken), null);
        }
        catch (InvalidDataException)
        {
            return (null, new ServiceError(
                "The multipart upload is invalid or exceeds the allowed size.",
                "statement_import_multipart_invalid",
                StatusCodes.Status400BadRequest));
        }
        catch (BadHttpRequestException exception)
        {
            return (null, new ServiceError(
                exception.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "The multipart upload exceeds the allowed size."
                    : "The multipart upload is invalid.",
                exception.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "statement_import_multipart_too_large"
                    : "statement_import_multipart_invalid",
                exception.StatusCode));
        }
    }
}
