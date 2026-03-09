using System.Net.Http.Headers;
using NSFinTech.Api.Common.Contracts;

namespace NSFinTech.Api.Modules.Banking.Services;

public sealed class TrueLayerHttpClient(HttpClient httpClient)
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

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ServiceResult<string>.Fail(payload, "provider_http_error", (int)response.StatusCode);
        }

        return ServiceResult<string>.Ok(payload);
    }
}
