using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class TrueLayerHttpClient(
    HttpClient httpClient,
    IRequestContextAccessor? requestContextAccessor = null,
    ILogger<TrueLayerHttpClient>? logger = null)
{
    public async Task<ServiceResult<string>> PostFormAsync(
        string absoluteUrl,
        IReadOnlyDictionary<string, string> formFields,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, absoluteUrl)
        {
            Content = new FormUrlEncodedContent(formFields)
        };
        ApplyPsuIpHeaderIfAvailable(request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ServiceResult<string>.Fail(payload, "provider_http_error", (int)response.StatusCode);
        }

        return ServiceResult<string>.Ok(payload);
    }

    public async Task<ServiceResult<string>> GetAsync(
        string absoluteUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, absoluteUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        ApplyPsuIpHeaderIfAvailable(request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ServiceResult<string>.Fail(payload, "provider_http_error", (int)response.StatusCode);
        }

        return ServiceResult<string>.Ok(payload);
    }

    private void ApplyPsuIpHeaderIfAvailable(HttpRequestMessage request)
    {
        if (requestContextAccessor is null)
        {
            return;
        }

        var ip = requestContextAccessor.IpAddress?.Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            return;
        }

        if (request.Headers.Contains("X-PSU-IP"))
        {
            return;
        }

        if (!request.Headers.TryAddWithoutValidation("X-PSU-IP", ip))
        {
            logger?.LogDebug("Unable to add X-PSU-IP header to outbound TrueLayer request.");
        }
    }
}
