namespace NSFinance.Api.Modules.ExpenseTracker.Models;

public static class ExpensePlanStatuses
{
    public const string Drafted = "drafted";
    public const string Scheduled = "scheduled";
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Archived = "archived";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Drafted,
        Scheduled,
        Active,
        Completed,
        Archived,
        Cancelled
    };

    public static readonly IReadOnlySet<string> Mutable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Drafted,
        Scheduled
    };
}

public static class ExpensePlanTypes
{
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Seasonal = "seasonal";
    public const string CustomRange = "custom_range";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Weekly,
        Monthly,
        Seasonal,
        CustomRange
    };
}

public static class ExpensePlanOriginTypes
{
    public const string Manual = "manual";
    public const string Duplicated = "duplicated";
    public const string Shared = "shared";
    public const string Imported = "imported";
    public const string RecurringGenerated = "recurring_generated";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Manual,
        Duplicated,
        Shared,
        Imported,
        RecurringGenerated
    };
}

public static class ExpensePlanSharingModes
{
    public const string Private = "private";
    public const string DirectShare = "direct_share";
    public const string Community = "community";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Private,
        DirectShare,
        Community
    };
}

public static class ExpensePlanRecurrenceTypes
{
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Seasonal = "seasonal";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Weekly,
        Monthly,
        Seasonal
    };
}
