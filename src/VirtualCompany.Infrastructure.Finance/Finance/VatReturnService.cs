using System.Globalization;
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
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class VatReturnService : IVatReturnService
{
    private const int MaximumSourceLineCount = 10_000;
    private const string PackageMediaType = "application/json";
    private static readonly JsonSerializerOptions PackageJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _membershipResolver;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly IApprovalRequestService _approvals;
    private readonly ICompanyDocumentStorage _storage;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _timeProvider;
    private readonly AccountingOperationsTelemetry? _telemetry;

    public VatReturnService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver membershipResolver,
        ICurrentUserAccessor currentUser, IAccountingPolicyPackResolver packResolver,
        IApprovalRequestService approvals, ICompanyDocumentStorage storage, IAuditEventWriter audit,
        TimeProvider timeProvider, AccountingOperationsTelemetry? telemetry = null)
    {
        _db = db;
        _membershipResolver = membershipResolver;
        _currentUser = currentUser;
        _packResolver = packResolver;
        _approvals = approvals;
        _storage = storage;
        _audit = audit;
        _timeProvider = timeProvider;
        _telemetry = telemetry;
    }

    public async Task<VatFilingPeriodDto> CreateFilingPeriodAsync(CreateVatFilingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        if (command.EndDate < command.StartDate)
            throw Error(VatReturnIssueCodes.FilingPeriodAmbiguous, "The VAT filing period end date must not precede its start date.");
        if (!string.Equals(command.Currency?.Trim(), "SEK", StringComparison.OrdinalIgnoreCase))
            throw Error(VatReturnIssueCodes.CurrencyMismatch, "The Swedish launch VAT return supports SEK filing periods only.");

        var overlap = await _db.VatFilingPeriods.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == command.CompanyId && x.StartDate <= command.EndDate && command.StartDate <= x.EndDate,
            cancellationToken);
        if (overlap)
            throw Error(VatReturnIssueCodes.FilingPeriodAmbiguous,
                "The requested VAT filing period overlaps another filing period. Resolve the dates before calculating a return.");

        if (command.FiscalPeriodId.HasValue)
        {
            var fiscal = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == command.CompanyId && x.Id == command.FiscalPeriodId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("The linked fiscal period was not found in the requested company.");
            var fiscalStart = DateOnly.FromDateTime(fiscal.StartUtc);
            var fiscalEnd = DateOnly.FromDateTime(fiscal.EndUtc.AddTicks(-1));
            if (command.StartDate < fiscalStart || command.EndDate > fiscalEnd)
                throw Error(VatReturnIssueCodes.FilingPeriodAmbiguous,
                    "The VAT filing period must fit completely inside its linked fiscal period.");
        }

        var now = Now();
        var period = new VatFilingPeriod(Guid.NewGuid(), command.CompanyId, command.PeriodCode,
            command.StartDate, command.EndDate, "SEK", command.FiscalPeriodId, now, command.DueDate);
        _db.VatFilingPeriods.Add(period);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.VatFilingPeriodCreated,
            AuditTargetTypes.VatFilingPeriod, period.Id, AuditEventOutcomes.Succeeded,
            "Created a bounded Swedish VAT filing period with non-overlapping dates.",
            new() { ["periodCode"] = period.PeriodCode, ["startDate"] = period.StartDate.ToString("yyyy-MM-dd"),
                ["endDate"] = period.EndDate.ToString("yyyy-MM-dd") }, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(period);
    }

    public async Task<IReadOnlyList<VatFilingPeriodDto>> ListFilingPeriodsAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        await RequireViewAsync(companyId, cancellationToken);
        return (await _db.VatFilingPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.StartDate)
            .ToListAsync(cancellationToken)).Select(Map).ToArray();
    }

    public async Task<VatFilingPeriodDto> SetFilingPeriodDueDateAsync(SetVatFilingPeriodDueDateCommand command,CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId,command.ActorUserId,cancellationToken);var period=await _db.VatFilingPeriods.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CompanyId==command.CompanyId&&x.Id==command.FilingPeriodId,cancellationToken)??throw new KeyNotFoundException("VAT filing period was not found in the requested company.");if(await _db.ComplianceObligationInstances.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==command.CompanyId&&x.VatFilingPeriodId==period.Id&&x.Status!=ComplianceObligationStatuses.Generated,cancellationToken))throw Error("compliance_deadline_frozen","The deadline is frozen after obligation preparation. Create a correction instead of rewriting the source deadline.");period.SetDueDate(command.DueDate);await WriteAuditAsync(command.CompanyId,command.ActorUserId,"vat_filing_period_due_date_set",AuditTargetTypes.VatFilingPeriod,period.Id,AuditEventOutcomes.Succeeded,"Recorded an explicit authority-sourced filing deadline; no statutory deadline was inferred.",new(){["dueDate"]=command.DueDate.ToString("yyyy-MM-dd"),["dueDateRule"]="explicit_operator_supplied"},cancellationToken);await SaveAsync(cancellationToken);return Map(period);
    }

    public async Task<VatReturnDto> CalculateAsync(CalculateVatReturnCommand command,
        CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var idempotencyKey = Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 200);
        var existingReplay = await _db.VatReturns.IgnoreQueryFilters()
            .Include(x => x.FilingPeriod).Include(x => x.Boxes).Include(x => x.Contributions)
            .Include(x => x.Issues).Include(x => x.Reviews)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (!command.VatReturnId.HasValue && existingReplay is not null)
            return await MapAsync(existingReplay, cancellationToken);

        var period = await _db.VatFilingPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.FilingPeriodId,
                cancellationToken) ?? throw new KeyNotFoundException("VAT filing period was not found in the requested company.");
        await EnsureUnambiguousAsync(period, cancellationToken);

        VatReturn vatReturn;
        if (command.VatReturnId.HasValue)
        {
            vatReturn = await LoadTrackedAsync(command.CompanyId, command.VatReturnId.Value, cancellationToken);
            if (vatReturn.FilingPeriodId != period.Id) throw new KeyNotFoundException("VAT return was not found for the requested filing period.");
            if (vatReturn.Status == VatReturnStatuses.Locked)
                throw Error("vat_return_locked", "A finalized VAT return is immutable. Create a linked correction return instead.");
        }
        else
        {
            var version = (await _db.VatReturns.IgnoreQueryFilters().Where(x => x.CompanyId == command.CompanyId && x.FilingPeriodId == period.Id)
                .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
            vatReturn = new VatReturn(Guid.NewGuid(), command.CompanyId, period.Id, version,
                idempotencyKey, null, null, null, Now());
            _db.VatReturns.Add(vatReturn);
        }

        var calculation = await BuildCalculationAsync(period, cancellationToken);
        _db.VatReturnBoxResults.RemoveRange(vatReturn.Boxes);
        _db.VatReturnSourceContributions.RemoveRange(vatReturn.Contributions);
        _db.VatReturnValidationIssues.RemoveRange(vatReturn.Issues);
        vatReturn.Boxes.Clear(); vatReturn.Contributions.Clear(); vatReturn.Issues.Clear();
        foreach (var box in calculation.Boxes)
            vatReturn.Boxes.Add(new VatReturnBoxResult(Guid.NewGuid(), command.CompanyId, vatReturn.Id,
                box.BoxCode, box.FactType, box.ExactAmount, box.FilingAmount, period.Currency, box.SourceCount));
        foreach (var item in calculation.Contributions)
            vatReturn.Contributions.Add(new VatReturnSourceContribution(Guid.NewGuid(), command.CompanyId, vatReturn.Id,
                item.LedgerEntryId, item.VoucherNumber, item.PostingDate, item.SourceType, item.SourceId,
                item.SourceVersion, item.PolicyPackKey, item.PolicyPackVersion, item.TaxRuleKey,
                item.TaxRuleVersion, item.BoxCode, item.FactType, item.ExactAmount, item.Currency, item.SourceChecksum));
        foreach (var issue in calculation.Issues)
            vatReturn.Issues.Add(new VatReturnValidationIssue(Guid.NewGuid(), command.CompanyId, vatReturn.Id,
                issue.Code, issue.Explanation, true, issue.LedgerEntryId, issue.SourceReference, issue.Difference));
        vatReturn.ReplaceCalculation(calculation.CutoffUtc, calculation.InputHash, calculation.Checksum,
            calculation.IncludedSourceCount, calculation.ExcludedSourceCount, calculation.OutputVat,
            calculation.InputVat, calculation.Settlement, calculation.SettlementFilingAmount,
            calculation.Issues.Count > 0);
        AddReview(vatReturn, new VatReturnReview(Guid.NewGuid(), command.CompanyId, vatReturn.Id,
            "calculated", command.ActorUserId, null, calculation.InputHash, calculation.CutoffUtc));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.VatReturnCalculated,
            AuditTargetTypes.VatReturn, vatReturn.Id,
            calculation.Issues.Count == 0 ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            calculation.Issues.Count == 0
                ? "Calculated the VAT return from immutable posted tax facts and reconciled control accounts."
                : "Calculated the VAT return and retained blocking review issues without inventing missing tax treatment.",
            new() { ["inputHash"] = calculation.InputHash, ["calculationChecksum"] = calculation.Checksum,
                ["includedSourceCount"] = calculation.IncludedSourceCount.ToString(CultureInfo.InvariantCulture),
                ["issueCount"] = calculation.Issues.Count.ToString(CultureInfo.InvariantCulture) }, cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry?.VatReturnCalculated(command.CompanyId, vatReturn.Status, calculation.IncludedSourceCount,
            calculation.Issues.Count);
        return await MapAsync(vatReturn, cancellationToken);
    }

    public async Task<VatReturnDto> GetAsync(GetVatReturnQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        return await MapAsync(await LoadReadAsync(query.CompanyId, query.VatReturnId, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<VatReturnDto>> ListAsync(ListVatReturnsQuery query,
        CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var items = await _db.VatReturns.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.FilingPeriod).Include(x => x.Boxes).Include(x => x.Contributions)
            .Include(x => x.Issues).Include(x => x.Reviews)
            .Where(x => x.CompanyId == query.CompanyId && (!query.FilingPeriodId.HasValue || x.FilingPeriodId == query.FilingPeriodId))
            .OrderByDescending(x => x.FilingPeriod.StartDate).ThenByDescending(x => x.Version)
            .Take(250).ToListAsync(cancellationToken);
        var result = new List<VatReturnDto>(items.Count);
        foreach (var item in items) result.Add(await MapAsync(item, cancellationToken));
        return result;
    }

    public async Task<VatReturnDto> RequestApprovalAsync(RequestVatReturnApprovalCommand command,
        CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var vatReturn = await LoadTrackedAsync(command.CompanyId, command.VatReturnId, cancellationToken);
        await EnsureCurrentAndCleanAsync(vatReturn, command.ExpectedInputHash, cancellationToken);
        if (vatReturn.ApprovalRequestId.HasValue)
            return await MapAsync(vatReturn, cancellationToken);

        await SaveAsync(cancellationToken); // Ensure a newly calculated target is visible to the approval boundary.
        var approval = await _approvals.CreateAsync(command.CompanyId, new CreateApprovalRequestCommand(
            ApprovalTargetEntityType.VatReturn.ToStorageValue(), vatReturn.Id, AuditActorTypes.Human,
            command.ActorUserId, "swedish_vat_return_finalization",
            new Dictionary<string, JsonNode?> { ["vatReturnId"] = vatReturn.Id, ["filingPeriodId"] = vatReturn.FilingPeriodId,
                ["version"] = vatReturn.Version, ["inputHash"] = vatReturn.InputHash,
                ["calculationChecksum"] = vatReturn.CalculationChecksum,
                ["settlementFilingAmount"] = vatReturn.SettlementFilingAmount, ["currency"] = vatReturn.FilingPeriod.Currency },
            RequiredRole: "finance_approver"), cancellationToken);
        var now = Now();
        vatReturn.AttachApproval(approval.Id, now);
        AddReview(vatReturn, new VatReturnReview(Guid.NewGuid(), command.CompanyId, vatReturn.Id,
            "approval_requested", command.ActorUserId, approval.Id, vatReturn.InputHash!, now));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.VatReturnApprovalRequested,
            AuditTargetTypes.VatReturn, vatReturn.Id, AuditEventOutcomes.Requested,
            "Submitted the current VAT return evidence for separate finance approval.",
            new() { ["approvalRequestId"] = approval.Id.ToString("D"), ["inputHash"] = vatReturn.InputHash }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await MapAsync(vatReturn, cancellationToken);
    }

    public async Task<VatReturnDto> FinalizeAsync(FinalizeVatReturnCommand command,
        CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var vatReturn = await LoadTrackedAsync(command.CompanyId, command.VatReturnId, cancellationToken);
        if (vatReturn.Status == VatReturnStatuses.Locked) return await MapAsync(vatReturn, cancellationToken);
        await EnsureCurrentAndCleanAsync(vatReturn, command.ExpectedInputHash, cancellationToken);
        if (!vatReturn.ApprovalRequestId.HasValue)
            throw Error(VatReturnIssueCodes.ApprovalRequired, "The current VAT return must be approved before it can be finalized.");
        var approval = await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == command.CompanyId && x.Id == vatReturn.ApprovalRequestId.Value &&
            x.TargetEntityId == vatReturn.Id && x.TargetEntityType == ApprovalTargetEntityType.VatReturn.ToStorageValue(),
            cancellationToken);
        if (approval?.Status != ApprovalRequestStatus.Approved)
            throw Error(VatReturnIssueCodes.ApprovalRequired, "The VAT return approval is not currently approved.");

        var now = Now();
        vatReturn.MarkApproved(now);
        AddReview(vatReturn, new VatReturnReview(Guid.NewGuid(), command.CompanyId, vatReturn.Id,
            "approved_evidence_rechecked", command.ActorUserId, approval.Id, vatReturn.InputHash!, now));
        var package = BuildPackage(vatReturn, now, command.ActorUserId, approval.Id);
        var content = JsonSerializer.SerializeToUtf8Bytes(package, PackageJson);
        var checksum = Hash(content);
        var fileName = $"swedish-vat-return-{SafeFile(vatReturn.FilingPeriod.PeriodCode)}-v{vatReturn.Version}.json";
        var storageKey = $"{command.CompanyId:N}/finance/vat-returns/{vatReturn.Id:N}/{checksum}.json";
        await using (var stream = new MemoryStream(content, writable: false))
            await _storage.WriteAsync(new DocumentStorageWriteRequest(command.CompanyId, vatReturn.Id, storageKey,
                fileName, PackageMediaType, stream), cancellationToken);

        vatReturn.Finalize(command.ActorUserId, now, storageKey, checksum, fileName, PackageMediaType, content.LongLength);
        AddReview(vatReturn, new VatReturnReview(Guid.NewGuid(), command.CompanyId, vatReturn.Id,
            "finalized", command.ActorUserId, approval.Id, checksum, now));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.VatReturnFinalized,
            AuditTargetTypes.VatReturn, vatReturn.Id, AuditEventOutcomes.Succeeded,
            "Locked the approved VAT return and retained a checksummed human-filing package. No authority submission was attempted.",
            new() { ["approvalRequestId"] = approval.Id.ToString("D"), ["inputHash"] = vatReturn.InputHash,
                ["packageChecksum"] = checksum, ["packageContentLength"] = content.LongLength.ToString(CultureInfo.InvariantCulture),
                ["submissionCapability"] = "not_configured" }, cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry?.VatReturnFinalized(command.CompanyId, vatReturn.Id, vatReturn.Version, checksum);
        return await MapAsync(vatReturn, cancellationToken);
    }

    public async Task<VatReturnDto> CreateCorrectionAsync(CreateVatReturnCorrectionCommand command,
        CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var reason = Required(command.Reason, nameof(command.Reason), 1000);
        var evidence = Required(command.EvidenceReference, nameof(command.EvidenceReference), 500);
        var key = Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 200);
        var replay = await _db.VatReturns.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.FilingPeriod).Include(x => x.Boxes).Include(x => x.Contributions).Include(x => x.Issues).Include(x => x.Reviews)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == key, cancellationToken);
        if (replay is not null) return await MapAsync(replay, cancellationToken);
        var original = await LoadReadAsync(command.CompanyId, command.OriginalVatReturnId, cancellationToken);
        if (original.Status != VatReturnStatuses.Locked)
            throw Error("vat_return_not_finalized", "Only a finalized VAT return can be corrected.");
        var version = (await _db.VatReturns.IgnoreQueryFilters().Where(x => x.CompanyId == command.CompanyId && x.FilingPeriodId == original.FilingPeriodId)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? original.Version) + 1;
        var correction = new VatReturn(Guid.NewGuid(), command.CompanyId, original.FilingPeriodId, version,
            key, original.Id, reason, evidence, Now());
        _db.VatReturns.Add(correction);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.VatReturnCorrectionCreated,
            AuditTargetTypes.VatReturn, correction.Id, AuditEventOutcomes.Succeeded,
            "Created a linked correction return while preserving the finalized original and its package.",
            new() { ["originalVatReturnId"] = original.Id.ToString("D"), ["originalPackageChecksum"] = original.PackageChecksum,
                ["reason"] = reason, ["evidenceReference"] = evidence }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await MapAsync(await LoadReadAsync(command.CompanyId, correction.Id, cancellationToken), cancellationToken);
    }

    public async Task<VatReturnPackageDownloadDto> DownloadPackageAsync(GetVatReturnPackageQuery query,
        CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var vatReturn = await LoadReadAsync(query.CompanyId, query.VatReturnId, cancellationToken);
        if (vatReturn.Status != VatReturnStatuses.Locked || string.IsNullOrWhiteSpace(vatReturn.PackageStorageKey) ||
            string.IsNullOrWhiteSpace(vatReturn.PackageChecksum) || string.IsNullOrWhiteSpace(vatReturn.PackageFileName))
            throw Error("vat_return_package_unavailable", "The VAT return filing package is not available.");
        await using var stream = await _storage.OpenReadAsync(vatReturn.PackageStorageKey, cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var content = memory.ToArray();
        if (!string.Equals(Hash(content), vatReturn.PackageChecksum, StringComparison.OrdinalIgnoreCase))
            throw Error("vat_return_package_checksum_mismatch", "The filing package checksum does not match its finalized evidence.");
        return new(vatReturn.PackageFileName, vatReturn.PackageMediaType ?? PackageMediaType, content, vatReturn.PackageChecksum);
    }

    private async Task<Calculation> BuildCalculationAsync(VatFilingPeriod period, CancellationToken cancellationToken)
    {
        var cutoff = Now();
        var rows = await LoadSourceRowsAsync(period, cancellationToken);
        var inputHash = ComputeInputHash(rows);
        var issues = new List<IssueSeed>();
        var contributions = new List<ContributionSeed>();
        if (period.FiscalPeriodId.HasValue && await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == period.CompanyId && x.Id == period.FiscalPeriodId.Value &&
                    x.IsReportingLocked, cancellationToken))
            issues.Add(new(VatReturnIssueCodes.FiscalPeriodLocked,
                "The linked fiscal period is reporting-locked. Review the period boundary before calculating or replacing VAT filing evidence.",
                null, $"fiscal-period:{period.FiscalPeriodId.Value:D}"));
        var factRows = rows.Where(x => !string.IsNullOrWhiteSpace(x.TaxFactsJson)).ToArray();
        if (factRows.Length > MaximumSourceLineCount * 3)
            throw Error(VatReturnIssueCodes.SourceLimitExceeded,
                $"The filing period exceeds the synchronous limit of {MaximumSourceLineCount:N0} VAT source lines. Split or process the period with bounded background calculation.");

        foreach (var group in factRows.GroupBy(x => new { x.LedgerEntryId, FactsHash = Hash(Encoding.UTF8.GetBytes(x.TaxFactsJson!)) }))
        {
            var first = group.OrderBy(x => x.LedgerEntryLineId).First();
            if (!TryParseFacts(first, out var facts, out var parseIssue))
            {
                issues.Add(new(VatReturnIssueCodes.InvalidTaxFacts, parseIssue!, first.LedgerEntryId,
                    $"voucher:{first.VoucherNumber}"));
                continue;
            }
            if (!string.Equals(first.Currency, period.Currency, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(facts.DocumentCurrency, period.Currency, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new(VatReturnIssueCodes.CurrencyMismatch,
                    "The retained tax fact currency does not match the SEK filing period.", first.LedgerEntryId,
                    $"voucher:{first.VoucherNumber}"));
                continue;
            }
            if (!_packResolver.TryResolve(facts.PolicyPackKey, facts.PolicyPackVersion, out var pack))
            {
                issues.Add(new(VatReturnIssueCodes.PackVersionUnavailable,
                    "The exact policy-pack version retained by this source is unavailable.", first.LedgerEntryId,
                    $"voucher:{first.VoucherNumber}"));
                continue;
            }
            var rule = pack!.Definition.TaxRules.SingleOrDefault(x =>
                string.Equals(x.Key, facts.TaxRuleKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.RuleVersion, facts.TaxRuleVersion, StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(pack.Definition.CountryOrRegion, "SE", StringComparison.OrdinalIgnoreCase) ||
                rule is null || !string.Equals(rule.Direction, facts.Direction, StringComparison.OrdinalIgnoreCase) ||
                rule.DocumentTypes is not { Count: > 0 } documentTypes ||
                !documentTypes.Contains(facts.DocumentType, StringComparer.OrdinalIgnoreCase) ||
                first.PostingDate < rule.EffectiveFrom ||
                rule.EffectiveTo.HasValue && first.PostingDate > rule.EffectiveTo.Value ||
                !SetEquals(rule.VatBoxMappings, facts.Boxes))
            {
                issues.Add(new(VatReturnIssueCodes.PackVersionIncompatible,
                    "The retained policy rule is not compatible with the selected Swedish VAT return mapping.",
                    first.LedgerEntryId, $"voucher:{first.VoucherNumber}"));
                continue;
            }
            var sourceMultiplicity = group.Count(x => x.ControlAccountRole is not AccountingAccountRoleKeys.TaxOutput25 and not AccountingAccountRoleKeys.TaxInput);
            if (sourceMultiplicity <= 0)
            {
                issues.Add(new(VatReturnIssueCodes.DuplicateSource,
                    "A VAT posting has no distinct taxable source line and cannot be included safely.", first.LedgerEntryId,
                    $"voucher:{first.VoucherNumber}"));
                continue;
            }
            if (sourceMultiplicity > MaximumSourceLineCount - contributions.Select(x => x.SourceChecksum).Distinct().Count())
                throw Error(VatReturnIssueCodes.SourceLimitExceeded,
                    $"The filing period exceeds the synchronous limit of {MaximumSourceLineCount:N0} VAT source lines.");

            var sign = facts.DocumentType.EndsWith("credit_note", StringComparison.OrdinalIgnoreCase) ? -1m : 1m;
            for (var instance = 0; instance < sourceMultiplicity; instance++)
            {
                var sourceChecksum = Hash(Encoding.UTF8.GetBytes($"{group.Key.FactsHash}|{instance}"));
                foreach (var box in facts.Boxes)
                {
                    var value = box switch
                    {
                        "05" when facts.Direction == AccountingTaxDirectionValues.Sales => facts.TaxableBasis,
                        "10" when facts.Direction == AccountingTaxDirectionValues.Sales => facts.TaxAmount,
                        "48" when facts.Direction == AccountingTaxDirectionValues.Purchase => facts.RecoverableTaxAmount ?? facts.TaxAmount,
                        _ => decimal.MinValue
                    };
                    if (value == decimal.MinValue)
                    {
                        issues.Add(new(VatReturnIssueCodes.UnsupportedBox,
                            $"VAT box {box} is not supported for the retained transaction direction.",
                            first.LedgerEntryId, $"voucher:{first.VoucherNumber}"));
                        continue;
                    }
                    contributions.Add(new(first.LedgerEntryId, first.VoucherNumber, first.PostingDate,
                        first.SourceType ?? "unknown", first.SourceId ?? first.LedgerEntryId.ToString("D"),
                        first.SourceVersion ?? "unknown", facts.PolicyPackKey, facts.PolicyPackVersion,
                        facts.TaxRuleKey, facts.TaxRuleVersion, box, FactType(box), sign * value,
                        first.Currency, sourceChecksum));
                }
            }
        }

        foreach (var missing in rows.Where(x =>
                     (x.ControlAccountRole is AccountingAccountRoleKeys.TaxOutput25 or AccountingAccountRoleKeys.TaxInput) &&
                     string.IsNullOrWhiteSpace(x.TaxFactsJson)))
            issues.Add(new(VatReturnIssueCodes.MissingTaxFacts,
                "A VAT control-account posting has no immutable tax classification.", missing.LedgerEntryId,
                $"voucher:{missing.VoucherNumber}"));

        var distinctContributions = contributions.DistinctBy(x => new { x.LedgerEntryId, x.SourceChecksum, x.BoxCode }).ToArray();
        if (distinctContributions.Length != contributions.Count)
            issues.Add(new(VatReturnIssueCodes.DuplicateSource,
                "A source contribution would be included more than once in the same VAT box.", null, null));
        contributions = distinctContributions.ToList();
        var boxes = contributions.GroupBy(x => x.BoxCode).Select(x => new BoxSeed(x.Key, FactType(x.Key),
            x.Sum(y => y.ExactAmount), RoundFiling(x.Sum(y => y.ExactAmount)),
            x.Select(y => new { y.LedgerEntryId, y.SourceChecksum }).Distinct().Count())).OrderBy(x => x.BoxCode).ToList();
        EnsureBox(boxes, "05"); EnsureBox(boxes, "10"); EnsureBox(boxes, "48");
        var output = boxes.Single(x => x.BoxCode == "10").ExactAmount;
        var input = boxes.Single(x => x.BoxCode == "48").ExactAmount;
        var settlement = output - input;
        boxes.Add(new BoxSeed("49", FactType("49"), settlement, RoundFiling(settlement),
            contributions.Select(x => new { x.LedgerEntryId, x.SourceChecksum }).Distinct().Count()));

        var ledgerOutput = rows.Where(x => x.ControlAccountRole == AccountingAccountRoleKeys.TaxOutput25).Sum(x => x.Credit - x.Debit);
        var ledgerInput = rows.Where(x => x.ControlAccountRole == AccountingAccountRoleKeys.TaxInput).Sum(x => x.Debit - x.Credit);
        AddDifference(issues, output, ledgerOutput, AccountingAccountRoleKeys.TaxOutput25);
        AddDifference(issues, input, ledgerInput, AccountingAccountRoleKeys.TaxInput);
        var excluded = issues.Where(x => x.LedgerEntryId.HasValue).Select(x => x.LedgerEntryId).Distinct().Count();
        var checksum = ComputeCalculationChecksum(inputHash, boxes, contributions, issues);
        return new(cutoff, inputHash, checksum, boxes.OrderBy(x => x.BoxCode).ToArray(), contributions,
            issues.DistinctBy(x => new { x.Code, x.LedgerEntryId, x.SourceReference, x.Difference }).ToArray(),
            contributions.Select(x => new { x.LedgerEntryId, x.SourceChecksum }).Distinct().Count(), excluded,
            output, input, settlement, RoundFiling(settlement));
    }

    private async Task EnsureCurrentAndCleanAsync(VatReturn vatReturn, string expectedInputHash,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(vatReturn.InputHash, Required(expectedInputHash, nameof(expectedInputHash), 64), StringComparison.OrdinalIgnoreCase))
            throw Error(VatReturnIssueCodes.Stale, "The supplied VAT return evidence hash is stale. Recalculate before continuing.");
        var rows = await LoadSourceRowsAsync(vatReturn.FilingPeriod, cancellationToken);
        if (!string.Equals(vatReturn.InputHash, ComputeInputHash(rows), StringComparison.OrdinalIgnoreCase))
            throw Error(VatReturnIssueCodes.Stale, "Posted VAT sources changed after calculation. Recalculate before continuing.");
        if (vatReturn.Issues.Any(x => x.IsBlocking))
            throw Error("vat_return_blocking_issues", "Resolve every blocking VAT return issue and recalculate before continuing.");
        if (vatReturn.Status is not (VatReturnStatuses.Calculated or VatReturnStatuses.NeedsReview or VatReturnStatuses.Approved))
            throw Error("vat_return_state_invalid", "The VAT return is not in a state that allows this action.");
    }

    private async Task<List<SourceRow>> LoadSourceRowsAsync(VatFilingPeriod period, CancellationToken cancellationToken) =>
        await _db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == period.CompanyId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                (x.LedgerEntry.PostingDate ?? DateOnly.FromDateTime(x.LedgerEntry.EntryUtc)) >= period.StartDate &&
                (x.LedgerEntry.PostingDate ?? DateOnly.FromDateTime(x.LedgerEntry.EntryUtc)) <= period.EndDate &&
                (x.TaxFactsJson != null || x.FinanceAccount.ControlAccountRole == AccountingAccountRoleKeys.TaxOutput25 ||
                 x.FinanceAccount.ControlAccountRole == AccountingAccountRoleKeys.TaxInput))
            .OrderBy(x => x.LedgerEntry.PostingDate ?? DateOnly.FromDateTime(x.LedgerEntry.EntryUtc))
            .ThenBy(x => x.LedgerEntryId).ThenBy(x => x.Id)
            .Select(x => new SourceRow(x.Id, x.LedgerEntryId, x.LedgerEntry.EntryNumber,
                x.LedgerEntry.PostingDate ?? DateOnly.FromDateTime(x.LedgerEntry.EntryUtc),
                x.LedgerEntry.SourceType, x.LedgerEntry.SourceId, x.LedgerEntry.SourceVersion,
                x.LedgerEntry.PolicyPackKey, x.LedgerEntry.PolicyPackVersion, x.Currency,
                x.DebitAmount, x.CreditAmount, x.FinanceAccount.ControlAccountRole, x.TaxFactsJson))
            .ToListAsync(cancellationToken);

    private static string ComputeInputHash(IEnumerable<SourceRow> rows) => Hash(Encoding.UTF8.GetBytes(string.Join('\n', rows.Select(x =>
        $"{x.LedgerEntryId:N}|{x.LedgerEntryLineId:N}|{x.PostingDate:yyyy-MM-dd}|{x.PolicyPackKey}|{x.PolicyPackVersion}|{x.Currency}|{Amount(x.Debit)}|{Amount(x.Credit)}|{x.ControlAccountRole}|{Hash(Encoding.UTF8.GetBytes(x.TaxFactsJson ?? "missing"))}"))));

    private static bool TryParseFacts(SourceRow row, out TaxFacts facts, out string? issue)
    {
        facts = default!; issue = null;
        try
        {
            using var document = JsonDocument.Parse(row.TaxFactsJson!);
            var root = document.RootElement;
            var packKey = Text(root, "policyPackKey") ?? row.PolicyPackKey;
            var packVersion = Text(root, "policyPackVersion") ?? row.PolicyPackVersion;
            var ruleKey = Text(root, "taxRuleKey"); var ruleVersion = Text(root, "taxRuleVersion");
            var direction = Text(root, "direction"); var documentType = Text(root, "documentType");
            var currency = Text(root, "documentCurrency") ?? row.Currency;
            var boxes = (Text(root, "vatBoxes") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var basis = Decimal(root, "taxableBasis"); var tax = Decimal(root, "taxAmount");
            var recoverable = Decimal(root, "recoverableTaxAmount");
            if (string.IsNullOrWhiteSpace(packKey) || string.IsNullOrWhiteSpace(packVersion) ||
                string.IsNullOrWhiteSpace(ruleKey) || string.IsNullOrWhiteSpace(ruleVersion) ||
                direction is not (AccountingTaxDirectionValues.Sales or AccountingTaxDirectionValues.Purchase) ||
                string.IsNullOrWhiteSpace(documentType) || boxes.Length == 0 || boxes.Contains("none") ||
                !basis.HasValue || !tax.HasValue || basis.Value < 0m || tax.Value < 0m || recoverable < 0m)
            {
                issue = "The retained tax fact is incomplete for deterministic VAT return calculation.";
                return false;
            }
            facts = new(packKey, packVersion, ruleKey, ruleVersion, direction, documentType,
                currency, boxes.OrderBy(x => x, StringComparer.Ordinal).ToArray(), basis.Value, tax.Value, recoverable);
            return true;
        }
        catch (JsonException)
        {
            issue = "The retained tax fact is not valid JSON.";
            return false;
        }
    }

    private async Task<VatReturnDto> MapAsync(VatReturn x, CancellationToken cancellationToken)
    {
        var currentHash = x.InputHash is null ? null : ComputeInputHash(await LoadSourceRowsAsync(x.FilingPeriod, cancellationToken));
        var stale = x.InputHash is not null && !string.Equals(x.InputHash, currentHash, StringComparison.OrdinalIgnoreCase);
        var approvalState = x.ApprovalRequestId.HasValue
            ? await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking().Where(a => a.CompanyId == x.CompanyId && a.Id == x.ApprovalRequestId.Value)
                .Select(a => (ApprovalRequestStatus?)a.Status).SingleOrDefaultAsync(cancellationToken)
            : null;
        var approvalStatus = approvalState?.ToStorageValue();
        var superseded = await _db.VatReturns.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(v => v.CompanyId == x.CompanyId && v.CorrectionOfVatReturnId == x.Id, cancellationToken);
        var displayStatus = superseded && x.Status == VatReturnStatuses.Locked ? VatReturnStatuses.Corrected
            : stale && x.Status != VatReturnStatuses.Locked ? VatReturnStatuses.NeedsReview
            : x.Status == VatReturnStatuses.NeedsReview && approvalStatus == ApprovalRequestStatus.Approved.ToStorageValue()
                ? VatReturnStatuses.Approved : x.Status;
        var actions = new List<string>();
        if (x.Status == VatReturnStatuses.Draft) actions.Add(VatReturnAllowedActions.Calculate);
        if (x.Status != VatReturnStatuses.Locked) actions.Add(VatReturnAllowedActions.Recalculate);
        if (!stale && x.Status == VatReturnStatuses.Calculated && x.Issues.All(i => !i.IsBlocking)) actions.Add(VatReturnAllowedActions.RequestApproval);
        if (!stale && approvalStatus == ApprovalRequestStatus.Approved.ToStorageValue() && x.Issues.All(i => !i.IsBlocking)) actions.Add(VatReturnAllowedActions.Finalize);
        if (x.Status == VatReturnStatuses.Locked) { actions.Add(VatReturnAllowedActions.DownloadPackage); actions.Add(VatReturnAllowedActions.CreateCorrection); }
        return new(x.Id, x.CompanyId, x.FilingPeriodId, x.FilingPeriod.PeriodCode, x.FilingPeriod.StartDate,
            x.FilingPeriod.EndDate, x.FilingPeriod.Currency, x.Version, displayStatus, stale, superseded,
            x.CorrectionOfVatReturnId, x.CorrectionReason, x.CorrectionEvidenceReference, x.CutoffUtc,
            x.InputHash, x.CalculationChecksum, x.IncludedSourceCount, x.ExcludedSourceCount,
            x.OutputVatExact, x.InputVatExact, x.SettlementExact, x.SettlementFilingAmount,
            x.ApprovalRequestId, approvalStatus, x.FinalizedByUserId, x.FinalizedUtc,
            x.PackageChecksum, x.PackageFileName, x.PackageMediaType, x.PackageContentLength,
            x.Status == VatReturnStatuses.Locked && x.PackageStorageKey is not null,
            x.Boxes.OrderBy(b => b.BoxCode).Select(b => new VatReturnBoxResultDto(b.BoxCode, b.FactType,
                b.ExactAmount, b.FilingAmount, b.Currency, b.SourceCount)).ToArray(),
            x.Contributions.OrderBy(c => c.BoxCode).ThenBy(c => c.PostingDate).ThenBy(c => c.VoucherNumber)
                .Select(c => new VatReturnSourceContributionDto(c.Id, c.LedgerEntryId, c.VoucherNumber,
                    c.PostingDate, c.SourceType, c.SourceId, c.SourceVersion, c.PolicyPackKey,
                    c.PolicyPackVersion, c.TaxRuleKey, c.TaxRuleVersion, c.BoxCode, c.FactType,
                    c.ExactAmount, c.Currency, c.SourceChecksum)).ToArray(),
            x.Issues.OrderBy(i => i.Code).Select(i => new VatReturnValidationIssueDto(i.Id, i.Code,
                i.Explanation, i.IsBlocking, i.LedgerEntryId, i.SourceReference, i.Difference)).ToArray(),
            x.Reviews.OrderBy(r => r.OccurredUtc).Select(r => new VatReturnReviewDto(r.Id, r.Action,
                r.ActorUserId, r.ApprovalRequestId, r.EvidenceHash, r.OccurredUtc)).ToArray(), actions);
    }

    private object BuildPackage(VatReturn x, DateTime finalizedUtc, Guid actorUserId, Guid approvalRequestId) => new
    {
        schemaVersion = "1.0", packageType = "swedish_vat_human_filing_package",
        submissionCapability = "not_configured", filingInstruction = "Review these whole-krona values and enter them in the verified Swedish Tax Agency filing channel. This package was not submitted automatically.",
        vatReturnId = x.Id, x.CompanyId, x.FilingPeriodId, x.FilingPeriod.PeriodCode,
        periodStart = x.FilingPeriod.StartDate, periodEnd = x.FilingPeriod.EndDate, x.FilingPeriod.Currency,
        x.Version, x.CorrectionOfVatReturnId, x.CorrectionReason, x.CorrectionEvidenceReference,
        x.CutoffUtc, x.InputHash, x.CalculationChecksum, finalizedUtc, actorUserId, approvalRequestId,
        boxes = x.Boxes.OrderBy(b => b.BoxCode).Select(b => new { b.BoxCode, b.FactType, b.ExactAmount, b.FilingAmount, b.Currency, b.SourceCount }),
        reconciliation = new { x.OutputVatExact, x.InputVatExact, x.SettlementExact, x.SettlementFilingAmount, blockingIssueCount = x.Issues.Count(i => i.IsBlocking) },
        policyPacks = x.Contributions.Select(c => new { c.PolicyPackKey, c.PolicyPackVersion }).Distinct().OrderBy(p => p.PolicyPackKey).ThenBy(p => p.PolicyPackVersion),
        sourceManifest = x.Contributions.OrderBy(c => c.PostingDate).ThenBy(c => c.VoucherNumber).ThenBy(c => c.BoxCode)
            .Select(c => new { c.LedgerEntryId, c.VoucherNumber, c.PostingDate, c.SourceType, c.SourceId,
                c.SourceVersion, c.PolicyPackKey, c.PolicyPackVersion, c.TaxRuleKey, c.TaxRuleVersion,
                c.BoxCode, c.FactType, c.ExactAmount, c.Currency, c.SourceChecksum })
    };

    private async Task<VatReturn> LoadTrackedAsync(Guid companyId, Guid id, CancellationToken cancellationToken) =>
        await _db.VatReturns.IgnoreQueryFilters().Include(x => x.FilingPeriod).Include(x => x.Boxes)
            .Include(x => x.Contributions).Include(x => x.Issues).Include(x => x.Reviews)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("VAT return was not found in the requested company.");

    private async Task<VatReturn> LoadReadAsync(Guid companyId, Guid id, CancellationToken cancellationToken) =>
        await _db.VatReturns.IgnoreQueryFilters().AsNoTracking().Include(x => x.FilingPeriod).Include(x => x.Boxes)
            .Include(x => x.Contributions).Include(x => x.Issues).Include(x => x.Reviews)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("VAT return was not found in the requested company.");

    private async Task EnsureUnambiguousAsync(VatFilingPeriod period, CancellationToken cancellationToken)
    {
        var overlaps = await _db.VatFilingPeriods.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
            x.CompanyId == period.CompanyId && x.StartDate <= period.EndDate && period.StartDate <= x.EndDate,
            cancellationToken);
        if (overlaps != 1) throw Error(VatReturnIssueCodes.FilingPeriodAmbiguous,
            "VAT filing periods overlap and the source boundary is ambiguous.");
    }

    private async Task RequireViewAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipResolver.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanViewAccounting(membership.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }

    private async Task RequireManageAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId != actorUserId) throw new UnauthorizedAccessException();
        var membership = await _membershipResolver.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanManageAccounting(membership.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }

    private async Task WriteAuditAsync(Guid companyId, Guid actorUserId, string action, string targetType,
        Guid targetId, string outcome, string rationale, Dictionary<string, string?> metadata,
        CancellationToken cancellationToken) => await _audit.WriteAsync(new AuditEventWriteRequest(companyId,
        AuditActorTypes.User, actorUserId, action, targetType, targetId.ToString("D"), outcome, rationale,
        DataSources: ["posted_journals", "immutable_tax_facts", "vat_returns"], Metadata: metadata,
        OccurredUtc: Now()), cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Error("vat_return_state_changed", "The VAT return changed. Refresh its evidence and try again."); }
        catch (DbUpdateException ex) when (ex.InnerException is not null) { throw Error("vat_return_conflict", "The VAT return operation conflicts with an existing period, version, or idempotency key."); }
    }

    private void AddReview(VatReturn vatReturn, VatReturnReview review)
    {
        vatReturn.Reviews.Add(review);
        _db.Entry(review).State = EntityState.Added;
    }

    private static void EnsureBox(List<BoxSeed> boxes, string code)
    {
        if (boxes.All(x => x.BoxCode != code)) boxes.Add(new(code, FactType(code), 0m, 0L, 0));
    }

    private static void AddDifference(List<IssueSeed> issues, decimal calculated, decimal ledger, string role)
    {
        var difference = decimal.Round(calculated - ledger, 6, MidpointRounding.ToEven);
        if (difference != 0m) issues.Add(new(VatReturnIssueCodes.ControlAccountDifference,
            $"Calculated VAT does not reconcile to the {role.Replace('_', ' ')} control account.", null,
            $"account-role:{role}", difference));
    }

    private static string ComputeCalculationChecksum(string inputHash, IEnumerable<BoxSeed> boxes,
        IEnumerable<ContributionSeed> contributions, IEnumerable<IssueSeed> issues) => Hash(Encoding.UTF8.GetBytes(string.Join('\n',
        new[] { inputHash }.Concat(boxes.OrderBy(x => x.BoxCode).Select(x => $"box|{x.BoxCode}|{Amount(x.ExactAmount)}|{x.FilingAmount}"))
            .Concat(contributions.OrderBy(x => x.BoxCode).ThenBy(x => x.SourceChecksum).Select(x => $"source|{x.LedgerEntryId:N}|{x.SourceChecksum}|{x.BoxCode}|{Amount(x.ExactAmount)}"))
            .Concat(issues.OrderBy(x => x.Code).ThenBy(x => x.LedgerEntryId).Select(x => $"issue|{x.Code}|{x.LedgerEntryId}|{x.SourceReference}|{Amount(x.Difference ?? 0m)}")))));

    private static string FactType(string box) => box switch { "05" => "taxable_basis", "10" => "output_vat", "48" => "deductible_input_vat", "49" => "vat_payable_or_refundable", _ => "unsupported" };
    private static long RoundFiling(decimal value) => decimal.ToInt64(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
    private static bool SetEquals(IReadOnlyList<string>? expected, IReadOnlyList<string> actual) =>
        (expected ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(actual);
    private static string? Text(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static decimal? Decimal(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        const NumberStyles invariantDecimal = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), invariantDecimal, CultureInfo.InvariantCulture, out number)
            ? number : null;
    }
    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    private static string Amount(decimal value) => value.ToString("0.00######", CultureInfo.InvariantCulture);
    private static string SafeFile(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static VatReturnOperationException Error(string code, string message) => new(code, message);
    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;
    private static VatFilingPeriodDto Map(VatFilingPeriod x) => new(x.Id, x.CompanyId, x.PeriodCode, x.StartDate, x.EndDate, x.Currency, x.FiscalPeriodId, x.CreatedUtc, x.DueDate);

    private sealed record SourceRow(Guid LedgerEntryLineId, Guid LedgerEntryId, string VoucherNumber,
        DateOnly PostingDate, string? SourceType, string? SourceId, string? SourceVersion,
        string? PolicyPackKey, string? PolicyPackVersion, string Currency, decimal Debit,
        decimal Credit, string? ControlAccountRole, string? TaxFactsJson);
    private sealed record TaxFacts(string PolicyPackKey, string PolicyPackVersion, string TaxRuleKey,
        string TaxRuleVersion, string Direction, string DocumentType, string DocumentCurrency,
        IReadOnlyList<string> Boxes, decimal TaxableBasis, decimal TaxAmount, decimal? RecoverableTaxAmount);
    private sealed record ContributionSeed(Guid LedgerEntryId, string VoucherNumber, DateOnly PostingDate,
        string SourceType, string SourceId, string SourceVersion, string PolicyPackKey,
        string PolicyPackVersion, string TaxRuleKey, string TaxRuleVersion, string BoxCode,
        string FactType, decimal ExactAmount, string Currency, string SourceChecksum);
    private sealed record BoxSeed(string BoxCode, string FactType, decimal ExactAmount, long FilingAmount, int SourceCount);
    private sealed record IssueSeed(string Code, string Explanation, Guid? LedgerEntryId, string? SourceReference, decimal? Difference = null);
    private sealed record Calculation(DateTime CutoffUtc, string InputHash, string Checksum,
        IReadOnlyList<BoxSeed> Boxes, IReadOnlyList<ContributionSeed> Contributions,
        IReadOnlyList<IssueSeed> Issues, int IncludedSourceCount, int ExcludedSourceCount,
        decimal OutputVat, decimal InputVat, decimal Settlement, long SettlementFilingAmount);
}
