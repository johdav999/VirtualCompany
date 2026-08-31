using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AgentRoleBriefingService : IAgentRoleBriefingService
{
    private readonly ICompanyBriefingService _briefings; private readonly IAgentReasoningGateway _reasoning; private readonly ICurrentUserAccessor _user;
    public AgentRoleBriefingService(ICompanyBriefingService briefings, IAgentReasoningGateway reasoning, ICurrentUserAccessor user)
    { _briefings = briefings; _reasoning = reasoning; _user = user; }
    public async Task<AgentRoleBriefingDto> GenerateAsync(Guid companyId, Guid agentId, string cadence, CancellationToken ct)
    {
        cadence = cadence.Trim().ToLowerInvariant(); if (cadence is not ("daily" or "weekly" or "event_driven")) throw new ArgumentException("Cadence must be daily, weekly, or event_driven.");
        var aggregate = await _briefings.AggregateAsync(companyId, new GenerateCompanyBriefingCommand(cadence == "weekly" ? "weekly" : "daily"), ct);
        var items = aggregate.Alerts.Concat(aggregate.PendingApprovals).Concat(aggregate.KpiHighlights).Concat(aggregate.Anomalies).Concat(aggregate.NotableAgentUpdates)
            .Where(x => !x.AgentId.HasValue || x.AgentId == agentId).Take(40).ToArray();
        var sources = items.Select((x, i) => new AgentAiSource(x.SourceEntityId?.ToString("N") ?? $"briefing-{i}", x.SourceEntityType ?? "briefing_fact", x.Title, x.Summary, x.OccurredUtc)).ToArray();
        if (sources.Length == 0) return new AgentRoleBriefingDto(Guid.Empty, cadence, "No material updates are available for this briefing period.", [], [], false, DateTime.UtcNow);
        var result = await _reasoning.ReasonAsync(new AgentReasoningRequest(companyId, agentId, AgentCapabilityIds.RoleBriefing, "1.0.0",
            "role-briefing-v1", "1.0", "Summarize the deterministic briefing facts for this agent. Preserve conflicts, confidence, open decisions, and source references. Do not change deadlines or policy outcomes.",
            sources, ["read", "recommend"], [], _user.UserId), ct);
        return new AgentRoleBriefingDto(result.RunId, cadence, result.Summary, result.Claims, sources.Where(x => result.SourceIds.Contains(x.Id)).ToArray(), result.Status != "completed", DateTime.UtcNow);
    }
}

public sealed class AgentQuestionAnsweringService : IAgentQuestionAnsweringService
{
    private readonly IGroundedPromptContextService _context; private readonly IAgentReasoningGateway _reasoning;
    private readonly ICurrentUserAccessor _user;
    public AgentQuestionAnsweringService(IGroundedPromptContextService context, IAgentReasoningGateway reasoning, ICurrentUserAccessor user)
    { _context = context; _reasoning = reasoning; _user = user; }
    public async Task<AgentQuestionAnswerDto> AskAsync(Guid companyId, Guid agentId, AskAgentQuestionCommand command, CancellationToken ct)
    {
        var question = command.Question?.Trim(); if (string.IsNullOrWhiteSpace(question) || question.Length > 4000) throw new ArgumentException("A question of at most 4,000 characters is required.");
        var grounded = await _context.PrepareAsync(new GroundedPromptContextRequest(companyId, agentId, question, _user.UserId,
            command.TaskId, RetrievalPurpose: AgentCapabilityIds.GroundedQuestionAnswering), ct);
        var sources = grounded.Context.SourceReferences.Select(x => new AgentAiSource(x.SourceId, x.SourceType, x.Title, x.Snippet, x.TimestampUtc)).ToArray();
        if (sources.Length == 0)
            return new AgentQuestionAnswerDto(Guid.Empty, "insufficient_grounding", "There is not enough accessible company information to answer this question.", [], 0, [], ["Add or index an authoritative source for this question."], true, []);
        var result = await _reasoning.ReasonAsync(new AgentReasoningRequest(companyId, agentId,
            AgentCapabilityIds.GroundedQuestionAnswering, "1.0.0", "grounded-qa-v1", "1.0",
            $"Answer this company question concisely: {question}. Requested domain: {command.RequestedDomain ?? "general"}. Label facts, inferences, and unknowns.",
            sources, ["read", "recommend"], [], _user.UserId, command.TaskId, command.ConversationId), ct);
        var visibleSources = sources.Where(x => result.SourceIds.Contains(x.Id)).ToArray();
        var state = result.Status == "completed" ? "answered" : result.FailureCode is null ? "needs_review" : "failed";
        return new AgentQuestionAnswerDto(result.RunId, state, result.Summary, result.Claims, result.Confidence,
            visibleSources, result.MissingEvidence, state != "answered", result.NextActions);
    }
}

public sealed class AgentWorkPrioritizationService : IAgentWorkPrioritizationService
{
    private readonly VirtualCompanyDbContext _db; private readonly IAgentReasoningGateway _reasoning; private readonly ICurrentUserAccessor _user;
    public AgentWorkPrioritizationService(VirtualCompanyDbContext db, IAgentReasoningGateway reasoning, ICurrentUserAccessor user)
    { _db = db; _reasoning = reasoning; _user = user; }
    public async Task<IReadOnlyList<AgentWorkPriorityItem>> PrioritizeAsync(Guid companyId, Guid agentId, int take, CancellationToken ct)
    {
        var now = DateTime.UtcNow; take = Math.Clamp(take, 1, 50);
        if (!await _db.Agents.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == agentId, ct)) throw new KeyNotFoundException("Agent not found.");
        var tasks = await _db.WorkTasks.AsNoTracking().Where(x => x.CompanyId == companyId && x.AssignedAgentId == agentId &&
            x.Status != WorkTaskStatus.Completed && x.Status != WorkTaskStatus.Failed).OrderBy(x => x.DueUtc).ThenByDescending(x => x.Priority).Take(take).ToListAsync(ct);
        var deterministic = tasks.Select(x => new
        {
            Task = x,
            Score = (x.Priority switch { WorkTaskPriority.Critical => 50, WorkTaskPriority.High => 35, WorkTaskPriority.Normal => 20, _ => 10 }) +
                    (x.DueUtc is null ? 0 : x.DueUtc < now ? 40 : x.DueUtc < now.AddDays(1) ? 30 : x.DueUtc < now.AddDays(7) ? 15 : 0),
            Reasons = new List<string> { $"priority:{x.Priority.ToStorageValue()}" }.Concat(x.DueUtc is null ? [] : x.DueUtc < now ? ["overdue"] : x.DueUtc < now.AddDays(1) ? ["due_within_24_hours"] : ["scheduled_due_date"]).ToArray()
        }).OrderByDescending(x => x.Score).ThenBy(x => x.Task.DueUtc).ToArray();
        if (deterministic.Length == 0) return [];
        var sources = deterministic.Select(x => new AgentAiSource(x.Task.Id.ToString("N"), "work_task", x.Task.Title,
            $"Status {x.Task.Status.ToStorageValue()}; deterministic score {x.Score}; reasons {string.Join(", ", x.Reasons)}; due {x.Task.DueUtc:O}", x.Task.UpdatedUtc)).ToArray();
        var ai = await _reasoning.ReasonAsync(new AgentReasoningRequest(companyId, agentId, AgentCapabilityIds.WorkPrioritization,
            "1.0.0", "work-priority-v1", "1.0", "Explain the deterministic ranking. Do not change scores, statuses, deadlines, or policy outcomes.",
            sources, ["read", "recommend"], [], _user.UserId), ct);
        return deterministic.Select(x => new AgentWorkPriorityItem("work_task", x.Task.Id.ToString("N"), x.Task.Title,
            x.Task.Status.ToStorageValue(), x.Task.DueUtc, x.Score, x.Reasons,
            ai.Claims.FirstOrDefault(c => c.SourceIds.Contains(x.Task.Id.ToString("N")))?.Text ?? "Ranked from authoritative priority and due-date rules.",
            ai.FailureCode is null ? ai.Confidence : 1m, now, x.Task.UpdatedUtc)).ToArray();
    }
}

public sealed class AgentPlanningService : IAgentPlanningService
{
    private readonly VirtualCompanyDbContext _db; private readonly IAgentReasoningGateway _reasoning;
    private readonly ICompanyTaskService _tasks; private readonly ICurrentUserAccessor _user;
    private readonly IAgentEffectiveAuthorityResolver _authorityResolver;
    public AgentPlanningService(VirtualCompanyDbContext db, IAgentReasoningGateway reasoning, ICompanyTaskService tasks,
        ICurrentUserAccessor user, IAgentEffectiveAuthorityResolver authorityResolver)
    { _db = db; _reasoning = reasoning; _tasks = tasks; _user = user; _authorityResolver = authorityResolver; }
    public async Task<AgentPlanDto> GenerateAsync(Guid companyId, Guid agentId, GenerateAgentPlanCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Objective) || command.Objective.Length > 2000) throw new ArgumentException("A bounded objective is required.");
        var maximum = Math.Clamp(command.MaximumSteps, 1, 12);
        var authority = await _authorityResolver.ResolveAsync(companyId, agentId, ct);
        var usable = authority.Tools.Where(item => item.IsUsable).ToArray();
        var current = await _db.WorkTasks.AsNoTracking().Where(x => x.CompanyId == companyId && x.AssignedAgentId == agentId && x.Status != WorkTaskStatus.Completed)
            .OrderByDescending(x => x.UpdatedUtc).Take(10).Select(x => new AgentAiSource(x.Id.ToString("N"), "work_task", x.Title, x.Description ?? x.Title, x.UpdatedUtc)).ToListAsync(ct);
        var result = await _reasoning.ReasonAsync(new AgentReasoningRequest(companyId, agentId, AgentCapabilityIds.Planning,
            "1.0.0", "bounded-plan-v1", "1.0", $"Create at most {maximum} ordered, independently completable plan steps for this objective: {command.Objective}. State assumptions and completion evidence. Planning must not execute an external action.",
            current,
            usable.Select(item => item.ActionType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            usable.Select(item => item.ToolName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            _user.UserId, EffectiveAuthorityVersion: authority.AuthorityVersion,
            EffectiveAuthorityHash: authority.AuthorityHash), ct);
        var steps = result.Claims.Take(maximum).Select((claim, i) => new AgentPlanStepDto(i + 1,
            claim.Text.Length > 180 ? claim.Text[..180] : claim.Text, claim.Text, agentId,
            command.TargetUtc, i == 0 ? [] : [i], false, "A user verifies the task result.")).ToArray();
        var errors = new List<string>(); if (steps.Length == 0) errors.Add("The model did not produce any valid plan steps.");
        return new AgentPlanDto(result.RunId, errors.Count == 0 ? "draft" : "invalid", command.Objective,
            result.Uncertainty, steps, errors, true, authority.AuthorityVersion, authority.AuthorityHash);
    }
    public async Task<AgentPlanDto> CommitAsync(Guid companyId, Guid agentId, Guid runId, CancellationToken ct)
    {
        var result = await _reasoning.GetRunAsync(companyId, agentId, runId, ct) ?? throw new KeyNotFoundException("Plan run not found.");
        var run = await _db.AgentOrchestrationRuns.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == runId && x.AgentId == agentId && x.CapabilityId == AgentCapabilityIds.Planning, ct)
            ?? throw new KeyNotFoundException("Plan run not found.");
        var authority = await _authorityResolver.ResolveAsync(companyId, agentId, ct);
        if (string.IsNullOrWhiteSpace(run.EffectiveAuthorityHash) ||
            !string.Equals(run.EffectiveAuthorityVersion, authority.AuthorityVersion, StringComparison.Ordinal) ||
            !string.Equals(run.EffectiveAuthorityHash, authority.AuthorityHash, StringComparison.Ordinal))
            throw new AgentAiConflictException("Agent permissions changed after this plan was created. Refresh and review a new plan.");
        if (result.Status is not ("completed" or "needs_review") || result.Claims.Count == 0) throw new AgentAiConflictException("Only a valid reviewed plan can be committed.");
        var committed = new List<AgentPlanStepDto>(); Guid? parentId = null;
        for (var i = 0; i < result.Claims.Count; i++)
        {
            var claim = result.Claims[i]; var title = claim.Text.Length > 180 ? claim.Text[..180] : claim.Text;
            var task = await _tasks.CreateTaskAsync(companyId, new CreateTaskCommand("agent_plan_step", title, claim.Text, "normal", null,
                agentId, null, parentId, RationaleSummary: $"Committed from AI plan {runId:N} after explicit review.", ConfidenceScore: claim.Confidence, CorrelationId: runId.ToString("N")), ct);
            parentId ??= task.Id;
            committed.Add(new AgentPlanStepDto(i + 1, title, claim.Text, agentId, null, i == 0 ? [] : [i], false, "Task is completed with recorded output.", task.Id));
        }
        return new AgentPlanDto(runId, "committed", result.Summary, result.Uncertainty, committed, [], false,
            authority.AuthorityVersion, authority.AuthorityHash);
    }
}

public sealed class AgentExceptionInterpretationService : IAgentExceptionInterpretationService
{
    private readonly VirtualCompanyDbContext _db; private readonly IAgentReasoningGateway _reasoning; private readonly ICurrentUserAccessor _user;
    public AgentExceptionInterpretationService(VirtualCompanyDbContext db, IAgentReasoningGateway reasoning, ICurrentUserAccessor user)
    { _db = db; _reasoning = reasoning; _user = user; }
    public async Task<AgentExceptionInterpretationDto> InterpretAsync(Guid companyId, Guid agentId, Guid exceptionId, CancellationToken ct)
    {
        if (!await _db.Agents.AsNoTracking().AnyAsync(v => v.CompanyId == companyId && v.Id == agentId, ct)) throw new KeyNotFoundException("Agent not found.");
        var x = await _db.ExecutionExceptionRecords.AsNoTracking().SingleOrDefaultAsync(v => v.CompanyId == companyId && v.Id == exceptionId, ct) ?? throw new KeyNotFoundException("Exception not found.");
        var classification = Classify(x.FailureCode, x.Summary);
        var facts = new[] { x.Title, x.Summary, $"Status: {x.Status}", $"Failure classification: {classification}" };
        var sources = new[] { new AgentAiSource(x.Id.ToString("N"), "execution_exception", x.Title, string.Join("\n", facts), x.UpdatedUtc) };
        var result = await _reasoning.ReasonAsync(new AgentReasoningRequest(companyId, agentId, AgentCapabilityIds.ExceptionInterpretation,
            "1.0.0", "exception-interpretation-v1", "1.0", "Explain likely causes as hypotheses. Keep the deterministic classification authoritative. Do not retry or mutate the source.",
            sources, ["read", "recommend"], [], _user.UserId), ct);
        var allowed = classification switch { "retryable" => new[] { "Review retry history", "Retry through the owning workflow when policy permits" },
            "approval_blocked" => new[] { "Open the related approval" }, "configuration_missing" => new[] { "Open integration settings" },
            _ => new[] { "Review evidence", "Escalate to the record owner" } };
        return new AgentExceptionInterpretationDto(result.RunId, x.Id, classification, facts, result.Claims.Where(c => c.Type == "inference").ToArray(), allowed, result.Confidence, result.Status != "completed");
    }
    private static string Classify(string? code, string summary)
    {
        var value = $"{code} {summary}".ToLowerInvariant();
        if (value.Contains("approval")) return "approval_blocked"; if (value.Contains("config") || value.Contains("credential")) return "configuration_missing";
        if (value.Contains("timeout") || value.Contains("429") || value.Contains("temporar")) return "retryable";
        if (value.Contains("ambiguous") || value.Contains("unknown outcome")) return "ambiguous_provider_outcome";
        if (value.Contains("contradict")) return "data_contradiction"; return "human_decision_required";
    }
}
