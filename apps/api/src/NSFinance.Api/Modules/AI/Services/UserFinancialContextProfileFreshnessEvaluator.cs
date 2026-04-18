using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface IUserFinancialProfileFreshnessEvaluator
{
    UserFinancialProfileFreshnessState Evaluate(DateTime nowUtc, DateTime lastRefreshedUtc);
}

public sealed class UserFinancialProfileFreshnessEvaluator(
    IOptions<CompanionProfileLifecycleOptions> options) : IUserFinancialProfileFreshnessEvaluator
{
    private readonly CompanionProfileLifecycleOptions _options = options.Value;

    public UserFinancialProfileFreshnessState Evaluate(DateTime nowUtc, DateTime lastRefreshedUtc)
    {
        if (lastRefreshedUtc == default)
        {
            return UserFinancialProfileFreshnessState.RefreshNeeded;
        }

        var age = nowUtc - lastRefreshedUtc;
        if (age.TotalHours >= Math.Max(_options.StaleAfterHours + 1, _options.RefreshNeededAfterHours))
        {
            return UserFinancialProfileFreshnessState.RefreshNeeded;
        }

        return age.TotalHours >= Math.Max(1, _options.StaleAfterHours)
            ? UserFinancialProfileFreshnessState.Stale
            : UserFinancialProfileFreshnessState.Fresh;
    }
}
