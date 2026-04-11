namespace NSFinance.Api.Modules.AI.Services;

public interface IAIModelRouter
{
    AIModelRoute Resolve(AITaskType taskType, AIModelClass preferredModelClass, string? complexityHint = null);
}
