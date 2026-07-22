using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Memory;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AgentHandoffService : IAgentHandoffService
{
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
    { AgentHandoffTypes.WonDealInvoiceReadiness, AgentHandoffTypes.CustomerPaymentRisk, AgentHandoffTypes.RefundCreditDispute,
      AgentHandoffTypes.ChurnRisk, AgentHandoffTypes.DocumentationGap, AgentHandoffTypes.InternalRequest };
    private readonly VirtualCompanyDbContext _db; private readonly ICompanyTaskService _tasks; private readonly IAgentCapabilityCatalog _catalog;
    private readonly IAuditEventWriter _audit;
    public AgentHandoffService(VirtualCompanyDbContext db, ICompanyTaskService tasks, IAgentCapabilityCatalog catalog, IAuditEventWriter audit)
    { _db = db; _tasks = tasks; _catalog = catalog; _audit = audit; }
    public async Task<AgentHandoffDto> CreateAsync(Guid companyId, Guid requestingAgentId, CreateAgentHandoffCommand command, CancellationToken ct)
    {
        if (!Types.Contains(command.Type)) throw new ArgumentException("A supported handoff type is required.");
        _ = await _db.Agents.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == requestingAgentId, ct) ?? throw new KeyNotFoundException("Requesting agent not found.");
        var receiving = await _catalog.GetEffectiveCatalogAsync(companyId, command.ReceivingAgentId, ct);
        if (!string.Equals(receiving.AgentStatus, "active", StringComparison.OrdinalIgnoreCase)) throw new AgentAiConflictException("The receiving agent cannot accept work.");
        var correlation = HandoffIdentity(companyId, requestingAgentId, command);
        var existing = await _db.AgentHandoffs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.CorrelationId == correlation, ct);
        if (existing is not null) return Map(existing);
        var task = await _tasks.CreateTaskAsync(companyId, new CreateTaskCommand("agent_handoff", command.Objective, command.RequestedOutcome,
            "high", command.DueUtc, command.ReceivingAgentId,
            new Dictionary<string, JsonNode?> { ["handoffType"] = command.Type, ["requestingAgentId"] = requestingAgentId },
            RationaleSummary: "Typed cross-agent handoff awaiting acceptance.", CorrelationId: correlation), ct);
        var handoff = new AgentHandoff(companyId, command.Type, requestingAgentId, command.ReceivingAgentId, command.Objective,
            command.RequestedOutcome, command.DueUtc, JsonSerializer.Serialize((command.SourceIds ?? []).Distinct()), correlation, task.Id);
        _db.AgentHandoffs.Add(handoff); await _db.SaveChangesAsync(ct);
        await AuditAsync(handoff, "agent_handoff_created", "created", "A typed handoff was created and assigned for review.", ct);
        return Map(handoff);
    }
    public async Task<AgentHandoffDto> TransitionAsync(Guid companyId, Guid handoffId, TransitionAgentHandoffCommand command, CancellationToken ct)
    {
        var handoff = await _db.AgentHandoffs.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == handoffId, ct) ?? throw new KeyNotFoundException("Handoff not found.");
        try { handoff.Transition(command.Status.Trim().ToLowerInvariant(), command.Summary, command.Confidence); }
        catch (InvalidOperationException ex) { throw new AgentAiConflictException(ex.Message); }
        await _db.SaveChangesAsync(ct);
        if (handoff.Status == "completed") _db.AgentAiQualityEvents.Add(new AgentAiQualityEvent(companyId, handoff.ReceivingAgentId,
            AgentCapabilityIds.CrossAgentHandoff, null, AgentAiQualityEventTypes.HandoffCompleted, $"handoff:{handoff.Id:N}:completed", null, null, handoff.Confidence, handoff.CorrelationId));
        await _db.SaveChangesAsync(ct);
        await AuditAsync(handoff, "agent_handoff_transitioned", handoff.Status, command.Summary, ct);
        return Map(handoff);
    }
    public async Task<IReadOnlyList<AgentHandoffDto>> ListAsync(Guid companyId, Guid? agentId, CancellationToken ct) =>
        (await _db.AgentHandoffs.AsNoTracking().Where(x => x.CompanyId == companyId && (!agentId.HasValue || x.RequestingAgentId == agentId || x.ReceivingAgentId == agentId))
            .OrderByDescending(x => x.UpdatedUtc).Take(200).ToListAsync(ct)).Select(Map).ToArray();
    private static AgentHandoffDto Map(AgentHandoff x) => new(x.Id, x.Type, x.RequestingAgentId, x.ReceivingAgentId, x.Objective, x.RequestedOutcome, x.Status, x.DueUtc, x.RelatedTaskId, x.CompletionSummary, x.UpdatedUtc);
    private static string HandoffIdentity(Guid companyId, Guid requestingAgentId, CreateAgentHandoffCommand command)
    {
        var sources = string.Join(",", (command.SourceIds ?? []).OrderBy(x => x, StringComparer.Ordinal));
        var value = $"{companyId:N}|{command.Type.Trim().ToLowerInvariant()}|{requestingAgentId:N}|{command.ReceivingAgentId:N}|{command.Objective.Trim().ToLowerInvariant()}|{command.RequestedOutcome.Trim().ToLowerInvariant()}|{sources}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
    private Task AuditAsync(AgentHandoff handoff, string action, string outcome, string? rationale, CancellationToken ct) =>
        _audit.WriteAsync(new AuditEventWriteRequest(handoff.CompanyId, "agent", handoff.RequestingAgentId, action,
            "agent_handoff", handoff.Id.ToString("N"), outcome, rationale,
            Metadata: new Dictionary<string, string?> { ["type"] = handoff.Type, ["receivingAgentId"] = handoff.ReceivingAgentId.ToString("N") },
            CorrelationId: handoff.CorrelationId), ct);
}

public sealed class AgentMemoryCandidateService : IAgentMemoryCandidateService
{
    private static readonly HashSet<string> AllowedSensitivities = new(["public", "internal", "confidential"], StringComparer.OrdinalIgnoreCase);
    private readonly VirtualCompanyDbContext _db; private readonly ICompanyMemoryService _memory; private readonly ICurrentUserAccessor _user;
    private readonly IAuditEventWriter _audit;
    public AgentMemoryCandidateService(VirtualCompanyDbContext db, ICompanyMemoryService memory, ICurrentUserAccessor user, IAuditEventWriter audit)
    { _db = db; _memory = memory; _user = user; _audit = audit; }
    public async Task<AgentMemoryCandidateDto> ProposeAsync(Guid companyId, Guid agentId, ProposeAgentMemoryCommand command, CancellationToken ct)
    {
        if (!await _db.Agents.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == agentId, ct)) throw new KeyNotFoundException("Agent not found.");
        if (!MemoryTypeValues.TryParse(command.MemoryType, out _)) throw new ArgumentException(MemoryTypeValues.BuildValidationMessage(command.MemoryType));
        if (command.Scope is not (MemoryScopeValues.AgentSpecific or MemoryScopeValues.CompanyWide)) throw new ArgumentException("Memory scope must be agent_specific or company_wide.");
        if (!AllowedSensitivities.Contains(command.Sensitivity) || command.Sensitivity.Equals("confidential", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Confidential or unsupported content cannot be proposed as shared memory.");
        if (command.SourceIds.Count == 0) throw new ArgumentException("At least one evidence source is required.");
        if (ContainsSecret(command.Content)) throw new ArgumentException("The candidate appears to contain a secret or credential and cannot be stored.");
        var retention = Math.Clamp(command.RetentionDays, 1, 365); var fingerprint = Fingerprint(command.MemoryType, command.Scope, command.Content);
        var existing = await _db.AgentMemoryCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Fingerprint == fingerprint, ct);
        if (existing is not null) return Map(existing);
        var candidate = new AgentMemoryCandidate(companyId, agentId, command.MemoryType, command.Scope, command.Content,
            JsonSerializer.Serialize(command.SourceIds.Distinct()), command.Confidence, command.Sensitivity, DateTime.UtcNow.AddDays(retention), fingerprint, command.OrchestrationRunId);
        _db.AgentMemoryCandidates.Add(candidate); await _db.SaveChangesAsync(ct);
        await AuditAsync(candidate, "agent_memory_candidate_proposed", "needs_review", "An evidence-backed memory candidate was proposed for human review.", ct);
        return Map(candidate);
    }
    public async Task<AgentMemoryCandidateDto> ReviewAsync(Guid companyId, Guid candidateId, ReviewAgentMemoryCommand command, CancellationToken ct)
    {
        var reviewer = _user.UserId ?? throw new UnauthorizedAccessException("An authenticated reviewer is required.");
        var candidate = await _db.AgentMemoryCandidates.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == candidateId, ct) ?? throw new KeyNotFoundException("Memory candidate not found.");
        if (!command.Approve)
        {
            try { candidate.Reject(reviewer, command.Reason ?? "Rejected by reviewer."); } catch (InvalidOperationException ex) { throw new AgentAiConflictException(ex.Message); }
            _db.AgentAiQualityEvents.Add(new AgentAiQualityEvent(companyId, candidate.ProposingAgentId, AgentCapabilityIds.MemoryProposal,
                candidate.OrchestrationRunId, AgentAiQualityEventTypes.MemoryRejected, $"memory:{candidate.Id:N}:rejected", "reviewer_rejected", command.Reason, candidate.Confidence, candidate.Id.ToString("N")));
            await _db.SaveChangesAsync(ct);
            await AuditAsync(candidate, "agent_memory_candidate_reviewed", "rejected", command.Reason, ct);
            return Map(candidate);
        }
        try { candidate.Approve(reviewer); } catch (InvalidOperationException ex) { throw new AgentAiConflictException(ex.Message); }
        await _db.SaveChangesAsync(ct);
        var memory = await _memory.CreateAsync(companyId, new CreateMemoryItemCommand
        {
            AgentId = candidate.Scope == MemoryScopeValues.AgentSpecific ? candidate.ProposingAgentId : null,
            MemoryType = candidate.MemoryType, Summary = candidate.Content, SourceEntityType = "agent_memory_candidate", SourceEntityId = candidate.Id,
            Salience = candidate.Confidence, ValidFromUtc = DateTime.UtcNow, ValidToUtc = candidate.ExpiresUtc,
            Metadata = new Dictionary<string, JsonNode?> { ["reviewed"] = true, ["sensitivity"] = candidate.Sensitivity }
        }, ct);
        candidate.Activate(memory.Id);
        _db.AgentAiQualityEvents.Add(new AgentAiQualityEvent(companyId, candidate.ProposingAgentId, AgentCapabilityIds.MemoryProposal,
            candidate.OrchestrationRunId, AgentAiQualityEventTypes.MemoryApproved, $"memory:{candidate.Id:N}:approved", null, null, candidate.Confidence, candidate.Id.ToString("N")));
        await _db.SaveChangesAsync(ct);
        await AuditAsync(candidate, "agent_memory_candidate_activated", "activated", "The approved candidate was activated through the company memory service.", ct);
        return Map(candidate);
    }
    public async Task<IReadOnlyList<AgentMemoryCandidateDto>> ListAsync(Guid companyId, string? status, CancellationToken ct) =>
        (await _db.AgentMemoryCandidates.AsNoTracking().Where(x => x.CompanyId == companyId && (status == null || x.Status == status)).OrderByDescending(x => x.UpdatedUtc).Take(200).ToListAsync(ct)).Select(Map).ToArray();
    public async Task<int> ExpireAsync(Guid companyId, CancellationToken ct)
    {
        var candidates = await _db.AgentMemoryCandidates.Where(x => x.CompanyId == companyId && x.ExpiresUtc <= DateTime.UtcNow && x.Status != "expired" && x.Status != "activated" && x.Status != "rejected").ToListAsync(ct);
        foreach (var item in candidates) item.Expire();
        await _db.SaveChangesAsync(ct);
        foreach (var item in candidates)
            await AuditAsync(item, "agent_memory_candidate_expired", "expired", "The review window or configured retention period elapsed.", ct);
        return candidates.Count;
    }
    private static AgentMemoryCandidateDto Map(AgentMemoryCandidate x) => new(x.Id, x.ProposingAgentId, x.MemoryType, x.Scope, x.Content, x.Confidence, x.Sensitivity, x.Status, x.ExpiresUtc, x.ActivatedMemoryItemId, x.UpdatedUtc);
    private static string Fingerprint(string type, string scope, string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{type.Trim().ToLowerInvariant()}|{scope.Trim().ToLowerInvariant()}|{content.Trim().ToLowerInvariant()}"))).ToLowerInvariant();
    private static bool ContainsSecret(string value) { var v = value.ToLowerInvariant(); return v.Contains("api_key") || v.Contains("apikey") || v.Contains("password=") || v.Contains("bearer ") || v.Contains("private key"); }
    private Task AuditAsync(AgentMemoryCandidate candidate, string action, string outcome, string? rationale, CancellationToken ct) =>
        _audit.WriteAsync(new AuditEventWriteRequest(candidate.CompanyId, _user.UserId.HasValue ? "user" : "system", _user.UserId,
            action, "agent_memory_candidate", candidate.Id.ToString("N"), outcome, rationale,
            Metadata: new Dictionary<string, string?> { ["proposingAgentId"] = candidate.ProposingAgentId.ToString("N"), ["scope"] = candidate.Scope },
            CorrelationId: candidate.OrchestrationRunId?.ToString("N") ?? candidate.Id.ToString("N")), ct);
}

public sealed class AgentAiQualityService : IAgentAiQualityService
{
    private readonly VirtualCompanyDbContext _db;
    public AgentAiQualityService(VirtualCompanyDbContext db) { _db = db; }
    public async Task RecordAsync(Guid companyId, RecordAgentAiFeedbackCommand command, CancellationToken ct)
    {
        if (!await _db.Agents.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == command.AgentId, ct)) throw new KeyNotFoundException("Agent not found.");
        if (!AgentAiQualityEventTypes.All.Contains(command.EventType)) throw new ArgumentException("A supported feedback event type is required.");
        if (command.EventType == AgentAiQualityEventTypes.RecommendationProduced) throw new ArgumentException("Produced events are system-owned.");
        if (command.EventType is AgentAiQualityEventTypes.Rejected or AgentAiQualityEventTypes.Corrected && string.IsNullOrWhiteSpace(command.ReasonCode)) throw new ArgumentException("A reason code is required.");
        if (await _db.AgentAiQualityEvents.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.EventIdentity == command.EventIdentity, ct)) return;
        _db.AgentAiQualityEvents.Add(new AgentAiQualityEvent(companyId, command.AgentId, command.CapabilityId, command.RunId,
            command.EventType, command.EventIdentity, command.ReasonCode, command.Comment, command.Confidence, command.RunId?.ToString("N") ?? Guid.NewGuid().ToString("N")));
        try { await _db.SaveChangesAsync(ct); } catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
    }
    public async Task<AgentAiQualityMetricsDto> GetMetricsAsync(Guid companyId, DateTime fromUtc, DateTime toUtc, Guid? agentId, string? capabilityId, CancellationToken ct)
    {
        if (fromUtc >= toUtc || toUtc - fromUtc > TimeSpan.FromDays(366)) throw new ArgumentException("Select a valid range of at most 366 days.");
        var events = await _db.AgentAiQualityEvents.AsNoTracking().Where(x => x.CompanyId == companyId && x.OccurredUtc >= fromUtc && x.OccurredUtc < toUtc &&
            (!agentId.HasValue || x.AgentId == agentId) && (capabilityId == null || x.CapabilityId == capabilityId)).Select(x => x.EventType).ToListAsync(ct);
        int Count(string type) => events.Count(x => x == type); var produced = Count(AgentAiQualityEventTypes.RecommendationProduced); var accepted = Count(AgentAiQualityEventTypes.Accepted);
        var rejected = Count(AgentAiQualityEventTypes.Rejected); var corrected = Count(AgentAiQualityEventTypes.Corrected); var sample = accepted + rejected + corrected;
        var enough = sample >= 20; decimal? rate = sample == 0 ? null : Math.Round((decimal)accepted / sample, 4);
        return new AgentAiQualityMetricsDto(fromUtc, toUtc, sample, produced, accepted, rejected, corrected,
            Count(AgentAiQualityEventTypes.ValidationFailed), Count(AgentAiQualityEventTypes.PolicyBlocked), rate, enough, enough && rate >= .85m);
    }
}
