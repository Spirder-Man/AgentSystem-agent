namespace Agent1.Services
{
    public interface ISessionService
    {
        SessionContext CreateSession(SessionType type = SessionType.General);
        SessionContext? GetSession(string sessionId);
        void AddDialogTurn(string sessionId, string role, string content);
        void ClearDialogHistory(string sessionId);
        string GetFormattedHistory(string sessionId, int maxTurns = 10);
        string GetContextSummary(string sessionId, int maxTurns = 5);
        int GetHistoryCount(string sessionId);
    }
}