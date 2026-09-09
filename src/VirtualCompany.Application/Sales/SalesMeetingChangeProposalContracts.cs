using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Sales;

public static class SalesMeetingChangeProposalProblemCodes
{
    public const string InvalidRequest = "sales.meeting_change.invalid_request";
    public const string Conflict = "sales.meeting_change.conflict";
    public const string ApprovalRequired = "sales.meeting_change.approval_required";
    public const string ApprovalBindingChanged = "sales.meeting_change.approval_binding_changed";
    public const string TargetChanged = "sales.meeting_change.target_changed";
}

public static class SalesMeetingChangeApprovalTypes
{
    public const string CanonicalSensitiveChange = "sales_meeting_canonical_sensitive_change";
    public const string CustomerMinutesDelivery = "sales_meeting_customer_minutes_delivery";
    public const string NextMeetingScheduling = "sales_meeting_next_meeting_scheduling";
}

public sealed record SalesMeetingNextMeetingValue(Guid CalendarConnectionId, DateTime StartsUtc, DateTime EndsUtc,
    string TimeZoneId, string Title, string Description, string? Location, bool CreateOnlineMeeting = true, string? Conferencing=null);

public sealed record SalesMeetingProposedValue(string Kind, string? StringValue = null, decimal? DecimalValue = null,
    Guid? GuidValue = null, DateTime? DateTimeValue = null, SalesMeetingNextMeetingValue? NextMeetingValue = null);

public sealed record GenerateSalesMeetingChangeProposalItem(string TargetType, Guid TargetId, string Action, string Field,
    SalesMeetingProposedValue ProposedValue, Guid EvidenceArtifactId, IReadOnlyList<string> SourceIds,
    decimal Confidence, string Rationale);
public sealed record GenerateSalesMeetingChangeProposalsRequest(IReadOnlyList<GenerateSalesMeetingChangeProposalItem> Proposals);
public sealed record EditSalesMeetingChangeProposalRequest(long ExpectedVersion, SalesMeetingProposedValue ProposedValue,
    IReadOnlyList<string> SourceIds, decimal Confidence, string Rationale);
public sealed record ReviewSalesMeetingChangeProposalRequest(long ExpectedVersion, string? Reason = null);
public sealed record ExecuteSalesMeetingChangeProposalRequest(long ExpectedVersion);
public sealed record BulkApproveSalesMeetingChangeProposalsRequest(IReadOnlyList<Guid> ProposalIds);

public sealed record SalesMeetingChangePolicyDto(bool IsAllowed, string ReasonCode, string Explanation,
    bool RequiresApproval, bool IsSafeForBulkApproval, string RiskClass, string PolicyVersion);
public sealed record SalesMeetingChangeProposalDto(Guid Id, Guid SessionId, Guid EvidenceArtifactId,
    string TargetType, Guid TargetId, string TargetLabel, string Action, string Field, string FieldLabel,
    SalesMeetingProposedValue ProposedValue, string BeforeDisplayValue, string AfterDisplayValue,
    string TargetVersion, decimal Confidence, string Rationale, IReadOnlyList<string> SourceIds,
    string EvidenceVersionHash, SalesMeetingChangePolicyDto Policy, string Status, Guid? ApprovalRequestId,
    string? ApprovalBindingHash, Guid? ReviewedByUserId, DateTime? ReviewedUtc, DateTime? ApprovedUtc,
    DateTime? RejectedUtc, int ExecutionAttemptCount, string IdempotencyKey, string? ExecutedBeforeValue,
    string? ExecutedAfterValue, string? ProviderReference, string? LastErrorCode, string? LastErrorSummary,
    DateTime? ExecutedUtc, DateTime CreatedUtc, DateTime UpdatedUtc, long Version);
public sealed record BulkApproveSalesMeetingChangeProposalsResult(IReadOnlyList<SalesMeetingChangeProposalDto> Approved,
    IReadOnlyList<Guid> SkippedProposalIds);

public sealed record SalesMeetingChangePolicyDecision(bool IsAllowed, string ReasonCode, string Explanation,
    bool RequiresApproval, bool IsSafeForBulkApproval, SalesMeetingChangeRiskClass RiskClass, string PolicyVersion,
    string ApprovalType);

public interface ISalesMeetingChangePolicy
{
    SalesMeetingChangePolicyDecision Evaluate(SalesMeetingChangeTargetType targetType, SalesMeetingChangeAction action,
        SalesMeetingChangeField field, SalesMeetingChangeValueKind valueKind, CompanyMembershipRole actorRole);
}

public interface ISalesMeetingChangeProposalService
{
    Task<IReadOnlyList<SalesMeetingChangeProposalDto>> ListAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<SalesMeetingChangeProposalDto?> GetAsync(Guid companyId, Guid userId, Guid sessionId, Guid proposalId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesMeetingChangeProposalDto>> GenerateAsync(Guid companyId, Guid userId, Guid sessionId, GenerateSalesMeetingChangeProposalsRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingChangeProposalDto?> EditAsync(Guid companyId, Guid userId, Guid sessionId, Guid proposalId, EditSalesMeetingChangeProposalRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingChangeProposalDto?> ApproveAsync(Guid companyId, Guid userId, Guid sessionId, Guid proposalId, ReviewSalesMeetingChangeProposalRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingChangeProposalDto?> RejectAsync(Guid companyId, Guid userId, Guid sessionId, Guid proposalId, ReviewSalesMeetingChangeProposalRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<BulkApproveSalesMeetingChangeProposalsResult> BulkApproveSafeAsync(Guid companyId, Guid userId, Guid sessionId, BulkApproveSalesMeetingChangeProposalsRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingChangeProposalDto?> ExecuteAsync(Guid companyId, Guid userId, Guid sessionId, Guid proposalId, ExecuteSalesMeetingChangeProposalRequest request, string? correlationId, CancellationToken cancellationToken);
}

public sealed class SalesMeetingChangeProposalValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("The sales meeting proposal request is invalid.") { public IReadOnlyDictionary<string, string[]> Errors { get; } = errors; }
public sealed class SalesMeetingChangeProposalConflictException(string code, string message) : Exception(message) { public string Code { get; } = code; }

public sealed class SalesMeetingChangePolicy : ISalesMeetingChangePolicy
{
    public const string CurrentVersion = "sales-meeting-change-policy/1.0.0";
    public SalesMeetingChangePolicyDecision Evaluate(SalesMeetingChangeTargetType targetType, SalesMeetingChangeAction action,
        SalesMeetingChangeField field, SalesMeetingChangeValueKind valueKind, CompanyMembershipRole actorRole)
    {
        if (actorRole == CompanyMembershipRole.Accountant) return Denied("sales_internal_access_denied", "External accountant memberships cannot review internal sales changes.");
        if (field is SalesMeetingChangeField.Discount or SalesMeetingChangeField.PricePromise or SalesMeetingChangeField.ContractTerm)
            return Gated("commercial_commitment_approval_required", "Discounts, prices, promises, and contract terms always require an owner approval.", SalesMeetingChangeApprovalTypes.CanonicalSensitiveChange);
        if (targetType == SalesMeetingChangeTargetType.CustomerMinutes && action == SalesMeetingChangeAction.SendCustomerMinutes && field == SalesMeetingChangeField.CustomerMinutesRecipient && valueKind == SalesMeetingChangeValueKind.Email)
            return Gated("customer_communication_approval_required", "Customer communication requires an exact approval and durable delivery.", SalesMeetingChangeApprovalTypes.CustomerMinutesDelivery);
        if (targetType == SalesMeetingChangeTargetType.MeetingSession && action == SalesMeetingChangeAction.ScheduleNextMeeting && field == SalesMeetingChangeField.NextMeeting && valueKind == SalesMeetingChangeValueKind.NextMeeting)
            return Gated("calendar_action_approval_required", "Creating the next customer meeting requires an exact approval and durable calendar delivery.", SalesMeetingChangeApprovalTypes.NextMeetingScheduling);
        var valid = (targetType, action, field, valueKind) switch
        {
            (SalesMeetingChangeTargetType.Deal, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealStage, SalesMeetingChangeValueKind.Guid) => true,
            (SalesMeetingChangeTargetType.Deal, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealProbability, SalesMeetingChangeValueKind.Decimal) => true,
            (SalesMeetingChangeTargetType.Deal, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealValue, SalesMeetingChangeValueKind.Decimal) => true,
            (SalesMeetingChangeTargetType.Deal, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealNextStep, SalesMeetingChangeValueKind.String) => true,
            (SalesMeetingChangeTargetType.Lead, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.LeadEstimatedValue, SalesMeetingChangeValueKind.Decimal) => true,
            (SalesMeetingChangeTargetType.Lead, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.LeadNextAction, SalesMeetingChangeValueKind.String) => true,
            (SalesMeetingChangeTargetType.Contact, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.ContactEmail, SalesMeetingChangeValueKind.Email) => true,
            (SalesMeetingChangeTargetType.Contact, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.ContactFullName or SalesMeetingChangeField.ContactTitle or SalesMeetingChangeField.ContactPhone, SalesMeetingChangeValueKind.String) => true,
            _ => false
        };
        if (!valid) return Denied("target_field_not_allowed", "This target, action, field, and value type combination is not in the Sales allowlist.");
        if (field == SalesMeetingChangeField.DealValue)
            return Gated("deal_value_approval_required", "Changing canonical deal value requires owner approval.", SalesMeetingChangeApprovalTypes.CanonicalSensitiveChange);
        var canConfirm = actorRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager or CompanyMembershipRole.Employee or CompanyMembershipRole.Tester;
        return canConfirm
            ? new(true, "explicit_confirmation_required", "An authorized salesperson must explicitly confirm this canonical change.", false, true, SalesMeetingChangeRiskClass.ConfirmationRequired, CurrentVersion, string.Empty)
            : Gated("canonical_change_approval_required", "This membership requires owner approval for canonical sales changes.", SalesMeetingChangeApprovalTypes.CanonicalSensitiveChange);
    }
    private static SalesMeetingChangePolicyDecision Denied(string code, string text) => new(false, code, text, true, false, SalesMeetingChangeRiskClass.AlwaysGated, CurrentVersion, string.Empty);
    private static SalesMeetingChangePolicyDecision Gated(string code, string text, string type) => new(true, code, text, true, false, SalesMeetingChangeRiskClass.AlwaysGated, CurrentVersion, type);
}

public sealed record ApplySalesMeetingCanonicalChangeCommand(Guid CompanyId, string TargetType, Guid TargetId,
    string Field, SalesMeetingProposedValue ProposedValue, string ExpectedTargetVersion, Guid ActorUserId);
public sealed record ApplySalesMeetingCanonicalChangeResult(string BeforeValueJson, string AfterValueJson, string NewTargetVersion);
public interface ISalesMeetingCanonicalChangeCommandHandler
{
    Task<ApplySalesMeetingCanonicalChangeResult> ApplyAsync(ApplySalesMeetingCanonicalChangeCommand command, CancellationToken cancellationToken);
}

public sealed record SalesMeetingCustomerMinutesDeliveryRequestedMessage(Guid CompanyId, Guid ProposalId,
    string IdempotencyKey, string? CorrelationId);
public interface ISalesMeetingCustomerMinutesDeliveryDispatcher { Task DispatchAsync(SalesMeetingCustomerMinutesDeliveryRequestedMessage message, CancellationToken cancellationToken); }
