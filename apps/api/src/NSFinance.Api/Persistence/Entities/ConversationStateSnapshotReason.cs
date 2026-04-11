namespace NSFinance.Api.Persistence.Entities;

public enum ConversationStateSnapshotReason
{
    UserTurn = 1,
    AssistantTurn = 2,
    SummaryRefresh = 3,
    ManualUpdate = 4
}
