namespace NSFinance.Api.Modules.ExpenseTracker.DTOs;

public sealed record PublishExpensePlanRequest(
    Guid? SourcePlanId,
    string PublicTitle,
    string? PublicDescription,
    IReadOnlyList<string>? Tags);

public sealed record UpdateExpensePlanPublicationRequest(
    string PublicTitle,
    string? PublicDescription,
    IReadOnlyList<string>? Tags);

public sealed record BrowseExpensePlanPublicationsRequest(
    string? Search,
    string? Sort,
    string? PlanType,
    string? Creator,
    bool TemplatesOnly,
    int? Take);

public sealed record ReportExpensePlanPublicationRequest(
    string Reason,
    string? Notes);
