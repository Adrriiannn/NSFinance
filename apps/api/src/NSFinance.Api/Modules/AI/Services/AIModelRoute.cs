namespace NSFinance.Api.Modules.AI.Services;

public sealed record AIModelRoute(
    AITaskType TaskType,
    AIModelClass ModelClass,
    string Model,
    string Deployment,
    bool IsFallback,
    string Reason,
    IReadOnlyList<string> Notes);
