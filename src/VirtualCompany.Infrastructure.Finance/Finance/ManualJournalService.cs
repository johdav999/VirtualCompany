using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ManualJournalService : IManualJournalService
{
    private const string SourceType = "manual_journal_draft";
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IManualJournalPolicy _policy;
    private readonly IAccountingPostingService _postingService;
    private readonly IAuditEventWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public ManualJournalService(VirtualCompanyDbContext dbContext, IManualJournalPolicy policy,
        IAccountingPostingService postingService, IAuditEventWriter auditWriter, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _policy = policy;
        _postingService = postingService;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    public async Task<ManualJournalReferenceDataDto> GetReferenceDataAsync(GetManualJournalReferenceDataQuery query,
        CancellationToken cancellationToken)
    {
        var series = await _dbContext.VoucherSeries.AsNoTracking()
            .Where(item => item.CompanyId == query.CompanyId && item.IsActive)
            .OrderBy(item => item.Code)
            .Select(item => new ManualJournalVoucherSeriesDto(item.Code, item.DisplayName, item.NumberPrefix))
            .ToListAsync(cancellationToken);
        var documentCandidates = await _dbContext.CompanyKnowledgeDocuments.AsNoTracking()
            .Where(item => item.CompanyId == query.CompanyId)
            .OrderByDescending(item => item.UploadedUtc)
            .Take(250)
            .ToListAsync(cancellationToken);
        var documents = documentCandidates
            .Where(item => !string.IsNullOrWhiteSpace(Metadata(item, "checksum_sha256")))
            .Take(100)
            .Select(item => new ManualJournalEvidenceOptionDto(item.Id, item.Title, item.OriginalFileName,
                item.UploadedUtc ?? item.CreatedUtc))
            .ToArray();
        return new(series, documents);
    }

    public async Task<ManualJournalDraftDto> CreateAsync(CreateManualJournalDraftCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var requestHash = HashRequest(command.Draft);
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayDraftAsync(replay, requestHash, cancellationToken);

        var material = await MaterializeAsync(command.CompanyId, command.Draft, cancellationToken);
        var now = UtcNow();
        var draft = new ManualJournalDraft(Guid.NewGuid(), command.CompanyId, command.Draft.FiscalPeriodId,
            command.Draft.VoucherSeriesCode, command.Draft.DocumentDate, command.Draft.PostingDate,
            command.Draft.Explanation, command.Draft.Currency, material.PayloadHash, command.ActorUserId, now,
            command.Draft.OriginalLedgerEntryId, command.Draft.CorrectionReason, SerializeSources(command.Draft.SourceRecords));
        AddMaterial(draft, command.Draft, material, now);
        _dbContext.ManualJournalDrafts.Add(draft);
        _dbContext.ManualJournalOperations.Add(new ManualJournalOperation(Guid.NewGuid(), command.CompanyId, draft.Id,
            "create", command.IdempotencyKey, requestHash, draft.Version, null, null, now));
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingManualJournalDraftCreated,
            draft.Id, "A manual journal draft was created.", command.CorrelationId, now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, draft.Id), cancellationToken);
    }

    public async Task<ManualJournalDraftDto> UpdateAsync(UpdateManualJournalDraftCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var requestHash = HashRequest(command.Draft);
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayDraftAsync(replay, requestHash, cancellationToken);

        var material = await MaterializeAsync(command.CompanyId, command.Draft, cancellationToken);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var draft = await _dbContext.ManualJournalDrafts.Include(x => x.ApprovalRequest)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DraftId, cancellationToken)
            ?? throw NotFound();
        EnsureVersion(draft, command.ExpectedVersion);
        EnsureEditable(draft);
        if (command.Draft.OriginalLedgerEntryId != draft.OriginalLedgerEntryId)
            throw new ManualJournalException(ManualJournalReasonCodes.InvalidCorrection,
                "The journal being corrected cannot be changed after the draft is created.");
        if (draft.ApprovalRequest is { Status: ApprovalRequestStatus.Pending })
            draft.ApprovalRequest.MarkCancelled("The manual journal changed and requires a new approval.");
        await _dbContext.ManualJournalDraftLines
            .Where(line => line.CompanyId == command.CompanyId && line.DraftId == draft.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await _dbContext.ManualJournalEvidenceLinks
            .Where(link => link.CompanyId == command.CompanyId && link.DraftId == draft.Id)
            .ExecuteDeleteAsync(cancellationToken);
        var now = UtcNow();
        draft.ReplaceContent(command.Draft.FiscalPeriodId, command.Draft.VoucherSeriesCode, command.Draft.DocumentDate,
            command.Draft.PostingDate, command.Draft.Explanation, command.Draft.Currency, material.PayloadHash,
            command.Draft.CorrectionReason, SerializeSources(command.Draft.SourceRecords), command.ActorUserId, now);
        AddMaterial(draft, command.Draft, material, now);
        _dbContext.ManualJournalDraftLines.AddRange(draft.Lines);
        _dbContext.ManualJournalEvidenceLinks.AddRange(draft.EvidenceLinks);
        _dbContext.ManualJournalOperations.Add(new ManualJournalOperation(Guid.NewGuid(), command.CompanyId, draft.Id,
            "update", command.IdempotencyKey, requestHash, draft.Version, null, null, now));
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingManualJournalDraftUpdated,
            draft.Id, "The manual journal draft was updated and any earlier approval was invalidated.", command.CorrelationId, now, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ManualJournalException(ManualJournalReasonCodes.VersionConflict,
                "This draft changed elsewhere. Reload the current version before editing.", true, draft.Version);
        }
        return await GetAsync(new(command.CompanyId, draft.Id), cancellationToken);
    }

    public async Task<ManualJournalDraftDto> DiscardAsync(DiscardManualJournalDraftCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var requestHash = HashText($"{command.DraftId:N}:{command.ExpectedVersion}:discard");
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayDraftAsync(replay, requestHash, cancellationToken);
        var draft = await DraftQuery(true).SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DraftId, cancellationToken)
            ?? throw NotFound();
        EnsureVersion(draft, command.ExpectedVersion);
        EnsureEditable(draft);
        if (draft.ApprovalRequest is { Status: ApprovalRequestStatus.Pending }) draft.ApprovalRequest.MarkCancelled("The draft was discarded.");
        var now = UtcNow();
        draft.Discard(command.ActorUserId, now);
        _dbContext.ManualJournalOperations.Add(new ManualJournalOperation(Guid.NewGuid(), command.CompanyId, draft.Id,
            "discard", command.IdempotencyKey, requestHash, draft.Version, null, null, now));
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingManualJournalDraftDiscarded,
            draft.Id, "The manual journal draft was discarded.", command.CorrelationId, now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, draft.Id), cancellationToken);
    }

    public async Task<ManualJournalPreviewDto> PreviewAsync(PreviewManualJournalDraftQuery query, CancellationToken cancellationToken)
    {
        var draft = await LoadDraftAsync(query.CompanyId, query.DraftId, cancellationToken);
        EnsureVersion(draft, query.ExpectedVersion);
        var input = ToInput(draft);
        var policy = await _policy.EvaluateAsync(query.CompanyId, input, cancellationToken);
        var proposed = ToProposed(draft, query.ActorUserId, $"preview:{draft.Id:N}:{draft.Version}", requiresApproval: false);
        var preview = await _postingService.PreviewAsync(new(proposed), cancellationToken);
        return new(await MapAsync(draft, cancellationToken), preview, policy);
    }

    public async Task<ManualJournalSubmissionResult> SubmitAsync(SubmitManualJournalForApprovalCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var draft = await LoadDraftAsync(command.CompanyId, command.DraftId, cancellationToken);
        EnsureVersion(draft, command.ExpectedVersion);
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            EnsureReplay(replay, draft.PayloadHash);
            var replayDraft = await GetAsync(new(command.CompanyId, replay.DraftId), cancellationToken);
            return new(replayDraft, replay.ApprovalRequestId ?? throw new InvalidOperationException("Approval replay is incomplete."), true);
        }

        EnsureEditable(draft);
        var input = ToInput(draft);
        var policy = await _policy.EvaluateAsync(command.CompanyId, input, cancellationToken);
        var preview = await _postingService.PreviewAsync(new(ToProposed(draft, command.ActorUserId,
            $"submit-preview:{draft.Id:N}:{draft.Version}", requiresApproval: false)), cancellationToken);
        var firstIssue = policy.Issues.Concat(preview.Issues).FirstOrDefault();
        if (firstIssue is not null) throw new ManualJournalException(firstIssue.ReasonCode, firstIssue.Explanation);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        draft = await DraftQuery(true).SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DraftId, cancellationToken);
        EnsureVersion(draft, command.ExpectedVersion);
        if (draft.ApprovalRequestId.HasValue)
        {
            var existing = draft.ApprovalRequest!;
            if (ApprovalMatches(existing, draft))
            {
                await transaction.CommitAsync(cancellationToken);
                return new(await MapAsync(draft, cancellationToken), existing.Id, true);
            }
            if (existing.Status == ApprovalRequestStatus.Pending) existing.MarkCancelled("Superseded by a new manual journal approval request.");
        }

        var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), command.CompanyId,
            ApprovalTargetEntityType.ManualJournalDraft, draft.Id, AuditActorTypes.User, command.ActorUserId,
            "manual_journal_posting", new Dictionary<string, JsonNode?>
            {
                ["sourceVersion"] = JsonValue.Create(draft.Version.ToString(CultureInfo.InvariantCulture)),
                ["payloadHash"] = JsonValue.Create(draft.PayloadHash),
                ["debitTotal"] = JsonValue.Create(draft.Lines.Sum(x => x.DebitAmount)),
                ["currency"] = JsonValue.Create(draft.Currency),
                ["approvalThreshold"] = JsonValue.Create(policy.ApprovalThreshold)
            }, null, null,
            [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "finance_approver")]);
        _dbContext.ApprovalRequests.Add(approval);
        var now = UtcNow();
        draft.BindApproval(approval.Id, command.ActorUserId, now);
        _dbContext.ManualJournalOperations.Add(new ManualJournalOperation(Guid.NewGuid(), command.CompanyId, draft.Id,
            "submit", command.IdempotencyKey, draft.PayloadHash, draft.Version, approval.Id, null, now));
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingManualJournalApprovalRequested,
            draft.Id, "Approval was requested for the exact saved version of this manual journal.", command.CorrelationId,
            now, cancellationToken, approval.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(await MapAsync(draft, cancellationToken), approval.Id, false);
    }

    public async Task<ManualJournalPostingResult> PostAsync(PostApprovedManualJournalCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var draft = await LoadDraftAsync(command.CompanyId, command.DraftId, cancellationToken);
        EnsureVersion(draft, command.ExpectedVersion);
        if (draft.Status == ManualJournalDraftStatusValues.Posted && draft.LedgerEntryId.HasValue)
        {
            var existing = await _postingService.PostAsync(new(ToProposed(draft, command.ActorUserId, command.IdempotencyKey, true), command.CorrelationId), cancellationToken);
            return new(await GetAsync(new(command.CompanyId, draft.Id), cancellationToken), existing.Journal, true);
        }
        if (draft.ApprovalRequest is null) throw new ManualJournalException(ManualJournalReasonCodes.ApprovalRequired, "Submit this manual journal for approval before posting.");
        if (draft.ApprovalRequest.Status == ApprovalRequestStatus.Pending) throw new ManualJournalException(ManualJournalReasonCodes.ApprovalPending, "This manual journal is still waiting for approval.");
        if (draft.ApprovalRequest.Status != ApprovalRequestStatus.Approved) throw new ManualJournalException(ManualJournalReasonCodes.ApprovalRejected, "This manual journal was not approved.");
        if (!ApprovalMatches(draft.ApprovalRequest, draft)) throw new ManualJournalException(ManualJournalReasonCodes.ApprovalStale, "The approval does not match the current draft version.");
        var policy = await _policy.EvaluateAsync(command.CompanyId, ToInput(draft), cancellationToken);
        var issue = policy.Issues.FirstOrDefault();
        if (issue is not null) throw new ManualJournalException(issue.ReasonCode, issue.Explanation);

        var posted = await _postingService.PostAsync(new(ToProposed(draft, command.ActorUserId, command.IdempotencyKey, true), command.CorrelationId), cancellationToken);
        return new(await GetAsync(new(command.CompanyId, draft.Id), cancellationToken), posted.Journal, posted.IsIdempotentReplay);
    }

    public async Task<ManualJournalDraftDto> CreateAdjustmentAsync(CreateAdjustingJournalDraftCommand command, CancellationToken cancellationToken)
    {
        var original = await _dbContext.LedgerEntries.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == command.CompanyId && x.Id == command.OriginalLedgerEntryId && x.Status == LedgerEntryStatuses.Posted, cancellationToken)
            ?? throw new ManualJournalException(ManualJournalReasonCodes.InvalidCorrection, "The posted journal to adjust could not be found.");
        if (string.IsNullOrWhiteSpace(command.Draft.CorrectionReason))
            throw new ManualJournalException(AccountingPostingReasonCodes.CorrectionReasonRequired, "Explain why this adjustment is needed.");
        var input = command.Draft with { OriginalLedgerEntryId = original.Id };
        return await CreateAsync(new(command.CompanyId, input, command.IdempotencyKey, command.ActorUserId, command.CorrelationId), cancellationToken);
    }

    public async Task<ManualJournalDraftDto> GetAsync(GetManualJournalDraftQuery query, CancellationToken cancellationToken) =>
        await MapAsync(await LoadDraftAsync(query.CompanyId, query.DraftId, cancellationToken), cancellationToken);

    public async Task<ManualJournalDraftListResult> ListAsync(ListManualJournalDraftsQuery query, CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 250);
        var source = DraftQuery(false).Where(x => x.CompanyId == query.CompanyId);
        if (!string.IsNullOrWhiteSpace(query.Status)) source = source.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());
        var total = await source.CountAsync(cancellationToken);
        var drafts = await source.OrderByDescending(x => x.UpdatedUtc).Skip(skip).Take(take).ToListAsync(cancellationToken);
        var items = new List<ManualJournalDraftDto>(drafts.Count);
        foreach (var draft in drafts) items.Add(await MapAsync(draft, cancellationToken));
        return new(items, total, skip, take);
    }

    private IQueryable<ManualJournalDraft> DraftQuery(bool tracking) => (tracking ? _dbContext.ManualJournalDrafts : _dbContext.ManualJournalDrafts.AsNoTracking())
        .Include(x => x.Lines).ThenInclude(x => x.FinanceAccount)
        .Include(x => x.Lines).ThenInclude(x => x.DimensionAssignments)
        .Include(x => x.EvidenceLinks).ThenInclude(x => x.Document)
        .Include(x => x.ApprovalRequest);

    private async Task<ManualJournalDraft> LoadDraftAsync(Guid companyId, Guid draftId, CancellationToken cancellationToken) =>
        await DraftQuery(false).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == draftId, cancellationToken) ?? throw NotFound();

    private async Task<ManualJournalDraftDto> MapAsync(ManualJournalDraft draft, CancellationToken cancellationToken)
    {
        if (!_dbContext.Entry(draft).Collection(x => x.Lines).IsLoaded)
            draft = await LoadDraftAsync(draft.CompanyId, draft.Id, cancellationToken);
        var lines = draft.Lines.OrderBy(x => x.LineNumber).Select(x => new ManualJournalLineDto(x.Id, x.LineNumber,
            x.FinanceAccountId, x.FinanceAccount.Code, x.FinanceAccount.Name, x.DebitAmount, x.CreditAmount, x.Currency,
            x.Description, x.CostCenterId, AccountingPostingService.ParseFacts(x.TaxFactsJson),
            AccountingPostingService.ParseFacts(x.DimensionFactsJson),
            x.DimensionAssignments.Select(y => y.DimensionMemberId).OrderBy(y => y).ToArray())).ToArray();
        var evidence = draft.EvidenceLinks.OrderBy(x => x.Title).Select(x =>
            new ManualJournalEvidenceDto(x.DocumentId, x.Title, x.ContentHash, x.Document.OriginalFileName)).ToArray();
        ManualJournalApprovalDto? approval = null;
        if (draft.ApprovalRequest is not null)
            approval = new(draft.ApprovalRequest.Id, draft.ApprovalRequest.Status.ToStorageValue(), draft.ApprovalRequest.DecisionSummary,
                ReadApprovalVersion(draft.ApprovalRequest), ReadApprovalHash(draft.ApprovalRequest) ?? string.Empty,
                draft.ApprovalRequest.CreatedUtc, draft.ApprovalRequest.DecidedUtc);
        var status = draft.Status;
        if (status == ManualJournalDraftStatusValues.AwaitingApproval && draft.ApprovalRequest is not null)
            status = draft.ApprovalRequest.Status switch
            {
                ApprovalRequestStatus.Approved => "approved",
                ApprovalRequestStatus.Rejected => "rejected",
                ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.Expired => "approval_expired",
                _ => status
            };
        var debit = lines.Sum(x => x.DebitAmount);
        var credit = lines.Sum(x => x.CreditAmount);
        return new(draft.Id, draft.CompanyId, draft.FiscalPeriodId, draft.VoucherSeriesCode, draft.DocumentDate,
            draft.PostingDate, draft.Explanation, draft.Currency, status, draft.Version, draft.PayloadHash,
            draft.CreatedByUserId, draft.UpdatedByUserId, draft.ApprovalRequestId, draft.LedgerEntryId,
            draft.OriginalLedgerEntryId, draft.CorrectionReason, draft.CreatedUtc, draft.UpdatedUtc, draft.PostedUtc,
            debit, credit, debit - credit, lines, evidence, approval, DeserializeSources(draft.SourceReferencesJson));
    }

    private async Task<Material> MaterializeAsync(Guid companyId, ManualJournalDraftInput input, CancellationToken cancellationToken)
    {
        var documents = await _dbContext.CompanyKnowledgeDocuments.AsNoTracking()
            .Where(x => x.CompanyId == companyId && input.EvidenceDocumentIds.Contains(x.Id)).ToListAsync(cancellationToken);
        if (documents.Count != input.EvidenceDocumentIds.Distinct().Count())
            throw new ManualJournalException(ManualJournalReasonCodes.EvidenceNotFound, "One or more evidence documents could not be found.");
        var evidence = documents.Select(document => new Evidence(document.Id, document.Title,
            Metadata(document, "checksum_sha256") ?? throw new ManualJournalException(ManualJournalReasonCodes.InvalidEvidence,
                $"Evidence document '{document.Title}' does not have a verified content hash."))).OrderBy(x => x.DocumentId).ToArray();
        return new(evidence, ComputeDraftHash(input, evidence));
    }

    private static void AddMaterial(ManualJournalDraft draft, ManualJournalDraftInput input, Material material, DateTime now)
    {
        var lineNo = 0;
        foreach (var line in input.Lines)
        {
            var draftLine = new ManualJournalDraftLine(Guid.NewGuid(), draft.CompanyId, draft.Id, line.FinanceAccountId,
                ++lineNo, line.DebitAmount, line.CreditAmount, draft.Currency, line.Description, line.CostCenterId,
                Serialize(line.TaxFacts), Serialize(line.DimensionFacts));
            foreach (var memberId in (line.DimensionMemberIds ?? []).Distinct())
                draftLine.DimensionAssignments.Add(new ManualJournalDraftLineDimension(Guid.NewGuid(), draft.CompanyId,
                    draftLine.Id, memberId));
            draft.Lines.Add(draftLine);
        }
        foreach (var evidence in material.Evidence)
            draft.EvidenceLinks.Add(new ManualJournalEvidenceLink(Guid.NewGuid(), draft.CompanyId, draft.Id,
                evidence.DocumentId, evidence.ContentHash, evidence.Title, now));
    }

    private static ManualJournalDraftInput ToInput(ManualJournalDraft draft) => new(draft.FiscalPeriodId,
        draft.VoucherSeriesCode, draft.DocumentDate, draft.PostingDate, draft.Explanation, draft.Currency,
        draft.Lines.OrderBy(x => x.LineNumber).Select(x => new ManualJournalLineInput(x.FinanceAccountId, x.DebitAmount,
            x.CreditAmount, x.Description, x.CostCenterId, AccountingPostingService.ParseFacts(x.TaxFactsJson),
            AccountingPostingService.ParseFacts(x.DimensionFactsJson),
            x.DimensionAssignments.Select(y => y.DimensionMemberId).OrderBy(y => y).ToArray())).ToArray(),
        draft.EvidenceLinks.Select(x => x.DocumentId).ToArray(), draft.OriginalLedgerEntryId, draft.CorrectionReason,
        DeserializeSourceInputs(draft.SourceReferencesJson));

    private static ProposedAccountingEntry ToProposed(ManualJournalDraft draft, Guid actorUserId, string idempotencyKey, bool requiresApproval) =>
        new(draft.CompanyId, draft.FiscalPeriodId, draft.VoucherSeriesCode, draft.DocumentDate, draft.PostingDate,
            draft.OriginalLedgerEntryId.HasValue ? LedgerPostingTypeValues.Adjustment : LedgerPostingTypeValues.Manual,
            draft.Explanation, SourceType, draft.Id.ToString("N"), draft.Version.ToString(CultureInfo.InvariantCulture),
            idempotencyKey, draft.Lines.OrderBy(x => x.LineNumber).Select(x => new ProposedAccountingLine(
                x.FinanceAccountId, x.DebitAmount, x.CreditAmount, x.Currency, x.Description, x.CostCenterId,
                AccountingPostingService.ParseFacts(x.TaxFactsJson), AccountingPostingService.ParseFacts(x.DimensionFactsJson),
                DimensionMemberIds: x.DimensionAssignments.Select(y => y.DimensionMemberId).OrderBy(y => y).ToArray())).ToArray(),
            actorUserId, draft.ApprovalRequestId, requiresApproval,
            new Dictionary<string, string> { ["manualJournalDraftId"] = draft.Id.ToString("N"), ["draftPayloadHash"] = draft.PayloadHash },
            "post_manual_journal", draft.PayloadHash,
            draft.EvidenceLinks.Select(x => new ProposedAccountingEvidence(x.DocumentId, x.ContentHash, x.Title)).ToArray(),
            draft.OriginalLedgerEntryId, draft.CorrectionReason);

    private async Task<ManualJournalOperation?> FindOperationAsync(Guid companyId, string idempotencyKey, CancellationToken cancellationToken) =>
        await _dbContext.ManualJournalOperations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == idempotencyKey.Trim(), cancellationToken);

    private async Task<ManualJournalDraftDto> ReplayDraftAsync(ManualJournalOperation operation, string requestHash, CancellationToken cancellationToken)
    {
        EnsureReplay(operation, requestHash);
        return await GetAsync(new(operation.CompanyId, operation.DraftId), cancellationToken);
    }

    private static void EnsureReplay(ManualJournalOperation operation, string payloadHash)
    {
        if (!string.Equals(operation.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            throw new ManualJournalException(ManualJournalReasonCodes.IdempotencyConflict,
                "This request identity was already used with different manual journal content.", true);
    }

    private static void EnsureVersion(ManualJournalDraft draft, long expectedVersion)
    {
        if (draft.Version != expectedVersion)
            throw new ManualJournalException(ManualJournalReasonCodes.VersionConflict,
                $"This draft is now version {draft.Version}. Reload it before continuing.", true, draft.Version);
    }

    private static void EnsureEditable(ManualJournalDraft draft)
    {
        if (draft.Status is ManualJournalDraftStatusValues.Posted or ManualJournalDraftStatusValues.Discarded)
            throw new ManualJournalException(ManualJournalReasonCodes.NotEditable, "Posted or discarded manual journals cannot be changed.");
    }

    private static bool ApprovalMatches(ApprovalRequest approval, ManualJournalDraft draft) =>
        ReadApprovalVersion(approval) == draft.Version && string.Equals(ReadApprovalHash(approval), draft.PayloadHash, StringComparison.OrdinalIgnoreCase);

    private static long ReadApprovalVersion(ApprovalRequest approval) =>
        approval.ThresholdContext.TryGetValue("sourceVersion", out var node) && long.TryParse(NodeText(node), CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static string? ReadApprovalHash(ApprovalRequest approval) =>
        approval.ThresholdContext.TryGetValue("payloadHash", out var node) ? NodeText(node) : null;
    private static string? NodeText(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToString();

    private async Task AuditAsync(Guid companyId, Guid actorId, string action, Guid draftId, string summary,
        string? correlationId, DateTime occurredUtc, CancellationToken cancellationToken, Guid? approvalId = null) =>
        await _auditWriter.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actorId, action,
            AuditTargetTypes.ManualJournalDraft, draftId.ToString("N"), AuditEventOutcomes.Succeeded, summary,
            ["manual_journal_draft"], new Dictionary<string, string?> { ["approvalRequestId"] = approvalId?.ToString("N") },
            correlationId, occurredUtc), cancellationToken);

    private static string HashRequest(ManualJournalDraftInput input) => HashText(JsonSerializer.Serialize(new
    {
        input.FiscalPeriodId, input.VoucherSeriesCode, input.DocumentDate, input.PostingDate, input.Explanation, input.Currency,
        Lines = input.Lines.Select(x => new { x.FinanceAccountId, x.DebitAmount, x.CreditAmount, x.Description, x.CostCenterId,
            TaxFacts = Sorted(x.TaxFacts), DimensionFacts = Sorted(x.DimensionFacts),
            DimensionMemberIds = (x.DimensionMemberIds ?? []).OrderBy(y => y) }),
        Evidence = input.EvidenceDocumentIds.OrderBy(x => x), input.OriginalLedgerEntryId, input.CorrectionReason,
        SourceRecords = NormalizeSources(input.SourceRecords)
    }));
    private static string ComputeDraftHash(ManualJournalDraftInput input, IReadOnlyList<Evidence> evidence) => HashText(JsonSerializer.Serialize(new
    {
        Request = HashRequest(input), Evidence = evidence.Select(x => new { x.DocumentId, x.ContentHash })
    }));
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static SortedDictionary<string, string> Sorted(IReadOnlyDictionary<string, string>? value) =>
        new((value ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal);
    private static string? Serialize(IReadOnlyDictionary<string, string>? facts) => facts is null || facts.Count == 0 ? null : JsonSerializer.Serialize(Sorted(facts));
    private static string SerializeSources(IReadOnlyList<ManualJournalSourceReferenceInput>? sources) =>
        JsonSerializer.Serialize(NormalizeSources(sources));
    private static IReadOnlyList<ManualJournalSourceReferenceDto> DeserializeSources(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<ManualJournalSourceReferenceDto[]>(json) ?? [];
    private static IReadOnlyList<ManualJournalSourceReferenceInput> DeserializeSourceInputs(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<ManualJournalSourceReferenceInput[]>(json) ?? [];
    private static IReadOnlyList<ManualJournalSourceReferenceInput> NormalizeSources(IReadOnlyList<ManualJournalSourceReferenceInput>? sources) =>
        (sources ?? []).OrderBy(x => x.SourceType, StringComparer.Ordinal).ThenBy(x => x.RecordId)
            .Select(x => new ManualJournalSourceReferenceInput(x.SourceType.Trim().ToLowerInvariant(), x.RecordId, x.SourceVersion.Trim())).ToArray();
    private static string? Metadata(CompanyKnowledgeDocument document, string key) =>
        document.Metadata.TryGetValue(key, out var node) ? NodeText(node) : null;
    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static void ValidateCommand(Guid companyId, Guid actorId, string key)
    {
        if (companyId == Guid.Empty || actorId == Guid.Empty) throw NotFound();
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 200)
            throw new ManualJournalException(ManualJournalReasonCodes.IdempotencyConflict, "A stable request identity is required.");
    }
    private static ManualJournalException NotFound() => new(ManualJournalReasonCodes.NotFound, "The manual journal could not be found.");
    private sealed record Evidence(Guid DocumentId, string Title, string ContentHash);
    private sealed record Material(IReadOnlyList<Evidence> Evidence, string PayloadHash);
}
