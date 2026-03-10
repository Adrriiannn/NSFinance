namespace NSFinance.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int PollIntervalSeconds { get; set; } = 30;
}
