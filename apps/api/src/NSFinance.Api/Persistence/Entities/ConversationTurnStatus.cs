namespace NSFinance.Api.Persistence.Entities;

public enum ConversationTurnStatus
{
    Received = 1,
    PersistedUserTurn = 2,
    ContextBuilt = 3,
    AIInProgress = 4,
    AICompleted = 5,
    PersistedAssistantTurn = 6,
    Completed = 7,
    Cancelled = 8,
    Failed = 9,
    TimedOut = 10
}
