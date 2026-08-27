using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class StatutoryDocumentService : IStatutoryDocumentService
{
    private const int MaximumAttempts = 5;
    private readonly VirtualCompanyDbContext _db;
    private readonly IStatutoryDocumentPolicy _policy;
    private readonly IAccountingPolicyPackResolver _packs;
    private readonly IAuditEventWriter _audit;
    private readonly AccountingOperationsTelemetry _telemetry;
    private readonly TimeProvider _time;

    public StatutoryDocumentService(VirtualCompanyDbContext db, IStatutoryDocumentPolicy policy,
        IAccountingPolicyPackResolver packs, IAuditEventWriter audit,
        AccountingOperationsTelemetry telemetry, TimeProvider time)
    {
        _db = db; _policy = policy; _packs = packs; _audit = audit; _telemetry = telemetry; _time = time;
    }

    public Task<StatutoryDocumentPolicyDecisionDto> PreviewAsync(PreviewStatutoryDocumentQuery query, CancellationToken cancellationToken) =>
        _policy.EvaluateAsync(query, cancellationToken);

    public async Task<IReadOnlyList<StatutoryDocumentSeriesDto>> ListSeriesAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await _db.StatutoryDocumentSeries.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.FiscalYearStart).ThenBy(x => x.Code).ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<StatutoryDocumentSeriesDto> CreateSeriesAsync(CreateStatutoryDocumentSeriesCommand command, CancellationToken cancellationToken)
    {
        ValidateCompanyActor(command.CompanyId, command.ActorUserId);
        EnsureNativeCustomerType(command.DocumentType);
        var now = _time.GetUtcNow().UtcDateTime;
        var series = new StatutoryDocumentSeries(Guid.NewGuid(), command.CompanyId, command.Code, command.DocumentType,
            command.FiscalYearStart, command.FiscalYearEnd, command.Prefix, command.NumberWidth,
            command.FirstNumber, command.ActorUserId, now);
        _db.StatutoryDocumentSeries.Add(series);
        await _audit.WriteAsync(Audit(command.CompanyId, command.ActorUserId, AuditEventActions.StatutoryDocumentSeriesCreated,
            AuditTargetTypes.StatutoryDocumentSeries, series.Id, "created", command.CorrelationId,
            new Dictionary<string, string?> { ["series_code"] = series.Code, ["document_type"] = series.DocumentType, ["fiscal_year"] = series.FiscalYearKey }), cancellationToken);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { throw Conflict(StatutoryDocumentReasonCodes.SeriesConflict, "A series with this code and fiscal year already exists."); }
        _telemetry.StatutoryDocumentSeriesChanged(command.CompanyId, "created", series.DocumentType, command.CorrelationId);
        return Map(series);
    }

    public async Task<StatutoryDocumentSeriesDto> UpdateSeriesAsync(UpdateStatutoryDocumentSeriesCommand command, CancellationToken cancellationToken)
    {
        ValidateCompanyActor(command.CompanyId, command.ActorUserId);
        var series = await _db.StatutoryDocumentSeries.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.SeriesId, cancellationToken)
            ?? throw NotFound(StatutoryDocumentReasonCodes.SeriesNotFound, "The document series was not found.");
        if (series.Version != command.ExpectedVersion) throw Conflict(StatutoryDocumentReasonCodes.VersionConflict, "The series changed after it was loaded. Reload and try again.");
        var before = JsonSerializer.Serialize(new { series.Prefix, series.NumberWidth, series.IsActive, series.Version });
        series.Update(command.Prefix, command.NumberWidth, command.IsActive, command.ActorUserId, _time.GetUtcNow().UtcDateTime);
        await _audit.WriteAsync(Audit(command.CompanyId, command.ActorUserId, AuditEventActions.StatutoryDocumentSeriesUpdated,
            AuditTargetTypes.StatutoryDocumentSeries, series.Id, "updated", command.CorrelationId,
            new Dictionary<string, string?> { ["before"] = before, ["after"] = JsonSerializer.Serialize(new { series.Prefix, series.NumberWidth, series.IsActive, series.Version }) }), cancellationToken);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Conflict(StatutoryDocumentReasonCodes.VersionConflict, "The series changed while it was being saved. Reload and try again."); }
        _telemetry.StatutoryDocumentSeriesChanged(command.CompanyId, "updated", series.DocumentType, command.CorrelationId);
        return Map(series);
    }

    public async Task<IReadOnlyList<StatutoryDocumentAllocationDto>> ListAllocationsAsync(Guid companyId, Guid? seriesId, CancellationToken cancellationToken)
    {
        var query = _db.StatutoryDocumentNumberAllocations.AsNoTracking().Include(x => x.Series).Where(x => x.CompanyId == companyId);
        if (seriesId.HasValue) query = query.Where(x => x.SeriesId == seriesId.Value);
        return (await query.OrderByDescending(x => x.AllocatedUtc).ThenByDescending(x => x.Number).Take(1000).ToListAsync(cancellationToken)).Select(x => Map(x)).ToArray();
    }

    public async Task<StatutoryDocumentAllocationDto> RecordGapAsync(RecordStatutoryDocumentGapCommand command, CancellationToken cancellationToken)
    {
        ValidateCompanyActor(command.CompanyId, command.ActorUserId);
        if (string.IsNullOrWhiteSpace(command.Reason)) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "A clear operator reason is required for a preserved number gap.");
        if (command.Reason.Trim().Length > 512) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "The gap reason must be 512 characters or fewer.");
        var replay = await FindAllocationReplay(command.CompanyId, command.BusinessKey, command.SourceVersion, cancellationToken);
        if (replay is not null) return Map(replay);
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var series = await _db.StatutoryDocumentSeries.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.SeriesId, cancellationToken)
                    ?? throw NotFound(StatutoryDocumentReasonCodes.SeriesNotFound, "The document series was not found.");
                var now = _time.GetUtcNow().UtcDateTime;
                var number = series.Allocate(command.ActorUserId, now);
                var allocation = new StatutoryDocumentNumberAllocation(Guid.NewGuid(), command.CompanyId, series.Id,
                    series.FiscalYearKey, number, series.Format(number), StatutoryDocumentAllocationStatuses.Gap,
                    command.Reason.Trim(), RequiredBusinessKey(command.BusinessKey), command.SourceVersion, null,
                    command.ActorUserId, now);
                _db.StatutoryDocumentNumberAllocations.Add(allocation);
                await _audit.WriteAsync(Audit(command.CompanyId, command.ActorUserId, AuditEventActions.StatutoryDocumentNumberGapRecorded,
                    AuditTargetTypes.StatutoryDocumentNumberAllocation, allocation.Id, "recorded", command.CorrelationId,
                    new Dictionary<string, string?> { ["series_code"] = series.Code, ["number"] = allocation.FormattedNumber, ["reason"] = allocation.GapReason }), cancellationToken);
                await _db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
                _telemetry.StatutoryDocumentNumberAllocated(command.CompanyId, series.DocumentType, "gap", command.CorrelationId);
                return Map(allocation, series.Code);
            }
            catch (Exception exception) when (attempt < MaximumAttempts && IsRetryableConcurrency(exception))
            {
                await tx.RollbackAsync(cancellationToken); _db.ChangeTracker.Clear();
                var existing = await FindAllocationReplay(command.CompanyId, command.BusinessKey, command.SourceVersion, cancellationToken);
                if (existing is not null) return Map(existing);
            }
        }
        throw Conflict(StatutoryDocumentReasonCodes.SeriesConflict, "The number could not be allocated safely after concurrent changes. Try again.");
    }

    public async Task<StatutoryIssuedDocumentDto> IssueNativeCustomerAsync(IssueNativeCustomerDocumentCommand command, CancellationToken cancellationToken)
    {
        ValidateCompanyActor(command.CompanyId, command.ActorUserId);
        if (!string.Equals(command.Document.Authority, StatutoryDocumentAuthorities.Native, StringComparison.OrdinalIgnoreCase))
            throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.NativeIssuanceUnavailable, "Native issue commands must use native number authority.");
        EnsureNativeCustomerType(command.Document.DocumentType);
        await EnsureAllowed(command.CompanyId, command.Document, cancellationToken);
        var replay = await FindIssuedReplay(command.CompanyId, command.BusinessKey, command.Document.SourceVersion, cancellationToken);
        if (replay is not null) return Map(replay);

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var series = await _db.StatutoryDocumentSeries.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.SeriesId, cancellationToken)
                    ?? throw NotFound(StatutoryDocumentReasonCodes.SeriesNotFound, "The document series was not found.");
                if (!string.Equals(series.DocumentType, command.Document.DocumentType, StringComparison.OrdinalIgnoreCase) || command.Document.IssueDate < series.FiscalYearStart || command.Document.IssueDate > series.FiscalYearEnd)
                    throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.SeriesConflict, "The selected series does not match this document type and fiscal year.");
                var counterparty = await _db.FinanceCounterparties.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.Document.CounterpartyId, cancellationToken)
                    ?? throw NotFound(StatutoryDocumentReasonCodes.SourceNotFound, "The customer counterparty was not found.");
                if (counterparty.CounterpartyType != "customer") throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Native customer documents require a customer counterparty.");
                var context = await LoadContext(command.CompanyId, cancellationToken);
                var original = await LoadAndValidateOriginal(command.CompanyId, command.Document, cancellationToken);
                await EnsureApprovalsAsync(command.CompanyId, command.Document.ApprovalIds, cancellationToken);
                var now = _time.GetUtcNow().UtcDateTime;
                var number = series.Allocate(command.ActorUserId, now);
                var formatted = series.Format(number);
                var sourceId = Guid.NewGuid(); var issuedId = Guid.NewGuid();
                var isCredit = command.Document.DocumentType == StatutoryDocumentTypes.CustomerCredit;
                var invoice = new FinanceInvoice(sourceId, command.CompanyId, counterparty.Id, formatted,
                    command.Document.IssueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    command.Document.DueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    isCredit ? -Math.Abs(command.Document.GrossTotal) : Math.Abs(command.Document.GrossTotal),
                    command.Document.Currency, "approved", documentKind: isCredit ? FinanceDocumentKinds.CreditNote : FinanceDocumentKinds.Invoice,
                    createdUtc: now, updatedUtc: now);
                var issued = BuildIssued(issuedId, command.CompanyId, sourceId, formatted, command.BusinessKey,
                    command.Document, context, series.Id, series.FiscalYearKey, number, original?.Id, command.ActorUserId, now);
                var allocation = new StatutoryDocumentNumberAllocation(Guid.NewGuid(), command.CompanyId, series.Id,
                    series.FiscalYearKey, number, formatted, StatutoryDocumentAllocationStatuses.Issued, null,
                    RequiredBusinessKey(command.BusinessKey), command.Document.SourceVersion, issued.Id, command.ActorUserId, now);
                _db.FinanceInvoices.Add(invoice); _db.IssuedStatutoryDocuments.Add(issued); _db.StatutoryDocumentNumberAllocations.Add(allocation);
                await _audit.WriteAsync(Audit(command.CompanyId, command.ActorUserId, AuditEventActions.StatutoryDocumentIssued,
                    AuditTargetTypes.IssuedStatutoryDocument, issued.Id, "issued", command.CorrelationId,
                    new Dictionary<string, string?> { ["document_type"] = issued.DocumentType, ["document_number"] = issued.DocumentNumber, ["snapshot_hash"] = issued.SnapshotHash, ["source_record_id"] = sourceId.ToString("D") }), cancellationToken);
                await _db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
                _telemetry.StatutoryDocumentNumberAllocated(command.CompanyId, issued.DocumentType, "issued", command.CorrelationId);
                return Map(issued);
            }
            catch (Exception exception) when (attempt < MaximumAttempts && IsRetryableConcurrency(exception))
            {
                await tx.RollbackAsync(cancellationToken); _db.ChangeTracker.Clear();
                var existing = await FindIssuedReplay(command.CompanyId, command.BusinessKey, command.Document.SourceVersion, cancellationToken);
                if (existing is not null) return Map(existing);
            }
        }
        throw Conflict(StatutoryDocumentReasonCodes.SeriesConflict, "The document could not be issued safely after concurrent changes. Try again.");
    }

    public async Task<StatutoryIssuedDocumentDto> RegisterImportedAsync(RegisterImportedStatutoryDocumentCommand command, CancellationToken cancellationToken)
    {
        ValidateCompanyActor(command.CompanyId, command.ActorUserId);
        if (string.Equals(command.Document.Authority, StatutoryDocumentAuthorities.Native, StringComparison.OrdinalIgnoreCase))
            throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.NativeIssuanceUnavailable, "Use the native issue operation for native customer documents.");
        await EnsureAllowed(command.CompanyId, command.Document, cancellationToken);
        var replay = await FindIssuedReplay(command.CompanyId, command.BusinessKey, command.Document.SourceVersion, cancellationToken);
        if (replay is not null) return Map(replay);
        var documentNumber = RequiredDocumentNumber(command.Document.ProviderDocumentNumber!);
        await ValidateImportedSource(command.CompanyId, command.SourceRecordId, command.Document, documentNumber, cancellationToken);
        var context = await LoadContext(command.CompanyId, cancellationToken);
        var original = await LoadAndValidateOriginal(command.CompanyId, command.Document, cancellationToken);
        await EnsureApprovalsAsync(command.CompanyId, command.Document.ApprovalIds, cancellationToken);
        var now = _time.GetUtcNow().UtcDateTime;
        var issued = BuildIssued(Guid.NewGuid(), command.CompanyId, command.SourceRecordId, documentNumber,
            command.BusinessKey, command.Document, context, null, null, null, original?.Id, command.ActorUserId, now);
        _db.IssuedStatutoryDocuments.Add(issued);
        await _audit.WriteAsync(Audit(command.CompanyId, command.ActorUserId, AuditEventActions.StatutoryDocumentImportedRegistered,
            AuditTargetTypes.IssuedStatutoryDocument, issued.Id, "registered", command.CorrelationId,
            new Dictionary<string, string?> { ["authority"] = issued.Authority, ["document_type"] = issued.DocumentType, ["document_number"] = issued.DocumentNumber, ["snapshot_hash"] = issued.SnapshotHash }), cancellationToken);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            replay = await FindIssuedReplay(command.CompanyId, command.BusinessKey, command.Document.SourceVersion, cancellationToken);
            if (replay is not null) return Map(replay);
            throw Conflict(StatutoryDocumentReasonCodes.SourceAlreadyIssued, "This source document or business key is already registered with different immutable facts.");
        }
        _telemetry.StatutoryDocumentRegistered(command.CompanyId, issued.DocumentType, issued.Authority, command.CorrelationId);
        return Map(issued);
    }

    public async Task<StatutoryIssuedDocumentDto> GetIssuedAsync(Guid companyId, Guid issuedDocumentId, CancellationToken cancellationToken) =>
        Map(await _db.IssuedStatutoryDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == issuedDocumentId, cancellationToken)
            ?? throw NotFound(StatutoryDocumentReasonCodes.SourceNotFound, "The issued document was not found."));

    public async Task<StatutoryIssuedDocumentDto> AttachEvidenceAsync(AttachStatutoryDocumentEvidenceCommand command, CancellationToken cancellationToken)
    {
        ValidateCompanyActor(command.CompanyId, command.ActorUserId);
        var issued = await _db.IssuedStatutoryDocuments.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.IssuedDocumentId, cancellationToken)
            ?? throw NotFound(StatutoryDocumentReasonCodes.SourceNotFound, "The issued document was not found.");
        if (issued.EvidenceVersion != command.ExpectedEvidenceVersion)
            throw Conflict(StatutoryDocumentReasonCodes.VersionConflict, "Document evidence changed after it was loaded. Reload and try again.");
        issued.AttachEvidence(command.RenderedEvidenceReference, command.DeliveryEvidenceReference);
        await _audit.WriteAsync(Audit(command.CompanyId, command.ActorUserId, AuditEventActions.StatutoryDocumentEvidenceAttached,
            AuditTargetTypes.IssuedStatutoryDocument, issued.Id, "updated", command.CorrelationId,
            new Dictionary<string, string?> { ["snapshot_hash"] = issued.SnapshotHash, ["evidence_version"] = issued.EvidenceVersion.ToString() }), cancellationToken);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Conflict(StatutoryDocumentReasonCodes.VersionConflict, "Document evidence changed while it was being saved. Reload and try again."); }
        return Map(issued);
    }

    private async Task EnsureAllowed(Guid companyId, StatutoryDocumentInput input, CancellationToken cancellationToken)
    {
        var decision = await _policy.EvaluateAsync(new(companyId, input), cancellationToken);
        if (!decision.IsAllowed)
        {
            var first = decision.Issues[0];
            _telemetry.StatutoryDocumentBlocked(companyId, input.DocumentType, first.ReasonCode);
            throw new StatutoryDocumentException(first.ReasonCode, first.Explanation);
        }
    }

    private async Task<(CompanyStatutoryProfile Profile, IAccountingPolicyPack Pack)> LoadContext(Guid companyId, CancellationToken cancellationToken)
    {
        var profile = await _db.CompanyStatutoryProfiles.AsNoTracking().SingleAsync(x => x.CompanyId == companyId, cancellationToken);
        var configuration = await _db.AccountingConfigurations.AsNoTracking().SingleAsync(x => x.CompanyId == companyId, cancellationToken);
        return (profile, _packs.Resolve(configuration.PolicyPackKey, configuration.PolicyPackVersion));
    }

    private async Task<IssuedStatutoryDocument?> LoadAndValidateOriginal(Guid companyId, StatutoryDocumentInput input, CancellationToken cancellationToken)
    {
        if (input.DocumentType is not (StatutoryDocumentTypes.CustomerCredit or StatutoryDocumentTypes.SupplierCredit)) return null;
        var original = await _db.IssuedStatutoryDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == input.OriginalIssuedDocumentId, cancellationToken)
            ?? throw NotFound(StatutoryDocumentReasonCodes.CreditReferenceRequired, "The referenced original issued document was not found.");
        var expected = input.DocumentType == StatutoryDocumentTypes.CustomerCredit ? StatutoryDocumentTypes.CustomerInvoice : StatutoryDocumentTypes.SupplierInvoice;
        if (original.DocumentType != expected) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.CreditReferenceRequired, "The credit note must reference an original document of the matching type.");
        return original;
    }

    private async Task ValidateImportedSource(Guid companyId, Guid sourceId, StatutoryDocumentInput input, string documentNumber, CancellationToken cancellationToken)
    {
        if (sourceId == Guid.Empty) throw NotFound(StatutoryDocumentReasonCodes.SourceNotFound, "A source record is required.");
        if (input.DocumentType is StatutoryDocumentTypes.CustomerInvoice or StatutoryDocumentTypes.CustomerCredit)
        {
            var source = await _db.FinanceInvoices.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sourceId, cancellationToken)
                ?? throw NotFound(StatutoryDocumentReasonCodes.SourceNotFound, "The customer document source was not found.");
            if (!string.Equals(source.InvoiceNumber, documentNumber, StringComparison.Ordinal) || !Same(source.Amount, SignedTotal(input)) || !string.Equals(source.Currency, input.Currency, StringComparison.OrdinalIgnoreCase))
                throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.TotalsMismatch, "Imported snapshot facts must match the retained customer source number, currency, and gross total.");
        }
        else
        {
            var source = await _db.FinanceBills.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sourceId, cancellationToken)
                ?? throw NotFound(StatutoryDocumentReasonCodes.SourceNotFound, "The supplier document source was not found.");
            if (!string.Equals(source.BillNumber, documentNumber, StringComparison.Ordinal) || !Same(source.Amount, SignedTotal(input)) || !string.Equals(source.Currency, input.Currency, StringComparison.OrdinalIgnoreCase))
                throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.TotalsMismatch, "Imported snapshot facts must match the retained supplier source number, currency, and gross total.");
        }
    }

    private async Task EnsureApprovalsAsync(Guid companyId, IReadOnlyList<Guid>? approvalIds, CancellationToken cancellationToken)
    {
        var ids = (approvalIds ?? []).Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return;
        var approvedCount = await _db.ApprovalRequests.AsNoTracking().CountAsync(x =>
            x.CompanyId == companyId && ids.Contains(x.Id) && x.Status == ApprovalRequestStatus.Approved, cancellationToken);
        if (approvedCount != ids.Length)
            throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing,
                "Every supplied approval reference must identify a current approved request in this company.");
    }

    private static decimal SignedTotal(StatutoryDocumentInput input) => input.DocumentType is StatutoryDocumentTypes.CustomerCredit or StatutoryDocumentTypes.SupplierCredit ? -Math.Abs(input.GrossTotal) : Math.Abs(input.GrossTotal);
    private static bool Same(decimal left, decimal right) => decimal.Round(left, 2, MidpointRounding.AwayFromZero) == decimal.Round(right, 2, MidpointRounding.AwayFromZero);

    private static IssuedStatutoryDocument BuildIssued(Guid id, Guid companyId, Guid sourceId, string number,
        string businessKey, StatutoryDocumentInput input, (CompanyStatutoryProfile Profile, IAccountingPolicyPack Pack) context,
        Guid? seriesId, string? fiscalYearKey, long? sequence, Guid? originalId, Guid actorId, DateTime now)
    {
        var approvals = (input.ApprovalIds ?? []).Where(x => x != Guid.Empty).Distinct().Order().ToArray();
        var companyParty = new PartySnapshot(context.Profile.LegalName, context.Profile.SwedishOrganisationNumber,
            context.Profile.VatRegistrationNumber, context.Profile.RegisteredAddressLine1,
            context.Profile.RegisteredAddressLine2, context.Profile.RegisteredPostalCode,
            context.Profile.RegisteredCity, context.Profile.RegisteredCountryCode);
        var counterparty = new PartySnapshot(input.CounterpartyLegalName, null,
            input.CounterpartyVatIdentifier, input.CounterpartyAddressLine1, null,
            input.CounterpartyPostalCode, input.CounterpartyCity, input.CounterpartyCountryCode);
        var isSupplierDocument = input.DocumentType is StatutoryDocumentTypes.SupplierInvoice or StatutoryDocumentTypes.SupplierCredit;
        var snapshot = JsonSerializer.Serialize(new
        {
            schemaVersion = "swedish-statutory-documents-2026.1", documentNumber = number,
            document = input, seller = isSupplierDocument ? counterparty : companyParty,
            buyer = isSupplierDocument ? companyParty : counterparty, approvals
        });
        if (Encoding.UTF8.GetByteCount(snapshot) > 32768) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "The immutable document snapshot exceeds the supported size.");
        var taxFacts = string.IsNullOrWhiteSpace(input.TaxFactsJson) ? "{}" : input.TaxFactsJson.Trim();
        if (Encoding.UTF8.GetByteCount(taxFacts) > 16384) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Tax facts exceed the supported size.");
        try { JsonDocument.Parse(taxFacts).Dispose(); } catch (JsonException) { throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Tax facts must be valid JSON."); }
        return new IssuedStatutoryDocument(id, companyId, input.DocumentType.Trim().ToLowerInvariant(), input.Authority.Trim().ToLowerInvariant(),
            number, sourceId, input.SourceVersion, seriesId, fiscalYearKey, sequence, context.Profile.Id, context.Profile.Version,
            context.Pack.Definition.PackKey, context.Pack.Definition.Version, context.Pack.DefinitionHash,
            snapshot, Hash(snapshot), taxFacts, JsonSerializer.Serialize(approvals), RequiredBusinessKey(businessKey), originalId, actorId, now);
    }

    private async Task<IssuedStatutoryDocument?> FindIssuedReplay(Guid companyId, string key, long version, CancellationToken ct)
    {
        key = RequiredBusinessKey(key); if (version <= 0) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Source version must be positive.");
        var exact = await _db.IssuedStatutoryDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.BusinessKey == key && x.SourceVersion == version, ct);
        if (exact is not null) return exact;
        if (await _db.IssuedStatutoryDocuments.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.BusinessKey == key, ct))
            throw Conflict(StatutoryDocumentReasonCodes.IdempotencyConflict, "The business key was already used for another source version.");
        return null;
    }

    private async Task<StatutoryDocumentNumberAllocation?> FindAllocationReplay(Guid companyId, string key, long version, CancellationToken ct)
    {
        key = RequiredBusinessKey(key); if (version <= 0) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Source version must be positive.");
        return await _db.StatutoryDocumentNumberAllocations.AsNoTracking().Include(x => x.Series).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.BusinessKey == key && x.SourceVersion == version, ct);
    }

    private static string RequiredBusinessKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "A stable business key is required.");
        var normalized = value.Trim(); if (normalized.Length > 128) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Business keys and document numbers must be 128 characters or fewer.");
        return normalized;
    }
    private static string RequiredDocumentNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "A document number is required.");
        var normalized = value.Trim();
        if (normalized.Length > 64) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Document numbers must be 64 characters or fewer.");
        return normalized;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool IsRetryableConcurrency(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException) return true;
            if (current is SqlException sql && sql.Number is 1205 or 1222 or 2601 or 2627) return true;
        }
        return exception is DbUpdateException;
    }
    private static void EnsureNativeCustomerType(string type)
    {
        var normalized = type?.Trim().ToLowerInvariant();
        if (normalized is not (StatutoryDocumentTypes.CustomerInvoice or StatutoryDocumentTypes.CustomerCredit))
            throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.NativeIssuanceUnavailable, "Native series support customer invoices and customer credit notes only.");
    }
    private static void ValidateCompanyActor(Guid companyId, Guid actorId)
    {
        if (companyId == Guid.Empty || actorId == Guid.Empty) throw new StatutoryDocumentException(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Company and authenticated actor are required.");
    }
    private static StatutoryDocumentException NotFound(string code, string message) => new(code, message);
    private static StatutoryDocumentException Conflict(string code, string message) => new(code, message, true);
    private static AuditEventWriteRequest Audit(Guid companyId, Guid actorId, string action, string targetType, Guid targetId,
        string outcome, string? correlationId, IReadOnlyDictionary<string, string?> metadata) =>
        new(companyId, AuditActorTypes.User, actorId, action, targetType, targetId.ToString("D"), outcome,
            "Statutory document control changed through the authorized Finance boundary.", ["accounting_configuration", "statutory_profile", "statutory_document_policy"], metadata, correlationId);

    private static StatutoryDocumentSeriesDto Map(StatutoryDocumentSeries x) => new(x.Id, x.Code, x.DocumentType, x.FiscalYearStart, x.FiscalYearEnd, x.Prefix, x.NumberWidth, x.NextNumber, x.IsActive, x.Version, x.CreatedUtc, x.UpdatedUtc);
    private static StatutoryDocumentAllocationDto Map(StatutoryDocumentNumberAllocation x, string? code = null) => new(x.Id, x.SeriesId, code ?? x.Series.Code, x.FiscalYearKey, x.Number, x.FormattedNumber, x.Status, x.GapReason, x.BusinessKey, x.SourceVersion, x.IssuedDocumentId, x.ActorUserId, x.AllocatedUtc);
    private static StatutoryIssuedDocumentDto Map(IssuedStatutoryDocument x) => new(x.Id, x.DocumentType, x.Authority, x.DocumentNumber, x.SourceRecordId, x.SourceVersion, x.SeriesId, x.FiscalYearKey, x.SequenceNumber, x.StatutoryProfileId, x.StatutoryProfileVersion, x.PolicyPackKey, x.PolicyPackVersion, x.PolicyPackDefinitionHash, x.SnapshotHash, x.OriginalIssuedDocumentId, x.IssuedUtc, true, DeserializeIds(x.ApprovalIdsJson), x.RenderedEvidenceReference, x.DeliveryEvidenceReference, x.EvidenceVersion);
    private static IReadOnlyList<Guid> DeserializeIds(string json) { try { return JsonSerializer.Deserialize<Guid[]>(json) ?? []; } catch (JsonException) { return []; } }
    private sealed record PartySnapshot(string? LegalName, string? OrganisationNumber, string? VatIdentifier,
        string? AddressLine1, string? AddressLine2, string? PostalCode, string? City, string? CountryCode);
}
