using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingCloseGovernanceService : IAccountingCloseGovernanceService
{
    private static readonly string[] RequiredCategories =
    [
        "bank", "ar", "ap", "vat_tax", "suspense", "approvals", "delivery", "documents",
        "exports", "schedules", "accruals", "revaluation", "assets", "dimensions", "tasks"
    ];

    private static readonly HashSet<string> ExplicitlyWaivableCodes = new(StringComparer.Ordinal)
    {
        "close_document_gap", "close_delivery_backlog", "close_export_backlog"
    };

    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly IReportingPeriodCloseService _periodClose;
    private readonly IAccountingReportingService _reporting;
    private readonly IApprovalRequestService _approvals;
    private readonly IKnowledgeAccessPolicyEvaluator _knowledgeAccess;
    private readonly IAuditEventWriter _audit;
    private readonly AccountingCloseTelemetry _telemetry;
    private readonly TimeProvider _clock;

    public AccountingCloseGovernanceService(VirtualCompanyDbContext db,
        ICompanyMembershipContextResolver memberships, IReportingPeriodCloseService periodClose,
        IAccountingReportingService reporting, IApprovalRequestService approvals,
        IKnowledgeAccessPolicyEvaluator knowledgeAccess, IAuditEventWriter audit,
        AccountingCloseTelemetry telemetry, TimeProvider clock)
    {
        _db = db; _memberships = memberships; _periodClose = periodClose; _reporting = reporting;
        _approvals = approvals; _knowledgeAccess = knowledgeAccess; _audit = audit;
        _telemetry = telemetry; _clock = clock;
    }

    public async Task<AccountingClosePolicyDto> ConfigurePolicyAsync(
        ConfigureAccountingClosePolicyCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, PolicyRoles, cancellationToken);
        var policy = await _db.CompanyAccountingClosePolicies.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        var now = Now();
        if (policy is null)
        {
            if (command.ExpectedVersion.HasValue)
                throw Error(AccountingCloseGovernanceReasonCodes.NotFound, "The accounting close policy was not found.");
            policy = new(Guid.NewGuid(), command.CompanyId, command.MaterialityThreshold, command.Currency,
                command.WaiverValidityHours, member.UserId, now);
            _db.CompanyAccountingClosePolicies.Add(policy);
        }
        else
        {
            EnsureVersion(policy.Version, command.ExpectedVersion ?? throw new ArgumentException("ExpectedVersion is required when updating a close policy."));
            policy.Update(command.MaterialityThreshold, command.Currency, command.WaiverValidityHours, member.UserId, now);
        }
        await WriteAuditAsync(command.CompanyId, member.UserId, "accounting.close.policy_configured",
            AuditTargetTypes.AccountingCloseReadiness, policy.Id,
            "Configured company close materiality and waiver validity policy.", command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken);
        return MapPolicy(policy);
    }

    public async Task<AccountingClosePolicyDto> GetPolicyAsync(GetAccountingClosePolicyQuery query,
        CancellationToken cancellationToken)
    {
        await RequireMemberAsync(query.CompanyId, null, cancellationToken);
        var policy = await _db.CompanyAccountingClosePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        return policy is null ? DefaultPolicy(query.CompanyId) : MapPolicy(policy);
    }

    public async Task<AccountingCloseGovernanceDto> PrepareAsync(PrepareAccountingCloseReadinessCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, PrepareRoles, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var operationHash = Hash(new { Action = command.Refresh ? "refresh" : "prepare", command.CloseInstanceId,
            command.ExpectedInstanceVersion });
        if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, operationHash, cancellationToken))
            return await GetAsync(new(command.CompanyId, command.CloseInstanceId), cancellationToken);

        var close = await LoadCloseAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        EnsureVersion(close.Version, command.ExpectedInstanceVersion);
        if (close.Status == AccountingCloseInstanceStatuses.Cancelled)
            throw Error(AccountingCloseGovernanceReasonCodes.InvalidState, "A cancelled close cannot be prepared.", true);
        var periodLocked = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == command.CompanyId && x.Id == close.FiscalPeriodId && x.IsReportingLocked, cancellationToken);
        if (periodLocked)
            throw Error(AccountingCloseGovernanceReasonCodes.InvalidState,
                "A locked period cannot receive a replacement readiness snapshot. Use the controlled reopen workflow.", true);
        var existingCurrent = await CurrentSnapshotAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        if (!command.Refresh && existingCurrent is not null && existingCurrent.Status is not AccountingCloseReadinessStatuses.Stale
            and not AccountingCloseReadinessStatuses.Failed and not AccountingCloseReadinessStatuses.Cancelled)
            throw Error(AccountingCloseGovernanceReasonCodes.InvalidState, "Refresh the existing readiness snapshot instead of preparing another one.", true, existingCurrent.Version);

        var policy = await EnsurePolicyAsync(command.CompanyId, close.TemplateVersion.MaterialityAmount,
            member.UserId, cancellationToken);
        var now = Now();
        ReadinessEvaluation evaluation;
        try { evaluation = await EvaluateAsync(close, policy, now, cancellationToken); }
        catch (Exception exception) when (exception is not UnauthorizedAccessException and not OperationCanceledException)
        {
            if (existingCurrent is not null && existingCurrent.Status is not AccountingCloseReadinessStatuses.Locked
                and not AccountingCloseReadinessStatuses.Cancelled)
            {
                existingCurrent.MarkFailed("readiness_evaluation_failed", Safe(exception), now);
                await SaveAsync(cancellationToken);
            }
            _telemetry.Governance("prepare", "failed", "readiness_evaluation_failed");
            throw;
        }

        if (existingCurrent is not null && existingCurrent.Status is not AccountingCloseReadinessStatuses.Locked
            and not AccountingCloseReadinessStatuses.Cancelled)
            existingCurrent.MarkStale("A newer authoritative readiness snapshot was prepared.", now);
        var nextNumber = await _db.AccountingCloseReadinessSnapshots.IgnoreQueryFilters()
            .Where(x => x.CompanyId == command.CompanyId && x.CloseInstanceId == close.Id)
            .MaxAsync(x => (int?)x.SnapshotNumber, cancellationToken) ?? 0;
        var snapshot = new AccountingCloseReadinessSnapshot(Guid.NewGuid(), command.CompanyId, close.Id,
            close.FiscalPeriodId, nextNumber + 1, evaluation.Hash, evaluation.TrialBalanceChecksum,
            evaluation.Checks.All(x => !x.IsBlocking), member.UserId, now);
        _db.AccountingCloseReadinessSnapshots.Add(snapshot);
        foreach (var check in evaluation.Checks)
            _db.AccountingCloseReadinessChecks.Add(new(Guid.NewGuid(), command.CompanyId, snapshot.Id,
                check.Category, check.Code, check.Message, check.IsBlocking, check.IsWaivable,
                check.Amount, check.Currency, check.ItemCount, check.EvidenceJson, check.EvidenceHash, now));
        AddSignOff(command.CompanyId, close.Id, snapshot.Id, null, "prepared", snapshot.EvidenceHash,
            member, null, now);
        AddOperation(command.CompanyId, command.Refresh ? "readiness_refresh" : "readiness_prepare",
            command.IdempotencyKey, operationHash, close.Id, snapshot.Version, now);
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseReadinessPrepared,
            AuditTargetTypes.AccountingCloseReadiness, snapshot.Id,
            snapshot.IsReady ? "Prepared a current close readiness snapshot with no blockers."
                : $"Prepared a close readiness snapshot with {evaluation.Checks.Count(x => x.IsBlocking)} blocker(s).",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry.Governance(command.Refresh ? "refresh" : "prepare", "succeeded", snapshot.IsReady ? null : "blocked");
        return await GetAsync(new(command.CompanyId, close.Id), cancellationToken);
    }

    public async Task<AccountingCloseGovernanceDto> SubmitAsync(SubmitAccountingCloseReadinessCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, SubmitRoles, cancellationToken);
        return await ChangeSnapshotAsync(command.CompanyId, command.CloseInstanceId, command.SnapshotId,
            command.ExpectedSnapshotVersion, command.ExpectedEvidenceHash, command.IdempotencyKey,
            member, "submit", null, command.CorrelationId, cancellationToken);
    }

    public async Task<AccountingCloseGovernanceDto> ReviewAsync(ReviewAccountingCloseReadinessCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, ReviewRoles, cancellationToken);
        if (!command.Approve && string.IsNullOrWhiteSpace(command.Reason))
            throw new ArgumentException("A rejection reason is required.", nameof(command));
        return await ChangeSnapshotAsync(command.CompanyId, command.CloseInstanceId, command.SnapshotId,
            command.ExpectedSnapshotVersion, command.ExpectedEvidenceHash, command.IdempotencyKey,
            member, command.Approve ? "approve" : "reject", command.Reason, command.CorrelationId, cancellationToken);
    }

    public async Task<AccountingCloseGovernanceDto> CancelAsync(CancelAccountingCloseReadinessCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, SubmitRoles, cancellationToken);
        RequireReason(command.Reason, 10);
        return await ChangeSnapshotAsync(command.CompanyId, command.CloseInstanceId, command.SnapshotId,
            command.ExpectedSnapshotVersion, command.ExpectedEvidenceHash, command.IdempotencyKey,
            member, "cancel", command.Reason, command.CorrelationId, cancellationToken);
    }

    public async Task<AccountingCloseGovernanceDto> LockAsync(LockAccountingCloseCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, ReviewRoles, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey); RequireReason(command.Reason, 10);
        var operationHash = Hash(new { Action = "lock", command.CloseInstanceId, command.SnapshotId,
            command.ExpectedSnapshotVersion, command.ExpectedEvidenceHash, Reason = command.Reason.Trim() });
        if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, operationHash, cancellationToken))
            return await GetAsync(new(command.CompanyId, command.CloseInstanceId), cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var close = await LoadCloseAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        var snapshot = await LoadSnapshotAsync(command.CompanyId, close.Id, command.SnapshotId, true, cancellationToken);
        EnsureVersion(snapshot.Version, command.ExpectedSnapshotVersion); EnsureHash(snapshot.EvidenceHash, command.ExpectedEvidenceHash);
        if (snapshot.Status != AccountingCloseReadinessStatuses.Approved || !snapshot.IsReady || !snapshot.ReviewedByUserId.HasValue)
            throw Error(AccountingCloseGovernanceReasonCodes.ApprovalRequired, "A current, ready snapshot requires independent approval before lock.", true);
        if (snapshot.ReviewedByUserId == snapshot.PreparedByUserId || snapshot.ReviewedByUserId == snapshot.SubmittedByUserId)
            throw Error(AccountingCloseGovernanceReasonCodes.SelfReview, "The close readiness approval violates segregation of duties.", true);

        var policy = await EnsurePolicyAsync(command.CompanyId, close.TemplateVersion.MaterialityAmount, member.UserId, cancellationToken);
        var evaluation = await EvaluateAsync(close, policy, Now(), cancellationToken);
        if (!string.Equals(evaluation.Hash, snapshot.EvidenceHash, StringComparison.Ordinal) || evaluation.Checks.Any(x => x.IsBlocking))
        {
            snapshot.MarkStale("Authoritative accounting, task, approval, waiver, or backlog evidence changed after approval.", Now());
            AddSignOff(command.CompanyId, close.Id, snapshot.Id, null, "stale", snapshot.EvidenceHash, member,
                "The evidence changed before lock.", Now());
            await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            _telemetry.Governance("lock", "blocked", AccountingCloseGovernanceReasonCodes.EvidenceStale);
            throw Error(AccountingCloseGovernanceReasonCodes.EvidenceStale,
                "Close readiness changed after approval. Refresh, submit, and approve the new snapshot.", true, snapshot.Version);
        }

        await _periodClose.CloseAndLockAsync(new(command.CompanyId, close.FiscalPeriodId, command.Reason), cancellationToken);
        var now = Now(); snapshot.MarkLocked(member.UserId, command.ExpectedEvidenceHash, now);
        AddSignOff(command.CompanyId, close.Id, snapshot.Id, null, "locked", snapshot.EvidenceHash, member, command.Reason, now);
        var postCloseKey = $"accounting-close-post-lock:{command.CompanyId:N}:{close.Id:N}:{snapshot.Id:N}";
        if (!await _db.BackgroundExecutions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == command.CompanyId &&
            x.IdempotencyKey == postCloseKey, cancellationToken))
            _db.BackgroundExecutions.Add(new BackgroundExecution(Guid.NewGuid(), command.CompanyId,
                BackgroundExecutionType.FinanceReportRegeneration, BackgroundExecutionRelatedEntityTypes.FiscalPeriod,
                close.FiscalPeriodId.ToString("D"), command.CorrelationId ?? postCloseKey, postCloseKey, 3));
        AddOperation(command.CompanyId, "governed_lock", command.IdempotencyKey, operationHash, close.Id, snapshot.Version, now);
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseLocked,
            AuditTargetTypes.AccountingCloseInstance, close.Id,
            "Locked the period against the current independently approved close readiness evidence.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        _telemetry.Governance("lock", "succeeded", null);
        return await GetAsync(new(command.CompanyId, close.Id), cancellationToken);
    }

    public async Task<AccountingCloseGovernanceDto> ProposeWaiverAsync(ProposeAccountingCloseWaiverCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, SubmitRoles, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey); RequireReason(command.Reason, 10);
        var operationHash = Hash(new { Action = "waiver", command.CloseInstanceId, command.SnapshotId,
            command.CheckCode, command.ExpectedCheckEvidenceHash, command.Reason, command.Amount,
            command.EvidenceDocumentId, command.ExpiresUtc });
        if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, operationHash, cancellationToken))
            return await GetAsync(new(command.CompanyId, command.CloseInstanceId), cancellationToken);
        var close = await LoadCloseAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        var snapshot = await LoadSnapshotAsync(command.CompanyId, close.Id, command.SnapshotId, true, cancellationToken);
        var check = snapshot.Checks.SingleOrDefault(x => x.Code == command.CheckCode.Trim().ToLowerInvariant())
            ?? throw Error(AccountingCloseGovernanceReasonCodes.NotFound, "The readiness check was not found.");
        EnsureHash(check.EvidenceHash, command.ExpectedCheckEvidenceHash);
        if (!check.IsBlocking || !check.IsWaivable || !ExplicitlyWaivableCodes.Contains(check.Code))
            throw Error(AccountingCloseGovernanceReasonCodes.WaiverNotAllowed, "This authoritative close check cannot be waived.", true);
        var policy = await EnsurePolicyAsync(command.CompanyId, close.TemplateVersion.MaterialityAmount, member.UserId, cancellationToken);
        if (check.Amount.HasValue && check.Amount.Value > policy.MaterialityThreshold)
            throw Error(AccountingCloseGovernanceReasonCodes.WaiverNotAllowed, "The exception exceeds the company close materiality threshold.", true);
        if (command.Amount.HasValue && check.Amount.HasValue && command.Amount.Value != check.Amount.Value)
            throw Error(AccountingCloseGovernanceReasonCodes.EvidenceStale, "The waiver amount does not match the exact readiness evidence.", true);
        var document = await _db.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == command.CompanyId && x.Id == command.EvidenceDocumentId, cancellationToken)
            ?? throw Error(AccountingCloseReasonCodes.EvidenceAccessDenied, "The waiver evidence document is not available in this company.");
        var access = new CompanyKnowledgeAccessContext(command.CompanyId, member.MembershipId, member.UserId,
            member.MembershipRole.ToStorageValue(), ["finance", "accounting", "knowledge"]);
        if (!_knowledgeAccess.CanAccess(access, document))
            throw Error(AccountingCloseReasonCodes.EvidenceAccessDenied, "The waiver evidence document is not accessible.");
        var documentHash = Metadata(document, "content_hash") ?? Hash(new { document.Id, document.StorageKey,
            document.FileSizeBytes, document.UpdatedUtc });
        var expires = command.ExpiresUtc ?? Now().AddHours(policy.WaiverValidityHours);
        if (expires <= Now() || expires > Now().AddDays(90)) throw new ArgumentOutOfRangeException(nameof(command.ExpiresUtc));
        var waiverId = Guid.NewGuid();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var approval = await _approvals.CreateAsync(command.CompanyId, new(
            ApprovalTargetEntityType.AccountingCloseWaiver.ToStorageValue(), waiverId,
            AuditActorTypes.User, member.UserId, "accounting_close_exception_waiver",
            new Dictionary<string, JsonNode?>
            {
                ["closeInstanceId"] = JsonValue.Create(close.Id.ToString("D")),
                ["snapshotId"] = JsonValue.Create(snapshot.Id.ToString("D")),
                ["checkCode"] = JsonValue.Create(check.Code),
                ["checkEvidenceHash"] = JsonValue.Create(check.EvidenceHash),
                ["amount"] = JsonValue.Create(command.Amount ?? check.Amount),
                ["expiresUtc"] = JsonValue.Create(expires)
            }, Steps: [new(1, ApprovalStepApproverType.Role.ToStorageValue(), "finance_approver")]), cancellationToken);
        var waiver = new AccountingCloseWaiver(waiverId, command.CompanyId, close.Id, snapshot.Id, check.Code,
            check.EvidenceHash, command.Reason, command.Amount ?? check.Amount, document.Id, documentHash,
            approval.Id, member.UserId, expires, Now());
        _db.AccountingCloseWaivers.Add(waiver);
        AddOperation(command.CompanyId, "waiver_propose", command.IdempotencyKey, operationHash, close.Id, snapshot.Version, Now());
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseWaiverProposed,
            AuditTargetTypes.AccountingCloseWaiver, waiver.Id,
            "Proposed a time-limited close exception waiver bound to exact check and document evidence.",
            command.CorrelationId, Now(), cancellationToken);
        await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        _telemetry.Governance("waiver_propose", "succeeded", check.Code);
        return await GetAsync(new(command.CompanyId, close.Id), cancellationToken);
    }

    public async Task<AccountingCloseGovernanceDto> ReviewWaiverAsync(ReviewAccountingCloseWaiverCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, ReviewRoles, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var operationHash = Hash(new { Action = "waiver_review", command.CloseInstanceId, command.WaiverId,
            command.Approve, command.Comment });
        if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, operationHash, cancellationToken))
            return await GetAsync(new(command.CompanyId, command.CloseInstanceId), cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var waiver = await _db.AccountingCloseWaivers.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == command.CompanyId && x.CloseInstanceId == command.CloseInstanceId && x.Id == command.WaiverId, cancellationToken)
            ?? throw Error(AccountingCloseGovernanceReasonCodes.NotFound, "The close waiver was not found.");
        if (waiver.ProposedByUserId == member.UserId)
            throw Error(AccountingCloseGovernanceReasonCodes.SelfReview, "A waiver proposer cannot review their own waiver.", true);
        var decision = await _approvals.DecideAsync(command.CompanyId,
            new(waiver.ApprovalRequestId, command.Approve ? "approve" : "reject", Comment: command.Comment,
                ClientRequestId: GuidFrom(command.IdempotencyKey)), cancellationToken);
        if (command.Approve && decision.Approval.Status == ApprovalRequestStatus.Approved.ToStorageValue()) waiver.Approve(member.UserId, Now());
        else if (!command.Approve && decision.Approval.Status == ApprovalRequestStatus.Rejected.ToStorageValue()) waiver.Reject(member.UserId, Now());
        else throw Error(AccountingCloseGovernanceReasonCodes.ApprovalRequired, "The central approval is not final.", true);
        AddOperation(command.CompanyId, "waiver_review", command.IdempotencyKey, operationHash, command.CloseInstanceId, 1, Now());
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseWaiverReviewed,
            AuditTargetTypes.AccountingCloseWaiver, waiver.Id,
            command.Approve ? "Approved an exact, time-limited close exception waiver." : "Rejected a close exception waiver.",
            command.CorrelationId, Now(), cancellationToken);
        await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        _telemetry.Governance("waiver_review", "succeeded", command.Approve ? "approved" : "rejected");
        return await GetAsync(new(command.CompanyId, command.CloseInstanceId), cancellationToken);
    }

    public async Task<AccountingCloseGovernanceDto> RequestReopenAsync(RequestAccountingCloseReopenCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, SubmitRoles, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey); RequireReason(command.Reason, 10);
        if (string.IsNullOrWhiteSpace(command.Scope) || string.IsNullOrWhiteSpace(command.CorrectionPath))
            throw new ArgumentException("Reopen scope and correction path are required.");
        var operationHash = Hash(new { Action = "reopen_request", command.CloseInstanceId,
            command.PriorSnapshotId, command.ExpectedSnapshotHash, command.Reason, command.Scope,
            command.CorrectionPath, command.ExpiresUtc });
        if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, operationHash, cancellationToken))
            return await GetAsync(new(command.CompanyId, command.CloseInstanceId), cancellationToken);
        var close = await LoadCloseAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        var snapshot = await LoadSnapshotAsync(command.CompanyId, close.Id, command.PriorSnapshotId, true, cancellationToken);
        EnsureHash(snapshot.EvidenceHash, command.ExpectedSnapshotHash);
        if (snapshot.Status != AccountingCloseReadinessStatuses.Locked)
            throw Error(AccountingCloseGovernanceReasonCodes.InvalidState, "Only the locked close snapshot can anchor a reopen request.", true);
        var period = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.CompanyId == command.CompanyId && x.Id == close.FiscalPeriodId, cancellationToken);
        if (!period.IsClosed || !period.IsReportingLocked)
            throw Error(AccountingCloseGovernanceReasonCodes.PeriodStateChanged, "The period is no longer closed and reporting-locked.", true);
        var expires = command.ExpiresUtc ?? Now().AddHours(72);
        var request = new AccountingCloseReopenRequest(Guid.NewGuid(), command.CompanyId, close.Id, snapshot.Id,
            snapshot.EvidenceHash, command.Reason, command.Scope, command.CorrectionPath, member.UserId, expires, Now());
        _db.AccountingCloseReopenRequests.Add(request);
        AddSignOff(command.CompanyId, close.Id, snapshot.Id, request.Id, "reopen_requested", snapshot.EvidenceHash,
            member, command.Reason, Now());
        AddOperation(command.CompanyId, "reopen_request", command.IdempotencyKey, operationHash, close.Id, request.Version, Now());
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseReopenRequested,
            AuditTargetTypes.AccountingCloseReopenRequest, request.Id,
            "Requested controlled period reopen with retained scope and correction path.", command.CorrelationId, Now(), cancellationToken);
        await SaveAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, close.Id), cancellationToken);
    }

    public async Task<AccountingCloseGovernanceDto> ReviewReopenAsync(ReviewAccountingCloseReopenCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, ReopenRoles, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var operationHash = Hash(new { Action = "reopen_review", command.CloseInstanceId,
            command.ReopenRequestId, command.ExpectedVersion, command.Approve, command.Comment });
        if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, operationHash, cancellationToken))
            return await GetAsync(new(command.CompanyId, command.CloseInstanceId), cancellationToken);
        var request = await LoadReopenAsync(command.CompanyId, command.CloseInstanceId, command.ReopenRequestId, cancellationToken);
        EnsureVersion(request.Version, command.ExpectedVersion);
        try { request.Review(member.UserId, command.Approve, Now()); }
        catch (InvalidOperationException exception) { throw Error(AccountingCloseGovernanceReasonCodes.SelfReview, exception.Message, true, request.Version); }
        AddSignOff(command.CompanyId, command.CloseInstanceId, request.PriorSnapshotId, request.Id,
            command.Approve ? "reopen_approved" : "reopen_rejected", request.PriorSnapshotHash, member, command.Comment, Now());
        AddOperation(command.CompanyId, "reopen_review", command.IdempotencyKey, operationHash, command.CloseInstanceId, request.Version, Now());
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseReopenReviewed,
            AuditTargetTypes.AccountingCloseReopenRequest, request.Id,
            command.Approve ? "Approved a controlled period reopen request." : "Rejected a controlled period reopen request.",
            command.CorrelationId, Now(), cancellationToken);
        await SaveAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, command.CloseInstanceId), cancellationToken);
    }

    public async Task<AccountingCloseGovernanceDto> ExecuteReopenAsync(ExecuteAccountingCloseReopenCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireRoleAsync(command.CompanyId, command.ActorUserId, ReopenRoles, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var operationHash = Hash(new { Action = "reopen_execute", command.CloseInstanceId,
            command.ReopenRequestId, command.ExpectedVersion, command.ExpectedSnapshotHash });
        if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, operationHash, cancellationToken))
            return await GetAsync(new(command.CompanyId, command.CloseInstanceId), cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var request = await LoadReopenAsync(command.CompanyId, command.CloseInstanceId, command.ReopenRequestId, cancellationToken);
        EnsureVersion(request.Version, command.ExpectedVersion); EnsureHash(request.PriorSnapshotHash, command.ExpectedSnapshotHash);
        var now = Now();
        if (request.Status != AccountingCloseReopenStatuses.Approved ||
            !request.ReviewedByUserId.HasValue || request.ReviewedByUserId == request.RequestedByUserId)
            throw Error(AccountingCloseGovernanceReasonCodes.ApprovalRequired, "An independent reopen approval is required.", true);
        if (request.ExpiresUtc <= now)
            throw Error(AccountingCloseGovernanceReasonCodes.ApprovalRequired, "The reopen approval expired before execution.", true, request.Version);
        var snapshot = await LoadSnapshotAsync(command.CompanyId, command.CloseInstanceId, request.PriorSnapshotId, false, cancellationToken);
        if (snapshot.Status != AccountingCloseReadinessStatuses.Locked || snapshot.EvidenceHash != request.PriorSnapshotHash)
            throw Error(AccountingCloseGovernanceReasonCodes.EvidenceStale, "The retained lock snapshot no longer matches the reopen request.", true);
        var close = await LoadCloseAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        var period = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.CompanyId == command.CompanyId && x.Id == close.FiscalPeriodId, cancellationToken);
        if (!period.IsClosed || !period.IsReportingLocked)
            throw Error(AccountingCloseGovernanceReasonCodes.PeriodStateChanged, "The period state changed before reopen execution.", true);
        await _periodClose.ReopenAsync(new(command.CompanyId, close.FiscalPeriodId,
            $"{request.Reason} Scope: {request.Scope}. Correction path: {request.CorrectionPath}"), cancellationToken);
        request.MarkExecuted(member.UserId, now);
        AddSignOff(command.CompanyId, close.Id, snapshot.Id, request.Id, "reopened", snapshot.EvidenceHash,
            member, request.Reason, Now());
        AddOperation(command.CompanyId, "reopen_execute", command.IdempotencyKey, operationHash, close.Id, request.Version, Now());
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseReopened,
            AuditTargetTypes.AccountingCloseReopenRequest, request.Id,
            "Reopened the exact locked period after independent approval and retained the correction path.",
            command.CorrelationId, Now(), cancellationToken);
        await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        _telemetry.Governance("reopen", "succeeded", null);
        return await GetAsync(new(command.CompanyId, close.Id), cancellationToken);
    }

    public async Task<AccountingCloseGovernanceDto> GetAsync(GetAccountingCloseGovernanceQuery query,
        CancellationToken cancellationToken)
    {
        await RequireMemberAsync(query.CompanyId, null, cancellationToken);
        var close = await _db.AccountingCloseInstances.AsNoTracking().IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == query.CompanyId && x.Id == query.CloseInstanceId, cancellationToken)
            ?? throw Error(AccountingCloseGovernanceReasonCodes.NotFound, "The accounting close was not found.");
        var snapshots = await _db.AccountingCloseReadinessSnapshots.AsNoTracking().IgnoreQueryFilters()
            .Include(x => x.Checks).Where(x => x.CompanyId == query.CompanyId && x.CloseInstanceId == close.Id)
            .OrderByDescending(x => x.SnapshotNumber).ToListAsync(cancellationToken);
        var waivers = await _db.AccountingCloseWaivers.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.CompanyId == query.CompanyId && x.CloseInstanceId == close.Id)
            .OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken);
        var reopen = await _db.AccountingCloseReopenRequests.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.CompanyId == query.CompanyId && x.CloseInstanceId == close.Id)
            .OrderByDescending(x => x.RequestedUtc).ToListAsync(cancellationToken);
        var signoffs = await _db.AccountingCloseSignOffs.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.CompanyId == query.CompanyId && x.CloseInstanceId == close.Id)
            .OrderBy(x => x.OccurredUtc).ToListAsync(cancellationToken);
        var policy = await _db.CompanyAccountingClosePolicies.AsNoTracking().IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        var current = snapshots.FirstOrDefault(x => x.Status != AccountingCloseReadinessStatuses.Stale &&
            x.Status != AccountingCloseReadinessStatuses.Rejected && x.Status != AccountingCloseReadinessStatuses.Failed &&
            x.Status != AccountingCloseReadinessStatuses.Cancelled)
            ?? snapshots.FirstOrDefault();
        return new(close.Id, close.FiscalPeriodId, close.Status, close.Version,
            policy is null ? DefaultPolicy(query.CompanyId) : MapPolicy(policy), current is null ? null : MapSnapshot(current),
            snapshots.Select(MapSnapshot).ToArray(), waivers.Select(MapWaiver).ToArray(), reopen.Select(MapReopen).ToArray(),
            signoffs.Select(x => new AccountingCloseSignOffDto(x.Id, x.SnapshotId, x.ReopenRequestId, x.Action,
                x.EvidenceHash, x.ActorUserId, x.ActorRole, x.Reason, x.OccurredUtc)).ToArray(),
            AllowedActions(close, current, reopen));
    }

    private async Task<AccountingCloseGovernanceDto> ChangeSnapshotAsync(Guid companyId, Guid closeInstanceId,
        Guid snapshotId, long expectedVersion, string expectedHash, string idempotencyKey,
        ResolvedCompanyMembershipContext member, string action, string? reason, string? correlationId,
        CancellationToken cancellationToken)
    {
        ValidateIdempotency(idempotencyKey);
        var operationHash = Hash(new { Action = action, closeInstanceId, snapshotId, expectedVersion, expectedHash, reason });
        if (await ReplayAsync(companyId, idempotencyKey, operationHash, cancellationToken))
            return await GetAsync(new(companyId, closeInstanceId), cancellationToken);
        var snapshot = await LoadSnapshotAsync(companyId, closeInstanceId, snapshotId, true, cancellationToken);
        EnsureVersion(snapshot.Version, expectedVersion); EnsureHash(snapshot.EvidenceHash, expectedHash);
        try
        {
            if (action == "submit") snapshot.Submit(member.UserId, Now());
            else if (action == "approve") snapshot.Approve(member.UserId, Now());
            else if (action == "reject") snapshot.Reject(member.UserId, reason!, Now());
            else snapshot.Cancel(reason!, Now());
        }
        catch (InvalidOperationException exception)
        {
            var code = exception.Message.Contains("preparer", StringComparison.OrdinalIgnoreCase)
                ? AccountingCloseGovernanceReasonCodes.SelfReview : AccountingCloseGovernanceReasonCodes.InvalidState;
            throw Error(code, exception.Message, true, snapshot.Version);
        }
        AddSignOff(companyId, closeInstanceId, snapshot.Id, null,
            action == "approve" ? "approved" : action == "reject" ? "rejected" : action == "cancel" ? "cancelled" : "submitted",
            snapshot.EvidenceHash, member, reason, Now());
        AddOperation(companyId, $"readiness_{action}", idempotencyKey, operationHash, closeInstanceId, snapshot.Version, Now());
        var auditAction = action == "approve" ? AuditEventActions.AccountingCloseReadinessApproved
            : action == "reject" ? AuditEventActions.AccountingCloseReadinessRejected
            : action == "cancel" ? AuditEventActions.AccountingCloseReadinessCancelled
            : AuditEventActions.AccountingCloseReadinessSubmitted;
        var pastTense = action switch { "submit" => "submitted", "approve" => "approved",
            "reject" => "rejected", "cancel" => "cancelled", _ => action };
        await WriteAuditAsync(companyId, member.UserId, auditAction, AuditTargetTypes.AccountingCloseReadiness,
            snapshot.Id, $"Accounting close readiness was {pastTense} against exact evidence hash {snapshot.EvidenceHash}.",
            correlationId, Now(), cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry.Governance(action, "succeeded", null);
        return await GetAsync(new(companyId, closeInstanceId), cancellationToken);
    }

    private async Task<ReadinessEvaluation> EvaluateAsync(AccountingCloseInstance close,
        CompanyAccountingClosePolicy policy, DateTime now, CancellationToken cancellationToken)
    {
        var validation = await _periodClose.ValidateAsync(new(close.CompanyId, close.FiscalPeriodId), cancellationToken);
        var trialBalance = await _reporting.GetTrialBalanceAsync(new(close.CompanyId, close.FiscalPeriodId), cancellationToken);
        var raw = new List<EvaluationCheck>();
        foreach (var issue in validation.Issues)
        {
            var evidenceJson = JsonSerializer.Serialize(new
            {
                issue.Code, issue.Count, issue.SampleReferences, issue.Amount, issue.Currency,
                issue.RecordLinks, issue.Evidence, trialBalance.Checksum
            });
            raw.Add(new(Category(issue.Code), issue.Code, issue.Message, true, false,
                issue.Amount.HasValue ? Math.Abs(issue.Amount.Value) : null, issue.Currency, issue.Count,
                evidenceJson, Hash(evidenceJson), null));
        }

        var incomplete = close.Tasks.Where(x => x.Status != AccountingCloseTaskStatuses.Completed).OrderBy(x => x.Sequence).ToArray();
        if (incomplete.Length > 0)
            raw.Add(BuildCheck("tasks", "close_tasks_incomplete", "Required close tasks are incomplete.",
                false, null, null, incomplete.Select(x => new { x.Id, x.Key, x.Status, x.Version })));
        var documentGaps = close.Tasks.Where(x => x.Status != AccountingCloseTaskStatuses.Completed &&
            !x.Evidence.Any()).Select(x => new { x.Id, x.Key, x.Version }).ToArray();
        if (documentGaps.Length > 0)
            raw.Add(BuildCheck("documents", "close_document_gap", "Close task evidence is missing.",
                true, 0m, policy.Currency, documentGaps));

        var financeTargets = new[] { "manual_journal_draft", "customer_invoice_accounting", "supplier_bill_accounting",
            "vat_return", "payment_batch", "currency_revaluation_run", "accounting_allocation", "accounting_schedule",
            "accounting_close_task", "accounting_close_waiver" };
        var openApprovals = await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == close.CompanyId && x.Status == ApprovalRequestStatus.Pending && financeTargets.Contains(x.TargetEntityType))
            .Select(x => new { x.Id, x.TargetEntityType, x.TargetEntityId, x.UpdatedUtc }).ToListAsync(cancellationToken);
        if (openApprovals.Count > 0)
            raw.Add(BuildCheck("approvals", "close_open_approvals", "Finance approvals remain open.",
                false, null, null, openApprovals));

        var deliveryBacklog = await _db.CompanyOutboxMessages.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == close.CompanyId && x.Status != CompanyOutboxMessageStatus.Dispatched &&
            (x.Topic.StartsWith("finance.") || x.Topic.Contains("accounting") || x.Topic.Contains("invoice") || x.Topic.Contains("payment")))
            .Select(x => new { x.Id, x.Topic, x.Status, x.CreatedUtc, x.AttemptCount }).ToListAsync(cancellationToken);
        if (deliveryBacklog.Count > 0)
            raw.Add(BuildCheck("delivery", "close_delivery_backlog", "Finance provider or delivery work remains undispatched.",
                true, 0m, policy.Currency, deliveryBacklog));

        var exportBacklog = await _db.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == close.CompanyId && x.FiscalPeriodId == close.FiscalPeriodId &&
            x.Status != AccountingExportStatuses.Completed).Select(x => new { x.Id, x.Status, x.AttemptCount,
                x.UpdatedUtc }).ToListAsync(cancellationToken);
        if (exportBacklog.Count > 0)
            raw.Add(BuildCheck("exports", "close_export_backlog", "Accounting exports for the period are not complete.",
                true, 0m, policy.Currency, exportBacklog));

        foreach (var category in RequiredCategories.Where(category => raw.All(x => x.Category != category)))
            raw.Add(BuildCheck(category, $"{category}_ready", $"{Display(category)} readiness is current.",
                false, 0m, policy.Currency, new { trialBalance.Checksum, close.Version }));

        var waivers = await _db.AccountingCloseWaivers.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.ApprovalRequest).Include(x => x.EvidenceDocument).Where(x =>
            x.CompanyId == close.CompanyId && x.CloseInstanceId == close.Id &&
            x.Status == AccountingCloseWaiverStatuses.Approved).ToListAsync(cancellationToken);
        var evaluated = raw.Select(check =>
        {
            var waiver = check.IsWaivable ? waivers.FirstOrDefault(x =>
                x.AppliesTo(check.Code, check.EvidenceHash, now) &&
                x.ApprovalRequest.CompanyId == close.CompanyId && x.EvidenceDocument.CompanyId == close.CompanyId &&
                x.ApprovalRequest.Status == ApprovalRequestStatus.Approved &&
                x.ReviewedByUserId.HasValue && x.ReviewedByUserId != x.ProposedByUserId &&
                string.Equals(x.EvidenceDocumentHash, CurrentDocumentHash(x.EvidenceDocument), StringComparison.Ordinal)) : null;
            return check with { IsBlocking = check.IsBlocking && waiver is null, WaiverId = waiver?.Id };
        }).OrderBy(x => x.Category, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal).ToArray();
        var hash = Hash(new
        {
            close.Id, close.FiscalPeriodId, CloseVersion = close.Version, trialBalance.Checksum,
            Tasks = close.Tasks.OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.Version,
                Evidence = x.Evidence.OrderBy(e => e.Id).Select(e => new { e.Id, e.DocumentId, e.ContentHash }) }),
            Checks = evaluated.Select(x => new { x.Category, x.Code, x.IsBlocking, x.IsWaivable, x.Amount,
                x.Currency, x.ItemCount, x.EvidenceHash, x.WaiverId })
        });
        return new(hash, trialBalance.Checksum, evaluated);
    }

    private static EvaluationCheck BuildCheck<T>(string category, string code, string message,
        bool waivable, decimal? amount, string? currency, T evidence)
    {
        var json = JsonSerializer.Serialize(evidence);
        var count = evidence is System.Collections.ICollection collection ? collection.Count : 1;
        return new(category, code, message, !code.EndsWith("_ready", StringComparison.Ordinal),
            waivable && ExplicitlyWaivableCodes.Contains(code), amount, currency, count, json, Hash(json), null);
    }

    private async Task<CompanyAccountingClosePolicy> EnsurePolicyAsync(Guid companyId, decimal templateMateriality,
        Guid actorUserId, CancellationToken cancellationToken)
    {
        var policy = await _db.CompanyAccountingClosePolicies.SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        if (policy is not null) return policy;
        policy = new(Guid.NewGuid(), companyId, templateMateriality, "SEK", 72, actorUserId, Now());
        _db.CompanyAccountingClosePolicies.Add(policy); await SaveAsync(cancellationToken); return policy;
    }

    private async Task<AccountingCloseInstance> LoadCloseAsync(Guid companyId, Guid closeId, bool tracking,
        CancellationToken cancellationToken)
    {
        var query = tracking ? _db.AccountingCloseInstances.IgnoreQueryFilters() : _db.AccountingCloseInstances.IgnoreQueryFilters().AsNoTracking();
        return await query.Include(x => x.TemplateVersion).Include(x => x.Tasks).ThenInclude(x => x.Evidence)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == closeId, cancellationToken)
            ?? throw Error(AccountingCloseGovernanceReasonCodes.NotFound, "The accounting close was not found.");
    }

    private async Task<AccountingCloseReadinessSnapshot> LoadSnapshotAsync(Guid companyId, Guid closeId,
        Guid snapshotId, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? _db.AccountingCloseReadinessSnapshots.IgnoreQueryFilters() : _db.AccountingCloseReadinessSnapshots.IgnoreQueryFilters().AsNoTracking();
        return await query.Include(x => x.Checks).SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.CloseInstanceId == closeId && x.Id == snapshotId, cancellationToken)
            ?? throw Error(AccountingCloseGovernanceReasonCodes.NotFound, "The close readiness snapshot was not found.");
    }

    private async Task<AccountingCloseReadinessSnapshot?> CurrentSnapshotAsync(Guid companyId, Guid closeId,
        bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? _db.AccountingCloseReadinessSnapshots.IgnoreQueryFilters() : _db.AccountingCloseReadinessSnapshots.IgnoreQueryFilters().AsNoTracking();
        return await query.Where(x => x.CompanyId == companyId && x.CloseInstanceId == closeId)
            .OrderByDescending(x => x.SnapshotNumber).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<AccountingCloseReopenRequest> LoadReopenAsync(Guid companyId, Guid closeId,
        Guid requestId, CancellationToken cancellationToken) =>
        await _db.AccountingCloseReopenRequests.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.CloseInstanceId == closeId && x.Id == requestId, cancellationToken)
        ?? throw Error(AccountingCloseGovernanceReasonCodes.NotFound, "The close reopen request was not found.");

    private async Task<ResolvedCompanyMembershipContext> RequireMemberAsync(Guid companyId, Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var member = await _memberships.ResolveAsync(companyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Active company membership is required.");
        if (actorUserId.HasValue && actorUserId.Value != member.UserId)
            throw new UnauthorizedAccessException("The requested actor does not match the active company member.");
        return member;
    }

    private async Task<ResolvedCompanyMembershipContext> RequireRoleAsync(Guid companyId, Guid actorUserId,
        IReadOnlySet<CompanyMembershipRole> roles, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(companyId, actorUserId, cancellationToken);
        if (!roles.Contains(member.MembershipRole)) throw new UnauthorizedAccessException("This close action requires an authorized finance reviewer role.");
        return member;
    }

    private async Task<bool> ReplayAsync(Guid companyId, string key, string hash, CancellationToken cancellationToken)
    {
        var operation = await _db.AccountingCloseOperations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == key, cancellationToken);
        if (operation is null) return false;
        if (operation.PayloadHash != hash) throw Error(AccountingCloseReasonCodes.IdempotencyConflict,
            "The idempotency key was already used with a different close governance request.", true);
        return true;
    }

    private void AddOperation(Guid companyId, string action, string key, string hash, Guid targetId,
        long resultVersion, DateTime now) => _db.AccountingCloseOperations.Add(new(Guid.NewGuid(), companyId,
            action, key, hash, targetId, resultVersion, now));

    private void AddSignOff(Guid companyId, Guid closeId, Guid? snapshotId, Guid? reopenId, string action,
        string hash, ResolvedCompanyMembershipContext member, string? reason, DateTime now) =>
        _db.AccountingCloseSignOffs.Add(new(Guid.NewGuid(), companyId, closeId, snapshotId, reopenId,
            action, hash, member.UserId, member.MembershipRole.ToStorageValue(), reason, now));

    private async Task WriteAuditAsync(Guid companyId, Guid actorId, string action, string targetType,
        Guid targetId, string rationale, string? correlationId, DateTime now, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new(companyId, AuditActorTypes.User, actorId, action, targetType,
            targetId.ToString("D"), AuditEventOutcomes.Succeeded, rationale,
            CorrelationId: correlationId, OccurredUtc: now), cancellationToken);

    private static AccountingClosePolicyDto MapPolicy(CompanyAccountingClosePolicy x) =>
        new(x.Id, x.CompanyId, x.MaterialityThreshold, x.Currency, x.WaiverValidityHours,
            x.Version, x.UpdatedByUserId, x.UpdatedUtc);
    private static AccountingClosePolicyDto DefaultPolicy(Guid companyId) =>
        new(Guid.Empty, companyId, 0m, "SEK", 72, 0, Guid.Empty, DateTime.UnixEpoch);
    private static AccountingCloseReadinessSnapshotDto MapSnapshot(AccountingCloseReadinessSnapshot x) =>
        new(x.Id, x.SnapshotNumber, x.Status, x.EvidenceHash, x.TrialBalanceChecksum, x.IsReady,
            x.PreparedByUserId, x.PreparedUtc, x.SubmittedByUserId, x.SubmittedUtc, x.ReviewedByUserId,
            x.ReviewedUtc, x.ReviewReason, x.LockedByUserId, x.LockedUtc, x.FailureCode, x.FailureSummary,
            x.Version, x.Checks.OrderBy(c => c.Category).ThenBy(c => c.Code).Select(c =>
                new AccountingCloseReadinessCheckDto(c.Id, c.Category, c.Code, c.Message, c.IsBlocking,
                    c.IsWaivable, c.Amount, c.Currency, c.ItemCount, c.EvidenceHash, c.ObservedUtc)).ToArray());
    private static AccountingCloseWaiverDto MapWaiver(AccountingCloseWaiver x) =>
        new(x.Id, x.SnapshotId, x.CheckCode, x.CheckEvidenceHash, x.Reason, x.Amount,
            x.EvidenceDocumentId, x.EvidenceDocumentHash, x.ApprovalRequestId, x.Status,
            x.ProposedByUserId, x.ReviewedByUserId, x.CreatedUtc, x.ExpiresUtc, x.ReviewedUtc);
    private static AccountingCloseReopenRequestDto MapReopen(AccountingCloseReopenRequest x) =>
        new(x.Id, x.PriorSnapshotId, x.PriorSnapshotHash, x.Reason, x.Scope, x.CorrectionPath,
            x.Status, x.RequestedByUserId, x.RequestedUtc, x.ExpiresUtc, x.ReviewedByUserId,
            x.ReviewedUtc, x.ExecutedByUserId, x.ExecutedUtc, x.Version);
    private static IReadOnlyList<string> AllowedActions(AccountingCloseInstance close,
        AccountingCloseReadinessSnapshot? current, IReadOnlyList<AccountingCloseReopenRequest> reopen) =>
        current?.Status switch
        {
            AccountingCloseReadinessStatuses.Prepared when current.IsReady => ["refresh", "submit", "cancel"],
            AccountingCloseReadinessStatuses.Prepared => ["refresh", "propose_waiver", "cancel"],
            AccountingCloseReadinessStatuses.InReview => ["refresh", "approve", "reject", "cancel"],
            AccountingCloseReadinessStatuses.Approved => ["refresh", "lock", "cancel"],
            AccountingCloseReadinessStatuses.Locked when reopen.Any(x => x.Status == AccountingCloseReopenStatuses.Approved) => ["execute_reopen"],
            AccountingCloseReadinessStatuses.Locked => ["request_reopen"],
            AccountingCloseReadinessStatuses.Cancelled => ["prepare"],
            _ when close.Status != AccountingCloseInstanceStatuses.Cancelled => ["prepare", "refresh", "cancel"],
            _ => []
        };

    private static string Category(string code)
    {
        var value = code.ToLowerInvariant();
        if (value.Contains("bank") || value.Contains("reconciliation")) return "bank";
        if (value.Contains("receivable") || value.Contains("customer") || value.Contains("invoice")) return "ar";
        if (value.Contains("payable") || value.Contains("supplier") || value.Contains("bill")) return "ap";
        if (value.Contains("vat") || value.Contains("tax")) return "vat_tax";
        if (value.Contains("suspense")) return "suspense";
        if (value.Contains("approval")) return "approvals";
        if (value.Contains("deliver") || value.Contains("provider")) return "delivery";
        if (value.Contains("document") || value.Contains("source")) return "documents";
        if (value.Contains("export") || value.Contains("report")) return "exports";
        if (value.Contains("schedule")) return "schedules";
        if (value.Contains("accrual")) return "accruals";
        if (value.Contains("revaluation") || value.Contains("currency")) return "revaluation";
        if (value.Contains("asset") || value.Contains("depreciation")) return "assets";
        if (value.Contains("dimension") || value.Contains("mapping")) return "dimensions";
        return "tasks";
    }

    private static string Display(string category) => category switch
    {
        "ar" => "Accounts receivable", "ap" => "Accounts payable", "vat_tax" => "VAT and tax",
        _ => category.Replace('_', ' ')
    };

    private static void EnsureVersion(long current, long expected)
    { if (current != expected) throw Error(AccountingCloseReasonCodes.VersionConflict, "The close record changed. Refresh and retry.", true, current); }
    private static void EnsureHash(string current, string expected)
    { if (!string.Equals(current, expected?.Trim(), StringComparison.OrdinalIgnoreCase)) throw Error(AccountingCloseGovernanceReasonCodes.EvidenceStale, "The supplied evidence hash is stale.", true); }
    private static void ValidateIdempotency(string key)
    { if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 200) throw new ArgumentException("A valid idempotency key is required.", nameof(key)); }
    private static void RequireReason(string? reason, int minimum)
    { if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < minimum) throw new ArgumentException($"A reason of at least {minimum} characters is required.", nameof(reason)); }
    private async Task SaveAsync(CancellationToken cancellationToken)
    { try { await _db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { throw Error(AccountingCloseReasonCodes.VersionConflict, "Close governance state changed concurrently. Refresh and retry.", true); } }
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        value is string text ? text : JsonSerializer.Serialize(value))));
    private static string? Metadata(CompanyKnowledgeDocument document, string key) =>
        document.Metadata.TryGetValue(key, out var node) && node is not null ? node.ToString() : null;
    private static string CurrentDocumentHash(CompanyKnowledgeDocument document) =>
        (Metadata(document, "content_hash") ?? Hash(new { document.Id, document.StorageKey,
            document.FileSizeBytes, document.UpdatedUtc })).ToUpperInvariant();
    private static Guid GuidFrom(string value) { var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value)); return new Guid(bytes.AsSpan(0, 16)); }
    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private static string Safe(Exception exception) => exception is ArgumentException or InvalidOperationException
        ? exception.Message[..Math.Min(exception.Message.Length, 2000)] : "Close readiness evaluation failed. Review service logs and retry.";
    private static AccountingCloseGovernanceException Error(string code, string message, bool conflict = false,
        long? version = null) => new(code, message, conflict, version);

    private static readonly IReadOnlySet<CompanyMembershipRole> PrepareRoles = new HashSet<CompanyMembershipRole>
        { CompanyMembershipRole.Owner, CompanyMembershipRole.Admin, CompanyMembershipRole.Manager, CompanyMembershipRole.FinanceApprover };
    private static readonly IReadOnlySet<CompanyMembershipRole> SubmitRoles = PrepareRoles;
    private static readonly IReadOnlySet<CompanyMembershipRole> ReviewRoles = new HashSet<CompanyMembershipRole>
        { CompanyMembershipRole.Owner, CompanyMembershipRole.Admin, CompanyMembershipRole.Manager, CompanyMembershipRole.FinanceApprover };
    private static readonly IReadOnlySet<CompanyMembershipRole> ReopenRoles = new HashSet<CompanyMembershipRole>
        { CompanyMembershipRole.Owner, CompanyMembershipRole.Admin };
    private static readonly IReadOnlySet<CompanyMembershipRole> PolicyRoles = ReopenRoles;

    private sealed record EvaluationCheck(string Category, string Code, string Message, bool IsBlocking,
        bool IsWaivable, decimal? Amount, string? Currency, int ItemCount, string EvidenceJson,
        string EvidenceHash, Guid? WaiverId);
    private sealed record ReadinessEvaluation(string Hash, string TrialBalanceChecksum,
        IReadOnlyList<EvaluationCheck> Checks);
}
