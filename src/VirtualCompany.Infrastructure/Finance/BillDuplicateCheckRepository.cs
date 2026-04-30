using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class BillDuplicateCheckRepository : IBillDuplicateCheckRepository
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public BillDuplicateCheckRepository(VirtualCompanyDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<BillDuplicateCheckResult> CheckAndPersistAsync(
        BillDuplicateCheckRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(request));
        }

        var matchedBillIds = Array.Empty<Guid>();
        if (!string.IsNullOrWhiteSpace(request.InvoiceNumber) && request.TotalAmount.HasValue)
        {
            var supplierName = Normalize(request.SupplierName);
            var supplierOrgNumber = Normalize(request.SupplierOrgNumber);
            var amount = decimal.Round(request.TotalAmount.Value, 2);

            matchedBillIds = await _dbContext.FinanceBills
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == request.CompanyId)
                .Where(x => x.BillNumber == request.InvoiceNumber)
                .Where(x => decimal.Round(x.Amount, 2) == amount)
                .Where(x =>
                    string.IsNullOrWhiteSpace(supplierName) && string.IsNullOrWhiteSpace(supplierOrgNumber) ||
                    x.Counterparty.Name.ToLower() == supplierName ||
                    x.Counterparty.TaxId != null && x.Counterparty.TaxId.ToLower() == supplierOrgNumber)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken);
        }

        var checkedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var check = new BillDuplicateCheck(
            Guid.NewGuid(),
            request.CompanyId,
            request.SupplierName,
            request.SupplierOrgNumber,
            request.InvoiceNumber,
            request.TotalAmount,
            request.Currency,
            matchedBillIds.Length > 0,
            matchedBillIds,
            BuildCriteriaSummary(request),
            request.SourceEmailId,
            request.SourceAttachmentId,
            checkedUtc);

        _dbContext.BillDuplicateChecks.Add(check);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new BillDuplicateCheckResult(
            check.Id,
            check.IsDuplicate,
            check.GetMatchedBillIds(),
            check.CriteriaSummary,
            check.CheckedUtc);
    }

    private static string BuildCriteriaSummary(BillDuplicateCheckRequest request)
    {
        var supplier = request.SupplierOrgNumber ?? request.SupplierName ?? "unknown supplier";
        var amount = request.TotalAmount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "missing amount";
        return $"tenant={request.CompanyId:D}; supplier={supplier}; invoice={request.InvoiceNumber ?? "missing"}; amount={amount}; currency={request.Currency ?? "unknown"}";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
