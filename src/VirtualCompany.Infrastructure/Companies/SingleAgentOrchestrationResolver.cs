using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Chat;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class SingleAgentOrchestrationResolver : ISingleAgentOrchestrationResolver
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAgentRuntimeProfileResolver _agentRuntimeProfileResolver;
    private readonly TimeProvider _timeProvider;

    public SingleAgentOrchestrationResolver(
        VirtualCompanyDbContext dbContext,
        IAgentRuntimeProfileResolver agentRuntimeProfileResolver,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _agentRuntimeProfileResolver = agentRuntimeProfileResolver;
        _timeProvider = timeProvider;
    }

    public async Task<OrchestrationResolutionResult> ResolveAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = EnsureCorrelationId(request.CorrelationId);
        var validationFailure = Validate(request, correlationId);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.CompanyId, cancellationToken);

        if (company is null)
        {
            return Failure(
                correlationId,
                nameof(request.CompanyId),
                OrchestrationResolutionErrorCodes.MissingCompanyContext,
                "Company context was not found for the orchestration request.");
        }

        var task = request.TaskId.HasValue
            ? await GetTaskAsync(request.CompanyId, request.TaskId.Value, cancellationToken)
            : null;

        if (request.TaskId.HasValue && task is null)
        {
            return Failure(
                correlationId,
                nameof(request.TaskId),
                OrchestrationResolutionErrorCodes.TaskNotFound,
                "Task was not found in the requested company scope.");
        }

        var conversation = request.ConversationId.HasValue
            ? await GetConversationAsync(request.CompanyId, request.ConversationId.Value, cancellationToken)
            : null;

        if (request.ConversationId.HasValue && conversation is null)
        {
            return Failure(
                correlationId,
                nameof(request.ConversationId),
                OrchestrationResolutionErrorCodes.ConversationNotFound,
                "Conversation was not found in the requested company scope.");
        }

        var agentResolution = ResolveAgentId(request, task, conversation, correlationId);
        if (agentResolution.Failure is not null)
        {
            return agentResolution.Failure;
        }

        var agentProfile = await GetAgentProfileAsync(request.CompanyId, agentResolution.AgentId, cancellationToken);
        if (agentProfile is null)
        {
            return Failure(
                correlationId,
                nameof(request.AgentId),
                OrchestrationResolutionErrorCodes.AgentNotFound,
                "Agent was not found in the requested company scope.");
        }

        if (!agentProfile.CanReceiveAssignments)
        {
            return Failure(
                correlationId,
                nameof(request.AgentId),
                OrchestrationResolutionErrorCodes.AgentStatusNotExecutable,
                "The selected agent cannot execute single-agent requests in its current status.");
        }

        var resolvedAgent = ToResolvedAgentContext(agentProfile);
        var resolvedIntent = ResolveIntent(request, task, conversation);
        var companyContext = new CompanyRuntimeContext(
            company.Id,
            company.Name,
            company.Industry,
            company.BusinessType,
            company.Timezone,
            company.Currency,
            company.Language,
            company.ComplianceRegion);

        var runtimeContext = new RuntimeContext(
            Guid.NewGuid(),
            companyContext,
            resolvedAgent,
            resolvedIntent,
            new ActorRuntimeContext(
                NormalizeOptional(request.InitiatingActorType),
                request.InitiatingActorId,
                NormalizeOptional(request.UserInput),
                correlationId,
                _timeProvider.GetUtcNow().UtcDateTime,
                CloneNodes(request.ActorMetadata)),
            task is null ? null : ToTaskRuntimeContext(task),
            conversation is null ? null : ToConversationRuntimeContext(conversation),
            new PolicyRuntimeContext(
                resolvedAgent.AutonomyLevel,
                CloneNodes(resolvedAgent.ToolPermissions),
                CloneNodes(resolvedAgent.DataScopes)));

        return OrchestrationResolutionResult.Success(runtimeContext);
    }

    private static OrchestrationResolutionResult? Validate(OrchestrationRequest request, string correlationId)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Failure(
                correlationId,
                nameof(request.CompanyId),
                OrchestrationResolutionErrorCodes.MissingCompanyContext,
                "CompanyId is required.");
        }

        if (request.AgentId == Guid.Empty)
        {
            return Failure(correlationId, nameof(request.AgentId), OrchestrationResolutionErrorCodes.AgentNotFound, "AgentId cannot be empty.");
        }

        if (request.TaskId == Guid.Empty)
        {
            return Failure(correlationId, nameof(request.TaskId), OrchestrationResolutionErrorCodes.TaskNotFound, "TaskId cannot be empty.");
        }

        if (request.ConversationId == Guid.Empty)
        {
            return Failure(correlationId, nameof(request.ConversationId), OrchestrationResolutionErrorCodes.ConversationNotFound, "ConversationId cannot be empty.");
        }

        if (request.InitiatingActorId == Guid.Empty)
        {
            return Failure(correlationId, nameof(request.InitiatingActorId), OrchestrationResolutionErrorCodes.MissingCompanyContext, "InitiatingActorId cannot be empty.");
        }

        return null;
    }

    private async Task<WorkTask?> GetTaskAsync(Guid companyId, Guid taskId, CancellationToken cancellationToken) =>
        await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == taskId, cancellationToken);

    private async Task<Conversation?> GetConversationAsync(Guid companyId, Guid conversationId, CancellationToken cancellationToken) =>
        await _dbContext.Conversations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == conversationId, cancellationToken);

    private async Task<AgentRuntimeProfileDto?> GetAgentProfileAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _agentRuntimeProfileResolver.GetCurrentProfileAsync(companyId, agentId, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static (Guid AgentId, OrchestrationResolutionResult? Failure) ResolveAgentId(
        OrchestrationRequest request,
        WorkTask? task,
        Conversation? conversation,
        string correlationId)
    {
        if (request.AgentId.HasValue)
        {
            return (request.AgentId.Value, null);
        }

        if (task is not null)
        {
            if (task.AssignedAgentId.HasValue)
            {
                return (task.AssignedAgentId.Value, null);
            }

            return (Guid.Empty, Failure(
                correlationId,
                nameof(request.AgentId),
                OrchestrationResolutionErrorCodes.NoResolvableTargetAgent,
                "Task-backed orchestration requires an assigned agent when no AgentId is provided."));
        }

        if (conversation is not null)
        {
            if (string.Equals(conversation.ChannelType, ChatChannelTypes.DirectAgent, StringComparison.OrdinalIgnoreCase) &&
                conversation.AgentId.HasValue)
            {
                return (conversation.AgentId.Value, null);
            }

            return (Guid.Empty, Failure(
                correlationId,
                nameof(request.ConversationId),
                OrchestrationResolutionErrorCodes.AmbiguousTargetAgent,
                "Conversation does not have an unambiguous direct-agent target."));
        }

        return (Guid.Empty, Failure(
            correlationId,
            nameof(request.AgentId),
            OrchestrationResolutionErrorCodes.NoResolvableTargetAgent,
            "AgentId, an assigned TaskId, or a direct-agent ConversationId is required."));
    }

    private static ResolvedIntent ResolveIntent(
        OrchestrationRequest request,
        WorkTask? task,
        Conversation? conversation)
    {
        if (!string.IsNullOrWhiteSpace(request.IntentHint))
        {
            var normalizedIntent = NormalizeToken(request.IntentHint);
            return new ResolvedIntent(
                normalizedIntent,
                task is null ? normalizedIntent : NormalizeToken(task.Type),
                OrchestrationResolutionSources.Explicit,
                true,
                1m);
        }

        if (task is not null)
        {
            return new ResolvedIntent(
                OrchestrationIntentValues.TaskExecution,
                NormalizeToken(task.Type),
                OrchestrationResolutionSources.Task,
                true,
                1m);
        }

        if (conversation is not null)
        {
            return new ResolvedIntent(
                OrchestrationIntentValues.Chat,
                string.Equals(conversation.ChannelType, ChatChannelTypes.DirectAgent, StringComparison.OrdinalIgnoreCase)
                    ? "direct_agent_chat"
                    : NormalizeToken(conversation.ChannelType),
                OrchestrationResolutionSources.Conversation,
                true,
                1m);
        }

        if (!string.IsNullOrWhiteSpace(request.UserInput))
        {
            return new ResolvedIntent(
                OrchestrationIntentValues.Chat,
                OrchestrationIntentValues.Chat,
                OrchestrationResolutionSources.Heuristic,
                true,
                0.8m);
        }

        return new ResolvedIntent(
            OrchestrationIntentValues.GeneralAgentRequest,
            OrchestrationIntentValues.GeneralAgentRequest,
            OrchestrationResolutionSources.Heuristic,
            true,
            0.6m);
    }

    private static ResolvedAgentContext ToResolvedAgentContext(AgentRuntimeProfileDto agent) =>
        new(
            agent.Id,
            agent.CompanyId,
            agent.DisplayName,
            agent.RoleName,
            agent.Department,
            agent.Status,
            agent.AutonomyLevel,
            agent.RoleBrief,
            CloneNodes(agent.ToolPermissions),
            CloneNodes(agent.DataScopes),
            agent.CanReceiveAssignments,
            agent.UpdatedUtc);

    private static TaskRuntimeContext ToTaskRuntimeContext(WorkTask task) =>
        new(
            task.Id,
            task.Type,
            task.Title,
            task.Description,
            task.Priority.ToStorageValue(),
            task.Status.ToStorageValue(),
            task.AssignedAgentId,
            task.ParentTaskId,
            task.WorkflowInstanceId,
            CloneNodes(task.InputPayload));

    private static ConversationRuntimeContext ToConversationRuntimeContext(Conversation conversation) =>
        new(
            conversation.Id,
            conversation.ChannelType,
            conversation.Subject,
            conversation.CreatedByUserId,
            conversation.AgentId,
            conversation.UpdatedUtc);

    private static string EnsureCorrelationId(string? requestedCorrelationId)
    {
        if (!string.IsNullOrWhiteSpace(requestedCorrelationId))
        {
            return requestedCorrelationId.Trim();
        }

        return Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
    }

    private static string NormalizeToken(string value) =>
        value.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static OrchestrationResolutionResult Failure(
        string correlationId,
        string fieldName,
        string errorCode,
        string message) =>
        OrchestrationResolutionResult.Failure(correlationId, fieldName, errorCode, message);
}
