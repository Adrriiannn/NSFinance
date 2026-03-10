namespace NSFinance.Api.Infrastructure.RequestContext;

public interface IRequestContextAccessor
{
    string CorrelationId { get; }
    string SourceChannel { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? Platform { get; }
    string? AppVersion { get; }
}
