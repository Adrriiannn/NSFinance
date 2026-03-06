namespace NSFinTech.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int PollIntervalSeconds { get; set; } = 30;
}
