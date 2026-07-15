namespace NSFinance.Api.Persistence.Entities;

public class ImportJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Kind { get; set; } = ImportJobKinds.Legacy;
    public string Status { get; set; } = string.Empty;
    public string? SourceFingerprint { get; set; }
    public string? MappingFingerprint { get; set; }
    public string? ParserVersion { get; set; }
    public string? MappingVersion { get; set; }
    public string? MappingJson { get; set; }
    public string? AccountCurrency { get; set; }
    public string? Locale { get; set; }
    public string? TimeZoneId { get; set; }
    public long? FileSizeBytes { get; set; }
    public int TotalRowCount { get; set; }
    public int ValidRowCount { get; set; }
    public int InvalidRowCount { get; set; }
    public int ExactDuplicateRowCount { get; set; }
    public int LikelyDuplicateRowCount { get; set; }
    public int IncludedRowCount { get; set; }
    public int CommittedRowCount { get; set; }
    public int Revision { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? ReadyForReviewUtc { get; set; }
    public DateTime? CommittedUtc { get; set; }
    public DateTime? UndoneUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public DateTime? FailedUtc { get; set; }
    public string? FailureCode { get; set; }

    public User? User { get; set; }
    public FinancialAccount? FinancialAccount { get; set; }
    public ICollection<StatementImportRow> Rows { get; set; } = [];
}
