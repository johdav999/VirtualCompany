namespace VirtualCompany.Application.Sales;

public static class SalesMeetingPreparationReadinessStates
{
    public const string Blocked = "blocked";
    public const string SessionRequired = "session_required";
    public const string DeckRequired = "deck_required";
    public const string Processing = "processing";
    public const string ActivationRequired = "activation_required";
    public const string Ready = "ready";
}

public static class SalesMeetingPreparationReasonCodes
{
    public const string CustomerCompanyMissing = "customer_company_missing";
    public const string InvitationNotScheduled = "invitation_not_scheduled";
    public const string ProviderEventMissing = "provider_event_missing";
    public const string EligibleSalesAgentMissing = "eligible_sales_agent_missing";
    public const string SessionMissing = "session_missing";
    public const string DeckMissing = "deck_missing";
    public const string DeckProcessing = "deck_processing";
    public const string DeckProcessingFailed = "deck_processing_failed";
    public const string DeckProcessingBlocked = "deck_processing_blocked";
    public const string ActiveDeckMissing = "active_deck_missing";
    public const string ActiveDeckAgentIneligible = "active_deck_agent_ineligible";
}

public static class SalesMeetingPreparationActionValues
{
    public const string CreateSession = "create_session";
    public const string UpdateSession = "update_session";
    public const string UploadDeck = "upload_deck";
    public const string RetryProcessing = "retry_processing";
    public const string ActivateDeck = "activate_deck";
    public const string OpenPresenter = "open_presenter";
}

public sealed record SalesMeetingPreparationInvitationDto(
    Guid Id, Guid LeadId, Guid? DealId, Guid? ContactId, string Title,
    DateTime StartsUtc, DateTime EndsUtc, string TimeZoneId, string? Location,
    bool CreateOnlineMeeting, string Provider, string Status,
    bool HasProviderEvent, bool IsEligibleForSessionCreation);

public sealed record SalesMeetingPreparationAgentDto(
    Guid Id, string DisplayName, string RoleName, string TemplateId,
    string Department, string Status, string? AvatarUrl);

public sealed record SalesMeetingPreparationDeckDto(
    Guid Id, Guid AgentId, int Version, string Title, string OriginalFileName,
    string Status, int ProcessingVersion, int ProcessingAttemptCount, int SlideCount,
    string? FailureCode, string? FailureSummary, bool CanRetry, bool IsActive,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ProcessedUtc,
    DateTime? FailedUtc, DateTime? ActivatedUtc, long ConcurrencyVersion);

public sealed record SalesMeetingPreparationBlockerDto(string Code, string Explanation);

public sealed record SalesMeetingPresentationPreparationResponse(
    Guid CompanyId, Guid InvitationId, Guid LeadId, string LeadTitle,
    string? CustomerCompanyName, Guid? MeetingSessionId, long MaximumUploadBytes,
    SalesMeetingPreparationInvitationDto Invitation, SalesMeetingSessionResponse? Session,
    IReadOnlyList<SalesMeetingPreparationAgentDto> EligibleAgents,
    IReadOnlyList<SalesMeetingPreparationDeckDto> Decks,
    Guid? ActiveDeckId, int ActiveDeckSlideCount, string ReadinessState,
    bool CanOpenPresenter, IReadOnlyList<SalesMeetingPreparationBlockerDto> BlockingReasons,
    IReadOnlyList<string> AllowedActions);

public interface ISalesMeetingPresentationPreparationQuery
{
    Task<SalesMeetingPresentationPreparationResponse?> GetAsync(
        Guid companyId, Guid invitationId, CancellationToken cancellationToken);
}
