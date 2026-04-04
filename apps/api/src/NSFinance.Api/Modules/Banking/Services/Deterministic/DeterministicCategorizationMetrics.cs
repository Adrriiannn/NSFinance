using System.Diagnostics.Metrics;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class DeterministicCategorizationMetrics
{
    private readonly Meter meter = new("NSFinance.DeterministicCategorization", "1.0.0");

    public Counter<long> EvalTotal { get; }
    public Histogram<double> EvalDurationMs { get; }
    public Counter<long> ClassifiedTotal { get; }
    public Counter<long> DeferredTotal { get; }
    public Counter<long> AmbiguousTotal { get; }
    public Histogram<double> TerminalRatio { get; }
    public Histogram<double> PairingSuccessRatio { get; }
    public Counter<long> FalsePositiveCorrectionTotal { get; }

    public DeterministicCategorizationMetrics()
    {
        EvalTotal = meter.CreateCounter<long>("deterministic_eval_total");
        EvalDurationMs = meter.CreateHistogram<double>("deterministic_eval_duration_ms");
        ClassifiedTotal = meter.CreateCounter<long>("deterministic_classified_total");
        DeferredTotal = meter.CreateCounter<long>("deterministic_deferred_total");
        AmbiguousTotal = meter.CreateCounter<long>("deterministic_ambiguous_total");
        TerminalRatio = meter.CreateHistogram<double>("deterministic_terminal_ratio");
        PairingSuccessRatio = meter.CreateHistogram<double>("transfer_pairing_success_ratio");
        FalsePositiveCorrectionTotal = meter.CreateCounter<long>("transfer_pairing_false_positive_correction_total");
    }
}
