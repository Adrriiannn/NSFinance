namespace NSFinance.Api.Persistence.Entities;

public static class ImportJobKinds
{
    public const string Legacy = "legacy";
    public const string StatementCsv = "statement_csv";
}

public static class StatementImportBatchStatuses
{
    public const string Staging = "staging";
    public const string ReadyForReview = "ready_for_review";
    public const string Committed = "committed";
    public const string Failed = "failed";
    public const string Undone = "undone";
    public const string Expired = "expired";
    public const string Discarded = "discarded";
}

public static class StatementImportValidationStatuses
{
    public const string Valid = "valid";
    public const string Invalid = "invalid";
}

public static class StatementImportDuplicateClassifications
{
    public const string None = "none";
    public const string Exact = "exact";
    public const string Likely = "likely";
}

public static class StatementImportReviewDispositions
{
    public const string Included = "included";
    public const string Excluded = "excluded";
    public const string Pending = "pending";
}

public static class StatementImportTimestampPrecisions
{
    public const string Date = "date";
    public const string Instant = "instant";
}

public static class StatementImportIndexNames
{
    public const string ImportJobIdempotency = "UX_ImportJobs_StatementIdempotency";
    public const string ImportJobCommittedSource = "UX_ImportJobs_CommittedStatementSource";
    public const string BatchRowNumber = "UX_StatementImportRows_BatchRow";
    public const string CommittedTransaction = "UX_StatementImportRows_CommittedTransaction";
}

public sealed class StatementImportRow
{
    public Guid Id { get; set; }
    public Guid ImportJobId { get; set; }
    public int RowNumber { get; set; }
    public string RowFingerprint { get; set; } = string.Empty;
    public string? SourceReferenceFingerprint { get; set; }
    public string ValidationStatus { get; set; } = StatementImportValidationStatuses.Valid;
    public string? ValidationCode { get; set; }
    public string DuplicateClassification { get; set; } = StatementImportDuplicateClassifications.None;
    public string ReviewDisposition { get; set; } = StatementImportReviewDispositions.Included;
    public Guid? DuplicateCandidateTransactionId { get; set; }
    public string? SourceEvidenceJson { get; set; }
    public DateTime? EvidenceExpiresUtc { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public DateTime? EffectiveAtUtc { get; set; }
    public string? TimestampPrecision { get; set; }
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public Guid? CommittedTransactionId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public ImportJob? ImportJob { get; set; }
    public Transaction? DuplicateCandidateTransaction { get; set; }
    public Transaction? CommittedTransaction { get; set; }
}
