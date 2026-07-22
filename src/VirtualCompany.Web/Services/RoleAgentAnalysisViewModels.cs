namespace VirtualCompany.Web.Services;

public sealed class RoleAgentAnalysisRequestViewModel
{
    public string AnalysisType { get; set; } = string.Empty;
    public Guid? SubjectId { get; set; }
    public int HorizonDays { get; set; } = 30;
    public string? Objective { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string Cadence { get; set; } = "on_demand";
}

public sealed class RoleAgentAnalysisViewModel
{
    public Guid RunId { get; set; }
    public string CapabilityId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public DateTime AsOfUtc { get; set; }
    public List<RoleAgentMetricViewModel> Metrics { get; set; } = [];
    public List<RoleAgentPriorityViewModel> Priorities { get; set; } = [];
    public List<AgentAiClaimViewModel> Claims { get; set; } = [];
    public List<AgentAiSourceViewModel> Sources { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public List<AgentAiNextActionViewModel> NextActions { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class RoleAgentMetricViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string? Unit { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public DateTime AsOfUtc { get; set; }
}

public sealed class RoleAgentPriorityViewModel
{
    public string SubjectType { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Score { get; set; }
    public string Band { get; set; } = string.Empty;
    public List<string> ReasonCodes { get; set; } = [];
    public string SourceId { get; set; } = string.Empty;
}

public sealed class AgentAiNextActionViewModel
{
    public string Title { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public bool RequiresApproval { get; set; }
}
