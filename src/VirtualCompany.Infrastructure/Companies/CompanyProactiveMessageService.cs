using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.ProactiveMessaging;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyProactiveMessageService : IProactiveMessageService
{
    private const string ToolName = "proactive_messaging";
    private const string DeliveryScope = "proactive_delivery";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly IAgentRuntimeProfileResolver _agentRuntimeProfileResolver;
    private readonly IPolicyGuardrailEngine _policyGuardrailEngine;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly TimeProvider _timeProvider;

    public CompanyProactiveMessageService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        IAgentRuntimeProfileResolver agentRuntimeProfileResolver,
        IPolicyGuardrailEngine policyGuardrailEngine,
        IAuditEventWriter auditEventWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _agentRuntimeProfileResolver = agentRuntimeProfileResolver;
        _policyGuardrailEngine = policyGuardrailEngine;
        _auditEventWriter = auditEventWriter;
        _timeProvider = timeProvider;
    }

    public async Task<ProactiveMessageDeliveryResultDto> GenerateAndDeliverAsync(
        Guid companyId,
        GenerateProactiveMessageCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var channel = ProactiveMessageChannelValues.Parse(command.Channel);
        var sourceEntityType = ProactiveMessageSourceEntityTypeValues.Parse(command.SourceEntityType);
        var source = await LoadSourceAsync(companyId, sourceEntityType, command.SourceEntityId, cancellationToken);
        var recipient = await ResolveRecipientAsync(companyId, command.RecipientUserId ?? membership.UserId, cancellationToken);
        var generated = GenerateMessage(source);
        var decision = await EvaluatePolicyAsync(companyId, command, channel, sourceEntityType, cancellationToken);

        if (!string.Equals(decision.Outcome, PolicyDecisionOutcomeValues.Allow, StringComparison.OrdinalIgnoreCase))
        {
            var blockedDecision = CreatePolicyDecisionRecord(
                companyId,
                null,
                command,
                channel,
                recipient,
                sourceEntityType,
                source.SourceEntityId,
                decision,
                ProactiveMessagePolicyDecisionOutcomeValues.Parse(decision.Outcome),
                _timeProvider.GetUtcNow().UtcDateTime);

            _dbContext.ProactiveMessagePolicyDecisions.Add(blockedDecision);

            var block = CreatePolicyBlock(decision);
            await _auditEventWriter.WriteAsync(
                new AuditEventWriteRequest(
                    companyId,
                    AuditActorTypes.Agent,
                    command.AgentId,
                    AuditEventActions.ProactiveMessageBlocked,
                    AuditTargetTypes.ProactiveMessage,
                    command.SourceEntityId.ToString("N"),
                    AuditEventOutcomes.Denied,
                    RationaleSummary: block.RationaleSummary,
                    DataSources: ["proactive_messaging", "policy_guardrail"],
                    Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sourceEntityType"] = sourceEntityType.ToStorageValue(),
                        ["sourceEntityId"] = command.SourceEntityId.ToString("N"),
                        ["channel"] = channel.ToStorageValue(),
                        ["recipientUserId"] = recipient.Id.ToString("N"),
                        ["policyOutcome"] = decision.Outcome,
                        ["primaryReasonCode"] = decision.ReasonCodes.FirstOrDefault()
                    }),
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ProactiveMessageDeliveryResultDto("blocked", null, block);
        }

        var sentUtc = _timeProvider.GetUtcNow().UtcDateTime;
        CompanyNotification? notification = null;
        if (channel == ProactiveMessageChannel.Notification)
        {
            notification = new CompanyNotification(
                Guid.NewGuid(),
                companyId,
                recipient.Id,
                CompanyNotificationType.ProactiveMessage,
                CompanyNotificationPriority.Normal,
                generated.Subject,
                generated.Body,
                sourceEntityType.ToStorageValue(),
                source.SourceEntityId,
                null,
                JsonSerializer.Serialize(new
                {
                    proactive = true,
                    sourceEntityType = sourceEntityType.ToStorageValue(),
                    sourceEntityId = source.SourceEntityId
                }),
                $"proactive-message:{companyId:N}:{recipient.Id:N}:{sourceEntityType.ToStorageValue()}:{source.SourceEntityId:N}:{channel.ToStorageValue()}",
                channel: CompanyNotificationChannel.InApp);
            _dbContext.CompanyNotifications.Add(notification);
        }

        var message = new ProactiveMessage(
            Guid.NewGuid(),
            companyId,
            channel,
            recipient.Id,
            recipient.DisplayName,
            generated.Subject,
            generated.Body,
            sourceEntityType,
            source.SourceEntityId,
            command.AgentId,
            notification?.Id,
            sentUtc,
            ToolExecutionPolicyDecisionJsonSerializer.Serialize(decision),
            decision.ReasonCodes.FirstOrDefault());

        _dbContext.ProactiveMessages.Add(message);

        var allowedDecision = CreatePolicyDecisionRecord(
            companyId,
            message.Id,
            command,
            channel,
            recipient,
            sourceEntityType,
            source.SourceEntityId,
            decision,
            ProactiveMessagePolicyDecisionOutcome.Allowed,
            sentUtc);

        _dbContext.ProactiveMessagePolicyDecisions.Add(allowedDecision);

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.Agent,
                command.AgentId,
                AuditEventActions.ProactiveMessageDelivered,
                AuditTargetTypes.ProactiveMessage,
                message.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                RationaleSummary: "Proactive message delivered after policy guardrail approval.",
                DataSources: ["proactive_messaging", "policy_guardrail"],
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sourceEntityType"] = sourceEntityType.ToStorageValue(),
                    ["sourceEntityId"] = source.SourceEntityId.ToString("N"),
                    ["channel"] = channel.ToStorageValue(),
                    ["recipientUserId"] = recipient.Id.ToString("N"),
                    ["policyOutcome"] = decision.Outcome,
                    ["notificationId"] = notification?.Id.ToString("N")
                }),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new ProactiveMessageDeliveryResultDto("delivered", ToDto(message), null);
    }

    public async Task<IReadOnlyList<ProactiveMessageDto>> ListAsync(
        Guid companyId,
        ListProactiveMessagesQuery query,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var messages = _dbContext.ProactiveMessages
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.RecipientUserId == membership.UserId);

        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            var channel = ProactiveMessageChannelValues.Parse(query.Channel);
            messages = messages.Where(x => x.Channel == channel);
        }

        if (!string.IsNullOrWhiteSpace(query.SourceEntityType))
        {
            var sourceEntityType = ProactiveMessageSourceEntityTypeValues.Parse(query.SourceEntityType);
            messages = messages.Where(x => x.SourceEntityType == sourceEntityType);
        }

        if (query.SourceEntityId is Guid sourceEntityId && sourceEntityId != Guid.Empty)
        {
            messages = messages.Where(x => x.SourceEntityId == sourceEntityId);
        }

        return await messages
            .OrderByDescending(x => x.SentUtc)
            .ThenByDescending(x => x.Id)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 1, 200))
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);
    }

    private async Task<ToolExecutionDecisionDto> EvaluatePolicyAsync(
        Guid companyId,
        GenerateProactiveMessageCommand command,
        ProactiveMessageChannel channel,
        ProactiveMessageSourceEntityType sourceEntityType,
        CancellationToken cancellationToken)
    {
        var runtimeProfile = await _agentRuntimeProfileResolver.GetCurrentProfileAsync(companyId, command.AgentId, cancellationToken);
        return _policyGuardrailEngine.Evaluate(new PolicyEvaluationRequest(
            companyId,
            command.AgentId,
            runtimeProfile.CompanyId,
            runtimeProfile.Status,
            runtimeProfile.AutonomyLevel,
            runtimeProfile.CanReceiveAssignments,
            CloneNodes(runtimeProfile.ToolPermissions),
            CloneNodes(runtimeProfile.DataScopes),
            CloneNodes(runtimeProfile.ApprovalThresholds),
            CloneNodes(runtimeProfile.EscalationRules),
            ToolName,
            ToolActionType.Execute,
            DeliveryScope,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["channel"] = JsonValue.Create(channel.ToStorageValue()),
                ["sourceEntityType"] = JsonValue.Create(sourceEntityType.ToStorageValue()),
                ["sourceEntityId"] = JsonValue.Create(command.SourceEntityId),
                ["recipientUserId"] = command.RecipientUserId.HasValue ? JsonValue.Create(command.RecipientUserId.Value) : null
            },
            null,
            null,
            null,
            SensitiveAction: false,
            Guid.NewGuid(),
            null));
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _membershipContextResolver.ResolveAsync(companyId, cancellationToken)
        ?? throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");

    private async Task<User> ResolveRecipientAsync(Guid companyId, Guid recipientUserId, CancellationToken cancellationToken)
    {
        var activeMembership = await _dbContext.CompanyMemberships
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.CompanyId == companyId &&
                     x.UserId == recipientUserId &&
                     x.Status == CompanyMembershipStatus.Active,
                cancellationToken);

        if (!activeMembership)
        {
            throw new KeyNotFoundException("Proactive message recipient is not an active company member.");
        }

        return await _dbContext.Users.SingleAsync(x => x.Id == recipientUserId, cancellationToken);
    }

    private async Task<ProactiveMessageSource> LoadSourceAsync(
        Guid companyId,
        ProactiveMessageSourceEntityType sourceEntityType,
        Guid sourceEntityId,
        CancellationToken cancellationToken)
    {
        if (sourceEntityId == Guid.Empty)
        {
            throw new ArgumentException("Source entity id is required.", nameof(sourceEntityId));
        }

        return sourceEntityType switch
        {
            ProactiveMessageSourceEntityType.ProactiveTask => await _dbContext.WorkTasks
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id == sourceEntityId)
                .Select(x => new ProactiveMessageSource(x.Id, x.Title, x.Description ?? x.CreationReason ?? "A proactive task needs attention."))
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Proactive task source entity was not found."),

            ProactiveMessageSourceEntityType.Alert => await _dbContext.Alerts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id == sourceEntityId)
                .Select(x => new ProactiveMessageSource(x.Id, x.Title, x.Summary))
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Alert source entity was not found."),

            ProactiveMessageSourceEntityType.Escalation => await _dbContext.Escalations
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id == sourceEntityId)
                .Select(x => new ProactiveMessageSource(x.Id, $"Escalation level {x.EscalationLevel}", x.Reason))
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Escalation source entity was not found."),

            _ => throw new ArgumentOutOfRangeException(nameof(sourceEntityType), sourceEntityType, "Unsupported proactive source entity type.")
        };
    }

    private static ProactiveGeneratedMessage GenerateMessage(ProactiveMessageSource source)
    {
        var subject = $"Proactive update: {source.Title}";
        var body = $"A proactive update is ready for review.\n\n{source.Summary}";
        return new ProactiveGeneratedMessage(subject, body);
    }

    private static ProactiveMessagePolicyBlockDto CreatePolicyBlock(ToolExecutionDecisionDto decision)
    {
        var primaryReasonCode = decision.ReasonCodes.FirstOrDefault(static code => !string.IsNullOrWhiteSpace(code)) ?? "policy_denied";
        var rationale = decision.Reasons?.FirstOrDefault(x => string.Equals(x.Code, primaryReasonCode, StringComparison.OrdinalIgnoreCase))?.Summary
            ?? "Proactive delivery was blocked before any message side effects.";

        return new ProactiveMessagePolicyBlockDto(
            "policy_denied",
            BuildUserFacingBlockMessage(decision),
            decision.ReasonCodes.ToArray(),
            rationale,
            decision);
    }

    private static string BuildUserFacingBlockMessage(ToolExecutionDecisionDto decision) =>
        string.Equals(decision.Outcome, PolicyDecisionOutcomeValues.RequireApproval, StringComparison.OrdinalIgnoreCase)
            ? "This proactive message requires approval and was not delivered."
            : "This proactive message was blocked by policy and was not delivered.";

    private static ProactiveMessagePolicyDecision CreatePolicyDecisionRecord(
        Guid companyId,
        Guid? proactiveMessageId,
        GenerateProactiveMessageCommand command,
        ProactiveMessageChannel channel,
        User recipient,
        ProactiveMessageSourceEntityType sourceEntityType,
        Guid sourceEntityId,
        ToolExecutionDecisionDto decision,
        ProactiveMessagePolicyDecisionOutcome outcome,
        DateTime createdUtc)
    {
        var primaryReasonCode = GetPrimaryReasonCode(decision);
        var reasonSummary = decision.Reasons?.FirstOrDefault(reason =>
            string.Equals(reason.Code, primaryReasonCode, StringComparison.OrdinalIgnoreCase))?.Summary
            ?? decision.Explanation;

        return new ProactiveMessagePolicyDecision(
            Guid.NewGuid(),
            companyId,
            proactiveMessageId,
            channel,
            recipient.Id,
            recipient.DisplayName,
            sourceEntityType,
            sourceEntityId,
            command.AgentId,
            outcome,
            primaryReasonCode,
            reasonSummary,
            decision.EvaluatedAutonomyLevel,
            ToolExecutionPolicyDecisionJsonSerializer.Serialize(decision),
            createdUtc);
    }

    private static string? GetPrimaryReasonCode(ToolExecutionDecisionDto decision) =>
        decision.ReasonCodes.FirstOrDefault(static code => !string.IsNullOrWhiteSpace(code));

    private static ProactiveMessageDto ToDto(ProactiveMessage message) =>
        new(
            message.Id,
            message.CompanyId,
            message.Channel.ToStorageValue(),
            message.RecipientUserId,
            message.Recipient,
            message.Subject,
            message.Body,
            message.SourceEntityType.ToStorageValue(),
            message.SourceEntityId,
            message.Status.ToStorageValue(),
            message.SentUtc,
            message.NotificationId);

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private sealed record ProactiveMessageSource(Guid SourceEntityId, string Title, string Summary);

    private sealed record ProactiveGeneratedMessage(string Subject, string Body);
}
