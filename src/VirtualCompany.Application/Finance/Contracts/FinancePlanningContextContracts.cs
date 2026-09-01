using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Finance;

public static class FinancePlanningContextVersions
{
    public const string V1 = "finance-planning-context-v1";
}

public static class FinancePlanningResolutionStates
{
    public const string Ready = "ready";
    public const string NeedsClarification = "needs_clarification";
}

public static class FinancePlanningReferenceTypes
{
    public const string Invoice = "invoice";
    public const string Bill = "bill";
    public const string Customer = "customer";
    public const string Supplier = "supplier";
    public const string FiscalPeriod = "fiscal_period";
    public const string Migration = "migration";
    public const string Account = "account";
    public const string Journal = "journal";
    public const string VoucherSeries = "voucher_series";
    public const string ReportDefinition = "report_definition";
    public const string ReportLine = "report_line";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Invoice, Bill, Customer, Supplier, FiscalPeriod, Migration, Account, Journal, VoucherSeries,
        ReportDefinition, ReportLine
    };
}

public static class FinanceEntityResolutionStates
{
    public const string Resolved = "resolved";
    public const string NotFound = "not_found";
    public const string Ambiguous = "ambiguous";
}

public sealed record FinancePlanningReference(string Type, string Value);

public sealed record FinanceEntityResolutionRequest(
    Guid CompanyId,
    string ReferenceType,
    string ReferenceValue,
    int MaximumCandidates);

public sealed record FinanceEntityResolutionCandidate(
    string EntityType,
    string EntityId,
    string SourceId,
    string SourceVersion,
    DateTime UpdatedUtc,
    string SafeLabel);

public sealed record FinanceEntityResolutionResult(
    string State,
    string ReferenceType,
    string NormalizedReference,
    IReadOnlyList<FinanceEntityResolutionCandidate> Candidates);

public interface IFinancePlanningEntityResolver
{
    Task<FinanceEntityResolutionResult> ResolveAsync(
        FinanceEntityResolutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record FinanceProjectedToolManifest(
    string ToolName,
    string ToolVersion,
    string ActionClass,
    string Scope,
    string SafePurpose,
    IReadOnlyList<string> TargetEntityTypes,
    string SideEffectSummary,
    IReadOnlyList<string> RequiredEvidenceTypes,
    int MaximumEvidenceAgeSeconds,
    string ConfirmationBehavior,
    string ApprovalBehavior,
    string ResultSemantics,
    IReadOnlyList<string> NaturalLanguageExamples,
    int RankingScore,
    JsonObject InputSchema,
    string AuthorityState);

public sealed record FinancePlanningEvidenceReference(
    string SourceId,
    string SourceVersion,
    string EntityType,
    string EntityId,
    string SafeLabel,
    DateTime UpdatedUtc,
    bool IsFresh);

public sealed record FinancePlanningContextProjectionRequest(
    Guid CompanyId,
    Guid AgentId,
    string UserRequest,
    string CorrelationId,
    IReadOnlyList<FinancePlanningReference>? ExplicitReferences = null,
    int MaximumEvidenceRecords = 20);

public sealed record FinancePlanningContextBundle(
    string Version,
    string Hash,
    Guid CompanyId,
    Guid AgentId,
    string ResolutionState,
    string ResolutionReasonCode,
    string SafeExplanation,
    string EffectiveAuthorityVersion,
    string EffectiveAuthorityHash,
    IReadOnlyList<FinanceProjectedToolManifest> Tools,
    IReadOnlyList<FinancePlanningEvidenceReference> Evidence,
    IReadOnlyList<FinancePlanningReference> UnresolvedReferences,
    DateTime GeneratedUtc);

public sealed record FinancePlanningContextFreshnessResult(
    bool IsCurrent,
    string ExpectedHash,
    string CurrentHash,
    string ReasonCode);

public interface IFinancePlanningContextProjector
{
    Task<FinancePlanningContextBundle> ProjectAsync(
        FinancePlanningContextProjectionRequest request,
        AgentEffectiveAuthorityDto authority,
        CancellationToken cancellationToken);

    Task<FinancePlanningContextFreshnessResult> CheckFreshnessAsync(
        FinancePlanningContextProjectionRequest request,
        string expectedHash,
        CancellationToken cancellationToken);
}
