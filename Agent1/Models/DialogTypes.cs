
using System.Collections.Generic;
using Agent1.Services;

namespace Agent1.Models
{
    public class UserProfile
    {
        public string UserName { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string AssistantName { get; set; } = "SpirderMan";
    }

    public class ToolPlan
    {
        public bool NeedsTools { get; set; }
        public List<string> ToolNames { get; set; } = new List<string>();
    }

    public class PipelineContext
    {
        public SessionContext Session { get; set; } = null!;
        public string History { get; set; } = "";
        public Dictionary<string, string> Memory { get; set; } = new();
        public UserProfile UserProfile { get; set; } = null!;
        public IntentType Intent { get; set; }
    }
}

