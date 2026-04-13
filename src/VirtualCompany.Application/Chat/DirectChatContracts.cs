using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Chat;

public static class ChatChannelTypes
{
    public const string DirectAgent = "direct_agent";
}

public static class ChatSenderTypes
{
    public const string User = "user";
    public const string Agent = "agent";
    public const string System = "system";
}

public static class ChatMessageTypes
{
    public const string Text = "text";
}

public sealed record OpenDirectAgentConversationCommand(Guid AgentId);

public sealed record GetConversationMessagesQuery(int? Skip, int? Take);

public sealed record GetDirectConversationsQuery(int? Skip, int? Take);

public sealed record SendDirectAgentMessageCommand(string Body, Guid? ClientRequestId = null);

public sealed record CreateTaskFromChatCommand(
    Guid? SourceMessageId,
    string? Title,
    string? Description,
    string? Priority,
    DateTime? DueAt = null,
    Guid? AssignedAgentId = null);

public sealed record LinkConversationToTaskCommand(
    Guid TaskId,
    Guid? SourceMessageId);

public sealed record GetConversationRelatedTasksQuery(int? Skip, int? Take);

public sealed record DirectConversationDto(
    Guid Id,
    Guid CompanyId,
    string ChannelType,
    Guid CreatedByUserId,
    Guid AgentId,
    string AgentDisplayName,
    string AgentRoleName,
    string AgentStatus,
    string? Subject,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record DirectConversationPageDto(
    IReadOnlyList<DirectConversationDto> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record ChatMessageDto(
    Guid Id,
    Guid CompanyId,
    Guid ConversationId,
    string SenderType,
    Guid? SenderId,
    string MessageType,
    string Body,
    string? RationaleSummary,
    Dictionary<string, JsonNode?> StructuredPayload,
    DateTime CreatedAt);

public sealed record ChatMessagePageDto(
    IReadOnlyList<ChatMessageDto> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record SendDirectAgentMessageResultDto(
    DirectConversationDto Conversation,
    ChatMessageDto HumanMessage,
    ChatMessageDto AgentMessage);

public sealed record CreateTaskFromChatResultDto(
    Guid TaskId,
    Guid ConversationId,
    Guid? SourceMessageId,
    Guid LinkId);

public sealed record LinkConversationToTaskResultDto(
    Guid LinkId,
    Guid TaskId,
    Guid ConversationId,
    Guid? SourceMessageId,
    bool Created);

public sealed record ConversationRelatedTaskDto(
    Guid TaskId,
    string Title,
    string Status,
    string Priority,
    Guid? AssignedAgentId,
    string? AssignedAgentDisplayName,
    string LinkType,
    Guid? SourceMessageId,
    DateTime LinkedAt);

public sealed record ConversationRelatedTaskListDto(
    IReadOnlyList<ConversationRelatedTaskDto> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record DirectAgentChatOrchestrationRequest(
    Guid CompanyId,
    Guid ConversationId,
    Guid AgentId,
    Guid UserId,
    string HumanMessage,
    string CorrelationId);

public sealed record DirectAgentChatReply(
    string MessageText,
    string? RationaleSummary,
    Dictionary<string, JsonNode?> StructuredPayload);

public interface ICompanyDirectChatService
{
    Task<DirectConversationDto> GetOrCreateDirectAgentConversationAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken);
    Task<ChatMessagePageDto> GetMessagesAsync(Guid companyId, Guid conversationId, GetConversationMessagesQuery query, CancellationToken cancellationToken);
    Task<DirectConversationPageDto> GetDirectConversationsAsync(Guid companyId, GetDirectConversationsQuery query, CancellationToken cancellationToken);
    Task<SendDirectAgentMessageResultDto> SendMessageAsync(Guid companyId, Guid conversationId, SendDirectAgentMessageCommand command, CancellationToken cancellationToken);
    Task<CreateTaskFromChatResultDto> CreateTaskFromChatAsync(Guid companyId, Guid conversationId, CreateTaskFromChatCommand command, CancellationToken cancellationToken);
    Task<LinkConversationToTaskResultDto> LinkConversationToTaskAsync(Guid companyId, Guid conversationId, LinkConversationToTaskCommand command, CancellationToken cancellationToken);
    Task<ConversationRelatedTaskListDto> GetRelatedTasksAsync(Guid companyId, Guid conversationId, GetConversationRelatedTasksQuery query, CancellationToken cancellationToken);
}

public interface IDirectAgentChatOrchestrator
{
    Task<DirectAgentChatReply> GenerateReplyAsync(DirectAgentChatOrchestrationRequest request, CancellationToken cancellationToken);
}

public sealed class DirectChatValidationException : Exception
{
    public DirectChatValidationException(IDictionary<string, string[]> errors)
        : base("Direct chat validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}