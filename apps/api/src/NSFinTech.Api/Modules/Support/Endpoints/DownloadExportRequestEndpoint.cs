using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Support.Services;

namespace NSFinTech.Api.Modules.Support.Endpoints;

public static class DownloadExportRequestEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid requestId,
        SupportService supportService,
        CancellationToken cancellationToken)
    {
        var result = await supportService.DownloadExportRequestAsync(requestId, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        var payload = result.Value!;
        return Results.File(payload.Bytes, payload.ContentType, payload.FileName);
    }
}