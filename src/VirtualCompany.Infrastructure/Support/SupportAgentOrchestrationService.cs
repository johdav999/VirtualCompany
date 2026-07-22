using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportAgentOrchestrationService : ISupportAgentOrchestrationService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ISupportContextResolutionService _context;
    private readonly ISupportTriageService _triage;
    private readonly ISupportReplyDraftService _drafts;
    private readonly ISupportReplySafetyPolicy _safety;
    private readonly IAuditEventWriter _audit;

    public SupportAgentOrchestrationService(
        VirtualCompanyDbContext dbContext,
        ISupportContextResolutionService context,
        ISupportTriageService triage,
        ISupportReplyDraftService drafts,
        ISupportReplySafetyPolicy safety,
        IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _context = context;
        _triage = triage;
        _drafts = drafts;
        _safety = safety;
        _audit = audit;
    }

    public async Task<SupportAgentExecutionDto?> RunAsync(Guid companyId, Guid userId, Guid supportCaseId, RunSupportAgentRequest request, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.SupportCases.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (!exists) return null;
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? $"support-agent:{supportCaseId:N}" : request.IdempotencyKey.Trim();
        var execution = await _dbContext.SupportAgentExecutions.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (execution is { Status: "completed" }) return Map(execution);

        var agentId = await _dbContext.Agents.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Department == "Support")
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (execution is null)
        {
            execution = new SupportAgentExecution(Guid.NewGuid(), companyId, supportCaseId, agentId, idempotencyKey);
            _dbContext.SupportAgentExecutions.Add(execution);
        }

        try
        {
            execution.MoveTo("context", "Resolved tenant-scoped customer and case context.");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _context.ResolveAsync(companyId, supportCaseId, cancellationToken);

            execution.MoveTo("triage", "Classified case priority, category, and risk.");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _triage.TriageAsync(companyId, userId, supportCaseId, cancellationToken);

            execution.MoveTo("draft", "Generated a grounded reply draft for human review.");
            await _dbContext.SaveChangesAsync(cancellationToken);
            var draft = await _drafts.GenerateDraftAsync(companyId, userId, supportCaseId, new GenerateSupportReplyDraftRequest("Helpful", request.ForceReview), cancellationToken);

            if (draft is not null)
            {
                execution.MoveTo("safety", "Evaluated draft safety policy.");
                await _dbContext.SaveChangesAsync(cancellationToken);
                await _safety.EvaluateAsync(companyId, supportCaseId, draft.DraftBody, draft.SourceReferencesJson, cancellationToken);
            }

            execution.Complete(draft?.Id, draft is null ? "Support agent completed without creating a draft." : "Support agent created a policy-evaluated reply draft.");
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Agent, agentId, "support.agent.run_completed", "support_case", supportCaseId.ToString("D"), AuditEventOutcomes.Succeeded, execution.Summary, ["support", "agent"], Metadata: new Dictionary<string, string?> { ["executionId"] = execution.Id.ToString("D") }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Map(execution);
        }
        catch (Exception ex)
        {
            execution.Fail(execution.CurrentStep, "Support agent run stopped before an unsafe follow-up could run.");
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Agent, agentId, "support.agent.run_failed", "support_case", supportCaseId.ToString("D"), AuditEventOutcomes.Failed, execution.FailureSummary, ["support", "agent"], Metadata: new Dictionary<string, string?> { ["executionId"] = execution.Id.ToString("D"), ["exceptionType"] = ex.GetType().Name }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static SupportAgentExecutionDto Map(SupportAgentExecution execution) =>
        new(execution.Id, execution.SupportCaseId, execution.AgentId, execution.Status, execution.CurrentStep, execution.CreatedDraftId, execution.Summary, execution.FailureSummary, execution.CreatedUtc, execution.UpdatedUtc, execution.CompletedUtc);
}

