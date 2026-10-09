using System.Globalization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;
public sealed record ReconcileAccountingProviderSwitchTargetTransferItemRequest(bool ProviderConfirmedSuccess, string? ProviderExternalId, string Summary, long ExpectedItemVersion);
public sealed record ReplayAccountingProviderSwitchTargetTransferRequest(long ExpectedBatchVersion);
public sealed record StartAccountingProviderSwitchTargetTransferRequest(Guid PlanId, long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record StartAccountingProviderSwitchPreparationRequest(Guid PlanId, long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record RequestAccountingProviderSwitchPlanApprovalRequest(long ExpectedSwitchVersion);
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record GenerateAccountingProviderSwitchCutoverPlanRequest(Guid RehearsalId, long ExpectedSwitchVersion, DateTime FreezeStartsUtc, DateTime FreezeEndsUtc, string RecoveryBoundary, IReadOnlyList<Guid> ParticipantUserIds)
{
    public Guid RehearsalId { get; set; } = RehearsalId;
    public long ExpectedSwitchVersion { get; set; } = ExpectedSwitchVersion;
    public DateTime FreezeStartsUtc { get; set; } = FreezeStartsUtc;
    public DateTime FreezeEndsUtc { get; set; } = FreezeEndsUtc;
    public string RecoveryBoundary { get; set; } = RecoveryBoundary;
    public IReadOnlyList<Guid> ParticipantUserIds { get; set; } = ParticipantUserIds;

    public GenerateAccountingProviderSwitchCutoverPlanRequest() : this(default !, default !, default !, default !, string.Empty, [])
    {
    }
}

public sealed record RecordAccountingProviderSwitchManualEvidenceRequest(string Explanation, string EvidenceReference, DateTime? ExpiresUtc);
public sealed record ReplayAccountingProviderSwitchRehearsalRequest(long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record StartAccountingProviderSwitchRehearsalRequest(long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record ResolveAccountingProviderSwitchDispositionRequest(string Disposition, string Reason, Guid? MappingDecisionId, Guid? DuplicateOfStagedRecordId, long ExpectedVersion);
public sealed record RequestAccountingProviderSwitchMappingApprovalRequest(long ExpectedVersion);
public sealed record PreviewAccountingProviderSwitchMappingRequest(string MappingType, string SourceKey, string? ProposedTargetKey, string? SourceSemantic, IReadOnlyList<Guid> AffectedStagedRecordIds, bool IsMaterial);
public sealed record StageAccountingProviderSwitchRecordRequest(Guid ExtractionBatchId, string Dataset, string SourceIdentity, string SourceVersion, DateTime? ProviderModifiedUtc, string SourceHash, string NormalizedDataJson, string EvidenceJson, decimal FinancialAmount, string? Currency, string InitialDisposition);
public sealed record ReplayAccountingProviderSwitchAssessmentRequest(long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record StartAccountingProviderSwitchAssessmentRequest(long ExpectedSwitchVersion, string IdempotencyKey);
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CancelAccountingProviderSwitchRequest(string Reason, long ExpectedVersion)
{
    public string Reason { get; set; } = Reason;
    public long ExpectedVersion { get; set; } = ExpectedVersion;

    public CancelAccountingProviderSwitchRequest() : this(string.Empty, default !)
    {
    }
}

public sealed record UpdateAccountingProviderSwitchPlanRequest(string SourceKind, string? SourceProviderKey, string TargetKind, string? TargetProviderKey, Guid EffectiveFiscalPeriodId, string MigrationStrategy, string Reason, Guid ResponsibleUserId, Guid? ResponsibleAgentId, long ExpectedVersion);
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CreateAccountingProviderSwitchRequest(string SourceKind, string? SourceProviderKey, string TargetKind, string? TargetProviderKey, Guid EffectiveFiscalPeriodId, string MigrationStrategy, string Reason, Guid ResponsibleUserId, Guid? ResponsibleAgentId)
{
    public string SourceKind { get; set; } = SourceKind;
    public string? SourceProviderKey { get; set; } = SourceProviderKey;
    public string TargetKind { get; set; } = TargetKind;
    public string? TargetProviderKey { get; set; } = TargetProviderKey;
    public Guid EffectiveFiscalPeriodId { get; set; } = EffectiveFiscalPeriodId;
    public string MigrationStrategy { get; set; } = MigrationStrategy;
    public string Reason { get; set; } = Reason;
    public Guid ResponsibleUserId { get; set; } = ResponsibleUserId;
    public Guid? ResponsibleAgentId { get; set; } = ResponsibleAgentId;

    public CreateAccountingProviderSwitchRequest() : this(string.Empty, default !, string.Empty, default !, default !, string.Empty, string.Empty, default !, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record ReconcileAccountingProviderExportRequest(bool ProviderConfirmedSuccess, string? ProviderExternalId, string Summary, long ExpectedVersion)
{
    public bool ProviderConfirmedSuccess { get; set; } = ProviderConfirmedSuccess;
    public string? ProviderExternalId { get; set; } = ProviderExternalId;
    public string Summary { get; set; } = Summary;
    public long ExpectedVersion { get; set; } = ExpectedVersion;

    public ReconcileAccountingProviderExportRequest() : this(default !, default !, string.Empty, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record QueueAccountingProviderExportRequest(Guid LedgerEntryId, string ProviderKey)
{
    public Guid LedgerEntryId { get; set; } = LedgerEntryId;
    public string ProviderKey { get; set; } = ProviderKey;

    public QueueAccountingProviderExportRequest() : this(default !, string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CompleteAccountingAuthorityCutoverRequest(long ExpectedVersion)
{
    public long ExpectedVersion { get; set; } = ExpectedVersion;

    public CompleteAccountingAuthorityCutoverRequest() : this(default(long))
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record RecordAccountingCutoverValidationRequest(bool OpeningBalancesReconciled, bool TrialBalanceReconciled, bool SourceMappingsReconciled, int ConflictCount, string Summary, long ExpectedVersion)
{
    public bool OpeningBalancesReconciled { get; set; } = OpeningBalancesReconciled;
    public bool TrialBalanceReconciled { get; set; } = TrialBalanceReconciled;
    public bool SourceMappingsReconciled { get; set; } = SourceMappingsReconciled;
    public int ConflictCount { get; set; } = ConflictCount;
    public string Summary { get; set; } = Summary;
    public long ExpectedVersion { get; set; } = ExpectedVersion;

    public RecordAccountingCutoverValidationRequest() : this(default !, default !, default !, default !, string.Empty, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record StartAccountingAuthorityChangeRequest(Guid EffectiveFiscalPeriodId, string TargetAuthority, string? ProviderKey, string Reason, string PreviewToken, long ExpectedCurrentVersion)
{
    public Guid EffectiveFiscalPeriodId { get; set; } = EffectiveFiscalPeriodId;
    public string TargetAuthority { get; set; } = TargetAuthority;
    public string? ProviderKey { get; set; } = ProviderKey;
    public string Reason { get; set; } = Reason;
    public string PreviewToken { get; set; } = PreviewToken;
    public long ExpectedCurrentVersion { get; set; } = ExpectedCurrentVersion;

    public StartAccountingAuthorityChangeRequest() : this(default !, default !, default !, string.Empty, string.Empty, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record PreviewAccountingAuthorityChangeRequest(Guid EffectiveFiscalPeriodId, string TargetAuthority, string? ProviderKey)
{
    public Guid EffectiveFiscalPeriodId { get; set; } = EffectiveFiscalPeriodId;
    public string TargetAuthority { get; set; } = TargetAuthority;
    public string? ProviderKey { get; set; } = ProviderKey;

    public PreviewAccountingAuthorityChangeRequest() : this(default !, string.Empty, default !)
    {
    }
}
