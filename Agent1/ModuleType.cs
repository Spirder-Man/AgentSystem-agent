
namespace Agent1
{
    public enum ModuleType
    {
        CoTSolid = 1,
        CoTStream = 2,
        ReActSolid = 3,
        ReActStream = 4,
        Reflection = 5,
        RAG = 6,
        UnifiedDialog = 7,
        
        // 新增化工园区专用类型
        ComplianceCheck = 8,       // 日常合规自查
        TicketFollowup = 9,        // 整改工单跟进
        RegulatoryAudit = 10       // 监管核查辅助
    }
}
