using System.Text.Json.Nodes;

namespace VirtualCompany.Shared.Mobile;

public static class MobileBackendApiRoutes
{
    public const string AuthMe = "api/auth/me";
    public const string SelectCompany = "api/auth/select-company";

    public static string Inbox(Guid companyId) =>
        $"api/companies/{companyId}/inbox";

    public static string CompactInbox(Guid companyId, int take, DateTime? sinceUtc = null) =>
        $"api/companies/{companyId}/inbox/mobile?take={take}{FormatSince(sinceUtc)}";

    public static string MobileSummary(Guid companyId) =>
        $"api/companies/{companyId}/mobile/summary";

    public static string LatestBriefings(Guid companyId) =>
        $"api/companies/{companyId}/briefings/latest";

    public static string CompactLatestBriefing(Guid companyId) =>
        $"api/companies/{companyId}/briefings/latest/mobile";

    public static string Agents(Guid companyId) =>
        $"api/companies/{companyId}/agents";

    public static string ApprovalDecision(Guid companyId, Guid approvalId) =>
        $"api/companies/{companyId}/approvals/{approvalId}/decisions";

    public static string NotificationStatus(Guid companyId, Guid notificationId) =>
        $"api/companies/{companyId}/inbox/notifications/{notificationId}/status";

    public static string OpenDirectConversation(Guid companyId, Guid agentId) =>
        $"api/companies/{companyId}/agents/{agentId}/conversations/direct";

    public static string DirectConversations(Guid companyId, int skip, int take, DateTime? sinceUtc = null) =>
        $"api/companies/{companyId}/conversations/direct?skip={skip}&take={take}{FormatSince(sinceUtc)}";

    public static string CompactDirectConversations(Guid companyId, int skip, int take, DateTime? sinceUtc = null) =>
        $"api/companies/{companyId}/conversations/direct/mobile?skip={skip}&take={take}{FormatSince(sinceUtc)}";

    public static string ConversationMessages(Guid companyId, Guid conversationId, int skip, int take, DateTime? sinceUtc = null) =>
        $"api/companies/{companyId}/conversations/{conversationId}/messages?skip={skip}&take={take}{FormatSince(sinceUtc)}";

    public static string CompactConversationMessages(Guid companyId, Guid conversationId, int skip, int take, DateTime? sinceUtc = null) =>
        $"api/companies/{companyId}/conversations/{conversationId}/messages/mobile?skip={skip}&take={take}{FormatSince(sinceUtc)}";

    public static string SendConversationMessage(Guid companyId, Guid conversationId) =>
        $"api/companies/{companyId}/conversations/{conversationId}/messages";

    private static string FormatSince(DateTime? sinceUtc) =>
        sinceUtc.HasValue
            ? $"&sinceUtc={Uri.EscapeDataString(sinceUtc.Value.ToUniversalTime().ToString("O"))}"
            : string.Empty;
}

public sealed class SelectCompanyRequestDto
{
    public Guid CompanyId { get; set; }
}

public sealed class CurrentUserContextDto
{
    public CurrentUserDto User { get; set; } = new();
    public List<CompanyMembershipDto> Memberships { get; set; } = [];
    public ResolvedCompanyContextDto? ActiveCompany { get; set; }
    public bool CompanySelectionRequired { get; set; }
}

public sealed class CurrentUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AuthProvider { get; set; } = string.Empty;
    public string AuthSubject { get; set; } = string.Empty;
}

public sealed class CompanyMembershipDto
{
    public Guid MembershipId { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string MembershipRole { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class ResolvedCompanyContextDto
{
    public Guid MembershipId { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string MembershipRole { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class CompanySelectionDto
{
    public Guid CompanyId { get; set; }
    public string HeaderName { get; set; } = string.Empty;
    public string HeaderValue { get; set; } = string.Empty;
    public ResolvedCompanyContextDto ActiveCompany { get; set; } = new();
}

public sealed class NotificationInboxDto
{
    public List<NotificationListItemDto> Notifications { get; set; } = [];
    public List<ApprovalInboxItemDto> PendingApprovals { get; set; } = [];
    public int UnreadCount { get; set; }
    public int PendingApprovalCount { get; set; }
}

public sealed class NotificationListItemDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RecipientUserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string RelatedEntityType { get; set; } = string.Empty;
    public Guid? RelatedEntityId { get; set; }
    public string? ActionUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? ActionedAt { get; set; }
    public Guid? ActionedByUserId { get; set; }
}

public sealed class MobileInboxDto
{
    public List<MobileAlertListItemDto> Alerts { get; set; } = [];
    public List<MobileApprovalListItemDto> PendingApprovals { get; set; } = [];
    public int UnreadCount { get; set; }
    public int PendingApprovalCount { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}

public sealed class MobileAlertListItemDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public sealed class MobileApprovalListItemDto
{
    public Guid Id { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public string TargetEntityType { get; set; } = string.Empty;
    public Guid TargetEntityId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RationaleSummary { get; set; } = string.Empty;
    public string AffectedDataSummary { get; set; } = string.Empty;
    public string? ThresholdSummary { get; set; }
    public ApprovalStepDto? CurrentStep { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ApprovalInboxItemDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public string TargetEntityType { get; set; } = string.Empty;
    public Guid TargetEntityId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RationaleSummary { get; set; } = string.Empty;
    public string AffectedDataSummary { get; set; } = string.Empty;
    public string RequestedByActorType { get; set; } = string.Empty;
    public Guid RequestedByActorId { get; set; }
    public string? RequiredRole { get; set; }
    public Guid? RequiredUserId { get; set; }
    public string? ThresholdSummary { get; set; }
    public ApprovalStepDto? CurrentStep { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ApprovalStepDto
{
    public Guid Id { get; set; }
    public int SequenceNo { get; set; }
    public string ApproverType { get; set; } = string.Empty;
    public string ApproverRef { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Comment { get; set; }
}

public sealed class ApprovalRequestDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TargetEntityType { get; set; } = string.Empty;
    public Guid TargetEntityId { get; set; }
    public string RequestedByActorType { get; set; } = string.Empty;
    public Guid RequestedByActorId { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public string? RequiredRole { get; set; }
    public Guid? RequiredUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, JsonNode?> ThresholdContext { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ApprovalStepDto> Steps { get; set; } = [];
    public ApprovalStepDto? CurrentStep { get; set; }
    public string? DecisionSummary { get; set; }
    public string? RejectionComment { get; set; }
    public string RationaleSummary { get; set; } = string.Empty;
    public string AffectedDataSummary { get; set; } = string.Empty;
    public List<ApprovalAffectedEntityDto> AffectedEntities { get; set; } = [];
    public string? ThresholdSummary { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ApprovalAffectedEntityDto
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class ApprovalDecisionCommandDto
{
    public Guid ApprovalId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public Guid? StepId { get; set; }
    public string? Comment { get; set; }
    public Guid? ClientRequestId { get; set; }
}

public sealed class ApprovalDecisionResultDto
{
    public ApprovalRequestDto Approval { get; set; } = new();
    public ApprovalStepDto? DecidedStep { get; set; }
    public ApprovalStepDto? NextStep { get; set; }
    public bool IsFinalized { get; set; }
}

public sealed class SetNotificationStatusCommandDto
{
    public string Status { get; set; } = string.Empty;
}

public sealed class OpenDirectAgentConversationCommandDto
{
    public Guid AgentId { get; set; }
}

public sealed class DirectConversationDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ChannelType { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public Guid AgentId { get; set; }
    public string AgentDisplayName { get; set; } = string.Empty;
    public string AgentRoleName { get; set; } = string.Empty;
    public string AgentStatus { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class DirectConversationPageDto
{
    public List<DirectConversationDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public sealed class MobileConversationSummaryDto
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string AgentDisplayName { get; set; } = string.Empty;
    public string AgentRoleName { get; set; } = string.Empty;
    public string? LatestMessagePreview { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class MobileConversationPageDto
{
    public List<MobileConversationSummaryDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public sealed class MobileMessageListItemDto
{
    public Guid Id { get; set; }
    public string SenderType { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConversationId { get; set; }
    public string SenderType { get; set; } = string.Empty;
    public Guid? SenderId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? RationaleSummary { get; set; }
    public Dictionary<string, JsonNode?> StructuredPayload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; }
}

public sealed class ChatMessagePageDto
{
    public List<ChatMessageDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public sealed class MobileMessagePageDto
{
    public List<MobileMessageListItemDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}

public sealed class SendDirectAgentMessageCommandDto
{
    public string Body { get; set; } = string.Empty;
    public Guid? ClientRequestId { get; set; }
}

public sealed class SendDirectAgentMessageResultDto
{
    public DirectConversationDto Conversation { get; set; } = new();
    public ChatMessageDto? HumanMessage { get; set; }
    public ChatMessageDto? AgentMessage { get; set; }
}

public sealed class CompanyAgentSummaryDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Seniority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Personality { get; set; } = string.Empty;
}

public sealed class DashboardBriefingCardDto
{
    public CompanyBriefingDto? Daily { get; set; }
    public CompanyBriefingDto? Weekly { get; set; }
}

public sealed class MobileBriefingDto
{
    public Guid? Id { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public DateTime? GeneratedUtc { get; set; }
    public List<string> Highlights { get; set; } = [];
    public DateTime SyncedAtUtc { get; set; }
}

public sealed class CompanyBriefingDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string BriefingType { get; set; } = string.Empty;
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SummaryBody { get; set; } = string.Empty;
    public Dictionary<string, JsonNode?> StructuredPayload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<BriefingSourceReferenceDto> SourceReferences { get; set; } = [];
    public Guid? MessageId { get; set; }
    public DateTime GeneratedUtc { get; set; }
}

public sealed class BriefingSourceReferenceDto
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Route { get; set; }
}
