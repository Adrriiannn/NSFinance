namespace NSFinance.Api.Modules.Transactions.DTOs;

public sealed record UpdateTransactionMetadataRequest(
    string? Reason,
    string? Notes,
    int? TaxonomyCategoryId,
    int? TaxonomySubcategoryId);
