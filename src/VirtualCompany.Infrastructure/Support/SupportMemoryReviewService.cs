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

public sealed class SupportMemoryReviewService : ISupportMemoryReviewService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportMemoryReviewService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SupportMemoryObservationDto>> ListAsync(Guid companyId, Guid? contactId, string? status, CancellationToken cancellationToken)
    {
        var query = _dbContext.SupportMemoryObservations.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (contactId is Guid id) query = query.Where(x => x.ContactId == id);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == SupportMemoryObservationStatuses.Normalize(status));
        return await query.OrderByDescending(x => x.UpdatedUtc).Take(200).Select(x => MapMemoryObservation(x)).ToListAsync(cancellationToken);
    }

    public Task<SupportMemoryObservationDto?> ApproveAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken) =>
        MutateAsync(companyId, userId, observationId, "support.memory.approved", request.Note ?? "Support memory approved.", async observation =>
        {
            if (observation.Status is SupportMemoryObservationStatuses.Deleted or SupportMemoryObservationStatuses.Rejected)
            {
                throw new SupportValidationException(new Dictionary<string, string[]> { ["status"] = ["This memory observation cannot be approved."] });
            }

            if (string.IsNullOrWhiteSpace(observation.Value))
            {
                throw new SupportValidationException(new Dictionary<string, string[]> { ["value"] = ["There is no safe value to approve."] });
            }

            var profile = await _dbContext.CustomerMemoryProfiles.Include(x => x.Preferences).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ContactId == observation.ContactId, cancellationToken);
            if (profile is null)
            {
                profile = new CustomerMemoryProfile(Guid.NewGuid(), companyId, observation.ContactId);
                _dbContext.CustomerMemoryProfiles.Add(profile);
            }

            var source = $"Support memory observation {observation.Id:D}";
            var preference = profile.Preferences.FirstOrDefault(x => x.PreferenceKey == "support_preference" && x.PreferenceValue == observation.Value)
                ?? new CustomerMemoryProfilePreference(Guid.NewGuid(), companyId, profile.Id, "support_preference", observation.Value, source, observation.Confidence, observation.ObservedUtc);
            if (preference.Id != Guid.Empty && !profile.Preferences.Any(x => x.Id == preference.Id))
            {
                _dbContext.CustomerMemoryProfilePreferences.Add(preference);
            }

            observation.Approve(preference.Id);
        }, cancellationToken);

    public Task<SupportMemoryObservationDto?> RejectAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken) =>
        MutateAsync(companyId, userId, observationId, "support.memory.rejected", request.Note ?? "Support memory rejected.", observation => { observation.Reject(); return Task.CompletedTask; }, cancellationToken);

    public Task<SupportMemoryObservationDto?> ExpireAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken) =>
        MutateAsync(companyId, userId, observationId, "support.memory.expired", request.Note ?? "Support memory expired.", async observation => { await RemoveLinkedPreferenceAsync(companyId, observation, cancellationToken); observation.Expire(); }, cancellationToken);

    public Task<SupportMemoryObservationDto?> DeleteAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken) =>
        MutateAsync(companyId, userId, observationId, "support.memory.deleted", request.Note ?? "Support memory deleted.", async observation => { await RemoveLinkedPreferenceAsync(companyId, observation, cancellationToken); observation.Delete(); }, cancellationToken);

    private async Task<SupportMemoryObservationDto?> MutateAsync(Guid companyId, Guid userId, Guid observationId, string action, string summary, Func<SupportMemoryObservation, Task> mutation, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.SupportMemoryObservations.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == observationId, cancellationToken);
        if (observation is null) return null;
        await mutation(observation);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, action, "support_memory_observation", observation.Id.ToString("D"), AuditEventOutcomes.Succeeded, summary, ["support", "memory"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapMemoryObservation(observation);
    }

    private async Task RemoveLinkedPreferenceAsync(Guid companyId, SupportMemoryObservation observation, CancellationToken cancellationToken)
    {
        if (observation.CustomerMemoryProfilePreferenceId is not Guid preferenceId)
        {
            return;
        }

        var preference = await _dbContext.CustomerMemoryProfilePreferences.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == preferenceId, cancellationToken);
        if (preference is not null)
        {
            _dbContext.CustomerMemoryProfilePreferences.Remove(preference);
        }
    }

    private static SupportMemoryObservationDto MapMemoryObservation(SupportMemoryObservation observation) =>
        new(
            observation.Id,
            observation.SupportCaseId,
            observation.SupportCaseResolutionId,
            observation.ContactId,
            observation.CustomerMemoryProfilePreferenceId,
            observation.Status,
            SupportLabels.Event(observation.Status),
            observation.Status is SupportMemoryObservationStatuses.Rejected or SupportMemoryObservationStatuses.Deleted ? null : observation.Value,
            observation.EvidenceSummary,
            observation.Confidence,
            observation.ObservedUtc,
            observation.ValidUntilUtc,
            observation.PolicyVersion,
            observation.SourceEventKey,
            observation.UpdatedUtc,
            observation.Status switch
            {
                SupportMemoryObservationStatuses.Review => ["approve", "reject"],
                SupportMemoryObservationStatuses.Approved => ["expire", "delete"],
                SupportMemoryObservationStatuses.Expired => ["delete"],
                _ => []
            });
}

