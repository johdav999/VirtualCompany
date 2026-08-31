namespace VirtualCompany.Application.Finance;

public static class CloseComplianceReleaseDecisions
{
    public const string Ready = "ready";
    public const string NoGo = "no_go";
}

public static class CloseComplianceReleaseSignalStatuses
{
    public const string Ready = "ready";
    public const string ReleaseStop = "release_stop";
}

public static class CloseComplianceReleaseSignalKeys
{
    public const string OverdueCloseTasks = "overdue_close_tasks";
    public const string BlockedCloseTasks = "blocked_close_tasks";
    public const string UnresolvedReconciliations = "unresolved_reconciliations";
    public const string StaleReports = "stale_reports";
    public const string MissingSignOffs = "missing_signoffs";
    public const string MissingEvidence = "missing_evidence";
    public const string IncompletePackages = "incomplete_packages";
    public const string ComplianceAmbiguity = "compliance_ambiguity";
    public const string AccountantAccessAnomalies = "accountant_access_anomalies";
    public const string FailedRollover = "failed_rollover";
}

public sealed record GetCloseComplianceReleaseReadinessQuery(Guid CompanyId, Guid? FiscalPeriodId = null);

public sealed record CloseComplianceReleaseSignalDto(
    string Key,
    string Status,
    int Count,
    string Explanation,
    string Remediation,
    DateTime? EvidenceUtc,
    IReadOnlyList<string> SourceLinks);

public sealed record CloseComplianceReleaseReadinessDto(
    Guid CompanyId,
    Guid? FiscalPeriodId,
    DateTime GeneratedUtc,
    string Decision,
    int ReleaseStopCount,
    string EvidenceHash,
    IReadOnlyList<string> EvidenceSourceLinks,
    IReadOnlyList<CloseComplianceReleaseSignalDto> Signals,
    string EvidenceNotice = "Release is blocked until every release-stop signal and every required external proof lane has retained evidence.");

public interface ICloseComplianceReleaseReadinessService
{
    Task<CloseComplianceReleaseReadinessDto> GetAsync(
        GetCloseComplianceReleaseReadinessQuery query,
        CancellationToken cancellationToken);
}
