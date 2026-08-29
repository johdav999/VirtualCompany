using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class PaymentBatchService : IPaymentBatchService
{
    private const string ApprovalNotice = "Internal approval only — nothing is sent to a bank and the obligations are not marked paid.";
    private readonly VirtualCompanyDbContext _db;
    private readonly IPaymentBatchEligibilityPolicy _policy;
    private readonly IAuditEventWriter _audit;
    private readonly ICompanyContextAccessor? _companyContext;
    private readonly PaymentBatchPolicyOptions _options;
    private readonly PaymentBatchTelemetry _telemetry;
    private readonly TimeProvider _time;

    public PaymentBatchService(VirtualCompanyDbContext db, IPaymentBatchEligibilityPolicy policy,
        IAuditEventWriter audit, ICompanyContextAccessor? companyContext,
        IOptions<PaymentBatchPolicyOptions> options, PaymentBatchTelemetry telemetry, TimeProvider time)
    { _db = db; _policy = policy; _audit = audit; _companyContext = companyContext; _options = options.Value; _telemetry = telemetry; _time = time; }

    public async Task<PaymentBeneficiaryProfileDto> RegisterBeneficiaryAsync(
        RegisterPaymentBeneficiaryCommand command, CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId);
        ValidateBeneficiaryDestination(command.Rail, command.Destination, command.Currency);
        return await ExecuteInTransactionAsync(async () =>
        {
            var partyType = NormalizePartyType(command.PartyType);
            var partyExists = await _db.FinanceCounterparties.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == command.CompanyId && x.Id == command.PartyId &&
                    x.CounterpartyType == partyType && x.MergedIntoCounterpartyId == null, cancellationToken);
            if (!partyExists) throw Error(PaymentBatchReasonCodes.BeneficiaryMissing,
                "The beneficiary owner was not found in the active company.");
            var current = await _db.PaymentBeneficiaryProfiles.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.PartyType == partyType &&
                    x.PartyId == command.PartyId && x.IsCurrent, cancellationToken);
            if (current is not null && current.VerificationEvidenceHash.Equals(command.VerificationEvidenceHash, StringComparison.OrdinalIgnoreCase) &&
                current.Destination == command.Destination.Trim() && current.Rail == PaymentRails.Normalize(command.Rail) &&
                current.DisplayName == command.DisplayName.Trim() && current.MaskedDestination == command.MaskedDestination.Trim() &&
                current.Currency == command.Currency.Trim().ToUpperInvariant() &&
                current.VerificationEvidenceReference == command.VerificationEvidenceReference.Trim())
                return MapProfile(current);
            var affectedBatchIds = current is null ? [] : await (
                from snapshot in _db.PaymentBeneficiarySnapshots.IgnoreQueryFilters().AsNoTracking()
                join link in _db.PaymentBatchObligations.IgnoreQueryFilters().AsNoTracking()
                    on new { snapshot.CompanyId, Id = snapshot.ObligationLinkId }
                    equals new { link.CompanyId, link.Id }
                where snapshot.CompanyId == command.CompanyId && snapshot.ProfileId == current.Id &&
                      link.RemovedUtc == null
                select link.BatchId).Distinct().ToArrayAsync(cancellationToken);
            var nextVersion = (current?.Version ?? 0) + 1; var now = Now(); current?.Supersede(now);
            var profile = new PaymentBeneficiaryProfile(Guid.NewGuid(), command.CompanyId, partyType,
                command.PartyId, command.DisplayName, command.Rail, command.Destination,
                command.MaskedDestination, command.Currency, nextVersion,
                command.VerificationEvidenceReference, command.VerificationEvidenceHash,
                command.ActorUserId, now);
            _db.PaymentBeneficiaryProfiles.Add(profile);
            if (affectedBatchIds.Length > 0)
            {
                var affectedBatches = await _db.PaymentBatches.IgnoreQueryFilters()
                    .Where(x => x.CompanyId == command.CompanyId && affectedBatchIds.Contains(x.Id) &&
                        x.Status != PaymentBatchStatuses.Rejected && x.Status != PaymentBatchStatuses.Cancelled)
                    .ToListAsync(cancellationToken);
                foreach (var batch in affectedBatches)
                {
                    await InvalidateApprovalIfNeededAsync(batch, command.ActorUserId,
                        "Verified beneficiary details were versioned.", now, cancellationToken);
                    batch.InvalidateEvidence(batch.Version, command.ActorUserId, now);
                    await WriteAuditAsync(command.CompanyId, batch.Id, AuditEventActions.PaymentBatchChanged,
                        AuditEventOutcomes.Blocked, "Verified beneficiary details changed; approval and generated instructions were invalidated.",
                        command.ActorUserId, command.CorrelationId,
                        new Dictionary<string, string?> { ["reasonCode"] = PaymentBatchReasonCodes.BeneficiaryChanged, ["beneficiaryProfileVersion"] = nextVersion.ToString() }, cancellationToken);
                }
            }
            await _db.SaveChangesAsync(cancellationToken);
            await WriteAuditAsync(command.CompanyId, profile.Id, "finance.payment_beneficiary.verified",
                AuditEventOutcomes.Approved, "Beneficiary payment details were verified and versioned.",
                command.ActorUserId, command.CorrelationId, new Dictionary<string, string?> { ["partyType"] = partyType, ["version"] = nextVersion.ToString(), ["rail"] = profile.Rail }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken); return MapProfile(profile);
        }, cancellationToken);
    }

    public async Task<PaymentBatchListDto> ListAsync(ListPaymentBatchesQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId); var limit = Math.Clamp(query.Limit <= 0 ? 100 : query.Limit, 1, 500);
        var status = NormalizeOptional(query.Status);
        var batches = await _db.PaymentBatches.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && (status == null || x.Status == status))
            .OrderByDescending(x => x.UpdatedUtc).Take(limit).ToListAsync(cancellationToken);
        var summaries = new List<PaymentBatchSummaryDto>(batches.Count);
        foreach (var batch in batches) summaries.Add(await MapSummaryAsync(batch, false, cancellationToken));
        var planned = summaries.Where(x => x.Status is not (PaymentBatchStatuses.Rejected or PaymentBatchStatuses.Cancelled))
            .SelectMany(x => x.Totals).GroupBy(x => x.Currency).Select(x => new PaymentBatchTotalDto(
                x.Key, x.Sum(y => y.Amount), x.Select(y => y.AvailableCash).FirstOrDefault(y => y.HasValue), x.All(y => y.HasSufficientCash))).ToArray();
        return new(summaries, summaries.Count(x => x.Status == PaymentBatchStatuses.Draft),
            summaries.Count(x => x.Status == PaymentBatchStatuses.Draft),
            summaries.Count(x => x.Status == PaymentBatchStatuses.AwaitingApproval), planned);
    }

    public Task<PaymentBatchDetailDto?> GetAsync(GetPaymentBatchQuery query, CancellationToken cancellationToken)
    { EnsureTenant(query.CompanyId); return GetDetailAsync(query.CompanyId, query.BatchId, false, cancellationToken); }

    public async Task<IReadOnlyList<EligiblePaymentObligationDto>> ListEligibleObligationsAsync(
        ListEligiblePaymentObligationsQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId); var now = Now(); var cash = await LoadCashAsync(query.CompanyId, cancellationToken);
        var candidates = await LoadCandidateSetAsync(query.CompanyId, Math.Clamp(query.Limit, 1, 500), cancellationToken);
        var result = new List<EligiblePaymentObligationDto>();
        foreach (var candidate in candidates)
        {
            var duplicate = await HasDuplicateAsync(query.CompanyId, candidate.ObligationType, candidate.SourceId, null, cancellationToken);
            var decision = Evaluate(candidate, candidate.DueDate, cash.GetValueOrDefault(candidate.Currency), duplicate, true, now, normalizeInvalidDate: true);
            result.Add(new(candidate.ObligationType, candidate.SourceId, candidate.SourceReference,
                candidate.Beneficiary.DisplayName, candidate.Beneficiary.Rail, candidate.Beneficiary.MaskedDestination,
                candidate.Amount, candidate.Currency, candidate.DueDate, candidate.PaymentReference,
                decision.IsEligible && candidate.BaseEligible,
                candidate.BaseEligible ? decision.ReasonCode : PaymentBatchReasonCodes.ObligationIneligible,
                candidate.BaseEligible ? decision.Explanation : candidate.BaseExplanation,
                decision.RecommendedExecutionDate));
        }
        return result.OrderBy(x => x.DueDate).ThenBy(x => x.SourceReference).ToArray();
    }

    public async Task<PaymentBatchDetailDto> CreateAsync(CreatePaymentBatchCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId); var requestHash = Hash($"{command.CompanyId:N}|{command.Name.Trim()}|{command.PlannedExecutionDate:yyyy-MM-dd}");
        var replay = await ReplayAsync(command.CompanyId, PaymentBatchOperationTypes.Create, command.IdempotencyKey, requestHash, cancellationToken);
        if (replay is not null) return replay;
        var id = Guid.NewGuid(); var now = Now(); var reference = $"PB-{now:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}";
        var batch = new PaymentBatch(id, command.CompanyId, reference, command.Name,
            command.PlannedExecutionDate, command.IdempotencyKey, requestHash, command.ActorUserId, now);
        _db.PaymentBatches.Add(batch); AddOperation(batch, PaymentBatchOperationTypes.Create,
            command.IdempotencyKey, requestHash, command.ActorUserId, now);
        await _db.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(command.CompanyId, batch.Id, AuditEventActions.PaymentBatchCreated,
            AuditEventOutcomes.Succeeded, "A native payment batch was created without any bank side effect.",
            command.ActorUserId, command.CorrelationId, new Dictionary<string, string?> { ["status"] = batch.Status }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken); _telemetry.Operation(PaymentBatchOperationTypes.Create, batch.Status);
        return (await GetDetailAsync(command.CompanyId, batch.Id, false, cancellationToken))!;
    }

    public async Task<PaymentBatchDetailDto> AddObligationAsync(AddPaymentBatchObligationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId); var type = NormalizeObligationType(command.ObligationType);
        var requestHash = Hash($"{command.BatchId:N}|{type}|{command.SourceId:N}|{command.ExpectedVersion}");
        var replay = await ReplayAsync(command.CompanyId, PaymentBatchOperationTypes.AddObligation,
            command.IdempotencyKey, requestHash, cancellationToken); if (replay is not null) return replay;
        await ExecuteInTransactionAsync(async () =>
        {
            var batch = await LoadBatchAsync(command.CompanyId, command.BatchId, true, cancellationToken); EnsureVersion(batch, command.ExpectedVersion);
            await InvalidateApprovalIfNeededAsync(batch, command.ActorUserId, "Batch contents changed.", Now(), cancellationToken);
            var candidate = await LoadCandidateAsync(command.CompanyId, type, command.SourceId, cancellationToken)
                ?? throw Error(PaymentBatchReasonCodes.ObligationNotFound, "The payment obligation was not found in the active company.");
            var existing = await _db.PaymentBatchObligations.IgnoreQueryFilters().AnyAsync(x =>
                x.CompanyId == command.CompanyId && x.BatchId == batch.Id && x.ObligationType == type &&
                x.SourceId == command.SourceId && x.RemovedUtc == null, cancellationToken);
            var duplicate = existing || await HasDuplicateAsync(command.CompanyId, type, command.SourceId, batch.Id, cancellationToken);
            var cash = await LoadCashAsync(command.CompanyId, cancellationToken);
            var decision = Evaluate(candidate, batch.PlannedExecutionDate, cash.GetValueOrDefault(candidate.Currency), duplicate, true, Now(), normalizeInvalidDate: false);
            if (!candidate.BaseEligible || !decision.IsEligible)
                throw Error(candidate.BaseEligible ? decision.ReasonCode : PaymentBatchReasonCodes.ObligationIneligible,
                    candidate.BaseEligible ? decision.Explanation : candidate.BaseExplanation);
            var now = Now(); batch.MarkContentChanged(command.ExpectedVersion, command.ActorUserId, now);
            var link = new PaymentBatchObligationLink(Guid.NewGuid(), command.CompanyId, batch.Id,
                type, candidate.SourceId, candidate.SourceReference, candidate.SourceVersion,
                candidate.SourceHash, candidate.Amount, candidate.Currency, candidate.DueDate,
                candidate.PaymentReference, command.ActorUserId, now);
            var beneficiary = candidate.Beneficiary;
            var snapshot = new PaymentBeneficiarySnapshot(Guid.NewGuid(), command.CompanyId, link.Id,
                beneficiary.ProfileId, beneficiary.Version, beneficiary.DisplayName, beneficiary.Rail,
                beneficiary.Destination, beneficiary.MaskedDestination, beneficiary.EvidenceReference,
                beneficiary.EvidenceHash, beneficiary.VerifiedUtc, now);
            _db.AddRange(link, snapshot); AddOperation(batch, PaymentBatchOperationTypes.AddObligation,
                command.IdempotencyKey, requestHash, command.ActorUserId, now);
            await WriteAuditAsync(command.CompanyId, batch.Id, AuditEventActions.PaymentBatchChanged,
                AuditEventOutcomes.Succeeded, "An eligible obligation and verified beneficiary snapshot were added.",
                command.ActorUserId, command.CorrelationId, new Dictionary<string, string?> { ["obligationType"] = type, ["sourceId"] = candidate.SourceId.ToString("D") }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken); return 0;
        }, cancellationToken);
        _telemetry.Operation(PaymentBatchOperationTypes.AddObligation, PaymentBatchStatuses.Draft);
        return (await GetDetailAsync(command.CompanyId, command.BatchId, false, cancellationToken))!;
    }

    public async Task<PaymentBatchDetailDto> RemoveObligationAsync(RemovePaymentBatchObligationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId); var requestHash = Hash($"{command.BatchId:N}|{command.ObligationLinkId:N}|{command.ExpectedVersion}");
        var replay = await ReplayAsync(command.CompanyId, PaymentBatchOperationTypes.RemoveObligation,
            command.IdempotencyKey, requestHash, cancellationToken); if (replay is not null) return replay;
        await ExecuteInTransactionAsync(async () =>
        {
            var batch = await LoadBatchAsync(command.CompanyId, command.BatchId, true, cancellationToken); EnsureVersion(batch, command.ExpectedVersion);
            var link = await _db.PaymentBatchObligations.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == command.CompanyId && x.BatchId == batch.Id && x.Id == command.ObligationLinkId && x.RemovedUtc == null, cancellationToken)
                ?? throw Error(PaymentBatchReasonCodes.ObligationNotFound, "The active obligation link was not found.");
            var now = Now(); await InvalidateApprovalIfNeededAsync(batch, command.ActorUserId, "Batch contents changed.", now, cancellationToken);
            link.Remove(command.ActorUserId, now); batch.MarkContentChanged(command.ExpectedVersion, command.ActorUserId, now);
            AddOperation(batch, PaymentBatchOperationTypes.RemoveObligation, command.IdempotencyKey, requestHash, command.ActorUserId, now);
            await WriteAuditAsync(command.CompanyId, batch.Id, AuditEventActions.PaymentBatchChanged,
                AuditEventOutcomes.Succeeded, "An obligation was removed and generated evidence was invalidated.",
                command.ActorUserId, command.CorrelationId, new Dictionary<string, string?> { ["obligationLinkId"] = link.Id.ToString("D") }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken); return 0;
        }, cancellationToken);
        _telemetry.Operation(PaymentBatchOperationTypes.RemoveObligation, PaymentBatchStatuses.Draft);
        return (await GetDetailAsync(command.CompanyId, command.BatchId, false, cancellationToken))!;
    }

    public async Task<PaymentBatchPreviewDto> PreviewAsync(GetPaymentBatchQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId); var batch = await LoadBatchAsync(query.CompanyId, query.BatchId, false, cancellationToken);
        var evaluation = await EvaluateBatchAsync(batch, cancellationToken);
        return new(batch.Id, batch.Version, evaluation.Issues.Count == 0, evaluation.RecommendedExecutionDate,
            evaluation.Totals, evaluation.Issues.Select(MapPreviewIssue).ToArray(), ApprovalNotice);
    }

    public Task<PaymentBatchDetailDto> ValidateAsync(ValidatePaymentBatchCommand command, CancellationToken cancellationToken) =>
        GenerateAsync(command.CompanyId, command.BatchId, command.ExpectedVersion, command.IdempotencyKey,
            command.ActorUserId, command.CorrelationId, PaymentBatchOperationTypes.Validate, cancellationToken);

    public Task<PaymentBatchDetailDto> RegenerateAsync(RegeneratePaymentBatchCommand command, CancellationToken cancellationToken) =>
        GenerateAsync(command.CompanyId, command.BatchId, command.ExpectedVersion, command.IdempotencyKey,
            command.ActorUserId, command.CorrelationId, PaymentBatchOperationTypes.Regenerate, cancellationToken);

    private async Task<PaymentBatchDetailDto> GenerateAsync(Guid companyId, Guid batchId, long expectedVersion,
        string idempotencyKey, Guid actor, string? correlationId, string operation, CancellationToken cancellationToken)
    {
        EnsureCommand(companyId, actor); var requestHash = Hash($"{batchId:N}|{expectedVersion}|{operation}");
        var replay = await ReplayAsync(companyId, operation, idempotencyKey, requestHash, cancellationToken); if (replay is not null) return replay;
        await ExecuteInTransactionAsync(async () =>
        {
            var batch = await LoadBatchAsync(companyId, batchId, true, cancellationToken); EnsureVersion(batch, expectedVersion);
            var now = Now(); await InvalidateApprovalIfNeededAsync(batch, actor, "Instructions were regenerated.", now, cancellationToken);
            var evaluation = await EvaluateBatchAsync(batch, cancellationToken);
            var evaluatedVersion = batch.Version;
            if (evaluation.Issues.Count > 0)
            {
                var result = PersistValidation(batch, evaluatedVersion, batch.InstructionSetVersion,
                    evaluation, actor, now); batch.MarkValidationFailed(batch.Version, result.Id, actor, now);
                AddOperation(batch, operation, idempotencyKey, requestHash, actor, now);
                await _db.SaveChangesAsync(cancellationToken); _telemetry.Validated(evaluation.Obligations.Count, false);
                return 0;
            }
            await SupersedeArtifactsAsync(companyId, batch.Id, now, cancellationToken);
            var setVersion = batch.BeginInstructionSet(batch.Version, actor, now);
            var instructions = CreateInstructions(batch, evaluation, setVersion, now); _db.PaymentInstructions.AddRange(instructions);
            var artifactContent = JsonSerializer.Serialize(new
            {
                schemaVersion = "2026-08-28", batchId = batch.Id, batch.Reference,
                instructionSetVersion = setVersion, internalApprovalOnly = true,
                instructions = instructions.Select(x => new { x.Sequence, x.ExecutionDate, x.Amount, x.Currency,
                    x.PaymentReference, x.BeneficiaryName, x.Rail, x.Destination, x.SourceVersion, x.ContentHash })
            });
            var artifact = new PaymentBatchExportArtifact(Guid.NewGuid(), companyId, batch.Id, setVersion,
                "virtualcompany-payment-instruction-manifest-v1", "application/vnd.virtualcompany.payment-batch+json",
                artifactContent, Hash(artifactContent), now); _db.PaymentBatchExportArtifacts.Add(artifact);
            var validation = PersistValidation(batch, evaluatedVersion, setVersion, evaluation, actor, now);
            batch.MarkValidated(batch.Version, validation.Id, artifact.Id, actor, now);
            AddOperation(batch, operation, idempotencyKey, requestHash, actor, now);
            await WriteAuditAsync(companyId, batch.Id, AuditEventActions.PaymentBatchValidated,
                AuditEventOutcomes.Succeeded, "The batch was validated and immutable provider-neutral instructions were generated.",
                actor, correlationId, new Dictionary<string, string?> { ["instructionSetVersion"] = setVersion.ToString(), ["instructionCount"] = instructions.Count.ToString() }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken); _telemetry.Validated(evaluation.Obligations.Count, true); return 0;
        }, cancellationToken);
        _telemetry.Operation(operation, (await _db.PaymentBatches.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == batchId, cancellationToken)).Status);
        return (await GetDetailAsync(companyId, batchId, false, cancellationToken))!;
    }

    public async Task<PaymentBatchDetailDto> SubmitAsync(SubmitPaymentBatchCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId); var requestHash = Hash($"{command.BatchId:N}|{command.ExpectedVersion}|submit");
        var replay = await ReplayAsync(command.CompanyId, PaymentBatchOperationTypes.Submit, command.IdempotencyKey, requestHash, cancellationToken); if (replay is not null) return replay;
        PaymentBatchException? staleError = null;
        await ExecuteInTransactionAsync(async () =>
        {
            var batch = await LoadBatchAsync(command.CompanyId, command.BatchId, true, cancellationToken); EnsureVersion(batch, command.ExpectedVersion);
            if (batch.Status != PaymentBatchStatuses.Validated || !batch.CurrentValidationResultId.HasValue)
                throw Error(PaymentBatchReasonCodes.ValidationRequired, "Validate the current instruction set before submitting it.");
            var evaluation = await EvaluateBatchAsync(batch, cancellationToken);
            var validation = await _db.PaymentBatchValidationResults.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == batch.CurrentValidationResultId, cancellationToken);
            if (evaluation.Issues.Count > 0 || validation.SourceSetHash != evaluation.SourceSetHash)
            {
                var invalidatedUtc = Now();
                await InvalidateApprovalIfNeededAsync(batch, command.ActorUserId,
                    "Source or beneficiary evidence changed before approval submission.", invalidatedUtc, cancellationToken);
                batch.MarkContentChanged(batch.Version, command.ActorUserId, invalidatedUtc);
                await WriteAuditAsync(command.CompanyId, batch.Id, AuditEventActions.PaymentBatchChanged,
                    AuditEventOutcomes.Blocked, "Submission was blocked and generated artifacts were invalidated because source evidence changed.",
                    command.ActorUserId, command.CorrelationId, new Dictionary<string, string?> { ["reasonCode"] = PaymentBatchReasonCodes.ApprovalStale }, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                staleError = Error(PaymentBatchReasonCodes.ApprovalStale,
                    "The obligation or beneficiary evidence changed. Regenerate the batch before approval.", true, batch.Version);
                return 0;
            }
            var totals = evaluation.Totals; var highValue = totals.Any(x => x.Amount >= _options.DualApprovalThreshold);
            var steps = highValue
                ? new[] { new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, _options.ApprovalRole), new ApprovalStepDefinition(2, ApprovalStepApproverType.Role, _options.ApprovalRole) }
                : new[] { new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, _options.ApprovalRole) };
            var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), command.CompanyId,
                ApprovalTargetEntityType.PaymentBatch, batch.Id, "user", command.ActorUserId,
                "payment_batch_approval", new Dictionary<string, JsonNode?>
                {
                    ["batchVersion"] = batch.Version, ["instructionSetVersion"] = batch.InstructionSetVersion,
                    ["sourceSetHash"] = validation.SourceSetHash, ["dualApproval"] = highValue
                }, null, null, steps);
            _db.ApprovalRequests.Add(approval); var now = Now();
            var binding = new PaymentBatchApprovalBinding(Guid.NewGuid(), command.CompanyId, batch.Id,
                approval.Id, batch.InstructionSetVersion, validation.SourceSetHash, command.ActorUserId, now);
            _db.PaymentBatchApprovalBindings.Add(binding); batch.Submit(batch.Version, approval.Id, command.ActorUserId, now);
            AddOperation(batch, PaymentBatchOperationTypes.Submit, command.IdempotencyKey, requestHash, command.ActorUserId, now);
            await WriteAuditAsync(command.CompanyId, batch.Id, AuditEventActions.PaymentBatchApprovalRequested,
                AuditEventOutcomes.Requested, "Internal approval was requested for the current instruction and evidence versions.",
                command.ActorUserId, command.CorrelationId, new Dictionary<string, string?> { ["approvalRequestId"] = approval.Id.ToString("D"), ["dualApproval"] = highValue.ToString() }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken); return 0;
        }, cancellationToken);
        if (staleError is not null)
        {
            _telemetry.Blocked(PaymentBatchOperationTypes.Submit, PaymentBatchReasonCodes.ApprovalStale);
            throw staleError;
        }
        _telemetry.Operation(PaymentBatchOperationTypes.Submit, PaymentBatchStatuses.AwaitingApproval);
        return (await GetDetailAsync(command.CompanyId, command.BatchId, false, cancellationToken))!;
    }

    public async Task<PaymentBatchDetailDto> ApproveAsync(DecidePaymentBatchCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId); var requestHash = Hash($"{command.BatchId:N}|{command.ExpectedVersion}|approve|{command.Comment.Trim()}");
        var replay = await ReplayAsync(command.CompanyId, PaymentBatchOperationTypes.Approve, command.IdempotencyKey, requestHash, cancellationToken); if (replay is not null) return replay;
        PaymentBatchException? staleError = null;
        await ExecuteInTransactionAsync(async () =>
        {
            var batch = await LoadBatchAsync(command.CompanyId, command.BatchId, true, cancellationToken); EnsureVersion(batch, command.ExpectedVersion);
            if (batch.Status != PaymentBatchStatuses.AwaitingApproval || !batch.ApprovalRequestId.HasValue)
                throw Error(PaymentBatchReasonCodes.InvalidLifecycle, "The payment batch is not awaiting approval.");
            if (batch.CreatedByUserId == command.ActorUserId || batch.SubmittedByUserId == command.ActorUserId)
                throw Error(PaymentBatchReasonCodes.SegregationOfDuties, "The batch creator or submitter cannot approve its instructions.");
            var approval = await _db.ApprovalRequests.IgnoreQueryFilters().Include(x => x.Steps)
                .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == batch.ApprovalRequestId, cancellationToken);
            if (approval.Steps.Any(x => x.DecidedByUserId == command.ActorUserId))
                throw Error(PaymentBatchReasonCodes.SegregationOfDuties, "A second approval must be completed by a different finance approver.");
            var binding = await _db.PaymentBatchApprovalBindings.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == command.CompanyId && x.BatchId == batch.Id && x.ApprovalRequestId == approval.Id, cancellationToken);
            var evaluation = await EvaluateBatchAsync(batch, cancellationToken);
            if (evaluation.Issues.Count > 0 || evaluation.SourceSetHash != binding.SourceSetHash)
            {
                var invalidatedUtc = Now();
                await InvalidateApprovalIfNeededAsync(batch, command.ActorUserId,
                    "Source or beneficiary evidence changed during approval.", invalidatedUtc, cancellationToken);
                batch.MarkContentChanged(batch.Version, command.ActorUserId, invalidatedUtc);
                await WriteAuditAsync(command.CompanyId, batch.Id, AuditEventActions.PaymentBatchChanged,
                    AuditEventOutcomes.Blocked, "Approval was blocked and its exact instruction evidence was marked stale.",
                    command.ActorUserId, command.CorrelationId, new Dictionary<string, string?> { ["reasonCode"] = PaymentBatchReasonCodes.ApprovalStale }, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                staleError = Error(PaymentBatchReasonCodes.ApprovalStale,
                    "Approval is stale because obligation or beneficiary evidence changed. Regenerate the batch.", true, batch.Version);
                return 0;
            }
            var step = approval.CurrentActionableStep ?? throw Error(PaymentBatchReasonCodes.InvalidLifecycle, "The approval has no actionable step.");
            approval.ApproveCurrentStep(step.Id, command.ActorUserId, command.Comment); var now = Now();
            if (approval.CanExecuteGuardedAction)
            {
                batch.Approve(batch.Version, command.ActorUserId, command.Comment, now); binding.MarkApproved(command.ActorUserId, command.Comment, now);
                var instructions = await _db.PaymentInstructions.IgnoreQueryFilters().Where(x => x.CompanyId == command.CompanyId && x.BatchId == batch.Id && x.IsCurrent).ToListAsync(cancellationToken);
                foreach (var instruction in instructions) instruction.Approve(now);
            }
            AddOperation(batch, PaymentBatchOperationTypes.Approve, command.IdempotencyKey, requestHash, command.ActorUserId, now);
            await WriteAuditAsync(command.CompanyId, batch.Id, AuditEventActions.PaymentBatchApproved,
                approval.CanExecuteGuardedAction ? AuditEventOutcomes.Approved : AuditEventOutcomes.Pending,
                approval.CanExecuteGuardedAction ? "The exact internal instruction set was approved. Nothing was sent to a bank." : "One approval step completed; another distinct approver is required.",
                command.ActorUserId, command.CorrelationId, new Dictionary<string, string?> { ["approvalRequestId"] = approval.Id.ToString("D"), ["approvalStatus"] = approval.Status.ToStorageValue() }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken); return 0;
        }, cancellationToken);
        if (staleError is not null)
        {
            _telemetry.Blocked(PaymentBatchOperationTypes.Approve, PaymentBatchReasonCodes.ApprovalStale);
            throw staleError;
        }
        var detail = (await GetDetailAsync(command.CompanyId, command.BatchId, false, cancellationToken))!;
        _telemetry.Operation(PaymentBatchOperationTypes.Approve, detail.Summary.Status); return detail;
    }

    public async Task<PaymentBatchDetailDto> RejectAsync(DecidePaymentBatchCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId); var requestHash = Hash($"{command.BatchId:N}|{command.ExpectedVersion}|reject|{command.Comment.Trim()}");
        var replay = await ReplayAsync(command.CompanyId, PaymentBatchOperationTypes.Reject, command.IdempotencyKey, requestHash, cancellationToken); if (replay is not null) return replay;
        await ExecuteInTransactionAsync(async () =>
        {
            var batch = await LoadBatchAsync(command.CompanyId, command.BatchId, true, cancellationToken); EnsureVersion(batch, command.ExpectedVersion);
            if (batch.Status != PaymentBatchStatuses.AwaitingApproval || !batch.ApprovalRequestId.HasValue) throw Error(PaymentBatchReasonCodes.InvalidLifecycle, "The payment batch is not awaiting approval.");
            var approval = await _db.ApprovalRequests.IgnoreQueryFilters().Include(x => x.Steps).SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == batch.ApprovalRequestId, cancellationToken);
            var binding = await _db.PaymentBatchApprovalBindings.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == command.CompanyId && x.BatchId == batch.Id && x.ApprovalRequestId == approval.Id, cancellationToken);
            var step = approval.CurrentActionableStep ?? throw Error(PaymentBatchReasonCodes.InvalidLifecycle, "The approval has no actionable step."); approval.RejectCurrentStep(step.Id, command.ActorUserId, command.Comment);
            var now = Now(); batch.Reject(batch.Version, command.ActorUserId, command.Comment, now); binding.MarkRejected(command.ActorUserId, command.Comment, now);
            AddOperation(batch, PaymentBatchOperationTypes.Reject, command.IdempotencyKey, requestHash, command.ActorUserId, now);
            await WriteAuditAsync(command.CompanyId, batch.Id, AuditEventActions.PaymentBatchRejected, AuditEventOutcomes.Rejected,
                "The internal payment instruction set was rejected and no bank action occurred.", command.ActorUserId, command.CorrelationId, null, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken); return 0;
        }, cancellationToken);
        _telemetry.Operation(PaymentBatchOperationTypes.Reject, PaymentBatchStatuses.Rejected);
        return (await GetDetailAsync(command.CompanyId, command.BatchId, false, cancellationToken))!;
    }

    public async Task<PaymentBatchDetailDto> CancelAsync(CancelPaymentBatchCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId); var requestHash = Hash($"{command.BatchId:N}|{command.ExpectedVersion}|cancel|{command.Reason.Trim()}");
        var replay = await ReplayAsync(command.CompanyId, PaymentBatchOperationTypes.Cancel, command.IdempotencyKey, requestHash, cancellationToken); if (replay is not null) return replay;
        await ExecuteInTransactionAsync(async () =>
        {
            var batch = await LoadBatchAsync(command.CompanyId, command.BatchId, true, cancellationToken); EnsureVersion(batch, command.ExpectedVersion); var now = Now();
            if (batch.ApprovalRequestId.HasValue)
            {
                var approval = await _db.ApprovalRequests.IgnoreQueryFilters().Include(x => x.Steps).SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == batch.ApprovalRequestId, cancellationToken);
                if (approval is { Status: ApprovalRequestStatus.Pending }) approval.MarkCancelled(command.Reason);
                var binding = await _db.PaymentBatchApprovalBindings.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.BatchId == batch.Id && x.ApprovalRequestId == batch.ApprovalRequestId, cancellationToken);
                if (binding is not null && binding.Status == PaymentBatchApprovalBindingStatuses.Pending) binding.MarkCancelled(command.ActorUserId, command.Reason, now);
            }
            batch.Cancel(batch.Version, command.ActorUserId, command.Reason, now); AddOperation(batch, PaymentBatchOperationTypes.Cancel, command.IdempotencyKey, requestHash, command.ActorUserId, now);
            await WriteAuditAsync(command.CompanyId, batch.Id, AuditEventActions.PaymentBatchCancelled, AuditEventOutcomes.Succeeded,
                "The payment batch was cancelled before any bank submission existed.", command.ActorUserId, command.CorrelationId, null, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken); return 0;
        }, cancellationToken);
        _telemetry.Operation(PaymentBatchOperationTypes.Cancel, PaymentBatchStatuses.Cancelled);
        return (await GetDetailAsync(command.CompanyId, command.BatchId, false, cancellationToken))!;
    }

    public async Task<PaymentBatchSendReadinessDto> CheckSendReadinessAsync(
        CheckPaymentBatchSendReadinessQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId); var batch = await LoadBatchAsync(query.CompanyId, query.BatchId, false, cancellationToken);
        var evaluation = await EvaluateBatchAsync(batch, cancellationToken);
        var binding = await _db.PaymentBatchApprovalBindings.IgnoreQueryFilters().AsNoTracking().OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.BatchId == batch.Id, cancellationToken);
        var approvedHash = binding?.Status == PaymentBatchApprovalBindingStatuses.Approved ? binding.SourceSetHash : null;
        var evidenceIsStale = binding is not null &&
            (binding.SourceSetHash != evaluation.SourceSetHash || evaluation.Issues.Count > 0);
        var ready = batch.Status == PaymentBatchStatuses.Approved && approvedHash is not null &&
            !evidenceIsStale;
        var reason = ready ? PaymentBatchReasonCodes.Ready : evidenceIsStale
            ? PaymentBatchReasonCodes.ApprovalStale : PaymentBatchReasonCodes.ApprovalPending;
        var explanation = ready
            ? "The internally approved instruction evidence is current. A later prompt must still perform provider submission and acknowledgement controls."
            : evidenceIsStale ? "Approval is stale because source or beneficiary evidence changed. Regenerate and approve again."
            : "The batch does not have completed internal approval.";
        if (!ready) _telemetry.Blocked("send_readiness", reason);
        return new(batch.Id, ready, reason, explanation, batch.InstructionSetVersion, approvedHash,
            evaluation.SourceSetHash, evaluation.Issues.Select(MapPreviewIssue).ToArray());
    }

    private async Task<BatchEvaluation> EvaluateBatchAsync(PaymentBatch batch, CancellationToken cancellationToken)
    {
        var obligations = await _db.PaymentBatchObligations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == batch.CompanyId && x.BatchId == batch.Id && x.RemovedUtc == null)
            .OrderBy(x => x.CreatedUtc).ToListAsync(cancellationToken);
        var snapshots = await _db.PaymentBeneficiarySnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == batch.CompanyId && obligations.Select(y => y.Id).Contains(x.ObligationLinkId))
            .ToDictionaryAsync(x => x.ObligationLinkId, cancellationToken);
        var cash = await LoadCashAsync(batch.CompanyId, cancellationToken); var issues = new List<EvaluationIssue>();
        var evaluated = new List<EvaluatedObligation>(); var now = Now();
        if (obligations.Count == 0) issues.Add(new(null, PaymentBatchReasonCodes.NoObligations, "Add at least one eligible obligation before validation."));
        foreach (var link in obligations)
        {
            if (!snapshots.TryGetValue(link.Id, out var snapshot)) { issues.Add(new(link.Id, PaymentBatchReasonCodes.BeneficiaryMissing, "The beneficiary snapshot is missing.")); continue; }
            var candidate = await LoadCandidateAsync(batch.CompanyId, link.ObligationType, link.SourceId, cancellationToken);
            if (candidate is null) { issues.Add(new(link.Id, PaymentBatchReasonCodes.SourceChanged, "The obligation source is no longer available.")); continue; }
            var sourceCurrent = candidate.SourceHash == link.SourceHash && candidate.SourceVersion == link.SourceVersion;
            if (!sourceCurrent) issues.Add(new(link.Id, PaymentBatchReasonCodes.SourceChanged, "The obligation changed after it was added."));
            var beneficiaryCurrent = snapshot.ProfileId.HasValue
                ? candidate.Beneficiary.ProfileId == snapshot.ProfileId && candidate.Beneficiary.Version == snapshot.ProfileVersion && candidate.Beneficiary.EvidenceHash == snapshot.VerificationEvidenceHash
                : candidate.Beneficiary.EvidenceHash == snapshot.VerificationEvidenceHash && candidate.Beneficiary.Version == snapshot.ProfileVersion;
            if (!beneficiaryCurrent) issues.Add(new(link.Id, PaymentBatchReasonCodes.BeneficiaryChanged, "Verified beneficiary details changed after the snapshot was created."));
            var duplicate = await HasDuplicateAsync(batch.CompanyId, link.ObligationType, link.SourceId, batch.Id, cancellationToken);
            var decision = Evaluate(candidate, batch.PlannedExecutionDate, cash.GetValueOrDefault(link.Currency), duplicate, sourceCurrent && beneficiaryCurrent, now, normalizeInvalidDate: false);
            if (!candidate.BaseEligible) issues.Add(new(link.Id, PaymentBatchReasonCodes.ObligationIneligible, candidate.BaseExplanation));
            else if (!decision.IsEligible && !issues.Any(x => x.ObligationLinkId == link.Id && x.ReasonCode == decision.ReasonCode))
                issues.Add(new(link.Id, decision.ReasonCode, decision.Explanation));
            evaluated.Add(new(link, snapshot, candidate, decision));
        }
        var totals = obligations.GroupBy(x => x.Currency).Select(x =>
        {
            var amount = x.Sum(y => y.Amount); var available = cash.GetValueOrDefault(x.Key);
            if (available is null && !issues.Any(i => i.ReasonCode == PaymentBatchReasonCodes.CashAvailabilityUnknown)) issues.Add(new(null, PaymentBatchReasonCodes.CashAvailabilityUnknown, $"Current cash availability is missing for {x.Key}."));
            else if (available < amount && !issues.Any(i => i.ReasonCode == PaymentBatchReasonCodes.InsufficientCash && i.Explanation.Contains(x.Key))) issues.Add(new(null, PaymentBatchReasonCodes.InsufficientCash, $"Available {x.Key} cash does not cover the batch total."));
            return new PaymentBatchTotalDto(x.Key, amount, available, available >= amount);
        }).OrderBy(x => x.Currency).ToArray();
        var sourceSetHash = Hash(string.Join('|', evaluated.OrderBy(x => x.Link.ObligationType).ThenBy(x => x.Link.SourceId)
            .Select(x => $"{x.Link.ObligationType}:{x.Link.SourceId:N}:{x.Candidate.SourceHash}:{x.Candidate.Beneficiary.ProfileId:N}:{x.Candidate.Beneficiary.Version}:{x.Candidate.Beneficiary.EvidenceHash}")));
        var recommended = evaluated.Count == 0 ? batch.PlannedExecutionDate : evaluated.Min(x => x.Decision.RecommendedExecutionDate);
        return new(obligations, evaluated, totals, issues, sourceSetHash, recommended);
    }

    private PaymentBatchValidationResult PersistValidation(PaymentBatch batch, long evaluatedVersion,
        int instructionSetVersion, BatchEvaluation evaluation, Guid actor, DateTime now)
    {
        var result = new PaymentBatchValidationResult(Guid.NewGuid(), batch.CompanyId, batch.Id,
            evaluatedVersion, instructionSetVersion, evaluation.Issues.Count == 0, evaluation.SourceSetHash,
            JsonSerializer.Serialize(evaluation.Totals.Select(x => new { x.Currency, x.Amount })),
            JsonSerializer.Serialize(evaluation.Totals.Select(x => new { x.Currency, x.AvailableCash, x.HasSufficientCash })), actor, now);
        _db.PaymentBatchValidationResults.Add(result);
        foreach (var issue in evaluation.Issues) _db.PaymentBatchValidationIssues.Add(new(
            Guid.NewGuid(), batch.CompanyId, result.Id, issue.ObligationLinkId,
            PaymentBatchValidationSeverities.Error, issue.ReasonCode, issue.Explanation, now));
        return result;
    }

    private static List<PaymentInstruction> CreateInstructions(PaymentBatch batch, BatchEvaluation evaluation,
        int setVersion, DateTime now)
    {
        var sequence = 0; var result = new List<PaymentInstruction>();
        foreach (var row in evaluation.Evaluated.OrderBy(x => x.Decision.RecommendedExecutionDate).ThenBy(x => x.Link.SourceReference))
        {
            sequence++; var execution = batch.PlannedExecutionDate <= row.Decision.RecommendedExecutionDate ? batch.PlannedExecutionDate : row.Decision.RecommendedExecutionDate;
            var contentHash = Hash($"{batch.Id:N}|{setVersion}|{sequence}|{row.Link.SourceHash}|{row.Snapshot.VerificationEvidenceHash}|{row.Link.Amount:0.00}|{row.Link.Currency}|{execution:yyyy-MM-dd}|{row.Link.PaymentReference}|{row.Snapshot.Destination}");
            result.Add(new(Guid.NewGuid(), batch.CompanyId, batch.Id, row.Link.Id, setVersion, sequence,
                execution, row.Link.Amount, row.Link.Currency, row.Link.PaymentReference,
                row.Snapshot.DisplayName, row.Snapshot.Rail, row.Snapshot.Destination,
                row.Snapshot.MaskedDestination, row.Link.SourceVersion, row.Link.SourceHash, contentHash, now));
        }
        return result;
    }

    private async Task<IReadOnlyList<LiveCandidate>> LoadCandidateSetAsync(Guid companyId, int limit, CancellationToken cancellationToken)
    {
        var result = new List<LiveCandidate>();
        var proposals = await _db.SupplierInvoicePaymentProposals.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Bill).Include(x => x.Supplier).Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.DueUtc).Take(limit).ToListAsync(cancellationToken);
        var profiles = await _db.PaymentBeneficiaryProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PartyType == "supplier" && x.IsCurrent)
            .ToDictionaryAsync(x => x.PartyId, cancellationToken);
        foreach (var proposal in proposals) result.Add(BuildSupplierCandidate(proposal, profiles.GetValueOrDefault(proposal.SupplierId)));
        var remaining = Math.Max(0, limit - result.Count);
        if (remaining > 0)
        {
            var refunds = await _db.CustomerInvoiceCorrections.IgnoreQueryFilters().AsNoTracking().Include(x => x.Invoice).ThenInclude(x => x.Counterparty).Include(x => x.RefundExecution)
                .Where(x => x.CompanyId == companyId && x.CorrectionType == CustomerInvoiceCorrectionTypes.Refund)
                .OrderBy(x => x.CreatedUtc).Take(remaining).ToListAsync(cancellationToken);
            result.AddRange(refunds.Select(BuildRefundCandidate));
        }
        return result;
    }

    private async Task<LiveCandidate?> LoadCandidateAsync(Guid companyId, string type, Guid sourceId, CancellationToken cancellationToken)
    {
        if (type == PaymentBatchObligationTypes.SupplierPaymentProposal)
        {
            var proposal = await _db.SupplierInvoicePaymentProposals.IgnoreQueryFilters().AsNoTracking().Include(x => x.Bill).Include(x => x.Supplier)
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sourceId, cancellationToken);
            if (proposal is null) return null;
            var profile = await _db.PaymentBeneficiaryProfiles.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.PartyType == "supplier" && x.PartyId == proposal.SupplierId && x.IsCurrent, cancellationToken);
            return BuildSupplierCandidate(proposal, profile);
        }
        var refund = await _db.CustomerInvoiceCorrections.IgnoreQueryFilters().AsNoTracking().Include(x => x.Invoice).ThenInclude(x => x.Counterparty).Include(x => x.RefundExecution)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sourceId && x.CorrectionType == CustomerInvoiceCorrectionTypes.Refund, cancellationToken);
        return refund is null ? null : BuildRefundCandidate(refund);
    }

    private static LiveCandidate BuildSupplierCandidate(SupplierInvoicePaymentProposal proposal, PaymentBeneficiaryProfile? profile)
    {
        var baseEligible = proposal.Status == SupplierInvoicePaymentProposalStatuses.ReadyForPayment;
        var settled = proposal.Bill.SettlementStatus is FinanceSettlementStatuses.Paid or FinanceSettlementStatuses.Credited || proposal.Bill.PaidAmount >= Math.Abs(proposal.Bill.Amount) || proposal.Status == SupplierInvoicePaymentProposalStatuses.Exported;
        var statusText = $"{proposal.Bill.Status}|{proposal.Bill.ProcessingStatus}";
        var held = statusText.Contains("hold", StringComparison.OrdinalIgnoreCase); var disputed = statusText.Contains("disput", StringComparison.OrdinalIgnoreCase);
        var beneficiary = profile is null
            ? BeneficiaryData.Missing(proposal.SupplierName)
            : new(profile.Id, profile.Version, profile.DisplayName, profile.Rail, profile.Destination,
                profile.MaskedDestination, profile.Status == PaymentBeneficiaryVerificationStatuses.Verified && profile.IsCurrent,
                profile.VerificationEvidenceReference, profile.VerificationEvidenceHash, profile.VerifiedUtc);
        var sourceHash = Hash($"{proposal.Id:N}|{proposal.Status}|{proposal.Amount:0.00}|{proposal.Currency}|{proposal.DueUtc:O}|{proposal.PaymentReference}|{proposal.UpdatedUtc:O}|{proposal.Bill.Status}|{proposal.Bill.SettlementStatus}|{proposal.Bill.PaidAmount:0.00}|{proposal.Bill.UpdatedUtc:O}");
        return new(PaymentBatchObligationTypes.SupplierPaymentProposal, proposal.Id,
            proposal.Bill.BillNumber, proposal.UpdatedUtc.Ticks.ToString(), sourceHash, proposal.Amount,
            proposal.Currency, DateOnly.FromDateTime(proposal.DueUtc), proposal.PaymentReference,
            held, disputed, settled, baseEligible, baseEligible ? "Eligible supplier payment proposal." : "The supplier payment proposal is not approved and ready for payment.", beneficiary);
    }

    private static LiveCandidate BuildRefundCandidate(CustomerInvoiceCorrection correction)
    {
        var executionStatus = correction.RefundExecution?.Status; var settled = correction.Status == CustomerInvoiceCorrectionStatuses.Executed || executionStatus is CustomerInvoiceRefundExecutionStatuses.Succeeded or CustomerInvoiceRefundExecutionStatuses.Executing or CustomerInvoiceRefundExecutionStatuses.Queued or CustomerInvoiceRefundExecutionStatuses.RetryScheduled;
        var baseEligible = correction.Status is CustomerInvoiceCorrectionStatuses.Approved or CustomerInvoiceCorrectionStatuses.ManualInstruction;
        var destination = correction.BeneficiaryReference ?? string.Empty; var evidence = correction.PaymentEvidenceReference ?? string.Empty;
        var beneficiary = string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(evidence)
            ? BeneficiaryData.Missing(correction.Invoice.Counterparty.Name)
            : new(null, checked((int)correction.Version), correction.Invoice.Counterparty.Name, PaymentRails.RefundOriginalMethod,
                destination, Mask(destination), true, evidence, Hash(evidence), correction.UpdatedUtc);
        var sourceHash = Hash($"{correction.Id:N}|{correction.Version}|{correction.Status}|{correction.SourceHash}|{correction.Amount:0.00}|{correction.Currency}|{correction.BeneficiaryReference}|{correction.PaymentEvidenceReference}|{executionStatus}");
        return new(PaymentBatchObligationTypes.CustomerRefund, correction.Id,
            $"Refund {correction.Invoice.InvoiceNumber}", correction.Version.ToString(), sourceHash,
            correction.Amount, correction.Currency, DateOnly.FromDateTime(correction.UpdatedUtc),
            correction.Invoice.InvoiceNumber, false, false, settled, baseEligible,
            baseEligible ? "Eligible approved customer refund." : "The customer refund is not approved for instruction preparation.", beneficiary);
    }

    private PaymentBatchEligibilityDecision Evaluate(LiveCandidate candidate, DateOnly requestedDate,
        decimal? cash, bool duplicate, bool sourceCurrent, DateTime now, bool normalizeInvalidDate)
    {
        var input = new PaymentBatchEligibilityInput(candidate.ObligationType, candidate.Amount,
            candidate.Currency, candidate.DueDate, null, candidate.IsHeld, candidate.IsDisputed,
            candidate.IsSettled, duplicate, candidate.Beneficiary.IsVerified, sourceCurrent,
            candidate.Beneficiary.Rail, cash, requestedDate, now);
        var decision = _policy.Evaluate(input);
        if (normalizeInvalidDate && decision.ReasonCode == PaymentBatchReasonCodes.InvalidExecutionDate)
            decision = _policy.Evaluate(input with { RequestedExecutionDate = decision.RecommendedExecutionDate });
        return decision;
    }

    private async Task<Dictionary<string, decimal?>> LoadCashAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var accountIds = await _db.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive).Select(x => x.FinanceAccountId).ToArrayAsync(cancellationToken);
        if (accountIds.Length == 0) return new(StringComparer.OrdinalIgnoreCase);
        var balances = await _db.FinanceBalances.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && accountIds.Contains(x.AccountId)).ToListAsync(cancellationToken);
        return balances.GroupBy(x => new { x.AccountId, x.Currency }).Select(x => x.OrderByDescending(y => y.AsOfUtc).First())
            .GroupBy(x => x.Currency, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => (decimal?)x.Sum(y => y.Amount), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> HasDuplicateAsync(Guid companyId, string type, Guid sourceId, Guid? excludedBatchId, CancellationToken cancellationToken) =>
        await (from link in _db.PaymentBatchObligations.IgnoreQueryFilters().AsNoTracking()
               join batch in _db.PaymentBatches.IgnoreQueryFilters().AsNoTracking() on new { link.CompanyId, Id = link.BatchId } equals new { batch.CompanyId, batch.Id }
               where link.CompanyId == companyId && link.ObligationType == type && link.SourceId == sourceId && link.RemovedUtc == null &&
                     (!excludedBatchId.HasValue || link.BatchId != excludedBatchId) && batch.Status != PaymentBatchStatuses.Cancelled && batch.Status != PaymentBatchStatuses.Rejected
               select link.Id).AnyAsync(cancellationToken);

    private async Task SupersedeArtifactsAsync(Guid companyId, Guid batchId, DateTime now, CancellationToken cancellationToken)
    {
        var instructions = await _db.PaymentInstructions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.BatchId == batchId && x.IsCurrent).ToListAsync(cancellationToken);
        foreach (var instruction in instructions) instruction.Supersede(now);
        var artifacts = await _db.PaymentBatchExportArtifacts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.BatchId == batchId && x.IsCurrent).ToListAsync(cancellationToken);
        foreach (var artifact in artifacts) artifact.Supersede(now);
    }

    private async Task InvalidateApprovalIfNeededAsync(PaymentBatch batch, Guid actor, string reason, DateTime now, CancellationToken cancellationToken)
    {
        if (!batch.ApprovalRequestId.HasValue) { await SupersedeArtifactsAsync(batch.CompanyId, batch.Id, now, cancellationToken); return; }
        var approval = await _db.ApprovalRequests.IgnoreQueryFilters().Include(x => x.Steps).SingleOrDefaultAsync(x => x.CompanyId == batch.CompanyId && x.Id == batch.ApprovalRequestId, cancellationToken);
        if (approval is { Status: ApprovalRequestStatus.Pending }) approval.MarkCancelled(reason);
        var binding = await _db.PaymentBatchApprovalBindings.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == batch.CompanyId && x.BatchId == batch.Id && x.ApprovalRequestId == batch.ApprovalRequestId, cancellationToken);
        if (binding is not null && binding.Status is PaymentBatchApprovalBindingStatuses.Pending or PaymentBatchApprovalBindingStatuses.Approved)
            binding.MarkStale(actor, reason, now);
        await SupersedeArtifactsAsync(batch.CompanyId, batch.Id, now, cancellationToken);
    }

    private async Task<PaymentBatchDetailDto?> GetDetailAsync(Guid companyId, Guid batchId, bool replay, CancellationToken cancellationToken)
    {
        var batch = await _db.PaymentBatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == batchId, cancellationToken);
        if (batch is null) return null;
        var links = await _db.PaymentBatchObligations.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.BatchId == batchId && x.RemovedUtc == null).OrderBy(x => x.DueDate).ToListAsync(cancellationToken);
        var linkIds = links.Select(x => x.Id).ToArray(); var snapshots = await _db.PaymentBeneficiarySnapshots.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && linkIds.Contains(x.ObligationLinkId)).ToDictionaryAsync(x => x.ObligationLinkId, cancellationToken);
        var obligations = links.Where(x => snapshots.ContainsKey(x.Id)).Select(x => { var s = snapshots[x.Id]; return new PaymentBatchObligationDto(x.Id, x.ObligationType, x.SourceId, x.SourceReference, x.SourceVersion, x.SourceHash, x.Amount, x.Currency, x.DueDate, x.PaymentReference, s.DisplayName, s.Rail, s.MaskedDestination, s.ProfileVersion, s.VerificationEvidenceReference, s.VerifiedUtc, x.CreatedUtc); }).ToArray();
        var instructions = await _db.PaymentInstructions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.BatchId == batchId && x.IsCurrent).OrderBy(x => x.Sequence).Select(x => new PaymentInstructionDto(x.Id, x.InstructionSetVersion, x.Sequence, x.ExecutionDate, x.Amount, x.Currency, x.PaymentReference, x.BeneficiaryName, x.Rail, x.MaskedDestination, x.SourceVersion, x.ContentHash, x.Status, x.IsCurrent, x.CreatedUtc)).ToListAsync(cancellationToken);
        var validation = batch.CurrentValidationResultId.HasValue ? await MapValidationAsync(companyId, batch.CurrentValidationResultId.Value, cancellationToken) : null;
        var binding = await _db.PaymentBatchApprovalBindings.IgnoreQueryFilters().AsNoTracking().OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.BatchId == batchId, cancellationToken);
        var approval = binding is null ? null : new PaymentBatchApprovalDto(binding.Id, binding.ApprovalRequestId, binding.Status, binding.InstructionSetVersion, binding.SourceSetHash, binding.RequestedByUserId, binding.DecidedByUserId, binding.CreatedUtc, binding.DecidedUtc);
        var artifactHash = batch.CurrentExportArtifactId.HasValue ? await _db.PaymentBatchExportArtifacts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == batch.CurrentExportArtifactId).Select(x => x.ContentHash).SingleOrDefaultAsync(cancellationToken) : null;
        return new(await MapSummaryAsync(batch, replay, cancellationToken), obligations, instructions,
            validation, approval, Allowed(batch), ApprovalNotice, artifactHash);
    }

    private async Task<PaymentBatchSummaryDto> MapSummaryAsync(PaymentBatch batch, bool replay, CancellationToken cancellationToken)
    {
        var rows = await _db.PaymentBatchObligations.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == batch.CompanyId && x.BatchId == batch.Id && x.RemovedUtc == null).ToListAsync(cancellationToken);
        var cash = await LoadCashAsync(batch.CompanyId, cancellationToken); var totals = rows.GroupBy(x => x.Currency).Select(x => { var amount = x.Sum(y => y.Amount); var available = cash.GetValueOrDefault(x.Key); return new PaymentBatchTotalDto(x.Key, amount, available, available >= amount); }).ToArray();
        return new(batch.Id, batch.Reference, batch.Name, batch.PlannedExecutionDate, batch.Status,
            batch.Version, batch.InstructionSetVersion, rows.Count, totals, batch.CreatedByUserId,
            batch.SubmittedByUserId, batch.ApprovedByUserId, batch.CreatedUtc, batch.UpdatedUtc, replay);
    }

    private async Task<PaymentBatchValidationResultDto?> MapValidationAsync(Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        var result = await _db.PaymentBatchValidationResults.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken); if (result is null) return null;
        var issues = await _db.PaymentBatchValidationIssues.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.ValidationResultId == id).Select(x => new PaymentBatchValidationIssueDto(x.Id, x.ObligationLinkId, x.Severity, x.ReasonCode, x.Explanation)).ToListAsync(cancellationToken);
        var rows = await _db.PaymentBatchObligations.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.BatchId == result.BatchId && x.RemovedUtc == null).ToListAsync(cancellationToken);
        var cash = await LoadCashAsync(companyId, cancellationToken); var totals = rows.GroupBy(x => x.Currency).Select(x => { var amount = x.Sum(y => y.Amount); var available = cash.GetValueOrDefault(x.Key); return new PaymentBatchTotalDto(x.Key, amount, available, available >= amount); }).ToArray();
        return new(result.Id, result.EvaluatedBatchVersion, result.InstructionSetVersion, result.IsValid, result.SourceSetHash, totals, issues, result.CreatedUtc);
    }

    private static PaymentBatchAllowedActionsDto Allowed(PaymentBatch batch) => batch.Status switch
    {
        PaymentBatchStatuses.Draft => new(true, true, false, false, false, true, batch.InstructionSetVersion > 0, false, null, "Add eligible obligations, then validate the exact instruction set."),
        PaymentBatchStatuses.Validated => new(true, true, true, false, false, true, true, false, null, "The current evidence is validated and can be submitted for internal approval."),
        PaymentBatchStatuses.AwaitingApproval => new(false, false, false, true, true, true, true, false, PaymentBatchReasonCodes.ApprovalPending, "A different finance approver must review the exact instruction set."),
        PaymentBatchStatuses.Approved => new(false, false, false, false, false, true, false, true, null, ApprovalNotice),
        _ => new(false, false, false, false, false, false, false, false, PaymentBatchReasonCodes.InvalidLifecycle, "This batch is final and retained as immutable evidence.")
    };

    private async Task<PaymentBatchDetailDto?> ReplayAsync(Guid companyId, string operationType,
        string idempotencyKey, string requestHash, CancellationToken cancellationToken)
    {
        var operation = await _db.PaymentBatchOperations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.OperationType == operationType && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (operation is null) return null;
        if (operation.RequestHash != requestHash) throw Error(PaymentBatchReasonCodes.IdempotencyConflict, "The idempotency key was already used with a different payment batch payload.", true);
        return await GetDetailAsync(companyId, operation.BatchId, true, cancellationToken);
    }

    private void AddOperation(PaymentBatch batch, string type, string key, string hash, Guid actor, DateTime now) =>
        _db.PaymentBatchOperations.Add(new(Guid.NewGuid(), batch.CompanyId, batch.Id, type, key, hash, batch.Version, batch.Status, actor, now));
    private async Task<PaymentBatch> LoadBatchAsync(Guid companyId, Guid batchId, bool tracked, CancellationToken cancellationToken)
    {
        var query = _db.PaymentBatches.IgnoreQueryFilters(); if (!tracked) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == batchId, cancellationToken)
            ?? throw Error(PaymentBatchReasonCodes.BatchNotFound, "The payment batch was not found in the active company.");
    }

    private async Task WriteAuditAsync(Guid companyId, Guid batchId, string action, string outcome,
        string rationale, Guid actor, string? correlationId, IReadOnlyDictionary<string, string?>? metadata,
        CancellationToken cancellationToken) => await _audit.WriteAsync(new(companyId, AuditActorTypes.User,
            actor, action, AuditTargetTypes.PaymentBatch, batchId.ToString("D"), outcome, rationale,
            [$"payment_batch:{batchId:N}"], metadata, correlationId, Now()), cancellationToken);

    private static PaymentBeneficiaryProfileDto MapProfile(PaymentBeneficiaryProfile x) => new(x.Id,
        x.PartyType, x.PartyId, x.DisplayName, x.Rail, x.MaskedDestination, x.Currency, x.Version,
        x.Status, x.VerificationEvidenceReference, x.VerifiedUtc);
    private static PaymentBatchValidationIssueDto MapPreviewIssue(EvaluationIssue x) => new(Guid.Empty,
        x.ObligationLinkId, PaymentBatchValidationSeverities.Error, x.ReasonCode, x.Explanation);
    private static PaymentBatchException Error(string code, string message, bool conflict = false, long? version = null) => new(code, message, conflict, version);
    private static string NormalizeObligationType(string value) { var type = PaymentBatchObligationTypes.Normalize(value); return PaymentBatchObligationTypes.IsSupported(type) ? type : throw Error(PaymentBatchReasonCodes.ObligationIneligible, "The payment obligation type is not supported."); }
    private static string NormalizePartyType(string value) => value?.Trim().ToLowerInvariant() switch { "supplier" => "supplier", "customer" => "customer", _ => throw Error(PaymentBatchReasonCodes.BeneficiaryMissing, "Beneficiary party type must be supplier or customer.") };
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    private static string Mask(string value) { var normalized = value.Trim(); return normalized.Length <= 4 ? "••••" : $"•••• {normalized[^4..]}"; }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void EnsureVersion(PaymentBatch batch, long expected) { if (batch.Version != expected) throw Error(PaymentBatchReasonCodes.VersionConflict, "The payment batch changed after it was opened.", true, batch.Version); }
    private void EnsureTenant(Guid companyId) { if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId)); if (_companyContext?.CompanyId is Guid active && active != companyId) throw new UnauthorizedAccessException("Payment batches are scoped to the active company context."); }
    private void EnsureCommand(Guid companyId, Guid actor) { EnsureTenant(companyId); if (actor == Guid.Empty) throw new UnauthorizedAccessException("A resolved company user is required."); }
    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private static void ValidateBeneficiaryDestination(string railValue, string destinationValue, string currencyValue)
    {
        var rail = PaymentRails.Normalize(railValue); if (!PaymentRails.IsSupported(rail) || rail == PaymentRails.RefundOriginalMethod) throw Error(PaymentBatchReasonCodes.UnsupportedRail, "Supplier beneficiaries support Bankgiro, Plusgiro, or SEPA credit transfer.");
        var destination = new string((destinationValue ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(); if (destination.Length == 0) throw Error(PaymentBatchReasonCodes.BeneficiaryUnverified, "A beneficiary destination is required.");
        if (rail == PaymentRails.Bankgiro && (destination.Length is < 7 or > 8 || !destination.All(char.IsDigit))) throw Error(PaymentBatchReasonCodes.BeneficiaryUnverified, "Bankgiro must contain seven or eight digits.");
        if (rail == PaymentRails.Plusgiro && (destination.Length is < 2 or > 10 || !destination.All(char.IsDigit))) throw Error(PaymentBatchReasonCodes.BeneficiaryUnverified, "Plusgiro format is invalid.");
        if (rail == PaymentRails.SepaCreditTransfer && (destination.Length is < 15 or > 34 || !destination.Take(2).All(char.IsLetter))) throw Error(PaymentBatchReasonCodes.BeneficiaryUnverified, "A valid IBAN-shaped destination is required for SEPA credit transfer.");
        if ((rail is PaymentRails.Bankgiro or PaymentRails.Plusgiro) && !currencyValue.Equals("SEK", StringComparison.OrdinalIgnoreCase)) throw Error(PaymentBatchReasonCodes.UnsupportedCurrency, "Bankgiro and Plusgiro beneficiaries must use SEK.");
    }

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction is not null) return await action();
        var strategy = _db.Database.CreateExecutionStrategy(); return await strategy.ExecuteAsync(async () =>
        { await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken); var result = await action(); await tx.CommitAsync(cancellationToken); return result; });
    }

    private sealed record BeneficiaryData(Guid? ProfileId, int Version, string DisplayName, string Rail,
        string Destination, string MaskedDestination, bool IsVerified, string EvidenceReference,
        string EvidenceHash, DateTime VerifiedUtc)
    {
        public static BeneficiaryData Missing(string displayName) => new(null, 1, displayName,
            PaymentRails.SepaCreditTransfer, "missing", "Not verified", false, "missing", Hash("missing"), DateTime.UnixEpoch);
    }
    private sealed record LiveCandidate(string ObligationType, Guid SourceId, string SourceReference,
        string SourceVersion, string SourceHash, decimal Amount, string Currency, DateOnly DueDate,
        string PaymentReference, bool IsHeld, bool IsDisputed, bool IsSettled, bool BaseEligible,
        string BaseExplanation, BeneficiaryData Beneficiary);
    private sealed record EvaluationIssue(Guid? ObligationLinkId, string ReasonCode, string Explanation);
    private sealed record EvaluatedObligation(PaymentBatchObligationLink Link, PaymentBeneficiarySnapshot Snapshot,
        LiveCandidate Candidate, PaymentBatchEligibilityDecision Decision);
    private sealed record BatchEvaluation(IReadOnlyList<PaymentBatchObligationLink> Obligations,
        IReadOnlyList<EvaluatedObligation> Evaluated, IReadOnlyList<PaymentBatchTotalDto> Totals,
        IReadOnlyList<EvaluationIssue> Issues, string SourceSetHash, DateOnly RecommendedExecutionDate);
}
