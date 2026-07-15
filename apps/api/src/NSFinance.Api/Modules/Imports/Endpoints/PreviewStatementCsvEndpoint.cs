using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.Services;
using NSFinance.Api.Modules.Imports.Validators;

namespace NSFinance.Api.Modules.Imports.Endpoints;

internal static class PreviewStatementCsvEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpRequest request,
        StatementImportUploadService uploadService,
        CancellationToken cancellationToken)
    {
        var (form, readError) = await StatementImportEndpointForm.ReadAsync(
            request,
            cancellationToken);
        if (readError is not null)
        {
            return readError.ToApiError();
        }

        var requestError = StatementImportFormParser.TryCreatePreviewRequest(
            form!,
            out var previewRequest);
        if (requestError is not null)
        {
            return requestError.ToApiError();
        }

        var fileError = StatementImportFormParser.GetSingleFile(form!, out var file);
        if (fileError is not null)
        {
            return fileError.ToApiError();
        }

        var result = await uploadService.PreviewAsync(
            file,
            previewRequest!,
            cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}
