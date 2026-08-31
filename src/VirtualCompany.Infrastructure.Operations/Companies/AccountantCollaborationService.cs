using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AccountantCollaborationService : IAccountantCollaborationService
{
    private static readonly ActivitySource ActivitySource = new("VirtualCompany.AccountantCollaboration");
    private static readonly Meter Meter = new("VirtualCompany.AccountantCollaboration");
    private static readonly Counter<long> AccessDenied = Meter.CreateCounter<long>("accountant_collaboration.access_denied");
    private static readonly Counter<long> Mutations = Meter.CreateCounter<long>("accountant_collaboration.mutations");

    private readonly VirtualCompanyDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ICompanyMembershipContextResolver _membershipResolver;
    private readonly IAuditEventWriter _audit;
    private readonly ICompanyOutboxEnqueuer _outbox;
    private readonly IKnowledgeAccessPolicyEvaluator _knowledgeAccessPolicy;
    private readonly TimeProvider _clock;

    public AccountantCollaborationService(VirtualCompanyDbContext db, ICurrentUserAccessor currentUser,
        ICompanyMembershipContextResolver membershipResolver, IAuditEventWriter audit,
        ICompanyOutboxEnqueuer outbox, IKnowledgeAccessPolicyEvaluator knowledgeAccessPolicy, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _membershipResolver = membershipResolver;
        _audit = audit;
        _outbox = outbox;
        _knowledgeAccessPolicy = knowledgeAccessPolicy;
        _clock = clock;
    }

    public async Task<AccountantPortfolioDto> GetPortfolioAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("accountant.portfolio.read");
        var userId = RequireCurrentUser();
        var now = UtcNow();
        var grants = await _db.AccountantCompanyGrants.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Membership).ThenInclude(x => x.Company)
            .Where(x => x.AccountantUserId == userId && x.Status == AccountantGrantStatuses.Active &&
                x.EffectiveFromUtc <= now && (!x.EffectiveUntilUtc.HasValue || x.EffectiveUntilUtc > now))
            .OrderBy(x => x.Membership.Company.Name).ToListAsync(cancellationToken);

        var rows = new List<AccountantPortfolioCompanyDto>(grants.Count);
        foreach (var grant in grants)
        {
            var companyId = grant.CompanyId;
            var engagements = _db.AccountantReviewEngagements.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.GrantId == grant.Id && x.Status == AccountantEngagementStatuses.Open);
            var openEngagements = await engagements.CountAsync(cancellationToken);
            var nextDue = await engagements.MinAsync(x => (DateTime?)x.DueUtc, cancellationToken);
            var closeStatus = await _db.AccountingCloseInstances.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.UpdatedUtc)
                .Select(x => x.Status).FirstOrDefaultAsync(cancellationToken) ?? "not_started";
            var compliance = await _db.ComplianceObligationInstances.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == companyId && x.Status != "completed" && x.Status != "cancelled", cancellationToken);
            var unreconciled = await _db.BankTransactions.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == companyId && x.ReconciledAmount < (x.Amount < 0 ? -x.Amount : x.Amount), cancellationToken);
            var failedIntegrations = await _db.FinanceIntegrationSyncStates.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == companyId && (x.Status == FinanceIntegrationSyncStatuses.Failed || x.Status == FinanceIntegrationSyncStatuses.Partial), cancellationToken);
            var approvals = await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending, cancellationToken);
            var evidence = _db.AccountantEvidenceRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status != AccountantEvidenceRequestStatuses.Resolved &&
                    _db.AccountantReviewEngagements.IgnoreQueryFilters().Any(e => e.Id == x.EngagementId && e.GrantId == grant.Id));
            var openEvidence = await evidence.CountAsync(cancellationToken);
            var overdueEvidence = await evidence.CountAsync(x => x.DueUtc < now, cancellationToken);
            rows.Add(new AccountantPortfolioCompanyDto(companyId, grant.Membership.Company.Name, grant.Id,
                grant.Status, grant.EffectiveFromUtc, grant.EffectiveUntilUtc, grant.LastAccessUtc,
                openEngagements, nextDue, closeStatus, compliance, unreconciled, failedIntegrations,
                approvals, openEvidence, overdueEvidence));
        }

        var closingSoon = rows.Count(x => x.NextDueUtc.HasValue && x.NextDueUtc <= now.AddDays(14));
        var highRisk = rows.Count(x => x.VatOrComplianceIssues > 0 || x.FailedIntegrations > 0 || x.OverdueEvidenceRequests > 0);
        return new AccountantPortfolioDto(rows.Count, closingSoon, highRisk, rows.Sum(x => x.OpenEvidenceRequests), rows);
    }

    public async Task<IReadOnlyList<AccountantGrantDto>> ListGrantsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(companyId, cancellationToken);
        var grants = await _db.AccountantCompanyGrants.AsNoTracking().Include(x => x.Membership).ThenInclude(x => x.User)
            .Include(x => x.Membership).ThenInclude(x => x.Company).Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc).ToListAsync(cancellationToken);
        return grants.Select(x => ToGrantDto(x)).ToList();
    }

    public async Task<AccountantGrantDto> CreateGrantAsync(CreateAccountantGrantCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireAdministratorAsync(command.CompanyId, cancellationToken);
        EnsureActor(command.ActorUserId, actor.UserId);
        var membership = await _db.CompanyMemberships.Include(x => x.User).Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.MembershipId, cancellationToken)
            ?? throw Missing("membership_not_found", "Accountant membership not found.");
        if (membership.Role != CompanyMembershipRole.Accountant || membership.Status != CompanyMembershipStatus.Active || !membership.UserId.HasValue)
            throw Invalid("invalid_accountant_membership", "The grant must target an active external accountant membership.");
        if (command.ScopeKey != AccountantGrantScopes.AccountingReview)
            throw Invalid("unsupported_grant_scope", "Only the accounting review scope is supported.");
        if (await _db.AccountantCompanyGrants.AnyAsync(x => x.CompanyId == command.CompanyId && x.MembershipId == command.MembershipId &&
            (x.Status == AccountantGrantStatuses.Active || x.Status == AccountantGrantStatuses.PendingApproval), cancellationToken))
            throw Conflict("grant_already_exists", "An active or pending grant already exists for this membership.");
        var now = UtcNow();
        var grant = new AccountantCompanyGrant(Guid.NewGuid(), command.CompanyId, membership.Id, membership.UserId.Value,
            command.ScopeKey, command.CanViewDocuments, command.CanRequestEvidence, command.CanSignOff,
            command.EffectiveFromUtc, command.EffectiveUntilUtc, actor.UserId, now);
        _db.AccountantCompanyGrants.Add(grant);
        await SaveAndAuditAsync(command.CompanyId, actor.UserId, "accountant.grant.created", "accountant_grant", grant.Id,
            "Explicit accountant access grant created and awaits independent approval.", cancellationToken);
        Notify(command.CompanyId, membership.UserId.Value, "Accountant access awaiting approval",
            $"Access to {membership.Company.Name} was invited and awaits approval.", grant.Id, $"grant-pending:{grant.Id:N}");
        await _db.SaveChangesAsync(cancellationToken);
        return ToGrantDto(grant, membership);
    }

    public async Task<AccountantGrantDto> ApproveGrantAsync(ApproveAccountantGrantCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireAdministratorAsync(command.CompanyId, cancellationToken); EnsureActor(command.ActorUserId, actor.UserId);
        var grant = await GrantAsync(command.CompanyId, command.GrantId, cancellationToken);
        EnsureVersion(grant.Version, command.ExpectedVersion); grant.Approve(actor.UserId, UtcNow());
        await SaveAndAuditAsync(command.CompanyId, actor.UserId, "accountant.grant.approved", "accountant_grant", grant.Id,
            "Independent approval activated the explicit accountant access grant.", cancellationToken);
        Notify(command.CompanyId, grant.AccountantUserId, "Accountant access activated", "Your explicit company engagement is now active.", grant.Id, $"grant-approved:{grant.Id:N}:{grant.Version}");
        await _db.SaveChangesAsync(cancellationToken);
        return ToGrantDto(grant);
    }

    public async Task<AccountantGrantDto> RevokeGrantAsync(RevokeAccountantGrantCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireAdministratorAsync(command.CompanyId, cancellationToken); EnsureActor(command.ActorUserId, actor.UserId);
        var grant = await GrantAsync(command.CompanyId, command.GrantId, cancellationToken);
        EnsureVersion(grant.Version, command.ExpectedVersion); grant.Revoke(actor.UserId, command.Reason, UtcNow());
        await SaveAndAuditAsync(command.CompanyId, actor.UserId, "accountant.grant.revoked", "accountant_grant", grant.Id,
            "Explicit accountant access was revoked; retained collaboration history remains company-controlled.", cancellationToken);
        Notify(command.CompanyId, grant.AccountantUserId, "Accountant access revoked", "Your company engagement access has been revoked.", grant.Id, $"grant-revoked:{grant.Id:N}:{grant.Version}");
        await _db.SaveChangesAsync(cancellationToken);
        return ToGrantDto(grant);
    }

    public async Task<IReadOnlyList<AccountantEngagementDto>> ListEngagementsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var access = await RequireCollaborationAccessAsync(companyId, cancellationToken);
        var query = EngagementQuery().Where(x => x.CompanyId == companyId);
        if (access.Role == CompanyMembershipRole.Accountant) query = query.Where(x => x.GrantId == access.Grant!.Id);
        var items = await query.AsNoTracking().OrderBy(x => x.DueUtc).ToListAsync(cancellationToken);
        var accessibleDocumentIds = await LoadAccessibleDocumentIdsAsync(items, access, cancellationToken);
        return items.Select(x => ToEngagementDto(x, accessibleDocumentIds)).ToList();
    }

    public async Task<AccountantEngagementDto> GetEngagementAsync(Guid companyId, Guid engagementId, CancellationToken cancellationToken)
    {
        var access = await RequireCollaborationAccessAsync(companyId, cancellationToken);
        var engagement = await EngagementQuery().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == engagementId, cancellationToken)
            ?? throw Missing("engagement_not_found", "Review engagement not found.");
        EnsureEngagementGrant(access, engagement);
        var accessibleDocumentIds = await LoadAccessibleDocumentIdsAsync([engagement], access, cancellationToken);
        return ToEngagementDto(engagement, accessibleDocumentIds);
    }

    public async Task<AccountantEngagementDto> CreateEngagementAsync(CreateAccountantEngagementCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireInternalFinanceActorAsync(command.CompanyId, cancellationToken); EnsureActor(command.PreparedByUserId, actor.UserId);
        var grant = await GrantAsync(command.CompanyId, command.GrantId, cancellationToken);
        if (!grant.IsEffectiveAt(UtcNow())) throw Invalid("grant_not_active", "The engagement requires an effective accountant grant.");
        if (command.FiscalPeriodId.HasValue && !await _db.FiscalPeriods.AnyAsync(x => x.CompanyId == command.CompanyId && x.Id == command.FiscalPeriodId, cancellationToken))
            throw Missing("fiscal_period_not_found", "Fiscal period not found.");
        var engagement = new AccountantReviewEngagement(Guid.NewGuid(), command.CompanyId, grant.Id, command.FiscalPeriodId,
            command.Title, command.EngagementType, grant.AccountantUserId, actor.UserId, command.DueUtc, UtcNow());
        _db.AccountantReviewEngagements.Add(engagement);
        AddHistory(engagement, "engagement_created", "engagement", engagement.Id, actor.UserId, "Review engagement created.");
        await SaveMutationAsync(command.CompanyId, actor.UserId, "accountant.engagement.created", "accountant_engagement", engagement.Id, "Accountant review engagement created.", cancellationToken);
        Notify(command.CompanyId, grant.AccountantUserId, "New review engagement", command.Title, engagement.Id, $"engagement-created:{engagement.Id:N}");
        await _db.SaveChangesAsync(cancellationToken);
        return await GetEngagementForInternalAsync(command.CompanyId, engagement.Id, new CollaborationAccess(actor, null), cancellationToken);
    }

    public Task<AccountantEngagementDto> AddReviewItemAsync(AddAccountantReviewItemCommand command, CancellationToken cancellationToken) =>
        MutateEngagementAsync(command.CompanyId, command.EngagementId, command.ActorUserId, "accountant.review_item.added", async (engagement, actor, _) =>
        {
            var item = new AccountantReviewItem(Guid.NewGuid(), command.CompanyId, engagement.Id, command.IsFinding, command.Severity,
                command.Content, command.TargetType, command.TargetId, actor.UserId, UtcNow());
            _db.AccountantReviewItems.Add(item); AddHistory(engagement, "review_item_added", command.TargetType, item.Id, actor.UserId,
                command.IsFinding ? "Finding recorded." : "Review note recorded."); await Task.CompletedTask;
        }, cancellationToken);

    public Task<AccountantEngagementDto> ResolveReviewItemAsync(ResolveAccountantReviewItemCommand command, CancellationToken cancellationToken) =>
        MutateEngagementAsync(command.CompanyId, command.EngagementId, command.ActorUserId, "accountant.review_item.resolved", async (engagement, actor, _) =>
        {
            var item = engagement.ReviewItems.SingleOrDefault(x => x.Id == command.ItemId) ?? throw Missing("review_item_not_found", "Review item not found.");
            item.Resolve(actor.UserId, command.ResolutionSummary, UtcNow()); AddHistory(engagement, "review_item_resolved", item.TargetType, item.Id, actor.UserId, "Review item resolved."); await Task.CompletedTask;
        }, cancellationToken);

    public Task<AccountantEngagementDto> CreateEvidenceRequestAsync(CreateAccountantEvidenceRequestCommand command, CancellationToken cancellationToken) =>
        MutateEngagementAsync(command.CompanyId, command.EngagementId, command.ActorUserId, "accountant.evidence.requested", async (engagement, actor, access) =>
        {
            if (actor.MembershipRole == CompanyMembershipRole.Accountant && access.Grant?.CanRequestEvidence != true)
                throw Denied("evidence_request_not_granted", "The grant does not permit evidence requests.");
            var request = new AccountantEvidenceRequest(Guid.NewGuid(), command.CompanyId, engagement.Id, command.RequestText,
                command.TargetType, command.TargetId, actor.UserId, command.AssignedToUserId, command.DueUtc, UtcNow());
            _db.AccountantEvidenceRequests.Add(request); AddHistory(engagement, "evidence_requested", command.TargetType, request.Id, actor.UserId, "Evidence requested.");
            if (command.AssignedToUserId.HasValue) Notify(command.CompanyId, command.AssignedToUserId.Value, "Evidence requested", command.RequestText, request.Id, $"evidence-request:{request.Id:N}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<AccountantEngagementDto> RespondToEvidenceRequestAsync(RespondToAccountantEvidenceRequestCommand command, CancellationToken cancellationToken) =>
        MutateEngagementAsync(command.CompanyId, command.EngagementId, command.ActorUserId, "accountant.evidence.responded", async (engagement, actor, access) =>
        {
            var request = engagement.EvidenceRequests.SingleOrDefault(x => x.Id == command.RequestId) ?? throw Missing("evidence_request_not_found", "Evidence request not found.");
            if (command.DocumentId.HasValue)
            {
                var document = await _db.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleOrDefaultAsync(
                    x => x.CompanyId == command.CompanyId && x.Id == command.DocumentId, cancellationToken)
                    ?? throw Missing("document_not_found", "The evidence document is unavailable in this company.");
                if (!CanAccessDocument(access, document))
                    throw Denied("document_access_denied", "The evidence document is outside the actor's document access policy.");
            }
            var response = new AccountantEvidenceResponse(Guid.NewGuid(), command.CompanyId, request.Id, command.ResponseText, actor.UserId, command.DocumentId, UtcNow());
            _db.AccountantEvidenceResponses.Add(response); request.RecordResponse(UtcNow()); AddHistory(engagement, "evidence_responded", request.TargetType, response.Id, actor.UserId, "Evidence response recorded.");
            Notify(command.CompanyId, request.RequestedByUserId, "Evidence response received", "A response is ready for review.", request.Id, $"evidence-response:{response.Id:N}");
        }, cancellationToken);

    public Task<AccountantEngagementDto> ResolveEvidenceRequestAsync(ResolveAccountantEvidenceRequestCommand command, CancellationToken cancellationToken) =>
        MutateEngagementAsync(command.CompanyId, command.EngagementId, command.ActorUserId, "accountant.evidence.resolved", async (engagement, actor, _) =>
        {
            var request = engagement.EvidenceRequests.SingleOrDefault(x => x.Id == command.RequestId) ?? throw Missing("evidence_request_not_found", "Evidence request not found.");
            request.Resolve(actor.UserId, command.ResolutionSummary, UtcNow()); AddHistory(engagement, "evidence_resolved", request.TargetType, request.Id, actor.UserId, "Evidence request resolved."); await Task.CompletedTask;
        }, cancellationToken);

    public Task<AccountantEngagementDto> SignOffAsync(SignOffAccountantEngagementCommand command, CancellationToken cancellationToken) =>
        MutateEngagementAsync(command.CompanyId, command.EngagementId, command.ActorUserId, "accountant.engagement.signed_off", async (engagement, actor, access) =>
        {
            EnsureVersion(engagement.Version, command.ExpectedVersion);
            if (actor.MembershipRole != CompanyMembershipRole.Accountant || access.Grant?.CanSignOff != true)
                throw Denied("signoff_not_granted", "Only the assigned accountant with sign-off scope may sign off.");
            if (actor.UserId == engagement.PreparedByUserId) throw Denied("self_signoff_forbidden", "The preparer cannot sign off their own work.");
            if (engagement.SignOffs.Any(x => x.SignedByUserId == actor.UserId)) throw Conflict("already_signed_off", "This accountant already signed off.");
            if (engagement.ReviewItems.Any(x => x.Status == AccountantReviewItemStatuses.Open) || engagement.EvidenceRequests.Any(x => x.Status != AccountantEvidenceRequestStatuses.Resolved))
                throw Conflict("open_review_work", "Resolve findings and evidence requests before sign-off.");
            var scope = $"grant={access.Grant.Id:N};scope={access.Grant.ScopeKey};documents={access.Grant.CanViewDocuments}";
            var signOff = new AccountantEngagementSignOff(Guid.NewGuid(), command.CompanyId, engagement.Id, actor.UserId, command.Conclusion, scope, UtcNow());
            _db.AccountantEngagementSignOffs.Add(signOff); AddHistory(engagement, "engagement_signed_off", "engagement", engagement.Id, actor.UserId, "Independent accountant sign-off recorded."); await Task.CompletedTask;
        }, cancellationToken);

    private async Task<AccountantEngagementDto> MutateEngagementAsync(Guid companyId, Guid engagementId, Guid actorUserId,
        string action, Func<AccountantReviewEngagement, ResolvedCompanyMembershipContext, CollaborationAccess, Task> mutation, CancellationToken ct)
    {
        var access = await RequireCollaborationAccessAsync(companyId, ct); EnsureActor(actorUserId, access.Context.UserId);
        var engagement = await EngagementQuery().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == engagementId, ct)
            ?? throw Missing("engagement_not_found", "Review engagement not found.");
        EnsureEngagementGrant(access, engagement); await mutation(engagement, access.Context, access);
        await SaveMutationAsync(companyId, access.Context.UserId, action, "accountant_engagement", engagement.Id, "Accountant collaboration state changed.", ct);
        await _db.SaveChangesAsync(ct); return await GetEngagementForInternalAsync(companyId, engagement.Id, access, ct);
    }

    private IQueryable<AccountantReviewEngagement> EngagementQuery() => _db.AccountantReviewEngagements
        .Include(x => x.Grant).ThenInclude(x => x.Membership).ThenInclude(x => x.Company)
        .Include(x => x.FiscalPeriod).Include(x => x.ReviewItems)
        .Include(x => x.EvidenceRequests).ThenInclude(x => x.Responses)
        .Include(x => x.SignOffs).Include(x => x.History);

    private async Task<AccountantEngagementDto> GetEngagementForInternalAsync(Guid companyId, Guid id, CollaborationAccess access, CancellationToken ct)
    {
        var engagement = await EngagementQuery().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == id, ct);
        var accessibleDocumentIds = await LoadAccessibleDocumentIdsAsync([engagement], access, ct);
        return ToEngagementDto(engagement, accessibleDocumentIds);
    }

    private async Task<IReadOnlySet<Guid>> LoadAccessibleDocumentIdsAsync(
        IEnumerable<AccountantReviewEngagement> engagements,
        CollaborationAccess access,
        CancellationToken ct)
    {
        if (access.Role == CompanyMembershipRole.Accountant && access.Grant?.CanViewDocuments != true)
            return new HashSet<Guid>();

        var documentIds = engagements
            .SelectMany(x => x.EvidenceRequests)
            .SelectMany(x => x.Responses)
            .Where(x => x.DocumentId.HasValue)
            .Select(x => x.DocumentId!.Value)
            .Distinct()
            .ToArray();
        if (documentIds.Length == 0) return new HashSet<Guid>();

        var documents = await _db.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == access.Context.CompanyId && documentIds.Contains(x.Id))
            .ToListAsync(ct);
        return documents.Where(x => CanAccessDocument(access, x)).Select(x => x.Id).ToHashSet();
    }

    private bool CanAccessDocument(CollaborationAccess access, CompanyKnowledgeDocument document)
    {
        if (access.Role == CompanyMembershipRole.Accountant && access.Grant?.CanViewDocuments != true) return false;
        var context = new CompanyKnowledgeAccessContext(access.Context.CompanyId, access.Context.MembershipId,
            access.Context.UserId, access.Context.MembershipRole.ToStorageValue(), Array.Empty<string>());
        return _knowledgeAccessPolicy.CanAccess(context, document);
    }

    private async Task<CollaborationAccess> RequireCollaborationAccessAsync(Guid companyId, CancellationToken ct)
    {
        var context = await _membershipResolver.ResolveAsync(companyId, ct);
        if (context is null) throw Denied("company_access_denied", "Company access is denied.");
        if (context.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager or CompanyMembershipRole.Accountant))
            throw Denied("collaboration_role_denied", "The membership role does not permit accountant collaboration access.");
        if (context.MembershipRole != CompanyMembershipRole.Accountant) return new CollaborationAccess(context, null);
        var now = UtcNow();
        var grant = await _db.AccountantCompanyGrants.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MembershipId == context.MembershipId && x.AccountantUserId == context.UserId, ct);
        if (grant is null || !grant.IsEffectiveAt(now)) throw Denied("accountant_grant_inactive", "The explicit accountant engagement grant is not active.");
        grant.RecordAccess(now); await _db.SaveChangesAsync(ct);
        return new CollaborationAccess(context, grant);
    }

    private async Task<ResolvedCompanyMembershipContext> RequireAdministratorAsync(Guid companyId, CancellationToken ct)
    {
        var context = await _membershipResolver.ResolveAsync(companyId, ct);
        if (context is null || context.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin))
            throw Denied("accountant_grant_admin_required", "Only company owners and administrators may manage accountant grants.");
        return context;
    }

    private async Task<ResolvedCompanyMembershipContext> RequireInternalFinanceActorAsync(Guid companyId, CancellationToken ct)
    {
        var context = await _membershipResolver.ResolveAsync(companyId, ct);
        if (context is null || context.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager))
            throw Denied("internal_finance_role_required", "An internal finance manager must create the engagement.");
        return context;
    }

    private async Task<AccountantCompanyGrant> GrantAsync(Guid companyId, Guid grantId, CancellationToken ct) =>
        await _db.AccountantCompanyGrants.Include(x => x.Membership).ThenInclude(x => x.User).Include(x => x.Membership).ThenInclude(x => x.Company)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == grantId, ct)
        ?? throw Missing("grant_not_found", "Accountant grant not found.");

    private void EnsureEngagementGrant(CollaborationAccess access, AccountantReviewEngagement engagement)
    {
        if (access.Context.MembershipRole == CompanyMembershipRole.Accountant && engagement.GrantId != access.Grant!.Id)
            throw Denied("engagement_access_denied", "This engagement is outside the explicit accountant grant.");
    }

    private async Task SaveMutationAsync(Guid companyId, Guid actor, string action, string targetType, Guid targetId, string summary, CancellationToken ct)
    {
        Mutations.Add(1, new KeyValuePair<string, object?>("action", action));
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actor, action, targetType,
            targetId.ToString("N"), AuditEventOutcomes.Succeeded, summary), ct);
    }

    private Task SaveAndAuditAsync(Guid companyId, Guid actor, string action, string targetType, Guid targetId, string summary, CancellationToken ct) =>
        SaveMutationAsync(companyId, actor, action, targetType, targetId, summary, ct);

    private void Notify(Guid companyId, Guid recipient, string title, string body, Guid targetId, string idempotencyKey) =>
        _outbox.Enqueue(companyId, CompanyOutboxTopics.NotificationDeliveryRequested,
            new NotificationDeliveryRequestedMessage(companyId, "accountant_collaboration", "normal", title, body,
                "accountant_collaboration", targetId, $"/accountant/portfolio?companyId={companyId}", recipient, null,
                null, null, idempotencyKey, null), idempotencyKey: idempotencyKey);

    private void AddHistory(AccountantReviewEngagement engagement, string action, string targetType, Guid? targetId, Guid actor, string summary) =>
        _db.AccountantReviewHistory.Add(new AccountantReviewHistory(Guid.NewGuid(), engagement.CompanyId, engagement.Id, action, targetType, targetId, actor, summary, UtcNow()));

    private static AccountantGrantDto ToGrantDto(AccountantCompanyGrant x, CompanyMembership? membership = null)
    {
        membership ??= x.Membership;
        return new AccountantGrantDto(x.Id, x.CompanyId, membership.Company.Name, x.MembershipId, x.AccountantUserId,
            membership.User?.DisplayName ?? membership.User?.Email ?? "External accountant", x.ScopeKey, x.CanViewDocuments,
            x.CanRequestEvidence, x.CanSignOff, x.Status, x.EffectiveFromUtc, x.EffectiveUntilUtc, x.InvitedByUserId,
            x.ApprovedByUserId, x.RevokedByUserId, x.LastAccessUtc, x.CreatedUtc, x.UpdatedUtc, x.Version);
    }

    private static AccountantEngagementDto ToEngagementDto(AccountantReviewEngagement x, IReadOnlySet<Guid> accessibleDocumentIds) =>
        new(x.Id, x.CompanyId, x.Grant.Membership.Company.Name, x.GrantId, x.FiscalPeriodId, x.FiscalPeriod?.Name,
            x.Title, x.EngagementType, x.AssignedAccountantUserId, x.PreparedByUserId, x.Status, x.DueUtc,
            x.CreatedUtc, x.UpdatedUtc, x.CompletedUtc, x.Version,
            x.ReviewItems.OrderByDescending(i => i.CreatedUtc).Select(i => new AccountantReviewItemDto(i.Id, i.IsFinding, i.Severity, i.Content, i.TargetType, i.TargetId, i.Status, i.CreatedByUserId, i.CreatedUtc, i.ResolvedByUserId, i.ResolvedUtc, i.ResolutionSummary)).ToList(),
            x.EvidenceRequests.OrderBy(i => i.DueUtc).Select(i => new AccountantEvidenceRequestDto(i.Id, i.RequestText, i.TargetType, i.TargetId, i.RequestedByUserId, i.AssignedToUserId, i.DueUtc, i.Status, i.CreatedUtc, i.UpdatedUtc, i.ResolutionSummary,
                i.Responses.OrderBy(r => r.CreatedUtc).Select(r => new AccountantEvidenceResponseDto(r.Id, r.ResponseText, r.RespondedByUserId,
                    r.DocumentId.HasValue && accessibleDocumentIds.Contains(r.DocumentId.Value) ? r.DocumentId : null,
                    r.DocumentId.HasValue && accessibleDocumentIds.Contains(r.DocumentId.Value), r.CreatedUtc)).ToList())).ToList(),
            x.SignOffs.OrderBy(i => i.SignedUtc).Select(i => new AccountantSignOffDto(i.Id, i.SignedByUserId, i.Conclusion, i.ScopeSnapshot, i.SignedUtc)).ToList(),
            x.History.OrderByDescending(i => i.OccurredUtc).Select(i => new AccountantReviewHistoryDto(i.Id, i.Action, i.TargetType, i.TargetId, i.ActorUserId, i.SafeSummary, i.OccurredUtc)).ToList());

    private Guid RequireCurrentUser() => _currentUser.UserId ?? throw Denied("authentication_required", "Authentication is required.");
    private static void EnsureActor(Guid supplied, Guid current) { if (supplied != current) throw Denied("actor_mismatch", "The actor must match the authenticated user."); }
    private static void EnsureVersion(long current, long expected) { if (current != expected) throw Conflict("concurrency_conflict", "The record changed after it was loaded."); }
    private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;
    private static AccountantCollaborationException Missing(string code, string message) => new(code, message);
    private static AccountantCollaborationException Invalid(string code, string message) => new(code, message);
    private static AccountantCollaborationException Conflict(string code, string message) => new(code, message, true);
    private static AccountantCollaborationException Denied(string code, string message)
    {
        AccessDenied.Add(1, new KeyValuePair<string, object?>("reason", code)); return new(code, message);
    }
    private sealed record CollaborationAccess(ResolvedCompanyMembershipContext Context, AccountantCompanyGrant? Grant)
    {
        public CompanyMembershipRole Role => Context.MembershipRole;
    }
}
