using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public interface ICustomerBillingProviderSyncGuard
{
    Task<bool> ApplyOrDetectConflictAsync(Guid companyId, FinanceCounterparty counterparty, string legalName,
        string? email, string? taxIdentifier, string sourceReference, DateTime nowUtc, CancellationToken cancellationToken);
}

public sealed class CustomerBillingProfileService : ICustomerBillingProfileService, ICustomerBillingProviderSyncGuard
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyContextAccessor _companyContext;
    private readonly IAuditEventWriter _audit;
    private readonly CustomerBillingTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;

    public CustomerBillingProfileService(VirtualCompanyDbContext db, ICompanyContextAccessor companyContext,
        IAuditEventWriter audit, CustomerBillingTelemetry telemetry, TimeProvider timeProvider)
    {
        _db = db; _companyContext = companyContext; _audit = audit; _telemetry = telemetry; _timeProvider = timeProvider;
    }

    public async Task<CustomerBillingProfileDto?> GetAsync(GetCustomerBillingProfileQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var profile = await _db.CustomerBillingProfiles.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.CounterpartyId == query.CounterpartyId, cancellationToken);
        return profile is null ? null : await MapProfileAsync(profile, cancellationToken);
    }

    public async Task<bool> ApplyOrDetectConflictAsync(Guid companyId, FinanceCounterparty counterparty, string legalName,
        string? email, string? taxIdentifier, string sourceReference, DateTime nowUtc, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        if (counterparty.CompanyId != companyId || counterparty.CounterpartyType != "customer")
            throw new UnauthorizedAccessException("Provider customer billing data must match the active company customer.");
        var profile = await _db.CustomerBillingProfiles.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.CounterpartyId == counterparty.Id, cancellationToken);
        if (profile is null) return true;
        var current = ToInput(profile.ToValues());
        var incoming = ValidateAndNormalize(current with
        {
            LegalName = legalName,
            TaxIdentifier = taxIdentifier,
            InvoiceDeliveryEmail = current.InvoiceDeliveryChannel == CustomerBillingDeliveryChannels.Email ? email : current.InvoiceDeliveryEmail,
            IdentityValidationState = CustomerBillingValidationStates.ProviderSourced,
            SourceKind = CustomerBillingSourceKinds.Provider,
            SourceReference = sourceReference,
            ExternallyVerifiedUtc = null,
            VerificationSource = null
        }, companyId, counterparty.Id, profile.UpdatedByUserId, nowUtc);
        var changed = ChangedFields(current, incoming);
        if (changed.Count == 0) return true;
        if (profile.SourceKind != CustomerBillingSourceKinds.Provider)
        {
            var alreadyPending = await _db.CustomerBillingSourceConflicts.IgnoreQueryFilters().AnyAsync(x =>
                x.CompanyId == companyId && x.ProfileId == profile.Id && x.Status == "pending" &&
                x.IncomingSourceKind == CustomerBillingSourceKinds.Provider && x.IncomingSourceReference == sourceReference,
                cancellationToken);
            if (!alreadyPending)
            {
                _db.CustomerBillingSourceConflicts.Add(new CustomerBillingSourceConflict(Guid.NewGuid(), companyId,
                    profile.Id, profile.CounterpartyId, profile.Version, profile.SourceKind, CustomerBillingSourceKinds.Provider,
                    sourceReference, JoinFields(changed), Serialize(incoming), profile.UpdatedByUserId, nowUtc));
                profile.MarkConflict(profile.UpdatedByUserId, nowUtc);
                _telemetry.ConflictDetected(companyId, profile.CounterpartyId, profile.SourceKind, CustomerBillingSourceKinds.Provider);
            }
            return false;
        }
        profile.Update(ToValues(incoming), profile.UpdatedByUserId, nowUtc);
        AddVersion(profile, incoming, changed, profile.UpdatedByUserId, nowUtc);
        return true;
    }

    public async Task<IReadOnlyList<CustomerBillingProfileVersionDto>> GetHistoryAsync(
        GetCustomerBillingProfileHistoryQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var take = Math.Clamp(query.Limit, 1, 500);
        return await _db.CustomerBillingProfileVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.CounterpartyId == query.CounterpartyId)
            .OrderByDescending(x => x.ProfileVersion).Take(take)
            .Select(x => new CustomerBillingProfileVersionDto(x.Id, x.CounterpartyId, x.ProfileVersion,
                x.SourceKind, x.SourceReference, SplitFields(x.ChangedFields), x.SnapshotHash, x.ActorUserId, x.CreatedUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomerBillingProfileDto> UpsertAsync(UpsertCustomerBillingProfileCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        EnsureActor(command.ActorUserId);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var counterparty = await LoadCustomerAsync(command.CompanyId, command.CounterpartyId, cancellationToken);
        if (counterparty.MergedIntoCounterpartyId.HasValue)
            throw new CustomerBillingException(CustomerBillingReasonCodes.UnsafeMerge, "This customer redirects to a merged customer record.", true);

        var normalizedInput = ValidateAndNormalize(command.Profile, command.CompanyId, command.CounterpartyId,
            command.ActorUserId, now);
        var incomingValues = ToValues(normalizedInput);
        var profile = await _db.CustomerBillingProfiles.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.CounterpartyId == command.CounterpartyId, cancellationToken);

        if (profile is null)
        {
            if (command.ExpectedVersion is not null and not 0)
                throw ConcurrencyConflict();
            await CaptureInvoiceSnapshotsAsync(command.CompanyId, counterparty, null, cancellationToken);
            profile = new CustomerBillingProfile(Guid.NewGuid(), command.CompanyId, command.CounterpartyId,
                incomingValues, command.ActorUserId, now);
            _db.CustomerBillingProfiles.Add(profile);
            AddVersion(profile, normalizedInput, ["initial_profile"], command.ActorUserId, now);
        }
        else
        {
            if (command.ExpectedVersion != profile.Version) throw ConcurrencyConflict();
            var current = ToInput(profile.ToValues());
            var changedFields = ChangedFields(current, normalizedInput);
            if (changedFields.Count == 0) return await MapProfileAsync(profile, cancellationToken);

            if (RequiresSourceReview(profile.SourceKind, incomingValues.SourceKind))
            {
                var conflict = new CustomerBillingSourceConflict(Guid.NewGuid(), command.CompanyId, profile.Id,
                    profile.CounterpartyId, profile.Version, profile.SourceKind, incomingValues.SourceKind,
                    incomingValues.SourceReference, JoinFields(changedFields), Serialize(normalizedInput),
                    command.ActorUserId, now);
                _db.CustomerBillingSourceConflicts.Add(conflict);
                profile.MarkConflict(command.ActorUserId, now);
                await WriteAuditAsync(command.CompanyId, command.ActorUserId, "finance.customer_billing.source_conflict_detected",
                    profile.CounterpartyId, AuditEventOutcomes.Pending, "A conflicting customer billing source requires review.",
                    new Dictionary<string, string?> { ["existingSource"] = profile.SourceKind, ["incomingSource"] = incomingValues.SourceKind,
                        ["changedFields"] = JoinFields(changedFields) }, command.CorrelationId, null, cancellationToken);
                await SaveAsync(cancellationToken);
                _telemetry.ConflictDetected(command.CompanyId, profile.CounterpartyId, profile.SourceKind, incomingValues.SourceKind);
                return await MapProfileAsync(profile, cancellationToken);
            }

            await CaptureInvoiceSnapshotsAsync(command.CompanyId, counterparty, profile, cancellationToken);
            var before = Serialize(current);
            profile.Update(incomingValues, command.ActorUserId, now);
            AddVersion(profile, normalizedInput, changedFields, command.ActorUserId, now);
            await WriteAuditAsync(command.CompanyId, command.ActorUserId, "finance.customer_billing.updated",
                profile.CounterpartyId, AuditEventOutcomes.Succeeded, "Customer billing profile updated.",
                new Dictionary<string, string?> { ["source"] = profile.SourceKind, ["version"] = profile.Version.ToString(),
                    ["changedFields"] = JoinFields(changedFields) }, command.CorrelationId,
                JsonSerializer.Serialize(new { before = JsonSerializer.Deserialize<JsonElement>(before), after = normalizedInput }, SerializerOptions), cancellationToken);
        }

        await DetectDuplicatesAsync(profile, now, cancellationToken);
        if (profile.Version == 1)
            await WriteAuditAsync(command.CompanyId, command.ActorUserId, "finance.customer_billing.created",
                profile.CounterpartyId, AuditEventOutcomes.Succeeded, "Customer billing profile created.",
                new Dictionary<string, string?> { ["source"] = profile.SourceKind, ["version"] = "1" },
                command.CorrelationId, Serialize(normalizedInput), cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry.ProfileSaved(command.CompanyId, command.CounterpartyId, profile.SourceKind, profile.Version);
        return await MapProfileAsync(profile, cancellationToken);
    }

    public async Task<CustomerBillingProfileDto> ResolveConflictAsync(
        ResolveCustomerBillingSourceConflictCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId); EnsureActor(command.ActorUserId);
        var conflict = await _db.CustomerBillingSourceConflicts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ConflictId, cancellationToken)
            ?? throw new CustomerBillingException(CustomerBillingReasonCodes.ConflictNotFound, "Customer billing source conflict was not found.", isNotFound: true);
        var profile = await _db.CustomerBillingProfiles.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == conflict.ProfileId, cancellationToken);
        if (conflict.Version != command.ExpectedConflictVersion || profile.Version != command.ExpectedProfileVersion)
            throw ConcurrencyConflict();
        if (conflict.Status != "pending")
            throw new CustomerBillingException(CustomerBillingReasonCodes.AlreadyDecided, "This source conflict is already resolved.", true);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        conflict.Resolve(command.UseIncomingValues, command.Reason, command.ActorUserId, now);
        CustomerBillingProfileInputDto? appliedIncoming = null;
        if (command.UseIncomingValues)
        {
            appliedIncoming = JsonSerializer.Deserialize<CustomerBillingProfileInputDto>(conflict.IncomingSnapshotJson, SerializerOptions)
                ?? throw new CustomerBillingException(CustomerBillingReasonCodes.SourceConflict, "The retained incoming profile could not be read safely.", true);
            profile.Update(ToValues(appliedIncoming), command.ActorUserId, now);
        }
        var pendingCount = await _db.CustomerBillingSourceConflicts.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == command.CompanyId && x.ProfileId == profile.Id && x.Status == "pending" && x.Id != conflict.Id, cancellationToken);
        if (pendingCount == 0) profile.ClearConflict(command.ActorUserId, now);
        if (appliedIncoming is not null)
            AddVersion(profile, appliedIncoming, SplitFields(conflict.ChangedFields), command.ActorUserId, now);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "finance.customer_billing.source_conflict_resolved",
            profile.CounterpartyId, AuditEventOutcomes.Succeeded, command.Reason,
            new Dictionary<string, string?> { ["conflictId"] = conflict.Id.ToString("D"), ["usedIncomingValues"] = command.UseIncomingValues.ToString() },
            command.CorrelationId, null, cancellationToken);
        await DetectDuplicatesAsync(profile, now, cancellationToken);
        await SaveAsync(cancellationToken);
        return await MapProfileAsync(profile, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerDuplicateCandidateDto>> GetDuplicateCandidatesAsync(
        GetCustomerDuplicateCandidatesQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var normalizedStatus = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToLowerInvariant();
        var candidates = _db.CustomerDuplicateCandidates.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);
        if (normalizedStatus is not null) candidates = candidates.Where(x => x.Status == normalizedStatus);
        var rows = await candidates.OrderByDescending(x => x.UpdatedUtc).Take(Math.Clamp(query.Limit, 1, 500)).ToListAsync(cancellationToken);
        return await MapCandidatesAsync(query.CompanyId, rows, cancellationToken);
    }

    public async Task<CustomerDuplicateCandidateDto> DecideDuplicateAsync(DecideCustomerDuplicateCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId); EnsureActor(command.ActorUserId);
        var candidate = await _db.CustomerDuplicateCandidates.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.CandidateId, cancellationToken)
            ?? throw new CustomerBillingException(CustomerBillingReasonCodes.CandidateNotFound, "Customer duplicate candidate was not found.", isNotFound: true);
        var decision = command.Decision?.Trim().ToLowerInvariant();
        if (candidate.Status != CustomerDuplicateDecisionStatuses.Pending)
        {
            var isReplay = decision == CustomerDuplicateDecisions.KeepSeparate && candidate.Status == CustomerDuplicateDecisionStatuses.KeptSeparate ||
                decision == CustomerDuplicateDecisions.Merge && candidate.Status == CustomerDuplicateDecisionStatuses.Merged &&
                candidate.MergeSourceCounterpartyId == command.MergeSourceCounterpartyId && candidate.MergeTargetCounterpartyId == command.MergeTargetCounterpartyId;
            if (isReplay) return (await MapCandidatesAsync(command.CompanyId, [candidate], cancellationToken)).Single();
            throw new CustomerBillingException(CustomerBillingReasonCodes.AlreadyDecided, "This duplicate candidate already has a different decision.", true);
        }
        if (candidate.Version != command.ExpectedVersion) throw ConcurrencyConflict();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (decision == CustomerDuplicateDecisions.KeepSeparate)
        {
            candidate.KeepSeparate(command.Reason, command.ActorUserId, now);
        }
        else if (decision == CustomerDuplicateDecisions.Merge && command.MergeSourceCounterpartyId.HasValue && command.MergeTargetCounterpartyId.HasValue)
        {
            await MergeAsync(candidate, command.MergeSourceCounterpartyId.Value, command.MergeTargetCounterpartyId.Value,
                command.Reason, command.ActorUserId, now, cancellationToken);
        }
        else throw new CustomerBillingException(CustomerBillingReasonCodes.InvalidDecision, "Choose merge or keep separate and provide both merge records when merging.");

        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "finance.customer_duplicate.decided",
            candidate.Id, AuditEventOutcomes.Succeeded, command.Reason,
            new Dictionary<string, string?> { ["decision"] = decision, ["sourceCounterpartyId"] = command.MergeSourceCounterpartyId?.ToString("D"),
                ["targetCounterpartyId"] = command.MergeTargetCounterpartyId?.ToString("D") }, command.CorrelationId, null, cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry.DecisionRecorded(command.CompanyId, candidate.Id, decision!);
        return (await MapCandidatesAsync(command.CompanyId, [candidate], cancellationToken)).Single();
    }

    private async Task MergeAsync(CustomerDuplicateCandidate candidate, Guid sourceId, Guid targetId, string reason,
        Guid actorId, DateTime now, CancellationToken cancellationToken)
    {
        if (!new[] { candidate.FirstCounterpartyId, candidate.SecondCounterpartyId }.Contains(sourceId) ||
            !new[] { candidate.FirstCounterpartyId, candidate.SecondCounterpartyId }.Contains(targetId) || sourceId == targetId)
            throw new CustomerBillingException(CustomerBillingReasonCodes.InvalidDecision, "Merge source and target must match the duplicate candidate.");
        var records = await _db.FinanceCounterparties.IgnoreQueryFilters()
            .Where(x => x.CompanyId == candidate.CompanyId && (x.Id == sourceId || x.Id == targetId)).ToListAsync(cancellationToken);
        if (records.Count != 2 || records.Any(x => x.CounterpartyType != "customer"))
            throw new CustomerBillingException(CustomerBillingReasonCodes.UnsafeMerge, "Both merge records must be customers in the active company.", true);
        var source = records.Single(x => x.Id == sourceId); var target = records.Single(x => x.Id == targetId);
        if (target.MergedIntoCounterpartyId.HasValue || await CreatesRedirectCycleAsync(candidate.CompanyId, sourceId, targetId, cancellationToken))
            throw new CustomerBillingException(CustomerBillingReasonCodes.UnsafeMerge, "The merge would create an unsafe customer redirect cycle.", true);
        if (await _db.FinanceBills.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == candidate.CompanyId && x.CounterpartyId == sourceId, cancellationToken) ||
            await _db.FinanceAssets.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == candidate.CompanyId && x.CounterpartyId == sourceId, cancellationToken) ||
            await _db.SupplierSubscriptions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == candidate.CompanyId && x.CounterpartyId == sourceId, cancellationToken))
            throw new CustomerBillingException(CustomerBillingReasonCodes.UnsafeMerge, "The source customer has supplier-only records and cannot be merged safely.", true);

        var sourceProfile = await _db.CustomerBillingProfiles.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == candidate.CompanyId && x.CounterpartyId == sourceId, cancellationToken);
        await CaptureInvoiceSnapshotsAsync(candidate.CompanyId, source, sourceProfile, cancellationToken);
        var invoices = await _db.FinanceInvoices.IgnoreQueryFilters().Where(x => x.CompanyId == candidate.CompanyId && x.CounterpartyId == sourceId).ToListAsync(cancellationToken);
        foreach (var invoice in invoices) invoice.ReassignCounterpartyForApprovedMerge(targetId);
        var transactions = await _db.FinanceTransactions.IgnoreQueryFilters().Where(x => x.CompanyId == candidate.CompanyId && x.CounterpartyId == sourceId).ToListAsync(cancellationToken);
        foreach (var transaction in transactions) transaction.ReassignCounterpartyForApprovedMerge(targetId);
        var references = await _db.FinanceExternalReferences.IgnoreQueryFilters()
            .Where(x => x.CompanyId == candidate.CompanyId && x.InternalRecordId == sourceId && (x.EntityType == "customer" || x.EntityType == "counterparty"))
            .ToListAsync(cancellationToken);
        foreach (var reference in references) reference.RepointToInternalRecord(targetId, reference.ExternalNumber, reference.ExternalUpdatedUtc, now);
        source.MarkMergedInto(targetId, now);
        sourceProfile?.MarkMerged(targetId, actorId, now);
        _db.CustomerCounterpartyRedirects.Add(new CustomerCounterpartyRedirect(Guid.NewGuid(), candidate.CompanyId,
            sourceId, targetId, candidate.Id, actorId, now));
        candidate.MarkMerged(sourceId, targetId, reason, actorId, now);
    }

    private async Task<bool> CreatesRedirectCycleAsync(Guid companyId, Guid sourceId, Guid targetId, CancellationToken cancellationToken)
    {
        var current = targetId;
        for (var depth = 0; depth < 100; depth++)
        {
            if (current == sourceId) return true;
            var next = await _db.CustomerCounterpartyRedirects.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.SourceCounterpartyId == current)
                .Select(x => (Guid?)x.TargetCounterpartyId).SingleOrDefaultAsync(cancellationToken);
            if (!next.HasValue) return false;
            current = next.Value;
        }
        return true;
    }

    private async Task DetectDuplicatesAsync(CustomerBillingProfile profile, DateTime now, CancellationToken cancellationToken)
    {
        var others = await _db.CustomerBillingProfiles.IgnoreQueryFilters()
            .Where(x => x.CompanyId == profile.CompanyId && x.CounterpartyId != profile.CounterpartyId && x.MergedIntoCounterpartyId == null)
            .ToListAsync(cancellationToken);
        foreach (var other in others)
        {
            var (score, evidence) = CustomerBillingDuplicatePolicy.Evaluate(profile, other);
            if (score < CustomerBillingDuplicatePolicy.CandidateThreshold) continue;
            var first = profile.CounterpartyId.CompareTo(other.CounterpartyId) < 0 ? profile.CounterpartyId : other.CounterpartyId;
            var second = first == profile.CounterpartyId ? other.CounterpartyId : profile.CounterpartyId;
            var candidate = await _db.CustomerDuplicateCandidates.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == profile.CompanyId && x.FirstCounterpartyId == first && x.SecondCounterpartyId == second, cancellationToken);
            var evidenceJson = CustomerBillingDuplicatePolicy.SerializeEvidence(evidence);
            if (candidate is null)
            {
                candidate = new CustomerDuplicateCandidate(Guid.NewGuid(), profile.CompanyId, first, second, score, evidenceJson, now);
                _db.CustomerDuplicateCandidates.Add(candidate); _telemetry.CandidateDetected(profile.CompanyId, candidate.Id, score);
            }
            else candidate.Refresh(score, evidenceJson, now);
        }
    }

    private async Task CaptureInvoiceSnapshotsAsync(Guid companyId, FinanceCounterparty counterparty,
        CustomerBillingProfile? profile, CancellationToken cancellationToken)
    {
        var invoices = await _db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CounterpartyId == counterparty.Id)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        if (invoices.Count == 0) return;
        var existing = await _db.CustomerInvoiceCustomerSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && invoices.Contains(x.InvoiceId)).Select(x => x.InvoiceId).ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet();
        var snapshot = profile is null
            ? Serialize(new { counterparty.Name, counterparty.Email, counterparty.TaxId, counterparty.PaymentTerms, source = "legacy_counterparty" })
            : Serialize(ToInput(profile.ToValues()));
        var hash = Hash(snapshot);
        foreach (var invoiceId in invoices.Where(id => !existingSet.Contains(id)))
            _db.CustomerInvoiceCustomerSnapshots.Add(new CustomerInvoiceCustomerSnapshot(Guid.NewGuid(), companyId,
                invoiceId, counterparty.Id, profile?.Version, profile?.SourceKind ?? CustomerBillingSourceKinds.Migration, snapshot, hash,
                _timeProvider.GetUtcNow().UtcDateTime));
    }

    private void AddVersion(CustomerBillingProfile profile, CustomerBillingProfileInputDto input,
        IReadOnlyList<string> changedFields, Guid actorId, DateTime now)
    {
        var snapshot = Serialize(input);
        _db.CustomerBillingProfileVersions.Add(new CustomerBillingProfileVersion(Guid.NewGuid(), profile.CompanyId,
            profile.Id, profile.CounterpartyId, profile.Version, profile.SourceKind, profile.SourceReference,
            JoinFields(changedFields), snapshot, Hash(snapshot), actorId, now));
    }

    private async Task<FinanceCounterparty> LoadCustomerAsync(Guid companyId, Guid counterpartyId, CancellationToken cancellationToken)
    {
        var counterparty = await _db.FinanceCounterparties.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == counterpartyId, cancellationToken);
        return counterparty is not null && counterparty.CounterpartyType == "customer" ? counterparty :
            throw new CustomerBillingException(CustomerBillingReasonCodes.CustomerNotFound, "Customer was not found.", isNotFound: true);
    }

    private async Task<CustomerBillingProfileDto> MapProfileAsync(CustomerBillingProfile profile, CancellationToken cancellationToken)
    {
        var conflicts = await _db.CustomerBillingSourceConflicts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == profile.CompanyId && x.ProfileId == profile.Id)
            .OrderByDescending(x => x.DetectedUtc)
            .Select(x => new CustomerBillingSourceConflictDto(x.Id, x.BaseVersion, x.ExistingSourceKind,
                x.IncomingSourceKind, x.IncomingSourceReference, SplitFields(x.ChangedFields), x.Status,
                x.UsedIncomingValues, x.DecisionReason, x.DetectedUtc, x.DecidedUtc, x.Version))
            .ToListAsync(cancellationToken);
        return new CustomerBillingProfileDto(profile.Id, profile.CompanyId, profile.CounterpartyId,
            ToInput(profile.ToValues()), profile.ConflictState, profile.MergedIntoCounterpartyId, profile.Version,
            profile.CreatedByUserId, profile.UpdatedByUserId, profile.CreatedUtc, profile.UpdatedUtc, conflicts);
    }

    private async Task<IReadOnlyList<CustomerDuplicateCandidateDto>> MapCandidatesAsync(Guid companyId,
        IReadOnlyList<CustomerDuplicateCandidate> candidates, CancellationToken cancellationToken)
    {
        var ids = candidates.SelectMany(x => new[] { x.FirstCounterpartyId, x.SecondCounterpartyId }).Distinct().ToArray();
        var names = await _db.FinanceCounterparties.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        return candidates.Select(x => new CustomerDuplicateCandidateDto(x.Id, x.CompanyId, x.FirstCounterpartyId,
            names.GetValueOrDefault(x.FirstCounterpartyId, "Customer"), x.SecondCounterpartyId,
            names.GetValueOrDefault(x.SecondCounterpartyId, "Customer"), x.Score,
            JsonSerializer.Deserialize<IReadOnlyList<CustomerDuplicateEvidenceDto>>(x.EvidenceJson, SerializerOptions) ?? [],
            x.Status, x.MergeSourceCounterpartyId, x.MergeTargetCounterpartyId, x.DecisionReason,
            x.DetectedUtc, x.UpdatedUtc, x.Version)).ToArray();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw ConcurrencyConflict(); }
        catch (DbUpdateException) { throw ConcurrencyConflict(); }
    }

    private Task WriteAuditAsync(Guid companyId, Guid actorId, string action, Guid targetId, string outcome,
        string summary, IReadOnlyDictionary<string, string?> metadata, string? correlationId, string? diff,
        CancellationToken cancellationToken) => _audit.WriteAsync(new AuditEventWriteRequest(companyId,
            AuditActorTypes.User, actorId, action, "customer_billing_profile", targetId.ToString("D"), outcome,
            summary, ["customer_billing_profile"], metadata, correlationId, _timeProvider.GetUtcNow().UtcDateTime,
            PayloadDiffJson: diff), cancellationToken);

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId));
        if (_companyContext.CompanyId is Guid current && current != companyId)
            throw new UnauthorizedAccessException("Customer billing data is scoped to the active company.");
    }
    private static void EnsureActor(Guid actorId) { if (actorId == Guid.Empty) throw new UnauthorizedAccessException("An authenticated user is required."); }
    private static CustomerBillingException ConcurrencyConflict() => new(CustomerBillingReasonCodes.ConcurrencyConflict,
        "The customer billing record changed while this request was being applied. Reload and try again.", true);
    private static bool RequiresSourceReview(string existing, string incoming) => existing != incoming &&
        (existing == CustomerBillingSourceKinds.Provider || incoming == CustomerBillingSourceKinds.Provider);
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string JoinFields(IEnumerable<string> fields) => string.Join('|', fields.OrderBy(x => x, StringComparer.Ordinal));
    private static IReadOnlyList<string> SplitFields(string fields) => fields.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static IReadOnlyList<string> ChangedFields(CustomerBillingProfileInputDto current, CustomerBillingProfileInputDto incoming)
    {
        var result = new List<string>();
        foreach (var property in typeof(CustomerBillingProfileInputDto).GetProperties())
            if (!string.Equals(JsonSerializer.Serialize(property.GetValue(current), SerializerOptions),
                JsonSerializer.Serialize(property.GetValue(incoming), SerializerOptions), StringComparison.Ordinal)) result.Add(property.Name);
        return result;
    }
    private static CustomerBillingProfileInputDto ValidateAndNormalize(CustomerBillingProfileInputDto input,
        Guid companyId, Guid counterpartyId, Guid actorId, DateTime nowUtc)
    {
        var normalized = new CustomerBillingProfile(Guid.NewGuid(), companyId, counterpartyId, ToValues(input), actorId, nowUtc);
        return ToInput(normalized.ToValues());
    }
    private static CustomerBillingProfileValues ToValues(CustomerBillingProfileInputDto x) => new(x.LegalName, x.DisplayName,
        x.PartyKind, x.TaxIdentifier, x.VatIdentifier, x.IdentityValidationState, x.BillingAddress.Line1,
        x.BillingAddress.Line2, x.BillingAddress.PostalCode, x.BillingAddress.City, x.BillingAddress.Region,
        x.BillingAddress.CountryCode, x.DeliveryAddress?.Line1, x.DeliveryAddress?.Line2, x.DeliveryAddress?.PostalCode,
        x.DeliveryAddress?.City, x.DeliveryAddress?.Region, x.DeliveryAddress?.CountryCode, x.LanguageCode, x.CurrencyCode,
        x.PaymentTermKind, x.PaymentTermDays, x.PaymentMethod, x.InvoiceDeliveryChannel, x.InvoiceDeliveryEmail,
        x.BuyerReference, x.EInvoiceIdentifier, x.EInvoiceIdentifierType, x.CreditLimit, x.CreditStatus,
        x.DefaultAccountMapping, x.DefaultDimensionCode, x.EffectiveFrom, x.EffectiveTo, x.SourceKind, x.SourceReference,
        x.UserAttestedUtc, x.ExternallyVerifiedUtc, x.VerificationSource);
    private static CustomerBillingProfileInputDto ToInput(CustomerBillingProfileValues x) => new(x.LegalName, x.DisplayName,
        x.PartyKind, x.TaxIdentifier, x.VatIdentifier, x.IdentityValidationState,
        new CustomerBillingAddressDto(x.BillingAddressLine1, x.BillingAddressLine2, x.BillingPostalCode, x.BillingCity, x.BillingRegion, x.BillingCountryCode),
        x.DeliveryAddressLine1 is null ? null : new CustomerBillingAddressDto(x.DeliveryAddressLine1, x.DeliveryAddressLine2,
            x.DeliveryPostalCode!, x.DeliveryCity!, x.DeliveryRegion, x.DeliveryCountryCode!), x.LanguageCode, x.CurrencyCode,
        x.PaymentTermKind, x.PaymentTermDays, x.PaymentMethod, x.InvoiceDeliveryChannel, x.InvoiceDeliveryEmail,
        x.BuyerReference, x.EInvoiceIdentifier, x.EInvoiceIdentifierType, x.CreditLimit, x.CreditStatus,
        x.DefaultAccountMapping, x.DefaultDimensionCode, x.EffectiveFrom, x.EffectiveTo, x.SourceKind, x.SourceReference,
        x.UserAttestedUtc, x.ExternallyVerifiedUtc, x.VerificationSource);
}
