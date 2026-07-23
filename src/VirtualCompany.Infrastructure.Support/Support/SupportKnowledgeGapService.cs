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
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportKnowledgeGapService : ISupportKnowledgeGapService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportKnowledgeGapService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<SupportKnowledgeGapDto> CreateOrIncrementAsync(Guid companyId, CreateSupportKnowledgeGapRequest request, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SupportKnowledgeGaps.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Status == SupportKnowledgeGapStatuses.Open && x.Category == request.Category && x.QuestionSummary == request.QuestionSummary, cancellationToken);
        if (existing is not null)
        {
            existing.Increment();
            await EnsureDocumentationTaskAsync(companyId, Guid.Empty, existing, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return SupportCaseService.MapGap(existing);
        }

        var gap = new SupportKnowledgeGap(Guid.NewGuid(), companyId, request.SupportCaseId, request.SupportReplyDraftId, request.Category, request.QuestionSummary, request.MissingInformationSummary, request.RetrievalSourceSummary);
        _dbContext.SupportKnowledgeGaps.Add(gap);
        await EnsureDocumentationTaskAsync(companyId, Guid.Empty, gap, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapGap(gap);
    }

    public async Task<IReadOnlyList<SupportKnowledgeGapDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken) =>
        await _dbContext.SupportKnowledgeGaps.AsNoTracking()
            .Where(x => x.CompanyId == companyId && (string.IsNullOrWhiteSpace(status) || x.Status == status))
            .OrderByDescending(x => x.FrequencyCount)
            .ThenByDescending(x => x.UpdatedUtc)
            .Select(x => SupportCaseService.MapGap(x))
            .ToListAsync(cancellationToken);

    public async Task<SupportKnowledgeGapDto?> CreateDocumentationTaskAsync(Guid companyId, Guid userId, Guid knowledgeGapId, CancellationToken cancellationToken)
    {
        var gap = await _dbContext.SupportKnowledgeGaps.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == knowledgeGapId, cancellationToken);
        if (gap is null) return null;
        await EnsureDocumentationTaskAsync(companyId, userId, gap, cancellationToken, force: true);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapGap(gap);
    }

    public async Task<SupportKnowledgeGapDto?> ResolveAsync(Guid companyId, Guid userId, Guid knowledgeGapId, ResolveSupportKnowledgeGapRequest request, CancellationToken cancellationToken)
    {
        var gap = await _dbContext.SupportKnowledgeGaps.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == knowledgeGapId, cancellationToken);
        if (gap is null) return null;
        var approvedKnowledge = await _dbContext.CompanyKnowledgeDocuments.AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.Id == request.KnowledgeDocumentId &&
            x.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed &&
            x.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexed, cancellationToken);
        if (!approvedKnowledge) throw new InvalidOperationException("Select a processed and indexed knowledge document from this company before resolving the gap.");
        gap.Resolve(request.KnowledgeDocumentId);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.knowledge_gap.resolved", "support_knowledge_gap", gap.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Knowledge gap resolved with approved knowledge.", ["support", "knowledge"], Metadata: new Dictionary<string, string?> { ["knowledgeDocumentId"] = request.KnowledgeDocumentId.ToString("D") }), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapGap(gap);
    }

    public async Task<SupportKnowledgeGapDto?> ReopenAsync(Guid companyId, Guid userId, Guid knowledgeGapId, CancellationToken cancellationToken)
    {
        var gap = await _dbContext.SupportKnowledgeGaps.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == knowledgeGapId, cancellationToken);
        if (gap is null) return null;
        gap.Reopen();
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.knowledge_gap.reopened", "support_knowledge_gap", gap.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Knowledge gap reopened for further documentation.", ["support", "knowledge"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapGap(gap);
    }

    private async Task EnsureDocumentationTaskAsync(Guid companyId, Guid userId, SupportKnowledgeGap gap, CancellationToken cancellationToken, bool force = false)
    {
        if (gap.LinkedTaskId is not null || (!force && gap.FrequencyCount < 3))
        {
            return;
        }

        var actorType = userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human;
        Guid? actorId = userId == Guid.Empty ? null : userId;
        var task = new WorkTask(
            Guid.NewGuid(),
            companyId,
            "support_knowledge_gap",
            $"Document support answer: {gap.QuestionSummary}",
            gap.MissingInformationSummary,
            gap.FrequencyCount >= 5 ? WorkTaskPriority.High : WorkTaskPriority.Normal,
            null,
            null,
            actorType,
            actorId,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["knowledgeGapId"] = gap.Id.ToString("D"),
                ["supportCaseId"] = gap.SupportCaseId?.ToString("D"),
                ["supportReplyDraftId"] = gap.SupportReplyDraftId?.ToString("D"),
                ["category"] = gap.Category,
                ["frequencyCount"] = gap.FrequencyCount
            },
            sourceType: WorkTaskSourceTypes.Agent,
            triggerSource: "support_knowledge_gap",
            creationReason: "Repeated support outcomes exposed missing answer knowledge.");
        _dbContext.WorkTasks.Add(task);
        gap.LinkTask(task.Id);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, actorType, actorId, "support.knowledge_gap.task_created", "support_knowledge_gap", gap.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Documentation task created from support knowledge gap.", ["support", "knowledge"], Metadata: new Dictionary<string, string?> { ["taskId"] = task.Id.ToString("D"), ["frequencyCount"] = gap.FrequencyCount.ToString(System.Globalization.CultureInfo.InvariantCulture) }), cancellationToken);
    }
}
