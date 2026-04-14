using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.ProactiveMessaging;

public sealed record GenerateProactiveMessageCommand(
    Guid AgentId,
    string SourceEntityType,
    Guid SourceEntityId,
    string Channel,
    Guid? RecipientUserId = null);

public sealed record ProactiveMessageDto(
    Guid Id,
    Guid CompanyId,
    string Channel,
    Guid RecipientUserId,
    string Recipient,
    string Subject,
    string Body,
    string SourceEntityType,
    Guid SourceEntityId,
    string Status,
    DateTime SentAt,
    Guid? NotificationId);

public sealed record ProactiveMessagePolicyBlockDto(
    string Code,
    string UserFacingMessage,
    IReadOnlyList<string> ReasonCodes,
    string RationaleSummary,
    ToolExecutionDecisionDto PolicyDecision);

public sealed record ProactiveMessageDeliveryResultDto(
    string Status,
    ProactiveMessageDto? Message,
    ProactiveMessagePolicyBlockDto? PolicyBlock)
{
    public bool Delivered => string.Equals(Status, "delivered", StringComparison.OrdinalIgnoreCase);
}

public sealed record ListProactiveMessagesQuery(
    string? Channel = null,
    string? SourceEntityType = null,
    Guid? SourceEntityId = null,
    int Skip = 0,
    int Take = 100);

public interface IProactiveMessageService
{
    Task<ProactiveMessageDeliveryResultDto> GenerateAndDeliverAsync(
        Guid companyId,
        GenerateProactiveMessageCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProactiveMessageDto>> ListAsync(
        Guid companyId,
        ListProactiveMessagesQuery query,
        CancellationToken cancellationToken);
}
