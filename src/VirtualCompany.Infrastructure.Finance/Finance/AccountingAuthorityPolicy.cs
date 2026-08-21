using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingAuthorityPolicy : IAccountingAuthorityPolicy
{
    private readonly VirtualCompanyDbContext _dbContext;

    public AccountingAuthorityPolicy(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AccountingAuthorityPolicyDecision> EvaluateAsync(
        EvaluateAccountingAuthorityQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(query));
        var operation = AccountingAuthorityOperationValues.Normalize(query.Operation);
        var requestedProvider = NormalizeProvider(query.ProviderKey);
        var period = await _dbContext.AccountingAuthorityPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.EffectiveFrom <= query.AccountingDate &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= query.AccountingDate))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        var authority = period?.Authority;
        var authorityProvider = period?.ProviderKey;

        if (authority is null)
        {
            authority = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId)
                .Select(x => x.Authority)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (authority is null)
        {
            return Denied(AccountingAuthorityReasonCodes.AuthorityNotConfigured,
                "Accounting authority has not been configured for this company.");
        }

        var providerMatches = string.IsNullOrWhiteSpace(requestedProvider) ||
                              string.Equals(requestedProvider, authorityProvider, StringComparison.OrdinalIgnoreCase);

        return operation switch
        {
            AccountingAuthorityOperationValues.NativeAuthoritativePosting when authority == AccountingAuthorityValues.InternalLedger =>
                Allowed("Virtual Company is authoritative for this accounting period."),
            AccountingAuthorityOperationValues.NativeAuthoritativePosting =>
                Denied(AccountingAuthorityReasonCodes.NativePostingBlocked,
                    authority == AccountingAuthorityValues.Migration
                        ? "Native posting is paused while this accounting period is being cut over."
                        : $"{ProviderName(authorityProvider)} is authoritative for this accounting period, so Virtual Company cannot create a second authoritative posting."),

            AccountingAuthorityOperationValues.ProviderAuthoritativeWrite
                when authority == AccountingAuthorityValues.ExternalProvider && providerMatches =>
                Allowed($"{ProviderName(authorityProvider)} is authoritative for this accounting period."),
            AccountingAuthorityOperationValues.ProviderAuthoritativeWrite =>
                Denied(AccountingAuthorityReasonCodes.ProviderPostingBlocked,
                    authority == AccountingAuthorityValues.InternalLedger
                        ? "Virtual Company is authoritative for this period. Provider writes must be approved downstream exports of committed accounting records."
                        : "Provider-authoritative writes are paused until the authority cutover is reconciled."),

            AccountingAuthorityOperationValues.DownstreamExport
                when authority == AccountingAuthorityValues.InternalLedger && !string.IsNullOrWhiteSpace(requestedProvider) =>
                Allowed("The committed Virtual Company journal may be exported without changing the authoritative local books."),
            AccountingAuthorityOperationValues.DownstreamExport =>
                Denied(AccountingAuthorityReasonCodes.ExportBlocked,
                    authority == AccountingAuthorityValues.Migration
                        ? "Exports are paused until the cutover is reconciled."
                        : "A provider-authoritative period cannot receive a duplicate export from the local ledger."),

            AccountingAuthorityOperationValues.MigrationReconciliation
                when authority == AccountingAuthorityValues.Migration && providerMatches =>
                Allowed("This operation is limited to the active cutover reconciliation."),
            AccountingAuthorityOperationValues.MigrationReconciliation =>
                Denied(AccountingAuthorityReasonCodes.MigrationOperationRequired,
                    "Migration reconciliation is available only during a bounded authority cutover."),

            AccountingAuthorityOperationValues.ImportProjection
                when authority is AccountingAuthorityValues.ExternalProvider or AccountingAuthorityValues.Migration && providerMatches =>
                Allowed("Provider data may be imported as a read projection for this period."),
            AccountingAuthorityOperationValues.ImportProjection =>
                Denied(AccountingAuthorityReasonCodes.ProviderPostingBlocked,
                    "This period uses the Virtual Company ledger. Imported provider accounting must not replace its authoritative records."),
            _ => Denied(AccountingAuthorityReasonCodes.AuthorityPeriodNotFound,
                "No accounting authority rule allows this operation.")
        };

        AccountingAuthorityPolicyDecision Allowed(string explanation) =>
            new(query.CompanyId, query.AccountingDate, operation, authority, authorityProvider, period?.Id, true, null, explanation);

        AccountingAuthorityPolicyDecision Denied(string reasonCode, string explanation) =>
            new(query.CompanyId, query.AccountingDate, operation, authority ?? AccountingAuthorityValues.InternalLedger,
                authorityProvider, period?.Id, false, reasonCode, explanation);
    }

    private static string ProviderName(string? providerKey) =>
        string.IsNullOrWhiteSpace(providerKey) ? "The external provider" : providerKey;

    private static string? NormalizeProvider(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
