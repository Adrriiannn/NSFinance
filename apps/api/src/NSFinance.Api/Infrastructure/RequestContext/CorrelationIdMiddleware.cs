namespace NSFinance.Api.Infrastructure.RequestContext;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string CorrelationIdItemKey = "__CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("n");
        }

        context.TraceIdentifier = correlationId;
        context.Items[CorrelationIdItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await next(context);
    }
}
