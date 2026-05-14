namespace Agent1.Services
{
    public class SessionService : ISessionService
    {
        public SessionContext CreateSession(SessionType type = SessionType.General)
        {
            return SessionManager.CreateSession(null, type);
        }

        public SessionContext? GetSession(string sessionId)
        {
            return SessionManager.GetSession(sessionId);
        }

        public void AddDialogTurn(string sessionId, string role, string content)
        {
            SessionManager.AddDialogTurn(sessionId, role, content);
        }

        public void ClearDialogHistory(string sessionId)
        {
            SessionManager.ClearDialogHistory(sessionId);
        }

        public string GetFormattedHistory(string sessionId, int maxTurns = 10)
        {
            return SessionManager.GetFormattedHistory(sessionId, maxTurns);
        }

        public string GetContextSummary(string sessionId, int maxTurns = 5)
        {
            return SessionManager.GetContextSummary(sessionId, maxTurns);
        }

        public int GetHistoryCount(string sessionId)
        {
            return SessionManager.GetHistoryCount(sessionId);
        }
    }
}