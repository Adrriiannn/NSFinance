namespace NSFinTech.Api.Infrastructure.RequestContext;

public sealed class HttpRequestContextAccessor(IHttpContextAccessor httpContextAccessor) : IRequestContextAccessor
{
    public string CorrelationId
    {
        get
        {
            var context = httpContextAccessor.HttpContext;
            if (context is null)
            {
                return Guid.NewGuid().ToString("n");
            }

            if (context.Items.TryGetValue(CorrelationIdMiddleware.CorrelationIdItemKey, out var value) && value is string correlationId)
            {
                return correlationId;
            }

            return context.TraceIdentifier;
        }
    }

    public string SourceChannel => "api";

    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string? Platform => httpContextAccessor.HttpContext?.Request.Headers["x-platform"].ToString();

    public string? AppVersion => httpContextAccessor.HttpContext?.Request.Headers["x-app-version"].ToString();
}
