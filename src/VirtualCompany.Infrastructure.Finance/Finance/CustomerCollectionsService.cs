using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerCollectionsService(
    VirtualCompanyDbContext db,
    ICustomerInvoiceAccountingService accounting,
    ICompanyOutboxEnqueuer outbox,
    IAuditEventWriter audit,
    CustomerCollectionsTelemetry? telemetry = null) : ICustomerCollectionsService
{
    private const int MaximumProjectionItems = 5000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CustomerAgingResultDto> GetAgingAsync(CustomerAgingQuery query, CancellationToken ct)
    {
        EnsureCompany(query.CompanyId); var take = PageSize(query.Take); var skip = Math.Max(0, query.Skip);
        var cutoffExclusive = CutoffExclusive(query.CutoffDate, query.TimeZoneId);
        var currency = NormalizeCurrency(query.Currency);
        var invoiceQuery = db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().Include(x => x.Counterparty)
            .Where(x => x.CompanyId == query.CompanyId && x.IssuedUtc < cutoffExclusive && x.Amount > 0m &&
                x.PostingStatus == FinanceDocumentPostingStatuses.Booked && x.DocumentKind == FinanceDocumentKinds.Invoice);
        if (query.CustomerId.HasValue) invoiceQuery = invoiceQuery.Where(x => x.CounterpartyId == query.CustomerId.Value);
        if (currency is not null) invoiceQuery = invoiceQuery.Where(x => x.Currency == currency);
        var candidateCount = await invoiceQuery.CountAsync(ct);
        if (candidateCount > MaximumProjectionItems)
            throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The aging projection exceeds the supported bound. Filter by customer or currency.");
        var invoices = await invoiceQuery.OrderBy(x => x.DueUtc).ThenBy(x => x.Id).Take(MaximumProjectionItems).ToListAsync(ct);
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var allocations = await EffectiveAllocationsAsync(query.CompanyId, invoiceIds, cutoffExclusive, ct);
        var cases = await db.CustomerCollectionCases.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && invoiceIds.Contains(x.InvoiceId)).ToDictionaryAsync(x => x.InvoiceId, ct);
        var billingProfiles = await db.CustomerBillingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && invoices.Select(i => i.CounterpartyId).Contains(x.CounterpartyId))
            .ToDictionaryAsync(x => x.CounterpartyId, ct);
        var accountingProfiles = await db.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && invoiceIds.Contains(x.InvoiceId) &&
                x.Status == CustomerInvoiceAccountingStatuses.Posted)
            .ToDictionaryAsync(x => x.InvoiceId, ct);

        var openRows = invoices.Select(invoice =>
        {
            var allocation = allocations.GetValueOrDefault(invoice.Id);
            var allocated = allocation?.DocumentAmount ?? 0m;
            var open = Money(Math.Max(0m, invoice.Amount - allocated));
            cases.TryGetValue(invoice.Id, out var collectionCase);
            var accountingProfile = accountingProfiles.GetValueOrDefault(invoice.Id);
            var hasAuthoritativeFunctionalFacts = accountingProfile?.HasAuthoritativeCurrencyFacts == true &&
                (allocation?.HasAuthoritativeFunctionalFacts ?? true);
            var functionalAllocated = hasAuthoritativeFunctionalFacts
                ? allocation?.FunctionalAmount ?? 0m
                : (decimal?)null;
            var functionalOpen = hasAuthoritativeFunctionalFacts
                ? Money(Math.Max(0m, accountingProfile!.GrossBaseAmount - functionalAllocated!.Value))
                : (decimal?)null;
            return new AgingRow(invoice, allocated, open, collectionCase, accountingProfile,
                functionalAllocated, functionalOpen, hasAuthoritativeFunctionalFacts);
        }).Where(x => x.OpenAmount > 0m).ToArray();
        var currencies = openRows.Select(x => x.Invoice.Currency).Distinct(StringComparer.Ordinal).ToArray();
        if (currency is null && currencies.Length > 1)
            throw Error(CustomerCollectionReasonCodes.UnsupportedCurrency, "Aging totals cannot combine currencies. Request one currency at a time.");
        var resultCurrency = currency ?? currencies.SingleOrDefault() ?? "";
        var exposure = openRows.GroupBy(x => new CustomerCurrencyKey(x.Invoice.CounterpartyId, x.Invoice.Currency))
            .ToDictionary(x => x.Key, x => Money(x.Sum(y => y.OpenAmount)));
        var reconciliation = await accounting.ReconcileAsync(new(query.CompanyId, query.CutoffDate), ct);
        var items = openRows.Select(row => MapAging(row, query.CutoffDate, exposure,
                billingProfiles.GetValueOrDefault(row.Invoice.CounterpartyId)))
            .Skip(skip).Take(take).ToArray();
        decimal Bucket(string bucket) => openRows.Where(x => AgingBucket(DaysOverdue(x.Invoice.DueUtc, query.CutoffDate)) == bucket).Sum(x => x.OpenAmount);
        var hasCompleteFunctionalFacts = openRows.All(x => x.HasAuthoritativeFunctionalFacts);
        var functionalCurrencies = openRows.Where(x => x.AccountingProfile is not null)
            .Select(x => x.AccountingProfile!.BaseCurrency).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var functionalCurrency = hasCompleteFunctionalFacts && functionalCurrencies.Length == 1 ? functionalCurrencies[0] : null;
        decimal? FunctionalBucket(string bucket) => functionalCurrency is null ? null : Money(openRows
            .Where(x => AgingBucket(DaysOverdue(x.Invoice.DueUtc, query.CutoffDate)) == bucket)
            .Sum(x => x.FunctionalOpenAmount!.Value));
        telemetry?.Operation("aging", "succeeded");
        return new(query.CompanyId, query.CutoffDate, cutoffExclusive, query.TimeZoneId, resultCurrency, openRows.Length,
            Money(Bucket("current")), Money(Bucket("1_30")), Money(Bucket("31_60")), Money(Bucket("61_90")),
            Money(Bucket("over_90")), Money(openRows.Sum(x => x.OpenAmount)), reconciliation.Difference,
            reconciliation.IsReconciled, items, functionalCurrency, FunctionalBucket("current"),
            FunctionalBucket("1_30"), FunctionalBucket("31_60"), FunctionalBucket("61_90"),
            FunctionalBucket("over_90"), functionalCurrency is null ? null : Money(openRows.Sum(x =>
                x.FunctionalOpenAmount!.Value)));
    }

    public async Task<CustomerStatementDto> GenerateStatementAsync(GenerateCustomerStatementCommand command, CancellationToken ct)
    {
        EnsureCompany(command.CompanyId); EnsureActor(command.ActorUserId); Required(command.IdempotencyKey, 200);
        if (command.CutoffDate < command.FromDate) throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The statement cutoff cannot precede its start date.");
        var existing = await db.CustomerStatementSnapshots.IgnoreQueryFilters().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == command.IdempotencyKey.Trim(), ct);
        if (existing is not null)
        {
            if (existing.CustomerId != command.CustomerId || existing.FromDate != command.FromDate || existing.CutoffDate != command.CutoffDate)
                throw Error(CustomerCollectionReasonCodes.IdempotencyConflict, "This statement key was already used for different statement inputs.", true);
            telemetry?.Operation("statement", "succeeded", true); return MapStatement(existing, true);
        }

        var customer = await db.FinanceCounterparties.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.CustomerId, ct)
            ?? throw Error(CustomerCollectionReasonCodes.CustomerNotFound, "The customer could not be found.");
        var currency = NormalizeCurrency(command.Currency) ?? throw Error(CustomerCollectionReasonCodes.UnsupportedCurrency, "A statement currency is required.");
        var locale = Locale(command.Locale); var cutoffExclusive = CutoffExclusive(command.CutoffDate, command.TimeZoneId);
        var fromUtc = StartOfDate(command.FromDate, command.TimeZoneId);
        var invoices = await db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.CounterpartyId == command.CustomerId && x.Currency == currency &&
                x.IssuedUtc < cutoffExclusive && x.PostingStatus == FinanceDocumentPostingStatuses.Booked)
            .OrderBy(x => x.IssuedUtc).ThenBy(x => x.Id).Take(MaximumProjectionItems).ToListAsync(ct);
        var statementCandidateCount = await db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == command.CompanyId && x.CounterpartyId == command.CustomerId && x.Currency == currency &&
                x.IssuedUtc < cutoffExclusive && x.PostingStatus == FinanceDocumentPostingStatuses.Booked, ct);
        if (statementCandidateCount > MaximumProjectionItems)
            throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The statement exceeds the supported item bound. Use a later start date.");
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var accountingProfiles = await db.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && invoiceIds.Contains(x.InvoiceId))
            .ToDictionaryAsync(x => x.InvoiceId, ct);
        var allocationRows = await db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking().Include(x => x.Payment)
            .Where(x => x.CompanyId == command.CompanyId && x.InvoiceId != null && invoiceIds.Contains(x.InvoiceId.Value) &&
                x.SettlementStatus != PaymentAllocationSettlementStatuses.Reversed &&
                x.Payment.Status == PaymentStatuses.Completed && x.Payment.PaymentType == PaymentTypes.Incoming && x.Payment.PaymentDate < cutoffExclusive)
            .OrderBy(x => x.Payment.PaymentDate).ThenBy(x => x.Id).Take(MaximumProjectionItems).ToListAsync(ct);
        var released = await db.CustomerInvoiceCorrectionAllocationAdjustments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && allocationRows.Select(a => a.Id).Contains(x.PaymentAllocationId))
            .GroupBy(x => x.PaymentAllocationId).Select(x => new { Id = x.Key, Amount = x.Sum(y => y.ReleasedAmount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        var events = new List<StatementEvent>();
        foreach (var invoice in invoices)
        {
            var signed = invoice.DocumentKind == FinanceDocumentKinds.CreditNote || invoice.Amount < 0m ? -Math.Abs(invoice.Amount) : Math.Abs(invoice.Amount);
            accountingProfiles.TryGetValue(invoice.Id, out var profile);
            var evidenceReady = profile?.HasAuthoritativeCurrencyFacts == true;
            var functionalAmount = evidenceReady ? Money(Math.Abs(signed) * profile!.ExchangeRate) : (decimal?)null;
            events.Add(new(invoice.IssuedUtc, signed >= 0 ? "invoice" : "credit", invoice.Id, null, invoice.InvoiceNumber,
                signed >= 0 ? signed : 0m, signed < 0 ? Math.Abs(signed) : 0m,
                evidenceReady ? signed >= 0 ? functionalAmount : 0m : null,
                evidenceReady ? signed < 0 ? functionalAmount : 0m : null,
                evidenceReady ? profile!.BaseCurrency : null, evidenceReady ? profile!.ExchangeRate : null,
                evidenceReady ? profile!.ExchangeRateDate : null, evidenceReady ? profile!.ExchangeRateIdentity : null,
                evidenceReady ? profile!.CurrencyProvenance : null,
                Hash($"invoice|{invoice.Id:N}|{invoice.UpdatedUtc:O}|{Money(signed)}|{profile?.ExchangeRateIdentity}")));
        }
        foreach (var allocation in allocationRows)
        {
            var applied = allocation.AllocatedAmount + allocation.WriteOffAmount;
            var effective = Money(Math.Max(0m, applied - released.GetValueOrDefault(allocation.Id)));
            if (effective > 0m)
            {
                var profile = allocation.InvoiceId.HasValue ? accountingProfiles.GetValueOrDefault(allocation.InvoiceId.Value) : null;
                var evidenceReady = allocation.AllocatedFunctionalAmount.HasValue &&
                    !string.IsNullOrWhiteSpace(allocation.FunctionalCurrency) &&
                    allocation.SettlementRate.HasValue && allocation.SettlementRateDate.HasValue &&
                    !string.IsNullOrWhiteSpace(allocation.SettlementRateIdentity);
                var functionalEffective = evidenceReady
                    ? Money(allocation.AllocatedFunctionalAmount!.Value * (effective / applied))
                    : (decimal?)null;
                events.Add(new(allocation.Payment.PaymentDate, "payment", allocation.InvoiceId, allocation.Id,
                    $"Payment {allocation.PaymentId:N}", 0m, effective,
                    evidenceReady ? 0m : null, functionalEffective,
                    evidenceReady ? allocation.FunctionalCurrency : null, evidenceReady ? allocation.SettlementRate : null,
                    evidenceReady ? allocation.SettlementRateDate : null, evidenceReady ? allocation.SettlementRateIdentity : null,
                    evidenceReady ? "authoritative_settlement" : profile?.CurrencyProvenance,
                    Hash($"allocation|{allocation.Id:N}|{allocation.UpdatedUtc:O}|{effective}|{allocation.SettlementRateIdentity}")));
            }
        }
        events = events.OrderBy(x => x.OccurredUtc).ThenBy(x => x.Reference, StringComparer.Ordinal).ToList();
        var opening = Money(events.Where(x => x.OccurredUtc < fromUtc).Sum(x => x.Debit - x.Credit));
        var period = events.Where(x => x.OccurredUtc >= fromUtc && x.OccurredUtc < cutoffExclusive).ToArray();
        var invoiceActivity = Money(period.Where(x => x.Type == "invoice").Sum(x => x.Debit));
        var creditActivity = Money(period.Where(x => x.Type == "credit").Sum(x => x.Credit));
        var allocationActivity = Money(period.Where(x => x.Type == "payment").Sum(x => x.Credit));
        var closing = Money(opening + invoiceActivity - creditActivity - allocationActivity);
        var functionalComplete = events.Count > 0 && events.All(x => x.FunctionalDebit.HasValue && x.FunctionalCredit.HasValue &&
            !string.IsNullOrWhiteSpace(x.FunctionalCurrency) && !string.IsNullOrWhiteSpace(x.ExchangeRateIdentity));
        var functionalCurrencies = events.Where(x => !string.IsNullOrWhiteSpace(x.FunctionalCurrency))
            .Select(x => x.FunctionalCurrency!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        functionalComplete = functionalComplete && functionalCurrencies.Length <= 1;
        var functionalCurrency = functionalCurrencies.SingleOrDefault();
        decimal? functionalOpening = functionalComplete ? Money(events.Where(x => x.OccurredUtc < fromUtc).Sum(x => x.FunctionalDebit!.Value - x.FunctionalCredit!.Value)) : null;
        decimal? functionalInvoiceActivity = functionalComplete ? Money(period.Where(x => x.Type == "invoice").Sum(x => x.FunctionalDebit!.Value)) : null;
        decimal? functionalCreditActivity = functionalComplete ? Money(period.Where(x => x.Type == "credit").Sum(x => x.FunctionalCredit!.Value)) : null;
        decimal? functionalAllocationActivity = functionalComplete ? Money(period.Where(x => x.Type == "payment").Sum(x => x.FunctionalCredit!.Value)) : null;
        decimal? functionalClosing = functionalComplete ? Money(functionalOpening!.Value + functionalInvoiceActivity!.Value - functionalCreditActivity!.Value - functionalAllocationActivity!.Value) : null;
        var manifest = JsonSerializer.Serialize(events.Select(x => new { x.Type, x.InvoiceId, x.PaymentAllocationId, x.SourceHash,
            x.FunctionalCurrency, x.ExchangeRateDate, x.ExchangeRateIdentity, x.CurrencyProvenance }), JsonOptions);
        var manifestHash = Hash(manifest); var statementId = Guid.NewGuid(); var running = opening;
        decimal? functionalRunning = functionalOpening; var itemEntities = new List<CustomerStatementItem>(); var sequence = 0;
        foreach (var item in period)
        {
            running = Money(running + item.Debit - item.Credit); sequence++;
            if (functionalRunning.HasValue)
                functionalRunning = Money(functionalRunning.Value + item.FunctionalDebit!.Value - item.FunctionalCredit!.Value);
            itemEntities.Add(new(Guid.NewGuid(), command.CompanyId, statementId, sequence, item.Type, item.InvoiceId,
                item.PaymentAllocationId, LocalDate(item.OccurredUtc, command.TimeZoneId), item.Reference, item.Debit, item.Credit, running, item.SourceHash,
                item.FunctionalDebit, item.FunctionalCredit, functionalRunning, item.FunctionalCurrency, item.ExchangeRate,
                item.ExchangeRateDate, item.ExchangeRateIdentity, item.CurrencyProvenance));
        }
        var canonical = string.Join('\n', itemEntities.Select(x => $"{x.EffectiveDate:yyyy-MM-dd}|{x.ItemType}|{x.Reference}|{x.DebitAmount:0.00}|{x.CreditAmount:0.00}|{x.RunningBalance:0.00}|{x.FunctionalDebitAmount:0.00}|{x.FunctionalCreditAmount:0.00}|{x.FunctionalRunningBalance:0.00}|{x.FunctionalCurrency}|{x.ExchangeRate}|{x.ExchangeRateDate:yyyy-MM-dd}|{x.ExchangeRateIdentity}|{x.CurrencyProvenance}|{x.SourceHash}"));
        var checksum = Hash($"{command.CompanyId:N}|{command.CustomerId:N}|{command.FromDate:yyyy-MM-dd}|{command.CutoffDate:yyyy-MM-dd}|{currency}|{opening:0.00}|{closing:0.00}|{functionalCurrency}|{functionalOpening:0.00}|{functionalClosing:0.00}|{functionalComplete}\n{canonical}");
        var csv = RenderStatementCsv(locale, customer.Name, command.FromDate, command.CutoffDate, currency, opening, closing,
            functionalCurrency, functionalOpening, functionalClosing, functionalComplete, itemEntities);
        var fileName = $"statement-{SafeFile(customer.Name)}-{command.CutoffDate:yyyyMMdd}.csv";
        var snapshot = new CustomerStatementSnapshot(statementId, command.CompanyId, command.CustomerId, customer.Name,
            command.FromDate, command.CutoffDate, command.TimeZoneId, locale, currency, opening, invoiceActivity,
            allocationActivity, creditActivity, closing, checksum, manifest, manifestHash, fileName, csv, Hash(csv),
            command.IdempotencyKey, command.ActorUserId, DateTime.UtcNow, functionalCurrency, functionalOpening,
            functionalInvoiceActivity, functionalAllocationActivity, functionalCreditActivity, functionalClosing,
            functionalComplete ? "authoritative" : "legacy_or_imported_unavailable");
        db.CustomerStatementSnapshots.Add(snapshot); db.CustomerStatementItems.AddRange(itemEntities);
        await audit.WriteAsync(new(command.CompanyId, AuditActorTypes.User, command.ActorUserId,
            "finance.customer_statement.generated", "customer_statement", statementId.ToString("N"), AuditEventOutcomes.Succeeded,
            "An immutable customer statement snapshot was generated.", ["finance", "receivables", "statement"],
            new Dictionary<string, string?> { ["customerId"] = command.CustomerId.ToString("N"), ["checksum"] = checksum,
                ["sourceManifestHash"] = manifestHash, ["functionalCurrency"] = functionalCurrency,
                ["functionalEvidenceStatus"] = functionalComplete ? "authoritative" : "legacy_or_imported_unavailable" }, command.CorrelationId), ct);
        await db.SaveChangesAsync(ct); telemetry?.Operation("statement", "succeeded"); return await GetStatementAsync(new(command.CompanyId, statementId), ct);
    }

    public async Task<CustomerStatementDto> GetStatementAsync(GetCustomerStatementQuery query, CancellationToken ct) =>
        MapStatement(await db.CustomerStatementSnapshots.IgnoreQueryFilters().AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.StatementId, ct)
            ?? throw Error(CustomerCollectionReasonCodes.NotFound, "The customer statement could not be found."));

    public async Task<CustomerStatementListResult> ListStatementsAsync(ListCustomerStatementsQuery query, CancellationToken ct)
    {
        var take = PageSize(query.Take); var source = db.CustomerStatementSnapshots.IgnoreQueryFilters().AsNoTracking().Include(x => x.Items).Where(x => x.CompanyId == query.CompanyId);
        if (query.CustomerId.HasValue) source = source.Where(x => x.CustomerId == query.CustomerId.Value);
        var count = await source.CountAsync(ct); var rows = await source.OrderByDescending(x => x.CutoffDate).ThenByDescending(x => x.CreatedUtc)
            .Skip(Math.Max(0, query.Skip)).Take(take).ToListAsync(ct); return new(count, rows.Select(x => MapStatement(x)).ToArray());
    }

    public async Task<(Stream Content, string MediaType, string FileName)> OpenStatementAsync(Guid companyId, Guid statementId, CancellationToken ct)
    {
        var row = await db.CustomerStatementSnapshots.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == statementId, ct)
            ?? throw Error(CustomerCollectionReasonCodes.NotFound, "The customer statement could not be found.");
        if (!string.Equals(Hash(row.RenderedContent), row.ContentHash, StringComparison.OrdinalIgnoreCase)) throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The stored statement artifact failed its integrity check.", true);
        return (new MemoryStream(row.RenderedContent, writable: false), row.MediaType, row.FileName);
    }

    public async Task<CustomerCollectionPolicyDto?> GetPolicyAsync(Guid companyId, CancellationToken ct)
    {
        var policy = await db.CustomerCollectionPolicies.IgnoreQueryFilters().AsNoTracking().Include(x => x.Stages).Include(x => x.Exceptions)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, ct); return policy is null ? null : MapPolicy(policy);
    }

    public async Task<CustomerCollectionPolicyDto> UpsertPolicyAsync(UpsertCustomerCollectionPolicyCommand command, CancellationToken ct)
    {
        EnsureCompany(command.CompanyId); EnsureActor(command.ActorUserId);
        var normalizedStages = command.Stages.OrderBy(x => x.Stage).ToArray();
        var normalizedExceptions = (command.CustomerExceptions ?? []).OrderBy(x => x.CustomerId).ToArray();
        if (command.FeesEnabled || command.InterestEnabled)
            throw Error(CustomerCollectionReasonCodes.UnsupportedCharges, "Statutory reminder fees and interest are not supported by the active policy packs.");
        _ = Locale(command.DefaultLocale);
        if (command.GracePeriodDays is < 0 or > 90 || command.MaterialityThreshold < 0m ||
            normalizedStages.Any(x => x.Stage is < 1 or > 20 || x.DaysAfterDue is < 0 or > 730 ||
                !string.Equals(x.Channel?.Trim(), "email", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(x.TemplateKey)))
            throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The collection policy contains an invalid stage, threshold, channel, or template.");
        if (normalizedStages.Length == 0 || normalizedStages.Select(x => x.Stage).Distinct().Count() != normalizedStages.Length ||
            normalizedStages.Select(x => x.DaysAfterDue).Distinct().Count() != normalizedStages.Length)
            throw Error(CustomerCollectionReasonCodes.StaleEvidence, "Collection stages must have unique stage numbers and due-day thresholds.");
        if (normalizedExceptions.Select(x => x.CustomerId).Distinct().Count() != normalizedExceptions.Length ||
            normalizedExceptions.Any(x => x.CustomerId == Guid.Empty || string.IsNullOrWhiteSpace(x.Reason) || x.Reason.Trim().Length > 500))
            throw Error(CustomerCollectionReasonCodes.StaleEvidence, "Customer collection exceptions must identify unique customers and include a reason.");
        var exceptionCustomerIds = normalizedExceptions.Select(x => x.CustomerId).ToArray();
        var validExceptionCustomers = await db.FinanceCounterparties.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && exceptionCustomerIds.Contains(x.Id) && x.CounterpartyType == "customer")
            .Select(x => x.Id).ToArrayAsync(ct);
        if (validExceptionCustomers.Length != normalizedExceptions.Length)
            throw Error(CustomerCollectionReasonCodes.CustomerNotFound, "A collection exception references a customer outside this company.");
        var policy = await db.CustomerCollectionPolicies.IgnoreQueryFilters().Include(x => x.Stages).Include(x => x.Exceptions)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, ct);
        var now = DateTime.UtcNow;
        if (policy is null)
        {
            if (command.ExpectedVersion.HasValue) throw Error(CustomerCollectionReasonCodes.StaleVersion, "The collection policy does not yet exist.", true);
            policy = new(Guid.NewGuid(), command.CompanyId, command.GracePeriodDays, command.MaterialityThreshold, command.DefaultLocale, command.RequireApproval, now);
            db.CustomerCollectionPolicies.Add(policy);
        }
        else
        {
            if (!command.ExpectedVersion.HasValue || command.ExpectedVersion.Value != policy.Version) throw Error(CustomerCollectionReasonCodes.StaleVersion, "The collection policy changed. Refresh and try again.", true, policy.Version);
            policy.Update(command.GracePeriodDays, command.MaterialityThreshold, command.DefaultLocale, command.RequireApproval,
                command.FeesEnabled, command.InterestEnabled, now);
            db.CustomerCollectionPolicyStages.RemoveRange(policy.Stages);
            db.CustomerCollectionPolicyExceptions.RemoveRange(policy.Exceptions);
        }
        foreach (var stage in normalizedStages) db.CustomerCollectionPolicyStages.Add(new(Guid.NewGuid(), command.CompanyId,
            policy.Id, stage.Stage, stage.DaysAfterDue, stage.Channel, stage.TemplateKey, stage.RequiresApproval));
        foreach (var exception in normalizedExceptions) db.CustomerCollectionPolicyExceptions.Add(new(Guid.NewGuid(), command.CompanyId,
            policy.Id, exception.CustomerId, exception.Reason, exception.ExcludedUntilDate, command.ActorUserId, now));
        await audit.WriteAsync(new(command.CompanyId, AuditActorTypes.User, command.ActorUserId, "finance.customer_collection_policy.updated",
            "customer_collection_policy", policy.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "The customer collection policy was updated.", ["finance", "receivables", "policy"],
            new Dictionary<string, string?> { ["version"] = policy.Version.ToString(CultureInfo.InvariantCulture), ["stageCount"] = normalizedStages.Length.ToString(CultureInfo.InvariantCulture), ["customerExceptionCount"] = normalizedExceptions.Length.ToString(CultureInfo.InvariantCulture), ["feesEnabled"] = "false", ["interestEnabled"] = "false" }, command.CorrelationId), ct);
        await db.SaveChangesAsync(ct); telemetry?.Operation("policy", "succeeded"); return (await GetPolicyAsync(command.CompanyId, ct))!;
    }

    public async Task<CustomerCollectionCaseListResult> ListCasesAsync(ListCustomerCollectionCasesQuery query, CancellationToken ct)
    {
        var source = db.CustomerCollectionCases.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == query.CompanyId);
        if (query.CustomerId.HasValue) source = source.Where(x => x.CustomerId == query.CustomerId.Value);
        if (query.InvoiceId.HasValue) source = source.Where(x => x.InvoiceId == query.InvoiceId.Value);
        if (!string.IsNullOrWhiteSpace(query.Status)) source = source.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());
        var count = await source.CountAsync(ct); var rows = await source.OrderBy(x => x.FollowUpDueUtc).ThenByDescending(x => x.UpdatedUtc)
            .Skip(Math.Max(0, query.Skip)).Take(PageSize(query.Take)).ToListAsync(ct); return new(count, rows.Select(MapCase).ToArray());
    }

    public Task<CustomerCollectionCaseDto> RecordDisputeAsync(RecordCustomerDisputeCommand command, CancellationToken ct) =>
        ChangeCaseAsync(command.CompanyId, command.InvoiceId, null, command.ExpectedVersion, command.IdempotencyKey,
            "dispute_recorded", command.Reason, command.ActorUserId, command.CorrelationId,
            (x, now) => x.RecordDispute(command.Amount, command.Reason, command.OwnerUserId, command.FollowUpDueUtc, now), ct);

    public Task<CustomerCollectionCaseDto> ResolveDisputeAsync(ResolveCustomerDisputeCommand command, CancellationToken ct) =>
        ChangeCaseAsync(command.CompanyId, null, command.CaseId, command.ExpectedVersion, $"resolve-dispute:{command.CaseId:N}:{command.ExpectedVersion}",
            "dispute_resolved", command.Resolution, command.ActorUserId, command.CorrelationId,
            (x, now) => x.ResolveDispute(command.Resolution, now), ct);

    public Task<CustomerCollectionCaseDto> RecordPromiseAsync(RecordPromiseToPayCommand command, CancellationToken ct) =>
        ChangeCaseAsync(command.CompanyId, command.InvoiceId, null, command.ExpectedVersion, command.IdempotencyKey,
            "promise_recorded", $"Promise to pay {command.Amount:0.00} by {command.DueDate:yyyy-MM-dd}.", command.ActorUserId, command.CorrelationId,
            (x, now) => x.RecordPromise(command.Amount, command.DueDate, command.OwnerUserId, command.FollowUpDueUtc, now), ct);

    public Task<CustomerCollectionCaseDto> ResolvePromiseAsync(ResolvePromiseToPayCommand command, CancellationToken ct) =>
        ChangeCaseAsync(command.CompanyId, null, command.CaseId, command.ExpectedVersion, $"resolve-promise:{command.CaseId:N}:{command.ExpectedVersion}",
            command.Kept ? "promise_kept" : "promise_broken", command.Resolution, command.ActorUserId, command.CorrelationId,
            (x, now) => x.ResolvePromise(command.Kept, command.Resolution, now), ct);

    public Task<CustomerCollectionCaseDto> RecordResponseAsync(RecordCustomerCollectionResponseCommand command, CancellationToken ct)
    {
        var responseType = Required(command.ResponseType, 40).ToLowerInvariant();
        if (!responseType.All(x => char.IsAsciiLetterOrDigit(x) || x is '_' or '-'))
            throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The customer response type is invalid.");
        var summary = Required(command.Summary, 1000);
        return ChangeCaseAsync(command.CompanyId, null, command.CaseId, command.ExpectedVersion, command.IdempotencyKey,
            $"customer_response_{responseType}", summary, command.ActorUserId, command.CorrelationId,
            (x, now) => x.RecordCustomerResponse(command.OwnerUserId, command.FollowUpDueUtc, now), ct);
    }

    public async Task<CustomerReminderDraftDto> PrepareReminderAsync(PrepareCustomerReminderCommand command, CancellationToken ct)
    {
        EnsureCompany(command.CompanyId); EnsureActor(command.ActorUserId); Required(command.IdempotencyKey, 200);
        var replay = await db.CustomerReminderDrafts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == command.IdempotencyKey.Trim(), ct);
        if (replay is not null)
        {
            if (replay.InvoiceId != command.InvoiceId) throw Error(CustomerCollectionReasonCodes.IdempotencyConflict, "This reminder key was already used for another invoice.", true);
            telemetry?.Operation("reminder_prepare", "succeeded", true); return MapDraft(replay, true);
        }
        var policy = await RequirePolicyAsync(command.CompanyId, ct); var invoice = await RequireInvoiceAsync(command.CompanyId, command.InvoiceId, ct);
        EnsureCustomerNotExcepted(policy, invoice.CounterpartyId, DateOnly.FromDateTime(DateTime.UtcNow));
        var live = await LiveEvidenceAsync(invoice, DateTime.UtcNow, ct);
        if (live.OpenAmount <= policy.MaterialityThreshold) throw Error(CustomerCollectionReasonCodes.NoOpenBalance, "The invoice no longer has a material open balance.");
        var daysOverdue = DaysOverdue(invoice.DueUtc, DateOnly.FromDateTime(DateTime.UtcNow));
        var stage = command.RequestedStage.HasValue ? policy.Stages.SingleOrDefault(x => x.Stage == command.RequestedStage.Value) :
            policy.Stages.Where(x => x.DaysAfterDue + policy.GracePeriodDays <= daysOverdue).OrderByDescending(x => x.Stage).FirstOrDefault();
        if (stage is null || stage.DaysAfterDue + policy.GracePeriodDays > daysOverdue) throw Error(CustomerCollectionReasonCodes.InvoiceNotOverdue, "No configured reminder stage is due for this invoice.");
        var collectionCase = await GetOrCreateCaseAsync(invoice, ct);
        EnsureCaseAllowsContact(collectionCase);
        var profile = await db.CustomerBillingProfiles.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.CounterpartyId == invoice.CounterpartyId, ct);
        var recipient = NormalizeEmail(profile?.InvoiceDeliveryEmail ?? invoice.Counterparty.Email)
            ?? throw Error(CustomerCollectionReasonCodes.RecipientMissing, "The customer does not have a valid invoice email address.");
        CustomerStatementSnapshot? statement = null;
        if (command.StatementId.HasValue) statement = await db.CustomerStatementSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.StatementId && x.CustomerId == invoice.CounterpartyId, ct)
            ?? throw Error(CustomerCollectionReasonCodes.NotFound, "The selected customer statement could not be found.");
        var sourceHash = ReminderSourceHash(invoice, live, collectionCase, recipient, stage.Stage, statement?.Checksum);
        var duplicate = await db.CustomerReminderDrafts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.InvoiceId == invoice.Id && x.Stage == stage.Stage && x.SourceHash == sourceHash, ct);
        if (duplicate is not null) { telemetry?.Operation("reminder_prepare", "succeeded", true); return MapDraft(duplicate, true); }
        var locale = profile?.LanguageCode is "sv" or "sv-SE" ? "sv-SE" : policy.DefaultLocale;
        var subject = locale == "sv-SE" ? $"Påminnelse om faktura {invoice.InvoiceNumber}" : $"Reminder for invoice {invoice.InvoiceNumber}";
        var body = locale == "sv-SE"
            ? $"Vi vill påminna om att faktura {invoice.InvoiceNumber}, med förfallodatum {DateOnly.FromDateTime(invoice.DueUtc):yyyy-MM-dd}, har ett kvarstående belopp på {live.OpenAmount:0.00} {invoice.Currency}. Kontakta oss om betalning redan har gjorts eller om fakturan behöver utredas."
            : $"This is a reminder that invoice {invoice.InvoiceNumber}, due {DateOnly.FromDateTime(invoice.DueUtc):yyyy-MM-dd}, has an outstanding balance of {live.OpenAmount:0.00} {invoice.Currency}. Please contact us if payment has already been made or the invoice needs review.";
        var draftId = Guid.NewGuid(); var requiresApproval = policy.RequireApproval || stage.RequiresApproval; Guid? approvalId = null;
        if (requiresApproval)
        {
            approvalId = Guid.NewGuid();
            var approval = ApprovalRequest.CreateForTarget(approvalId.Value, command.CompanyId,
                ApprovalTargetEntityType.CustomerCollectionReminder, draftId, AuditActorTypes.User, command.ActorUserId,
                "customer_collection_reminder_send", new Dictionary<string, JsonNode?>
                { ["invoiceId"] = JsonValue.Create(invoice.Id), ["sourceHash"] = JsonValue.Create(sourceHash), ["openAmount"] = JsonValue.Create(live.OpenAmount), ["currency"] = JsonValue.Create(invoice.Currency), ["stage"] = JsonValue.Create(stage.Stage) },
                null, null, [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "finance_approver")]);
            db.ApprovalRequests.Add(approval);
        }
        var draft = new CustomerReminderDraft(draftId, command.CompanyId, collectionCase.Id, invoice.Id, invoice.CounterpartyId,
            statement?.Id, stage.Stage, recipient, subject, body, live.OpenAmount, invoice.Currency, sourceHash,
            command.IdempotencyKey, requiresApproval, approvalId, command.ActorUserId, DateTime.UtcNow);
        db.CustomerReminderDrafts.Add(draft); collectionCase.MarkReminderPrepared(stage.Stage, DateTime.UtcNow);
        db.CustomerCollectionActions.Add(new(Guid.NewGuid(), command.CompanyId, collectionCase.Id, "reminder_prepared", "prepared",
            $"Reminder stage {stage.Stage} was prepared from current receivable evidence.", sourceHash, $"prepare:{command.IdempotencyKey.Trim()}", command.ActorUserId, DateTime.UtcNow));
        await audit.WriteAsync(new(command.CompanyId, AuditActorTypes.User, command.ActorUserId, "finance.customer_reminder.prepared",
            "customer_reminder", draft.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "A customer reminder draft was prepared. No customer communication was sent.", ["finance", "receivables", "reminder"],
            new Dictionary<string, string?> { ["invoiceId"] = invoice.Id.ToString("N"), ["sourceHash"] = sourceHash, ["stage"] = stage.Stage.ToString(CultureInfo.InvariantCulture), ["approvalRequestId"] = approvalId?.ToString("N") }, command.CorrelationId), ct);
        await db.SaveChangesAsync(ct); telemetry?.Operation("reminder_prepare", "succeeded"); return MapDraft(draft);
    }

    public async Task<CustomerReminderDeliveryDto> SendReminderAsync(SendCustomerReminderCommand command, CancellationToken ct)
    {
        EnsureCompany(command.CompanyId); EnsureActor(command.ActorUserId); Required(command.IdempotencyKey, 200);
        var replay = await db.CustomerReminderDeliveries.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == command.IdempotencyKey.Trim(), ct);
        if (replay is not null)
        {
            if (replay.ReminderDraftId != command.ReminderDraftId) throw Error(CustomerCollectionReasonCodes.IdempotencyConflict, "This send key was already used for another reminder.", true);
            telemetry?.Operation("reminder_send", "queued", true); return MapDelivery(replay, true);
        }
        var draft = await db.CustomerReminderDrafts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ReminderDraftId, ct)
            ?? throw Error(CustomerCollectionReasonCodes.NotFound, "The reminder draft could not be found.");
        if (draft.Version != command.ExpectedDraftVersion) throw Error(CustomerCollectionReasonCodes.StaleVersion, "The reminder draft changed. Refresh and try again.", true, draft.Version);
        if (!string.Equals(draft.SourceHash, command.ExpectedSourceHash, StringComparison.Ordinal)) throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The reminder evidence changed. Prepare a new reminder.", true);
        await EnsureDraftSendableAsync(draft, ct);
        var delivery = new CustomerReminderDelivery(Guid.NewGuid(), command.CompanyId, draft.Id, draft.SourceHash,
            draft.RecipientEmail, command.IdempotencyKey, command.ActorUserId, DateTime.UtcNow);
        db.CustomerReminderDeliveries.Add(delivery); draft.Queue(DateTime.UtcNow);
        outbox.Enqueue(command.CompanyId, CompanyOutboxTopics.CustomerReminderEmailDeliveryRequested,
            new CustomerReminderEmailDeliveryRequestedMessage(command.CompanyId, delivery.Id, command.CorrelationId), command.CorrelationId,
            idempotencyKey: $"customer-reminder-email:{command.CompanyId:N}:{command.IdempotencyKey.Trim()}");
        await audit.WriteAsync(new(command.CompanyId, AuditActorTypes.User, command.ActorUserId, "finance.customer_reminder.send_queued",
            "customer_reminder", draft.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "Customer reminder email delivery was queued for background execution.", ["finance", "receivables", "reminder", "outbox"],
            new Dictionary<string, string?> { ["deliveryId"] = delivery.Id.ToString("N"), ["sourceHash"] = draft.SourceHash }, command.CorrelationId), ct);
        await db.SaveChangesAsync(ct); telemetry?.Operation("reminder_send", "queued"); return MapDelivery(delivery);
    }

    public async Task<CustomerCollectionMetricsDto> GetMetricsAsync(CollectionMetricsQuery query, CancellationToken ct)
    {
        var asOfExclusive = query.AsOfDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); var lookback = Math.Clamp(query.LookbackDays, 1, 366);
        var metricsCurrency = NormalizeCurrency(query.Currency);
        var invoiceQuery = db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == query.CompanyId && x.IssuedUtc < asOfExclusive && x.Amount > 0m && x.PostingStatus == FinanceDocumentPostingStatuses.Booked);
        if (metricsCurrency is not null) invoiceQuery = invoiceQuery.Where(x => x.Currency == metricsCurrency);
        var invoices = await invoiceQuery
            .OrderByDescending(x => x.IssuedUtc).Take(MaximumProjectionItems).ToListAsync(ct);
        var metricCurrencies = invoices.Select(x => x.Currency).Distinct(StringComparer.Ordinal).ToArray();
        if (metricsCurrency is null && metricCurrencies.Length > 1)
            throw Error(CustomerCollectionReasonCodes.UnsupportedCurrency, "Collection metrics cannot combine currencies. Request one currency at a time.");
        var allocations = await EffectiveAllocationsAsync(query.CompanyId, invoices.Select(x => x.Id).ToArray(), asOfExclusive, ct);
        var open = invoices.Select(x => new
        {
            Invoice = x,
            Open = Money(Math.Max(0m, x.Amount - (allocations.GetValueOrDefault(x.Id)?.DocumentAmount ?? 0m)))
        }).ToArray();
        var openTotal = Money(open.Sum(x => x.Open)); var overdue = Money(open.Where(x => DateOnly.FromDateTime(x.Invoice.DueUtc) < query.AsOfDate).Sum(x => x.Open));
        var salesStart = asOfExclusive.AddDays(-lookback); var creditSales = Money(invoices.Where(x => x.IssuedUtc >= salesStart).Sum(x => x.Amount));
        decimal? dso = creditSales <= 0m ? null : decimal.Round(openTotal / creditSales * lookback, 2);
        var deliveries = await db.CustomerReminderDeliveries.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == query.CompanyId && x.CreatedUtc >= salesStart).ToListAsync(ct);
        var acceptedDeliveries = deliveries.Where(x => x.Status == "accepted" && x.AcceptedUtc.HasValue).ToArray();
        var acceptedDraftIds = acceptedDeliveries.Select(x => x.ReminderDraftId).ToArray();
        var acceptedDrafts = await db.CustomerReminderDrafts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && acceptedDraftIds.Contains(x.Id))
            .Select(x => new { x.Id, x.InvoiceId }).ToListAsync(ct);
        var acceptedAtByInvoice = (from delivery in acceptedDeliveries join draft in acceptedDrafts on delivery.ReminderDraftId equals draft.Id
            group delivery by draft.InvoiceId into grouped select new { InvoiceId = grouped.Key, AcceptedUtc = grouped.Min(x => x.AcceptedUtc!.Value) })
            .ToDictionary(x => x.InvoiceId, x => x.AcceptedUtc);
        var draftInvoiceIds = acceptedAtByInvoice.Keys.ToArray();
        var reminderPaymentRows = await db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking().Include(x => x.Payment)
            .Where(x => x.CompanyId == query.CompanyId && x.InvoiceId != null && draftInvoiceIds.Contains(x.InvoiceId.Value) &&
                x.SettlementStatus != PaymentAllocationSettlementStatuses.Reversed &&
                x.Payment.Status == PaymentStatuses.Completed && x.Payment.PaymentDate >= salesStart)
            .Select(x => new { InvoiceId = x.InvoiceId!.Value, x.Payment.PaymentDate }).ToListAsync(ct);
        var reminderPayments = reminderPaymentRows.Where(x => x.PaymentDate >= acceptedAtByInvoice[x.InvoiceId])
            .Select(x => x.InvoiceId).Distinct().Count();
        var cases = await db.CustomerCollectionCases.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == query.CompanyId).ToListAsync(ct);
        var actions = await db.CustomerCollectionActions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == query.CompanyId && x.OccurredUtc >= salesStart).ToListAsync(ct);
        var openDisputes = cases.Where(x => x.DisputeStatus == "open").ToArray(); var averageDisputeAge = openDisputes.Length == 0 ? 0m : decimal.Round((decimal)openDisputes.Average(x => (DateTime.UtcNow - x.UpdatedUtc).TotalDays), 2);
        var accepted = deliveries.Count(x => x.Status == "accepted");
        return new(query.AsOfDate, metricsCurrency ?? metricCurrencies.SingleOrDefault() ?? "",
            overdue, openTotal, creditSales, lookback, openTotal, creditSales, dso, accepted, reminderPayments,
            accepted == 0 ? null : decimal.Round((decimal)reminderPayments / accepted, 4), cases.Count(x => x.PromiseStatus == "kept"),
            cases.Count(x => x.PromiseStatus == "broken"), averageDisputeAge, actions.Count(x => x.ActionType == "manual_override"),
            deliveries.Count(x => x.Status is "failed" or "blocked" or "reconciliation_required"));
    }

    internal async Task EnsureDraftSendableAsync(CustomerReminderDraft draft, CancellationToken ct)
    {
        var invoice = await RequireInvoiceAsync(draft.CompanyId, draft.InvoiceId, ct);
        EnsureCustomerNotExcepted(await RequirePolicyAsync(draft.CompanyId, ct), invoice.CounterpartyId, DateOnly.FromDateTime(DateTime.UtcNow));
        var collectionCase = await db.CustomerCollectionCases.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == draft.CompanyId && x.Id == draft.CaseId, ct) ?? throw Error(CustomerCollectionReasonCodes.NotFound, "The collection case could not be found.");
        EnsureCaseAllowsContact(collectionCase); var live = await LiveEvidenceAsync(invoice, DateTime.UtcNow, ct);
        var profile = await db.CustomerBillingProfiles.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == draft.CompanyId && x.CounterpartyId == invoice.CounterpartyId, ct);
        var currentRecipient = NormalizeEmail(profile?.InvoiceDeliveryEmail ?? invoice.Counterparty.Email);
        if (!string.Equals(currentRecipient, draft.RecipientEmail, StringComparison.OrdinalIgnoreCase))
            throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The customer's reminder recipient changed after preparation. Prepare a new reminder.", true);
        var currentHash = ReminderSourceHash(invoice, live, collectionCase, draft.RecipientEmail, draft.Stage,
            draft.StatementId.HasValue ? await db.CustomerStatementSnapshots.IgnoreQueryFilters().Where(x => x.CompanyId == draft.CompanyId && x.Id == draft.StatementId).Select(x => x.Checksum).SingleOrDefaultAsync(ct) : null);
        if (live.OpenAmount <= 0m || !string.Equals(currentHash, draft.SourceHash, StringComparison.Ordinal))
            throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The balance or collection evidence changed after reminder preparation. Prepare a new reminder.", true);
        if (draft.ApprovalRequestId.HasValue)
        {
            var approval = await db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == draft.CompanyId && x.Id == draft.ApprovalRequestId && x.TargetEntityId == draft.Id, ct);
            if (approval?.Status != ApprovalRequestStatus.Approved) throw Error(CustomerCollectionReasonCodes.ApprovalRequired, "An approved, current reminder decision is required before sending.");
            if (!approval.ThresholdContext.TryGetValue("sourceHash", out var hash) || !string.Equals(hash?.ToString(), draft.SourceHash, StringComparison.Ordinal))
                throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The reminder approval does not match the current source evidence.", true);
        }
    }

    private async Task<CustomerCollectionCaseDto> ChangeCaseAsync(Guid companyId, Guid? invoiceId, Guid? caseId,
        long? expectedVersion, string idempotencyKey, string actionType, string summary, Guid actorUserId,
        string? correlationId, Action<CustomerCollectionCase, DateTime> change, CancellationToken ct)
    {
        EnsureCompany(companyId); EnsureActor(actorUserId); Required(idempotencyKey, 200);
        var replay = await db.CustomerCollectionActions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == idempotencyKey.Trim(), ct);
        if (replay is not null)
        {
            var replayCase = await db.CustomerCollectionCases.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == replay.CaseId, ct);
            if (!string.Equals(replay.ActionType, actionType, StringComparison.Ordinal) || invoiceId.HasValue && replayCase.InvoiceId != invoiceId.Value)
                throw Error(CustomerCollectionReasonCodes.IdempotencyConflict, "This case action key was already used for different collection inputs.", true);
            return MapCase(replayCase);
        }
        CustomerCollectionCase collectionCase;
        if (caseId.HasValue) collectionCase = await db.CustomerCollectionCases.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == caseId, ct) ?? throw Error(CustomerCollectionReasonCodes.NotFound, "The collection case could not be found.");
        else collectionCase = await GetOrCreateCaseAsync(await RequireInvoiceAsync(companyId, invoiceId!.Value, ct), ct);
        if (expectedVersion.HasValue && collectionCase.Version != expectedVersion.Value) throw Error(CustomerCollectionReasonCodes.StaleVersion, "The collection case changed. Refresh and try again.", true, collectionCase.Version);
        var now = DateTime.UtcNow; change(collectionCase, now); var sourceHash = Hash($"case|{collectionCase.Id:N}|{collectionCase.Version}|{collectionCase.Status}|{collectionCase.DisputeStatus}|{collectionCase.PromiseStatus}");
        db.CustomerCollectionActions.Add(new(Guid.NewGuid(), companyId, collectionCase.Id, actionType, "recorded", summary, sourceHash, idempotencyKey, actorUserId, now));
        await audit.WriteAsync(new(companyId, AuditActorTypes.User, actorUserId, $"finance.customer_collection.{actionType}", "customer_collection_case", collectionCase.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "The customer collection case was updated.", ["finance", "receivables", "collections"], new Dictionary<string, string?> { ["sourceHash"] = sourceHash, ["version"] = collectionCase.Version.ToString(CultureInfo.InvariantCulture) }, correlationId), ct);
        await db.SaveChangesAsync(ct); return MapCase(collectionCase);
    }

    private async Task<CustomerCollectionPolicy> RequirePolicyAsync(Guid companyId, CancellationToken ct) =>
        await db.CustomerCollectionPolicies.IgnoreQueryFilters().Include(x => x.Stages).Include(x => x.Exceptions).SingleOrDefaultAsync(x => x.CompanyId == companyId, ct)
        ?? throw Error(CustomerCollectionReasonCodes.PolicyMissing, "Configure a customer collection policy before preparing reminders.");

    private static void EnsureCustomerNotExcepted(CustomerCollectionPolicy policy, Guid customerId, DateOnly today)
    {
        var exception = policy.Exceptions.SingleOrDefault(x => x.CustomerId == customerId &&
            (!x.ExcludedUntilDate.HasValue || x.ExcludedUntilDate.Value >= today));
        if (exception is not null)
            throw Error(CustomerCollectionReasonCodes.CollectionOnHold,
                $"The customer is excluded from automated collection contact: {exception.Reason}");
    }

    private async Task<FinanceInvoice> RequireInvoiceAsync(Guid companyId, Guid invoiceId, CancellationToken ct) =>
        await db.FinanceInvoices.IgnoreQueryFilters().Include(x => x.Counterparty).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invoiceId && x.Amount > 0m && x.DocumentKind == FinanceDocumentKinds.Invoice && x.PostingStatus == FinanceDocumentPostingStatuses.Booked, ct)
        ?? throw Error(CustomerCollectionReasonCodes.InvoiceNotFound, "The posted customer invoice could not be found.");

    private async Task<CustomerCollectionCase> GetOrCreateCaseAsync(FinanceInvoice invoice, CancellationToken ct)
    {
        var row = await db.CustomerCollectionCases.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == invoice.CompanyId && x.InvoiceId == invoice.Id, ct);
        if (row is not null) return row; row = new(Guid.NewGuid(), invoice.CompanyId, invoice.CounterpartyId, invoice.Id, DateTime.UtcNow); db.CustomerCollectionCases.Add(row); return row;
    }

    private async Task<LiveEvidence> LiveEvidenceAsync(FinanceInvoice invoice, DateTime cutoffExclusive, CancellationToken ct)
    {
        var allocations = await EffectiveAllocationsAsync(invoice.CompanyId, [invoice.Id], cutoffExclusive, ct);
        var allocated = allocations.GetValueOrDefault(invoice.Id)?.DocumentAmount ?? 0m;
        return new(Money(allocated), Money(Math.Max(0m, invoice.Amount - allocated)));
    }

    private async Task<Dictionary<Guid, EffectiveAllocation>> EffectiveAllocationsAsync(Guid companyId, Guid[] invoiceIds, DateTime cutoffExclusive, CancellationToken ct)
    {
        if (invoiceIds.Length == 0) return [];
        var rows = await db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking().Include(x => x.Payment)
            .Where(x => x.CompanyId == companyId && x.InvoiceId != null && invoiceIds.Contains(x.InvoiceId.Value) &&
                x.SettlementStatus != PaymentAllocationSettlementStatuses.Reversed &&
                x.Payment.Status == PaymentStatuses.Completed && x.Payment.PaymentType == PaymentTypes.Incoming && x.Payment.PaymentDate < cutoffExclusive)
            .Select(x => new
            {
                x.Id,
                InvoiceId = x.InvoiceId!.Value,
                AppliedAmount = x.AllocatedAmount + x.WriteOffAmount,
                x.AllocatedFunctionalAmount
            }).Take(MaximumProjectionItems).ToListAsync(ct);
        var rowIds = rows.Select(x => x.Id).ToArray();
        var released = await db.CustomerInvoiceCorrectionAllocationAdjustments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && rowIds.Contains(x.PaymentAllocationId)).GroupBy(x => x.PaymentAllocationId)
            .Select(x => new { Id = x.Key, Amount = x.Sum(y => y.ReleasedAmount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        return rows.GroupBy(x => x.InvoiceId).ToDictionary(group => group.Key, group =>
        {
            var effectiveRows = group.Select(row =>
            {
                var effectiveDocument = Money(Math.Max(0m, row.AppliedAmount - released.GetValueOrDefault(row.Id)));
                var effectiveFunctional = row.AllocatedFunctionalAmount.HasValue && row.AppliedAmount > 0m
                    ? Money(row.AllocatedFunctionalAmount.Value * (effectiveDocument / row.AppliedAmount))
                    : (decimal?)null;
                return new { Document = effectiveDocument, Functional = effectiveFunctional };
            }).ToArray();
            var authoritative = effectiveRows.All(row => row.Functional.HasValue);
            return new EffectiveAllocation(
                Money(effectiveRows.Sum(row => row.Document)),
                authoritative ? Money(effectiveRows.Sum(row => row.Functional!.Value)) : null,
                authoritative);
        });
    }

    private static void EnsureCaseAllowsContact(CustomerCollectionCase collectionCase)
    {
        if (collectionCase.DisputeStatus == "open") throw Error(CustomerCollectionReasonCodes.DisputeOpen, "The invoice has an open dispute. Resolve it before preparing or sending a reminder.");
        if (collectionCase.IsOnHold) throw Error(CustomerCollectionReasonCodes.CollectionOnHold, "Collections are on hold for this invoice.");
        if (collectionCase.Status == CustomerCollectionCaseStatuses.Resolved) throw Error(CustomerCollectionReasonCodes.NoOpenBalance, "The collection case is resolved.");
    }

    private static CustomerAgingItemDto MapAging(AgingRow row, DateOnly cutoff,
        IReadOnlyDictionary<CustomerCurrencyKey, decimal> exposure, CustomerBillingProfile? profile)
    {
        var days = DaysOverdue(row.Invoice.DueUtc, cutoff); var key = new CustomerCurrencyKey(row.Invoice.CounterpartyId, row.Invoice.Currency);
        var customerExposure = exposure.TryGetValue(key, out var amount) ? amount : row.OpenAmount;
        var action = row.Case?.DisputeStatus == "open" ? "Review the customer dispute before taking collection action."
            : row.Case?.PromiseStatus == "pending" ? "Follow up on the recorded promise to pay."
            : days <= 0 ? "Monitor until the invoice due date." : "Prepare the next governed reminder stage.";
        return new(row.Invoice.Id, row.Invoice.CounterpartyId, row.Invoice.InvoiceNumber, row.Invoice.Counterparty.Name,
            DateOnly.FromDateTime(row.Invoice.IssuedUtc), DateOnly.FromDateTime(row.Invoice.DueUtc), days, AgingBucket(days),
            row.Invoice.Currency, row.Invoice.Amount, row.AllocatedAmount, row.OpenAmount, row.Case?.DisputeStatus == "open",
            row.Case?.IsOnHold == true, row.Case?.PromiseStatus, row.Case?.PromiseDueDate, row.Case?.ReminderStage ?? 0,
            profile?.CreditLimit, customerExposure, action,
            [$"Invoice {row.Invoice.InvoiceNumber}: due {DateOnly.FromDateTime(row.Invoice.DueUtc):yyyy-MM-dd}, open {row.OpenAmount:0.00} {row.Invoice.Currency}.", $"Recorded allocations through cutoff: {row.AllocatedAmount:0.00} {row.Invoice.Currency}."],
            row.AccountingProfile?.GrossBaseAmount,
            row.FunctionalAllocatedAmount,
            row.FunctionalOpenAmount,
            row.AccountingProfile?.BaseCurrency, row.AccountingProfile?.ExchangeRate,
            row.AccountingProfile?.ExchangeRateDate, row.AccountingProfile?.ExchangeRateIdentity);
    }

    private static CustomerStatementDto MapStatement(CustomerStatementSnapshot x, bool replay = false) => new(x.Id, x.CustomerId,
        x.CustomerName, x.FromDate, x.CutoffDate, x.TimeZoneId, x.Locale, x.Currency, x.OpeningBalance, x.InvoiceActivity,
        x.AllocationActivity, x.CreditActivity, x.ClosingBalance, x.Checksum, x.SourceManifestHash, x.MediaType, x.FileName,
        x.ContentHash, x.ContentLength, x.CreatedUtc, x.Items.OrderBy(y => y.Sequence).Select(y => new CustomerStatementItemDto(y.Id,
            y.ItemType, y.InvoiceId, y.PaymentAllocationId, y.EffectiveDate, y.Reference, y.DebitAmount, y.CreditAmount,
            y.RunningBalance, y.SourceHash, y.FunctionalDebitAmount, y.FunctionalCreditAmount, y.FunctionalRunningBalance,
            y.FunctionalCurrency, y.ExchangeRate, y.ExchangeRateDate, y.ExchangeRateIdentity, y.CurrencyProvenance)).ToArray(), replay,
        x.FunctionalCurrency, x.FunctionalOpeningBalance, x.FunctionalInvoiceActivity, x.FunctionalAllocationActivity,
        x.FunctionalCreditActivity, x.FunctionalClosingBalance, x.FunctionalEvidenceStatus);
    private static CustomerCollectionPolicyDto MapPolicy(CustomerCollectionPolicy x) => new(x.Id, x.GracePeriodDays,
        x.MaterialityThreshold, x.DefaultLocale, x.RequireApproval, x.FeesEnabled, x.InterestEnabled, x.Version, x.UpdatedUtc,
        x.Stages.OrderBy(y => y.Stage).Select(y => new CustomerCollectionPolicyStageDto(y.Stage, y.DaysAfterDue, y.Channel, y.TemplateKey, y.RequiresApproval)).ToArray(),
        x.Exceptions.OrderBy(y => y.CustomerId).Select(y => new CustomerCollectionPolicyExceptionDto(y.CustomerId, y.Reason, y.ExcludedUntilDate)).ToArray());
    private static CustomerCollectionCaseDto MapCase(CustomerCollectionCase x) => new(x.Id, x.CustomerId, x.InvoiceId, x.Status,
        x.ReminderStage, x.IsOnHold, x.HoldReason, x.DisputeStatus, x.DisputeReason, x.DisputedAmount, x.PromiseStatus,
        x.PromiseAmount, x.PromiseDueDate, x.OwnerUserId, x.FollowUpDueUtc, x.WorkTaskId, x.Version, x.CreatedUtc, x.UpdatedUtc);
    private static CustomerReminderDraftDto MapDraft(CustomerReminderDraft x, bool replay = false) => new(x.Id, x.CaseId,
        x.InvoiceId, x.CustomerId, x.StatementId, x.Stage, x.RecipientEmail, x.Subject, x.Body, x.PreparedOpenAmount,
        x.Currency, x.SourceHash, x.Status, x.ApprovalRequestId, x.Version, x.CreatedUtc, x.UpdatedUtc, replay);
    private static CustomerReminderDeliveryDto MapDelivery(CustomerReminderDelivery x, bool replay = false) => new(x.Id,
        x.ReminderDraftId, x.Status, x.Attempts, x.ProviderReference, x.FailureCode, x.FailureSummary, x.CreatedUtc,
        x.UpdatedUtc, x.AcceptedUtc, replay);

    private static string ReminderSourceHash(FinanceInvoice invoice, LiveEvidence live, CustomerCollectionCase collectionCase,
        string recipient, int stage, string? statementChecksum) => Hash($"{invoice.CompanyId:N}|{invoice.Id:N}|{invoice.UpdatedUtc:O}|{invoice.Amount:0.00}|{live.AllocatedAmount:0.00}|{live.OpenAmount:0.00}|{collectionCase.IsOnHold}|{collectionCase.DisputeStatus}|{collectionCase.PromiseStatus}|{collectionCase.PromiseAmount:0.00}|{collectionCase.PromiseDueDate:yyyy-MM-dd}|{recipient}|{stage}|{statementChecksum}");
    private static int DaysOverdue(DateTime dueUtc, DateOnly cutoff) => cutoff.DayNumber - DateOnly.FromDateTime(dueUtc).DayNumber;
    private static string AgingBucket(int days) => days <= 0 ? "current" : days <= 30 ? "1_30" : days <= 60 ? "31_60" : days <= 90 ? "61_90" : "over_90";
    private static DateTime CutoffExclusive(DateOnly cutoff, string timeZoneId) => StartOfDate(cutoff.AddDays(1), timeZoneId);
    private static DateTime StartOfDate(DateOnly date, string timeZoneId)
    {
        try { var zone = TimeZoneInfo.FindSystemTimeZoneById(Required(timeZoneId, 100)); return TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone); }
        catch (TimeZoneNotFoundException) { throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The requested statement time zone is not supported."); }
        catch (InvalidTimeZoneException) { throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The requested statement time zone is invalid."); }
    }
    private static DateOnly LocalDate(DateTime utc, string timeZoneId)
    {
        try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime(), TimeZoneInfo.FindSystemTimeZoneById(Required(timeZoneId, 100)))); }
        catch (TimeZoneNotFoundException) { throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The requested statement time zone is not supported."); }
        catch (InvalidTimeZoneException) { throw Error(CustomerCollectionReasonCodes.StaleEvidence, "The requested statement time zone is invalid."); }
    }
    private static string Locale(string value) => value?.Trim().ToLowerInvariant() switch { "sv" or "sv-se" => "sv-SE", "en" or "en-us" => "en-US", _ => throw Error(CustomerCollectionReasonCodes.StaleEvidence, "Only English and Swedish statements are supported.") };
    private static string? NormalizeCurrency(string? value) => string.IsNullOrWhiteSpace(value) ? null : Required(value, 3).ToUpperInvariant();
    private static string? NormalizeEmail(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; var x = value.Trim().ToLowerInvariant(); return x.Length <= 320 && x.Contains('@') && !x.Contains(' ') ? x : null; }
    private static int PageSize(int value) => Math.Clamp(value <= 0 ? 100 : value, 1, 200);
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Required(string value, int max) { var x = value?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw Error(CustomerCollectionReasonCodes.StaleEvidence, "A required collection value is missing or too long.") : x; }
    private static void EnsureCompany(Guid id) { if (id == Guid.Empty) throw Error(CustomerCollectionReasonCodes.NotFound, "Company context is required."); }
    private static void EnsureActor(Guid id) { if (id == Guid.Empty) throw Error(CustomerCollectionReasonCodes.StaleEvidence, "An authenticated actor is required."); }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string SafeFile(string text) => string.Concat(text.Select(x => Path.GetInvalidFileNameChars().Contains(x) ? '_' : x)).Replace(' ', '-').ToLowerInvariant();
    private static byte[] RenderStatementCsv(string locale, string customer, DateOnly from, DateOnly cutoff, string currency,
        decimal opening, decimal closing, string? functionalCurrency, decimal? functionalOpening, decimal? functionalClosing,
        bool functionalComplete, IReadOnlyList<CustomerStatementItem> items)
    {
        var sb = new StringBuilder(); sb.Append('\uFEFF');
        sb.AppendLine(locale == "sv-SE" ? "Kundreskontra" : "Customer statement");
        sb.AppendLine($"Customer,{Csv(customer)}"); sb.AppendLine($"Period,{from:yyyy-MM-dd} - {cutoff:yyyy-MM-dd}"); sb.AppendLine($"Document currency,{currency}");
        sb.AppendLine($"Functional currency,{functionalCurrency}"); sb.AppendLine($"Functional evidence,{(functionalComplete ? "authoritative" : "legacy_or_imported_unavailable")}");
        sb.AppendLine($"Opening balance,{opening:0.00},{functionalOpening:0.00}");
        sb.AppendLine("Date,Type,Reference,Document debit,Document credit,Document balance,Functional debit,Functional credit,Functional balance,Functional currency,Exchange rate,Rate date,Rate identity,Currency provenance,Source checksum");
        foreach (var x in items.OrderBy(x => x.Sequence)) sb.AppendLine($"{x.EffectiveDate:yyyy-MM-dd},{Csv(x.ItemType)},{Csv(x.Reference)},{x.DebitAmount:0.00},{x.CreditAmount:0.00},{x.RunningBalance:0.00},{x.FunctionalDebitAmount:0.00},{x.FunctionalCreditAmount:0.00},{x.FunctionalRunningBalance:0.00},{x.FunctionalCurrency},{x.ExchangeRate},{x.ExchangeRateDate:yyyy-MM-dd},{x.ExchangeRateIdentity},{x.CurrencyProvenance},{x.SourceHash}");
        sb.AppendLine($"Closing balance,,,,,{closing:0.00},,,{functionalClosing:0.00},,,,,,"); return Encoding.UTF8.GetBytes(sb.ToString());
    }
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static CustomerCollectionException Error(string code, string message, bool conflict = false, long? version = null) => new(code, message, conflict, version);

    private sealed record AgingRow(FinanceInvoice Invoice, decimal AllocatedAmount, decimal OpenAmount,
        CustomerCollectionCase? Case, CustomerInvoiceAccountingProfile? AccountingProfile,
        decimal? FunctionalAllocatedAmount, decimal? FunctionalOpenAmount,
        bool HasAuthoritativeFunctionalFacts);
    private sealed record EffectiveAllocation(decimal DocumentAmount, decimal? FunctionalAmount,
        bool HasAuthoritativeFunctionalFacts);
    private sealed record CustomerCurrencyKey(Guid CustomerId, string Currency);
    private sealed record LiveEvidence(decimal AllocatedAmount, decimal OpenAmount);
    private sealed record StatementEvent(DateTime OccurredUtc, string Type, Guid? InvoiceId, Guid? PaymentAllocationId,
        string Reference, decimal Debit, decimal Credit, decimal? FunctionalDebit, decimal? FunctionalCredit,
        string? FunctionalCurrency, decimal? ExchangeRate, DateOnly? ExchangeRateDate, string? ExchangeRateIdentity,
        string? CurrencyProvenance, string SourceHash);
}
