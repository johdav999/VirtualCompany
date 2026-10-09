

namespace VirtualCompany.Application.Support;


public sealed record SupportOperationalReport(Guid CompanyId, string View, DateTime AsOfUtc,
    SupportBusinessCalendarDto Calendar, string Definition, string ClockRules,
    IReadOnlyList<SupportOperationalCase> Cases, int AtRisk, int Breached, int Waiting,
    int Escalated, int AwaitingApproval, int MissingTargets, IReadOnlyList<SupportMetricBucket> AgeBuckets);


public sealed record SupportOperationalCase(SupportCaseListItem Case, string Owner, decimal AgeHours,
    string AgeBucket, DateTime? NextDeadlineUtc, bool Waiting, bool MissingTarget);


public sealed record SupportOperationalReportQuery(string View = "backlog", string? Status = null,
    string? Priority = null, string? Category = null, string? Search = null, Guid? AssignedUserId = null,
    Guid? AssignedAgentId = null, bool Unassigned = false, Guid? ContactId = null,
    Guid? CustomerCompanyId = null, string? AgeBucket = null, bool FailedReply = false);
