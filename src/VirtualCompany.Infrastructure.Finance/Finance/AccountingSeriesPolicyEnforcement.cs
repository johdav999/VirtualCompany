using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal static class AccountingSeriesPolicyEnforcement
{
    internal static async Task<bool> IsStatutoryDocumentSeriesAllowedAsync(
        VirtualCompanyDbContext db,
        Guid companyId,
        Guid seriesId,
        string sourceType,
        string transactionType,
        DateOnly issueDate,
        string? jurisdiction,
        string policyPackKey,
        string policyPackVersion,
        CancellationToken cancellationToken)
    {
        var normalizedSource = sourceType.Trim().ToLowerInvariant();
        var normalizedTransaction = transactionType.Trim().ToLowerInvariant();
        var normalizedJurisdiction = string.IsNullOrWhiteSpace(jurisdiction) ? null : jurisdiction.Trim().ToUpperInvariant();
        var candidates = await db.AccountingSeriesPolicies.AsNoTracking().Where(x =>
                x.CompanyId == companyId && x.SeriesKind == AccountingSeriesKinds.StatutoryDocument && x.IsActive &&
                (x.SourceType == "*" || x.SourceType == normalizedSource) &&
                (x.TransactionType == "*" || x.TransactionType == normalizedTransaction) &&
                (!x.FiscalYear.HasValue || x.FiscalYear == issueDate.Year) &&
                !x.LocationDimensionMemberId.HasValue &&
                (x.Jurisdiction == null || x.Jurisdiction == normalizedJurisdiction) &&
                x.PolicyPackKey == policyPackKey && x.PolicyPackVersion == policyPackVersion)
            .Select(x => x.SeriesId)
            .ToListAsync(cancellationToken);

        return candidates.Count == 0 || candidates.Contains(seriesId);
    }
}
