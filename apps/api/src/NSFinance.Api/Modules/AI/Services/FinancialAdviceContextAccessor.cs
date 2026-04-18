namespace NSFinance.Api.Modules.AI.Services;

internal static class FinancialAdviceContextAccessor
{
    public static TContext? TryGetContext<TContext>(
        IReadOnlyDictionary<string, object?> toolOutputs,
        CompanionTool tool)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(toolOutputs);

        var key = tool.ToOutputKey();
        if (toolOutputs.TryGetValue(key, out var value) && value is TContext typed)
        {
            return typed;
        }

        return null;
    }
}
