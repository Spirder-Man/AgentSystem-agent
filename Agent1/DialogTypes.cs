
using System.Collections.Generic;

namespace Agent1
{
    public class UserProfile
    {
        public string UserName { get; set; }
        public string JobTitle { get; set; }
        public string AssistantName { get; set; }
    }

    public class ToolPlan
    {
        public bool NeedsTools { get; set; }
        public List<string> ToolNames { get; set; } = new List<string>();
    }

    public class PipelineContext
    {
        public SessionContext Session { get; set; }
        public string History { get; set; }
        public Dictionary<string, string> Memory { get; set; }
        public UserProfile UserProfile { get; set; }
        public IntentType Intent { get; set; }
    }
}

