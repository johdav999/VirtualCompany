using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinancePlanningEntityResolver : IFinancePlanningEntityResolver
{
    private readonly VirtualCompanyDbContext _db;

    public FinancePlanningEntityResolver(VirtualCompanyDbContext db)
    {
        _db = db;
    }

    public async Task<FinanceEntityResolutionResult> ResolveAsync(
        FinanceEntityResolutionRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var referenceType = request.ReferenceType.Trim().ToLowerInvariant();
        var reference = request.ReferenceValue.Trim();
        var take = request.MaximumCandidates + 1;
        IReadOnlyList<FinanceEntityResolutionCandidate> candidates = referenceType switch
        {
            FinancePlanningReferenceTypes.Invoice => await ResolveInvoicesAsync(request.CompanyId, reference, take, cancellationToken),
            FinancePlanningReferenceTypes.Bill => await ResolveBillsAsync(request.CompanyId, reference, take, cancellationToken),
            FinancePlanningReferenceTypes.Customer => await ResolveCounterpartiesAsync(request.CompanyId, reference, "customer", take, cancellationToken),
            FinancePlanningReferenceTypes.Supplier => await ResolveCounterpartiesAsync(request.CompanyId, reference, "supplier", take, cancellationToken),
            FinancePlanningReferenceTypes.FiscalPeriod => await ResolvePeriodsAsync(request.CompanyId, reference, take, cancellationToken),
            FinancePlanningReferenceTypes.Migration => await ResolveMigrationsAsync(request.CompanyId, reference, take, cancellationToken),
            FinancePlanningReferenceTypes.Account => await ResolveAccountsAsync(request.CompanyId, reference, take, cancellationToken),
            FinancePlanningReferenceTypes.Journal => await ResolveJournalsAsync(request.CompanyId, reference, take, cancellationToken),
            FinancePlanningReferenceTypes.VoucherSeries => await ResolveVoucherSeriesAsync(request.CompanyId, reference, take, cancellationToken),
            FinancePlanningReferenceTypes.ReportDefinition => await ResolveReportDefinitionsAsync(request.CompanyId, reference, take, cancellationToken),
            FinancePlanningReferenceTypes.ReportLine => await ResolveReportLinesAsync(request.CompanyId, reference, take, cancellationToken),
            _ => []
        };

        var state = candidates.Count switch
        {
            0 => FinanceEntityResolutionStates.NotFound,
            1 => FinanceEntityResolutionStates.Resolved,
            _ => FinanceEntityResolutionStates.Ambiguous
        };
        return new FinanceEntityResolutionResult(state, referenceType, reference, candidates.Take(request.MaximumCandidates).ToArray());
    }

    private async Task<IReadOnlyList<FinanceEntityResolutionCandidate>> ResolveInvoicesAsync(
        Guid companyId, string reference, int take, CancellationToken cancellationToken) =>
        await _db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.InvoiceNumber == reference)
            .OrderBy(row => row.Id)
            .Take(take)
            .Select(row => new FinanceEntityResolutionCandidate(
                FinancePlanningReferenceTypes.Invoice,
                row.Id.ToString(),
                "finance_invoice:" + row.Id,
                row.UpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                row.UpdatedUtc,
                "Accessible invoice match"))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<FinanceEntityResolutionCandidate>> ResolveBillsAsync(
        Guid companyId, string reference, int take, CancellationToken cancellationToken) =>
        await _db.FinanceBills.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.BillNumber == reference)
            .OrderBy(row => row.Id)
            .Take(take)
            .Select(row => new FinanceEntityResolutionCandidate(
                FinancePlanningReferenceTypes.Bill,
                row.Id.ToString(),
                "finance_bill:" + row.Id,
                row.UpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                row.UpdatedUtc,
                "Accessible supplier bill match"))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<FinanceEntityResolutionCandidate>> ResolveCounterpartiesAsync(
        Guid companyId, string reference, string type, int take, CancellationToken cancellationToken) =>
        await _db.FinanceCounterparties.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.CounterpartyType == type &&
                          row.MergedIntoCounterpartyId == null && row.Name == reference)
            .OrderBy(row => row.Id)
            .Take(take)
            .Select(row => new FinanceEntityResolutionCandidate(
                type,
                row.Id.ToString(),
                "finance_counterparty:" + row.Id,
                row.UpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                row.UpdatedUtc,
                type == "customer" ? "Accessible customer match" : "Accessible supplier match"))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<FinanceEntityResolutionCandidate>> ResolvePeriodsAsync(
        Guid companyId, string reference, int take, CancellationToken cancellationToken) =>
        await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Name == reference)
            .OrderBy(row => row.Id)
            .Take(take)
            .Select(row => new FinanceEntityResolutionCandidate(
                FinancePlanningReferenceTypes.FiscalPeriod,
                row.Id.ToString(),
                "fiscal_period:" + row.Id,
                row.UpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                row.UpdatedUtc,
                "Accessible fiscal period match"))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<FinanceEntityResolutionCandidate>> ResolveMigrationsAsync(
        Guid companyId, string reference, int take, CancellationToken cancellationToken)
    {
        var hasId = Guid.TryParse(reference, out var id);
        return await _db.AccountingProviderSwitches.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == companyId &&
                          (hasId ? row.Id == id : row.CorrelationId == reference))
            .OrderBy(row => row.Id)
            .Take(take)
            .Select(row => new FinanceEntityResolutionCandidate(
                FinancePlanningReferenceTypes.Migration,
                row.Id.ToString(),
                "accounting_provider_switch:" + row.Id,
                row.Version.ToString(CultureInfo.InvariantCulture) + ":" +
                row.UpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                row.UpdatedUtc,
                "Accessible accounting migration match"))
            .ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<FinanceEntityResolutionCandidate>> ResolveAccountsAsync(
        Guid companyId, string reference, int take, CancellationToken cancellationToken) =>
        await _db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == companyId && (row.Code == reference || row.Name == reference))
            .OrderBy(row => row.Code).ThenBy(row => row.Id).Take(take)
            .Select(row => new FinanceEntityResolutionCandidate(FinancePlanningReferenceTypes.Account,
                row.Id.ToString(), "finance_account:" + row.Id,
                row.LifecycleVersion.ToString(CultureInfo.InvariantCulture) + ":" + row.UpdatedUtc.Ticks,
                row.UpdatedUtc, "Accessible account " + row.Code))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<FinanceEntityResolutionCandidate>> ResolveJournalsAsync(
        Guid companyId, string reference, int take, CancellationToken cancellationToken) =>
        await _db.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.EntryNumber == reference)
            .OrderBy(row => row.Id).Take(take)
            .Select(row => new FinanceEntityResolutionCandidate(FinancePlanningReferenceTypes.Journal,
                row.Id.ToString(), "ledger_entry:" + row.Id, row.UpdatedUtc.Ticks.ToString(), row.UpdatedUtc,
                "Accessible posted journal " + row.EntryNumber))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<FinanceEntityResolutionCandidate>> ResolveVoucherSeriesAsync(
        Guid companyId, string reference, int take, CancellationToken cancellationToken) =>
        await _db.VoucherSeries.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == companyId && (row.Code == reference || row.DisplayName == reference))
            .OrderBy(row => row.Code).ThenBy(row => row.Id).Take(take)
            .Select(row => new FinanceEntityResolutionCandidate(FinancePlanningReferenceTypes.VoucherSeries,
                row.Id.ToString(), "voucher_series:" + row.Id, row.UpdatedUtc.Ticks.ToString(), row.UpdatedUtc,
                "Accessible voucher series " + row.Code))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<FinanceEntityResolutionCandidate>> ResolveReportDefinitionsAsync(
        Guid companyId, string reference, int take, CancellationToken cancellationToken) =>
        await _db.ReportDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == companyId && (row.Code == reference || row.Name == reference))
            .OrderBy(row => row.Code).ThenBy(row => row.Id).Take(take)
            .Select(row => new FinanceEntityResolutionCandidate(FinancePlanningReferenceTypes.ReportDefinition,
                row.Id.ToString(), "report_definition:" + row.Id, row.CreatedUtc.Ticks.ToString(), row.CreatedUtc,
                "Accessible report definition " + row.Code))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<FinanceEntityResolutionCandidate>> ResolveReportLinesAsync(
        Guid companyId, string reference, int take, CancellationToken cancellationToken) =>
        await _db.ReportDefinitionLines.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == companyId && (row.Code == reference || row.Label == reference))
            .OrderBy(row => row.Code).ThenBy(row => row.Id).Take(take)
            .Select(row => new FinanceEntityResolutionCandidate(FinancePlanningReferenceTypes.ReportLine,
                row.Id.ToString(), "report_definition_line:" + row.Id, row.VersionId.ToString(),
                row.Version.UpdatedUtc, "Accessible report line " + row.Code))
            .ToArrayAsync(cancellationToken);

    private static void Validate(FinanceEntityResolutionRequest request)
    {
        if (request.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(request));
        }
        if (!FinancePlanningReferenceTypes.All.Contains(request.ReferenceType?.Trim().ToLowerInvariant() ?? string.Empty))
        {
            throw new ArgumentOutOfRangeException(nameof(request.ReferenceType));
        }
        if (string.IsNullOrWhiteSpace(request.ReferenceValue) || request.ReferenceValue.Trim().Length > 128)
        {
            throw new ArgumentException("A bounded Finance entity reference is required.", nameof(request.ReferenceValue));
        }
        if (request.MaximumCandidates is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MaximumCandidates));
        }
    }
}
