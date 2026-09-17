namespace VirtualCompany.Application.Sales;

public static class SalesPresentationAdHocProblemCodes
{
    public const string Conflict="sales.presentation_ad_hoc.conflict";
    public const string ContextInvalid="sales.presentation_ad_hoc.context_invalid";
    public const string PresetUnavailable="sales.presentation_ad_hoc.preset_unavailable";
    public const string PresenterInvalid="sales.presentation_ad_hoc.presenter_invalid";
    public const string RuntimeUnavailable="sales.presentation_ad_hoc.runtime_unavailable";
}

public sealed record CreateAdHocSalesPresentationCommand(Guid ClientRequestId,Guid PresetVersionId,Guid? CustomerCompanyId,
    Guid? ContactId,Guid? LeadId,Guid? DealId,Guid? PresenterAgentId,string? Goal,string? Audience,int? DurationMinutes,
    string? DemoScenario,string? Language,string? ControlMode,string RuntimeStrategy);
public sealed record SalesPresentationContextOptionDto(Guid Id,string Label,Guid? CustomerCompanyId=null,Guid? ContactId=null,Guid? LeadId=null);
public sealed record SalesPresentationAdHocOptionsDto(IReadOnlyList<SalesPresentationContextOptionDto> Accounts,
    IReadOnlyList<SalesPresentationContextOptionDto> Contacts,IReadOnlyList<SalesPresentationContextOptionDto> Leads,
    IReadOnlyList<SalesPresentationContextOptionDto> Deals,IReadOnlyList<SalesPresentationContextOptionDto> Presenters);
public sealed record ResolvedSalesPresentationContext(Guid? CustomerCompanyId,Guid? ContactId,Guid? LeadId,Guid? DealId,
    string AudienceLabel,IReadOnlyList<SalesPresentationRunArtifactDraft> Evidence,bool RequiresReview,IReadOnlyList<string> Blockers);
public sealed record SalesPresentationRunArtifactDraft(string Type,string Content,string Classification,string? SourceReference,int Order);
public sealed record SalesPresentationAdHocRunDto(Guid Id,Guid PresetId,string PresetName,Guid PresetVersionId,int PresetVersionNumber,
    Guid PresenterAgentId,string PresenterName,string Goal,string Audience,int DurationMinutes,string? DemoScenario,string Language,
    string ControlMode,string RuntimeStrategy,Guid? CustomerCompanyId,Guid? ContactId,Guid? LeadId,Guid? DealId,
    string PreparationStatus,IReadOnlyList<string> ReadinessBlockers,bool CanOpenPresenter,Guid? MeetingSessionId,
    long ConcurrencyVersion,IReadOnlyList<SalesPresentationRunArtifactDto> Artifacts);
public sealed record SalesPresentationLegacyCompatibilityDto(Guid Id,Guid DeckId,Guid SessionId,Guid? PresentationRunId,
    string Status,string Disposition,string? ReasonCode,string? Summary,int AttemptCount,DateTime UpdatedUtc);
public sealed record SalesPresentationLegacyReconciliationDto(int Scanned,int PresetBacked,int CompatibilityOnly,int Failed,
    bool HasMore,IReadOnlyList<SalesPresentationLegacyCompatibilityDto> Items);

public interface ISalesPresentationRunContextResolver
{
    Task<ResolvedSalesPresentationContext> ResolveAdHocAsync(Guid companyId,CreateAdHocSalesPresentationCommand command,CancellationToken ct);
}
public interface ISalesPresentationAdHocService
{
    Task<SalesPresentationAdHocOptionsDto> GetOptionsAsync(Guid companyId,Guid actorUserId,CancellationToken ct);
    Task<SalesPresentationAdHocRunDto> CreateAsync(Guid companyId,Guid actorUserId,CreateAdHocSalesPresentationCommand command,string? correlationId,CancellationToken ct);
    Task<SalesPresentationAdHocRunDto?> GetAsync(Guid companyId,Guid actorUserId,Guid runId,CancellationToken ct);
}
public interface ISalesPresentationLegacyMigrationService
{
    Task<SalesPresentationLegacyReconciliationDto> ReconcileAsync(Guid companyId,Guid actorUserId,int batchSize,string? correlationId,CancellationToken ct);
    Task<SalesPresentationLegacyReconciliationDto> GetStatusAsync(Guid companyId,Guid actorUserId,CancellationToken ct);
    Task<bool> RetryAsync(Guid companyId,Guid actorUserId,Guid recordId,CancellationToken ct);
}
