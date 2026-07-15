using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.Services;
using NSFinance.Api.Modules.Imports.Validators;

namespace NSFinance.Api.Modules.Imports.Endpoints;

internal static class InspectStatementCsvEndpoint
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

        var formError = StatementImportFormParser.ValidateInspectionForm(form!);
        if (formError is not null)
        {
            return formError.ToApiError();
        }

        var fileError = StatementImportFormParser.GetSingleFile(form!, out var file);
        if (fileError is not null)
        {
            return fileError.ToApiError();
        }

        var result = await uploadService.InspectAsync(
            file,
            form!["delimiter"].FirstOrDefault(),
            cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}
