using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Chat;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Observability;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyDirectChatService : ICompanyDirectChatService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int MessageMaxLength = 16000;
    private const int RationaleSummaryMaxLength = 512;

    private static readonly HashSet<string> UnsafePayloadKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "analysis",
        "chain_of_thought",
        "chainOfThought",
        "hidden_reasoning",
        "hiddenReasoning",
        "internal_reasoning",
        "internalReasoning",
        "raw_reasoning",
        "rawReasoning",
        "reasoning",
        "scratchpad",
        "thoughts"
    };

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly IDirectAgentChatOrchestrator _orchestrator;
    private readonly ICompanyTaskCommandService _taskCommandService;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;

    public CompanyDirectChatService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        IDirectAgentChatOrchestrator orchestrator,
        ICompanyTaskCommandService taskCommandService,
        IAuditEventWriter auditEventWriter,
        ICorrelationContextAccessor correlationContextAccessor)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _orchestrator = orchestrator;
        _taskCommandService = taskCommandService;
        _auditEventWriter = auditEventWriter;
        _correlationContextAccessor = correlationContextAccessor;
    }

    public async Task<DirectConversationDto> GetOrCreateDirectAgentConversationAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var agent = await GetChatAgentAsync(companyId, agentId, cancellationToken);

        var conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.ChannelType == ChatChannelTypes.DirectAgent &&
                x.CreatedByUserId == membership.UserId &&
                x.AgentId == agentId,
                cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            conversation = new Conversation(
                Guid.NewGuid(),
                companyId,
                ChatChannelTypes.DirectAgent,
                $"Direct chat with {agent.DisplayName}",
                membership.UserId,
                agentId,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["thread_policy"] = JsonValue.Create("one_per_user_agent"),
                    ["agent_role_name"] = JsonValue.Create(agent.RoleName)
                });
            _dbContext.Conversations.Add(conversation);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ToConversationDto(conversation, agent);
    }

    public async Task<ChatMessagePageDto> GetMessagesAsync(
        Guid companyId,
        Guid conversationId,
        GetConversationMessagesQuery query,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        await GetAccessibleConversationAsync(companyId, conversationId, cancellationToken);

        var skip = Math.Max(0, query.Skip ?? 0);
        var take = Math.Clamp(query.Take ?? DefaultPageSize, 1, MaxPageSize);
        var messagesQuery = _dbContext.Messages
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ConversationId == conversationId &&
                x.Conversation.CompanyId == companyId &&
                x.Conversation.ChannelType == ChatChannelTypes.DirectAgent &&
                x.Conversation.CreatedByUserId == membership.UserId);

        var totalCount = await messagesQuery.CountAsync(cancellationToken);
        var messages = (await messagesQuery
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)).AsEnumerable()
            .Reverse()
            .ToList();

        return new ChatMessagePageDto(
            messages.Select(ToMessageDto).ToList(),
            totalCount,
            skip,
            take);
    }

    public async Task<DirectConversationPageDto> GetDirectConversationsAsync(
        Guid companyId,
        GetDirectConversationsQuery query,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var skip = Math.Max(0, query.Skip ?? 0);
        var take = Math.Clamp(query.Take ?? DefaultPageSize, 1, MaxPageSize);

        var conversationsQuery = _dbContext.Conversations
            .AsNoTracking()
            .Include(x => x.Agent)
            .Where(x =>
                x.CompanyId == companyId &&
                x.ChannelType == ChatChannelTypes.DirectAgent &&
                x.CreatedByUserId == membership.UserId &&
                x.AgentId != null);

        var totalCount = await conversationsQuery.CountAsync(cancellationToken);
        var conversations = await conversationsQuery
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new DirectConversationPageDto(
            conversations.Select(x => ToConversationDto(x, x.Agent!)).ToList(),
            totalCount,
            skip,
            take);
    }

    public async Task<SendDirectAgentMessageResultDto> SendMessageAsync(
        Guid companyId,
        Guid conversationId,
        SendDirectAgentMessageCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var clientRequestId = command.ClientRequestId is Guid requestId && requestId != Guid.Empty
            ? requestId
            : (Guid?)null;
        if (clientRequestId is Guid retryRequestId && await TryGetRetriedSendResultAsync(companyId, conversationId, retryRequestId, cancellationToken) is { } retryResult)
        {
            return retryResult;
        }

        var conversation = await GetAccessibleConversationAsync(companyId, conversationId, cancellationToken);
        if (conversation.AgentId is not Guid agentId)
        {
            throw new KeyNotFoundException("Direct agent conversation not found.");
        }

        var agent = await GetChatAgentAsync(companyId, agentId, cancellationToken);
        var humanMessage = new Message(
            Guid.NewGuid(),
            companyId,
            conversation.Id,
            ChatSenderTypes.User,
            membership.UserId,
            ChatMessageTypes.Text,
            command.Body,
            clientRequestId is null
                ? null
                : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase) { ["client_request_id"] = JsonValue.Create(clientRequestId.Value.ToString("N")) });

        _dbContext.Messages.Add(humanMessage);
        conversation.Touch();
        await _dbContext.SaveChangesAsync(cancellationToken);

        var reply = await _orchestrator.GenerateReplyAsync(
            new DirectAgentChatOrchestrationRequest(
                companyId,
                conversation.Id,
                agentId,
                membership.UserId,
                command.Body,
                ResolveCorrelationId()),
            cancellationToken).ConfigureAwait(false);

        var agentPayload = SanitizeStructuredPayload(reply.StructuredPayload);
        var rationaleSummary = NormalizeRationaleSummary(reply.RationaleSummary);
        if (!string.IsNullOrWhiteSpace(rationaleSummary))
        {
            agentPayload["rationale_summary"] = JsonValue.Create(rationaleSummary);
        }

        var agentMessage = new Message(
            Guid.NewGuid(),
            companyId,
            conversation.Id,
            ChatSenderTypes.Agent,
            agentId,
            ChatMessageTypes.Text,
            reply.MessageText,
            agentPayload);

        _dbContext.Messages.Add(agentMessage);
        conversation.Touch();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SendDirectAgentMessageResultDto(
            ToConversationDto(conversation, agent),
            ToMessageDto(humanMessage),
            ToMessageDto(agentMessage));
    }

    private async Task<SendDirectAgentMessageResultDto?> TryGetRetriedSendResultAsync(
        Guid companyId,
        Guid conversationId,
        Guid clientRequestId,
        CancellationToken cancellationToken)
    {
        var recentMessages = await _dbContext.Messages
            .AsNoTracking()
            .Include(x => x.Conversation)
            .ThenInclude(x => x.Agent)
            .Where(x =>
                x.CompanyId == companyId &&
                x.ConversationId == conversationId &&
                x.Conversation.CompanyId == companyId &&
                x.Conversation.ChannelType == ChatChannelTypes.DirectAgent)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(80)
            .ToListAsync(cancellationToken);

        var humanMessage = recentMessages
            .Where(x => string.Equals(x.SenderType, ChatSenderTypes.User, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(x => TryReadClientRequestId(x.StructuredPayload) == clientRequestId);
        if (humanMessage is null || humanMessage.Conversation.Agent is null)
        {
            return null;
        }

        var agentMessage = recentMessages
            .Where(x => x.CreatedUtc >= humanMessage.CreatedUtc)
            .OrderBy(x => x.CreatedUtc)
            .FirstOrDefault(x => string.Equals(x.SenderType, ChatSenderTypes.Agent, StringComparison.OrdinalIgnoreCase));

        return new SendDirectAgentMessageResultDto(
            ToConversationDto(humanMessage.Conversation, humanMessage.Conversation.Agent),
            ToMessageDto(humanMessage),
            agentMessage is null ? null! : ToMessageDto(agentMessage));
    }

    private static Guid? TryReadClientRequestId(IReadOnlyDictionary<string, JsonNode?> payload) =>
        payload.TryGetValue("client_request_id", out var node) &&
        node is JsonValue value &&
        value.TryGetValue<string>(out var text) &&
        Guid.TryParse(text, out var requestId)
            ? requestId
            : null;

    public async Task<CreateTaskFromChatResultDto> CreateTaskFromChatAsync(
        Guid companyId,
        Guid conversationId,
        CreateTaskFromChatCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var conversation = await GetAccessibleConversationAsync(companyId, conversationId, cancellationToken);

        if (conversation.AgentId is not Guid agentId)
        {
            throw new KeyNotFoundException("Direct agent conversation not found.");
        }

        Message? sourceMessage = null;
        if (command.SourceMessageId is Guid sourceMessageId)
        {
            sourceMessage = await _dbContext.Messages
                .AsNoTracking()
                .SingleOrDefaultAsync(x =>
                    x.Id == sourceMessageId &&
                    x.CompanyId == companyId &&
                    x.Conversation.CompanyId == companyId &&
                    x.Conversation.ChannelType == ChatChannelTypes.DirectAgent &&
                    x.Conversation.CreatedByUserId == conversation.CreatedByUserId &&
                    x.ConversationId == conversationId,
                    cancellationToken);

            if (sourceMessage is null)
            {
                throw new KeyNotFoundException("Source message not found.");
            }
        }

        var recentMessages = await _dbContext.Messages
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ConversationId == conversationId &&
                x.Conversation.CompanyId == companyId &&
                x.Conversation.ChannelType == ChatChannelTypes.DirectAgent &&
                x.Conversation.CreatedByUserId == conversation.CreatedByUserId)
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Take(8)
            .ToListAsync(cancellationToken);
        recentMessages.Reverse();

        var title = NormalizeTaskTitle(command.Title, sourceMessage?.Body, conversation.Subject);
        var description = NormalizeTaskDescription(command.Description, recentMessages);
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = JsonValue.Create("direct_agent_chat"),
            ["conversation_id"] = JsonValue.Create(conversation.Id.ToString()),
            ["agent_id"] = JsonValue.Create(agentId.ToString())
        };

        if (sourceMessage is not null)
        {
            payload["source_message_id"] = JsonValue.Create(sourceMessage.Id.ToString());
        }

        var task = await _taskCommandService.CreateTaskAsync(
            companyId,
            new CreateTaskCommand(
                "chat_follow_up",
                title,
                description,
                string.IsNullOrWhiteSpace(command.Priority) ? "normal" : command.Priority,
                command.DueAt,
                command.AssignedAgentId ?? agentId,
                payload,
                RationaleSummary: "Created explicitly from a direct agent chat interaction."),
            cancellationToken);

        var linkType = sourceMessage is null ? "created_from_conversation" : "created_from_message";
        var link = await UpsertConversationTaskLinkAsync(
            companyId,
            conversation.Id,
            sourceMessage?.Id,
            task.Id,
            linkType,
            membership.UserId,
            cancellationToken);

        _dbContext.Messages.Add(new Message(
            Guid.NewGuid(),
            companyId,
            conversation.Id,
            ChatSenderTypes.System,
            null,
            ChatMessageTypes.Text,
            $"Task '{title}' was created from this conversation."));
        conversation.Touch();

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                membership.UserId,
                AuditEventActions.DirectChatTaskCreated,
                AuditTargetTypes.ConversationTaskLink,
                link.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                "Created a task from an explicit direct chat action.",
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["conversation_id"] = conversation.Id.ToString("N"),
                    ["message_id"] = sourceMessage?.Id.ToString("N"),
                    ["task_id"] = task.Id.ToString("N")
                }),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CreateTaskFromChatResultDto(task.Id, conversation.Id, sourceMessage?.Id, link.Id);
    }

    public async Task<LinkConversationToTaskResultDto> LinkConversationToTaskAsync(
        Guid companyId,
        Guid conversationId,
        LinkConversationToTaskCommand command,
        CancellationToken cancellationToken)
    {
        if (command.TaskId == Guid.Empty)
        {
            throw new DirectChatValidationException(
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(command.TaskId)] = ["TaskId is required."]
                });
        }

        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var conversation = await GetAccessibleConversationAsync(companyId, conversationId, cancellationToken);
        var sourceMessage = await GetOptionalSourceMessageAsync(companyId, conversationId, command.SourceMessageId, cancellationToken);
        var task = await _dbContext.WorkTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == command.TaskId, cancellationToken);

        if (task is null)
        {
            throw new KeyNotFoundException("Task not found.");
        }

        var existing = await _dbContext.ConversationTaskLinks
            .SingleOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.ConversationId == conversationId &&
                x.TaskId == command.TaskId &&
                x.MessageId == command.SourceMessageId,
                cancellationToken);

        if (existing is not null)
        {
            return new LinkConversationToTaskResultDto(existing.Id, command.TaskId, conversationId, command.SourceMessageId, false);
        }

        var linkType = sourceMessage is null ? "linked_from_conversation" : "linked_from_message";
        var link = await UpsertConversationTaskLinkAsync(
            companyId,
            conversation.Id,
            sourceMessage?.Id,
            task.Id,
            linkType,
            membership.UserId,
            cancellationToken);

        _dbContext.Messages.Add(new Message(
            Guid.NewGuid(),
            companyId,
            conversation.Id,
            ChatSenderTypes.System,
            null,
            ChatMessageTypes.Text,
            $"Linked this conversation to task '{task.Title}'."));
        conversation.Touch();

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                membership.UserId,
                AuditEventActions.DirectChatTaskLinked,
                AuditTargetTypes.ConversationTaskLink,
                link.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                "Linked a task from an explicit direct chat action.",
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["conversation_id"] = conversation.Id.ToString("N"),
                    ["message_id"] = sourceMessage?.Id.ToString("N"),
                    ["task_id"] = task.Id.ToString("N")
                }),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new LinkConversationToTaskResultDto(link.Id, task.Id, conversation.Id, sourceMessage?.Id, true);
    }

    public async Task<ConversationRelatedTaskListDto> GetRelatedTasksAsync(
        Guid companyId,
        Guid conversationId,
        GetConversationRelatedTasksQuery query,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        await GetAccessibleConversationAsync(companyId, conversationId, cancellationToken);

        var skip = Math.Max(0, query.Skip ?? 0);
        var take = Math.Clamp(query.Take ?? DefaultPageSize, 1, MaxPageSize);
        var linksQuery = _dbContext.ConversationTaskLinks
            .AsNoTracking()
            .Include(x => x.Task)
            .ThenInclude(x => x.AssignedAgent)
            .Where(x => x.CompanyId == companyId && x.ConversationId == conversationId);

        var totalCount = await linksQuery.CountAsync(cancellationToken);
        var links = await linksQuery
            .OrderByDescending(x => x.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new ConversationRelatedTaskListDto(
            links.Select(x => new ConversationRelatedTaskDto(
                x.TaskId,
                x.Task.Title,
                x.Task.Status.ToStorageValue(),
                x.Task.Priority.ToStorageValue(),
                x.Task.AssignedAgentId,
                x.Task.AssignedAgent?.DisplayName,
                x.LinkType,
                x.MessageId,
                x.CreatedUtc)).ToList(),
            totalCount,
            skip,
            take);
    }

    private async Task<Conversation> GetAccessibleConversationAsync(
        Guid companyId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.Id == conversationId &&
                x.ChannelType == ChatChannelTypes.DirectAgent &&
                x.CreatedByUserId == membership.UserId,
                cancellationToken);

        return conversation ?? throw new KeyNotFoundException("Conversation not found.");
    }

    private async Task<Message?> GetOptionalSourceMessageAsync(
        Guid companyId,
        Guid conversationId,
        Guid? sourceMessageId,
        CancellationToken cancellationToken)
    {
        if (sourceMessageId is null)
        {
            return null;
        }

        var sourceMessage = await _dbContext.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == sourceMessageId.Value &&
                x.CompanyId == companyId &&
                x.Conversation.CompanyId == companyId &&
                x.Conversation.ChannelType == ChatChannelTypes.DirectAgent &&
                x.ConversationId == conversationId &&
                x.ConversationId == conversationId &&
                x.Conversation.CompanyId == companyId,
                cancellationToken);

        return sourceMessage ?? throw new KeyNotFoundException("Source message not found.");
    }

    private async Task<ConversationTaskLink> UpsertConversationTaskLinkAsync(
        Guid companyId,
        Guid conversationId,
        Guid? messageId,
        Guid taskId,
        string linkType,
        Guid createdByUserId,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.ConversationTaskLinks
            .SingleOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.ConversationId == conversationId &&
                x.TaskId == taskId &&
                x.MessageId == messageId,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var link = new ConversationTaskLink(Guid.NewGuid(), companyId, conversationId, messageId, taskId, linkType, createdByUserId);
        _dbContext.ConversationTaskLinks.Add(link);
        return link;
    }

    private async Task<Agent> GetChatAgentAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await _dbContext.Agents
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);

        if (agent is null)
        {
            throw new KeyNotFoundException("Agent not found.");
        }

        if (agent.Status is AgentStatus.Paused or AgentStatus.Archived)
        {
            throw new DirectChatValidationException(
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(OpenDirectAgentConversationCommand.AgentId)] = ["Paused and archived agents cannot participate in direct chat."]
                });
        }

        return agent;
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipContextResolver.ResolveAsync(companyId, cancellationToken);
        return membership ?? throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
    }

    private static void Validate(SendDirectAgentMessageCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(command.Body))
        {
            errors[nameof(command.Body)] = ["Body is required."];
        }
        else if (command.Body.Trim().Length > MessageMaxLength)
        {
            errors[nameof(command.Body)] = [$"Body must be {MessageMaxLength} characters or fewer."];
        }

        if (errors.Count > 0)
        {
            throw new DirectChatValidationException(errors);
        }
    }

    private static string NormalizeTaskTitle(string? commandTitle, string? sourceMessageBody, string? conversationSubject)
    {
        var title = FirstNonEmpty(commandTitle, sourceMessageBody, conversationSubject, "Chat follow-up")!;
        title = title.ReplaceLineEndings(" ").Trim();
        return title.Length <= 200 ? title : string.Concat(title.AsSpan(0, 197), "...");
    }

    private static string NormalizeTaskDescription(string? commandDescription, IReadOnlyList<Message> recentMessages)
    {
        if (!string.IsNullOrWhiteSpace(commandDescription))
        {
            return commandDescription.Trim();
        }

        var builder = new StringBuilder("Created from direct agent chat.");
        foreach (var message in recentMessages)
        {
            builder.AppendLine();
            builder.Append(message.SenderType);
            builder.Append(": ");
            builder.Append(message.Body);
        }

        var description = builder.ToString();
        return description.Length <= 4000 ? description : string.Concat(description.AsSpan(0, 3997), "...");
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static DirectConversationDto ToConversationDto(Conversation conversation, Agent agent) =>
        new(
            conversation.Id,
            conversation.CompanyId,
            conversation.ChannelType,
            conversation.CreatedByUserId,
            agent.Id,
            agent.DisplayName,
            agent.RoleName,
            agent.Status.ToStorageValue(),
            conversation.Subject,
            conversation.CreatedUtc,
            conversation.UpdatedUtc);

    private static ChatMessageDto ToMessageDto(Message message) =>
        new(
            message.Id,
            message.CompanyId,
            message.ConversationId,
            message.SenderType,
            message.SenderId,
            message.MessageType,
            message.Body,
            ExtractRationaleSummary(message.StructuredPayload),
            SanitizeStructuredPayload(message.StructuredPayload),
            message.CreatedUtc);

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?> nodes) =>
        nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, JsonNode?> SanitizeStructuredPayload(IReadOnlyDictionary<string, JsonNode?> nodes)
    {
        if (nodes.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        var sanitized = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in nodes)
        {
            if (UnsafePayloadKeys.Contains(pair.Key))
            {
                continue;
            }

            sanitized[pair.Key] = pair.Value?.DeepClone();
        }

        return sanitized;
    }

    private static string? ExtractRationaleSummary(IReadOnlyDictionary<string, JsonNode?> payload)
    {
        if (!payload.TryGetValue("rationale_summary", out var node) || node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var rationale)
            ? NormalizeRationaleSummary(rationale)
            : null;
    }

    private static string? NormalizeRationaleSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.ReplaceLineEndings(" ").Trim();
        return trimmed.Length <= RationaleSummaryMaxLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, RationaleSummaryMaxLength - 3), "...");
    }

    private string ResolveCorrelationId() =>
        string.IsNullOrWhiteSpace(_correlationContextAccessor.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : _correlationContextAccessor.CorrelationId!;
}

public sealed class DirectAgentChatOrchestrator : IDirectAgentChatOrchestrator
{
    private readonly ISingleAgentOrchestrationService _singleAgentOrchestrationService;

    public DirectAgentChatOrchestrator(
        ISingleAgentOrchestrationService singleAgentOrchestrationService)
    {
        _singleAgentOrchestrationService = singleAgentOrchestrationService;
    }

    public async Task<DirectAgentChatReply> GenerateReplyAsync(
        DirectAgentChatOrchestrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Compatibility facade only: direct chat is intentionally routed through the shared single-agent engine.
        var result = await _singleAgentOrchestrationService.ExecuteAsync(
            new OrchestrationRequest(
                request.CompanyId,
                request.AgentId,
                ConversationId: request.ConversationId,
                UserInput: request.HumanMessage,
                InitiatingActorId: request.UserId,
                InitiatingActorType: "user",
                CorrelationId: request.CorrelationId,
                IntentHint: OrchestrationIntentValues.Chat,
                ActorMetadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["entry_point"] = JsonValue.Create("direct_agent_chat")
                }),
            cancellationToken);

        return new DirectAgentChatReply(
            result.UserFacingOutput,
            result.RationaleSummary,
            result.StructuredOutput);
    }
}
