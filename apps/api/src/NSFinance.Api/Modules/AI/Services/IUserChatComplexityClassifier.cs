namespace NSFinance.Api.Modules.AI.Services;

public sealed record UserChatComplexityEvaluation(
    UserChatComplexity Complexity,
    IReadOnlyList<string> ReasonCodes,
    int ConstraintCount,
    bool FinancialReasoningIntent,
    bool RankingIntent,
    bool MultiStepLanguageDetected);

public interface IUserChatComplexityClassifier
{
    UserChatComplexityEvaluation Evaluate(UserChatRequest request);
}
