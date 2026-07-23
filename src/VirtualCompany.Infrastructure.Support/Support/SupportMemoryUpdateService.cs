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

public sealed class SupportMemoryUpdateService : ISupportMemoryUpdateService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportMemoryUpdateService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit) { _dbContext = dbContext; _audit = audit; }

    public async Task UpdateFromResolvedCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var job = await _dbContext.SupportMemoryUpdateJobs.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.SupportCaseId == supportCaseId, cancellationToken);
        if (job is not null) await ProcessJobAsync(companyId, job.Id, cancellationToken);
    }

    public async Task ProcessJobAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _dbContext.SupportMemoryUpdateJobs.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException("Support memory update job was not found.");
        if (job.Status is "completed" or "skipped") return;
        job.Start();
        try
        {
            var supportCase = await _dbContext.SupportCases.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == job.SupportCaseId && x.Status == SupportCaseStatuses.Resolved, cancellationToken);
            var resolution = await _dbContext.SupportCaseResolutions.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.SupportCaseId == job.SupportCaseId, cancellationToken);
            if (supportCase?.ContactId is not Guid contactId || resolution is null || string.IsNullOrWhiteSpace(resolution.CustomerPreferenceObservations))
            {
                job.Complete(skipped: true);
                await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.System, null, "support.memory.skipped", "support_case", job.SupportCaseId.ToString("D"), AuditEventOutcomes.Succeeded, "No eligible explicit customer preference was available for memory.", ["support", "memory"]), cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            var candidate = resolution.CustomerPreferenceObservations.Trim();
            var existingObservation = await _dbContext.SupportMemoryObservations.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.SourceEventKey == job.EventKey && x.ContactId == contactId, cancellationToken);
            if (existingObservation is { Status: SupportMemoryObservationStatuses.Approved or SupportMemoryObservationStatuses.Rejected or SupportMemoryObservationStatuses.Deleted })
            {
                job.Complete(skipped: true);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var decision = SupportMemorySafetyPolicy.Evaluate(candidate);
            if (decision.Status == SupportMemoryObservationStatuses.Rejected)
            {
                if (existingObservation is null)
                {
                    _dbContext.SupportMemoryObservations.Add(new SupportMemoryObservation(Guid.NewGuid(), companyId, supportCase.Id, resolution.Id, contactId, SupportMemoryObservationStatuses.Rejected, null, decision.EvidenceSummary, 0m, resolution.ResolvedUtc, null, SupportMemorySafetyPolicy.PolicyVersion, job.EventKey));
                }
                else
                {
                    existingObservation.Reject();
                }
                job.Complete(skipped: true);
                await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.System, null, "support.memory.rejected", "support_case", job.SupportCaseId.ToString("D"), AuditEventOutcomes.Succeeded, "A support memory candidate was rejected by policy.", ["support", "memory", "privacy"]), cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            var profile = await _dbContext.CustomerMemoryProfiles.Include(x => x.Preferences).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ContactId == contactId, cancellationToken);
            if (profile is null) { job.Complete(skipped: true); await _dbContext.SaveChangesAsync(cancellationToken); return; }
            var source = $"Support case {supportCase.CaseNumber}; {job.EventKey}";
            var duplicate = profile.Preferences.FirstOrDefault(x => x.PreferenceKey == "support_preference" && (x.PreferenceValue == candidate || x.SourceSummary == source));
            var contradictory = profile.Preferences.Any(x => x.PreferenceKey == "support_preference" && x.PreferenceValue != candidate);
            if (existingObservation is null)
            {
                var status = decision.Status == SupportMemoryObservationStatuses.Approved && !contradictory
                    ? SupportMemoryObservationStatuses.Approved
                    : SupportMemoryObservationStatuses.Review;
                existingObservation = new SupportMemoryObservation(Guid.NewGuid(), companyId, supportCase.Id, resolution.Id, contactId, status, candidate, contradictory ? "A different support preference already exists and needs review." : decision.EvidenceSummary, decision.Confidence, resolution.ResolvedUtc, decision.ValidUntilUtc, SupportMemorySafetyPolicy.PolicyVersion, job.EventKey);
                _dbContext.SupportMemoryObservations.Add(existingObservation);
            }
            if (duplicate is not null)
            {
                existingObservation.Approve(duplicate.Id);
            }
            else if (existingObservation.Status == SupportMemoryObservationStatuses.Approved)
            {
                var preference = new CustomerMemoryProfilePreference(Guid.NewGuid(), companyId, profile.Id, "support_preference", candidate, source, decision.Confidence, resolution.ResolvedUtc);
                _dbContext.CustomerMemoryProfilePreferences.Add(preference);
                existingObservation.Approve(preference.Id);
            }
            else
            {
                existingObservation.MarkReviewRequired();
            }
            job.Complete();
            var summary = existingObservation.Status == SupportMemoryObservationStatuses.Approved
                ? "An explicit support preference was added to customer memory."
                : "A support memory candidate was queued for review.";
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.System, null, "support.memory.processed", "support_case", job.SupportCaseId.ToString("D"), AuditEventOutcomes.Succeeded, summary, ["support", "memory"]), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            job.Fail(ex is DbUpdateException ? "Memory persistence failed and will be retried." : "Memory processing failed and will be retried.");
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}
