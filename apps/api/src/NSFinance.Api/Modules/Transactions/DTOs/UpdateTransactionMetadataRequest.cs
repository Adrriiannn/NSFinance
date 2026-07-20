namespace NSFinance.Api.Modules.Transactions.DTOs;

public sealed record UpdateTransactionMetadataRequest(
    string? Reason,
    string? Notes,
    int? TaxonomyCategoryId,
    int? TaxonomySubcategoryId,
    // Explicit merchant-scope correction (CAT-001): when true, the correction
    // also teaches the knowledge base so every transaction from this merchant
    // - past and future - follows. Omitted or false = this transaction only.
    bool? LearnMerchant = null);
