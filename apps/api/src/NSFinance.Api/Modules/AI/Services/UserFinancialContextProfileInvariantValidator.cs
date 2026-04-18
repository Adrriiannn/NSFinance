namespace NSFinance.Api.Modules.AI.Services;

public interface IUserFinancialProfileLifecycleInvariantValidator
{
    void EnsureValid(UserFinancialContextProfileData state, DateTime nowUtc);
}

public sealed class UserFinancialProfileLifecycleInvariantValidator : IUserFinancialProfileLifecycleInvariantValidator
{
    public void EnsureValid(UserFinancialContextProfileData state, DateTime nowUtc)
    {
        var createdUtc = state.Lifecycle.CreatedUtc == default ? nowUtc : state.Lifecycle.CreatedUtc;
        var updatedUtc = state.Lifecycle.UpdatedUtc == default ? createdUtc : state.Lifecycle.UpdatedUtc;
        if (updatedUtc < createdUtc)
        {
            updatedUtc = createdUtc;
        }

        var refreshedUtc = state.Lifecycle.LastRefreshedUtc == default ? updatedUtc : state.Lifecycle.LastRefreshedUtc;
        if (refreshedUtc < createdUtc)
        {
            refreshedUtc = updatedUtc;
        }

        state.Lifecycle = state.Lifecycle with
        {
            SchemaVersion = Math.Max(1, state.Lifecycle.SchemaVersion),
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc,
            LastRefreshedUtc = refreshedUtc
        };
    }
}
