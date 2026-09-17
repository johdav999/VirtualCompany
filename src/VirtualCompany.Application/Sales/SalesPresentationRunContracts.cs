namespace VirtualCompany.Application.Sales;

public static class SalesPresentationRunProblemCodes { public const string Conflict="sales.presentation_run.conflict"; public const string NotReady="sales.presentation_run.not_ready"; public const string InvalidPresenter="sales.presentation_run.presenter_invalid"; public const string ActiveRunExists="sales.presentation_run.active_exists"; }
public sealed record ApplySalesPresentationPresetCommand(Guid? PresetId,Guid? PresetVersionId,Guid? PresenterAgentId,string? Goal,string? Audience,int? DurationMinutes,string? DemoScenario,string? Language,string? ControlMode,bool ReplaceActive,long? ExpectedActiveRunVersion);
public sealed record SalesPresentationRunArtifactDto(Guid Id,string Type,string Content,string Classification,string? SourceReference,Guid? PresetSlideId,int Order,Guid? AiRunId);
public sealed record SalesPresentationRunDto(Guid Id,Guid MeetingSessionId,Guid PresetId,string PresetName,Guid PresetVersionId,int PresetVersionNumber,Guid PresenterAgentId,string PresenterName,string Goal,string Audience,int DurationMinutes,string? DemoScenario,string Language,string ControlMode,bool GoalOverridden,bool AudienceOverridden,bool DurationOverridden,bool DemoScenarioOverridden,bool PresenterOverridden,string PreparationStatus,IReadOnlyList<string> ReadinessBlockers,bool CanRetry,string? FailureCode,string? FailureSummary,Guid? CompatibilityDeckId,bool IsActive,int? NewerPublishedVersion,long ConcurrencyVersion,IReadOnlyList<SalesPresentationRunArtifactDto> Artifacts);
public sealed record SalesPresentationRunVersionChangeDto(string Field,string CurrentValue,string NewValue,bool OverridePreserved,bool Incompatible);
public sealed record SalesPresentationRunVersionComparisonDto(Guid RunId,Guid CurrentVersionId,int CurrentVersion,Guid TargetVersionId,int TargetVersion,IReadOnlyList<SalesPresentationRunVersionChangeDto> Changes,bool CanUpdate,IReadOnlyList<string> Blockers);
public interface ISalesPresentationRunService
{
 Task<SalesPresentationRunDto?> ApplyToInvitationAsync(Guid companyId,Guid actorUserId,Guid invitationId,ApplySalesPresentationPresetCommand command,string? correlationId,CancellationToken ct);
 Task<SalesPresentationRunDto?> GetActiveAsync(Guid companyId,Guid actorUserId,Guid sessionId,CancellationToken ct);
 Task<SalesPresentationRunDto?> ApplyAsync(Guid companyId,Guid actorUserId,Guid sessionId,ApplySalesPresentationPresetCommand command,string? correlationId,CancellationToken ct);
 Task<SalesPresentationRunDto?> RetryAsync(Guid companyId,Guid actorUserId,Guid sessionId,Guid runId,long expectedVersion,string? correlationId,CancellationToken ct);
 Task<SalesPresentationRunVersionComparisonDto?> CompareVersionAsync(Guid companyId,Guid actorUserId,Guid sessionId,Guid runId,Guid targetVersionId,CancellationToken ct);
 Task<SalesPresentationPresetDto?> SaveAsPresetAsync(Guid companyId,Guid actorUserId,Guid sessionId,Guid deckId,string name,string? description,string? correlationId,CancellationToken ct);
}
