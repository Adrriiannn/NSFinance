namespace NSFinance.Api.Modules.Auth.Configuration;

public sealed class TransactionalEmailOptions
{
    public const string SectionName = "TransactionalEmail";
    public const string CanonicalSenderAddress = "noreply@nsireland.ie";

    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = CanonicalSenderAddress;
    public string? ReplyToAddress { get; set; }
    public string[] RecipientAllowList { get; set; } = [];
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 5;
}
