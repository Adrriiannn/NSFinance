using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public interface IOperationalFailureRecorder
{
    Task RecordAsync(OperationalFailureRecordInput input, CancellationToken cancellationToken);
}

public sealed record OperationalFailureRecordInput(
    OperationalFailureArea Area,
    OperationalFailureSeverity Severity,
    string FailureType,
    string Fingerprint,
    string? CorrelationId,
    string? SubjectKey,
    string? FailureMessage,
    string? DetailsJson);
