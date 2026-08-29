using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed record PaymentExecutionAuthoritySnapshot(PaymentBatch Batch,
    PaymentBatchApprovalBinding Approval, BankConnection Connection, BankConsentVersion Consent,
    CompanyBankAccount BankAccount, BankDiscoveredAccount DiscoveredAccount,
    BankProviderCredentialBundle Credentials, IPaymentInitiationProvider Provider,
    IReadOnlyList<PaymentInstruction> Instructions);

internal sealed class PaymentExecutionAuthorityValidator(
    VirtualCompanyDbContext db,
    IPaymentBatchService paymentBatches,
    IPaymentInitiationProviderRegistry providers,
    IProtectedBankCredentialStore credentials,
    IOptions<PaymentExecutionOptions> options,
    TimeProvider time)
{
    public async Task<PaymentExecutionAuthoritySnapshot> ValidateAsync(Guid companyId, Guid batchId,
        long? expectedBatchVersion, Guid bankConnectionId, Guid companyBankAccountId,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var batch = await db.PaymentBatches.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == batchId, cancellationToken)
            ?? throw Error(PaymentExecutionReasonCodes.NotFound, "The approved payment batch was not found in the active company.");
        if (expectedBatchVersion.HasValue && batch.Version != expectedBatchVersion.Value)
            throw Error(PaymentExecutionReasonCodes.VersionConflict, "The payment batch changed after it was opened.", true, batch.Version);
        if (batch.Status != PaymentBatchStatuses.Approved)
            throw Error(PaymentExecutionReasonCodes.BatchNotApproved, "Complete current internal approval before queuing bank execution.");
        var readiness = await paymentBatches.CheckSendReadinessAsync(new(companyId, batchId), cancellationToken);
        if (!readiness.IsReady)
            throw Error(readiness.ReasonCode == PaymentBatchReasonCodes.ApprovalStale
                    ? PaymentExecutionReasonCodes.ApprovalStale : PaymentExecutionReasonCodes.BatchNotApproved,
                readiness.Explanation);
        var approval = await db.PaymentBatchApprovalBindings.IgnoreQueryFilters().AsNoTracking()
            .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(x => x.CompanyId == companyId &&
                x.BatchId == batchId && x.Status == PaymentBatchApprovalBindingStatuses.Approved, cancellationToken)
            ?? throw Error(PaymentExecutionReasonCodes.BatchNotApproved, "The batch has no completed approval binding.");
        if (approval.InstructionSetVersion != batch.InstructionSetVersion || approval.SourceSetHash != readiness.CurrentSourceSetHash)
            throw Error(PaymentExecutionReasonCodes.ApprovalStale, "The approved instruction or source-evidence version is stale.");

        var connection = await db.BankConnections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == bankConnectionId, cancellationToken)
            ?? throw Error(PaymentExecutionReasonCodes.ConnectionUnavailable, "The selected bank connection was not found.");
        if (connection.Status != BankConnectionStatuses.Active || connection.HealthStatus != BankConnectionHealthStatuses.Healthy)
            throw Error(PaymentExecutionReasonCodes.ConnectionUnavailable,
                connection.ReasonSummary ?? "Restore a healthy bank connection before sending payments.");
        if (connection.ConsentExpiresUtc is DateTime expires && expires <= now)
            throw Error(PaymentExecutionReasonCodes.ConsentUnavailable, "Bank consent has expired. Renew it before sending payments.");
        var consent = await db.BankConsentVersions.IgnoreQueryFilters().AsNoTracking()
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(x => x.CompanyId == companyId &&
                x.ConnectionId == connection.Id && x.Status == BankConsentStatuses.Active &&
                (!x.ExpiresUtc.HasValue || x.ExpiresUtc > now), cancellationToken)
            ?? throw Error(PaymentExecutionReasonCodes.ConsentUnavailable, "The bank connection has no active consent evidence.");
        var hasCapability = await db.BankConnectionCapabilityGrants.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.ConnectionId == connection.Id &&
                x.ConsentVersionId == consent.Id && x.Capability == BankProviderCapabilities.PaymentInitiation,
                cancellationToken);
        if (!hasCapability)
            throw Error(PaymentExecutionReasonCodes.CapabilityMissing,
                "The bank connection does not grant payment initiation. Renew it with payment access or choose another connection.");

        var provider = providers.GetRequired(connection.ProviderKey);
        if (!provider.Descriptor.IsConfigured)
            throw Error(PaymentExecutionReasonCodes.ProviderNotConfigured,
                "Payment initiation is not configured for this provider installation.");
        var bankAccount = await db.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == companyBankAccountId && x.IsActive,
                cancellationToken)
            ?? throw Error(PaymentExecutionReasonCodes.AccountAuthorityMismatch,
                "The selected internal bank account is not active in this company.");
        var mapping = await (from discovered in db.BankDiscoveredAccounts.IgnoreQueryFilters().AsNoTracking()
                             join map in db.BankAccountMappings.IgnoreQueryFilters().AsNoTracking()
                                 on new { discovered.CompanyId, Id = discovered.Id }
                                 equals new { map.CompanyId, Id = map.DiscoveredAccountId }
                             where discovered.CompanyId == companyId && discovered.ConnectionId == connection.Id &&
                                   map.CompanyBankAccountId == bankAccount.Id && map.IsCurrent && discovered.IsAvailable
                             select discovered).SingleOrDefaultAsync(cancellationToken)
            ?? throw Error(PaymentExecutionReasonCodes.AccountMappingMissing,
                "Map the selected bank account explicitly to this connection before sending payments.");
        if (mapping.OwnershipStatus != BankAccountOwnershipStatuses.Verified)
            throw Error(PaymentExecutionReasonCodes.AccountOwnershipUnverified,
                "Verified ownership is required for the debit account before money can be moved.");

        var instructions = await db.PaymentInstructions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BatchId == batchId && x.IsCurrent &&
                x.Status == PaymentInstructionStatuses.Approved && x.InstructionSetVersion == batch.InstructionSetVersion)
            .OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
        if (instructions.Count == 0)
            throw Error(PaymentExecutionReasonCodes.InvalidLifecycle, "The approved batch has no current instructions.");
        if (instructions.Any(x => !string.Equals(x.Currency, bankAccount.Currency, StringComparison.OrdinalIgnoreCase)))
            throw Error(PaymentExecutionReasonCodes.AccountAuthorityMismatch,
                "Every instruction must use the selected debit account currency.");
        var unsupportedRail = instructions.Select(x => x.Rail).FirstOrDefault(x =>
            !provider.Descriptor.SupportedRails.Contains(x, StringComparer.OrdinalIgnoreCase));
        if (unsupportedRail is not null)
            throw Error(PaymentExecutionReasonCodes.RailUnsupported,
                "The selected provider does not support one or more approved payment rails. Regenerate the batch for a supported rail.");
        var availableCash = await db.FinanceBalances.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AccountId == bankAccount.FinanceAccountId && x.Currency == bankAccount.Currency)
            .OrderByDescending(x => x.AsOfUtc).Select(x => (decimal?)x.Amount).FirstOrDefaultAsync(cancellationToken);
        if (!availableCash.HasValue || availableCash.Value < instructions.Sum(x => x.Amount))
            throw Error(PaymentExecutionReasonCodes.InsufficientCash,
                "Current cash on the authorized debit account does not cover this approved batch.");
        var credentialBundle = await credentials.GetAsync(companyId, connection.Id, cancellationToken)
            ?? throw Error(PaymentExecutionReasonCodes.ConsentUnavailable,
                "Protected bank credentials are unavailable. Renew the bank connection before sending payments.");
        if (!Uri.TryCreate(options.Value.RedirectUri, UriKind.Absolute, out var redirectUri) ||
            !Uri.TryCreate(options.Value.WebhookUri, UriKind.Absolute, out var webhookUri) ||
            redirectUri.Scheme != Uri.UriSchemeHttps || webhookUri.Scheme != Uri.UriSchemeHttps)
            throw Error(PaymentExecutionReasonCodes.ProviderNotConfigured,
                "Configure HTTPS payment redirect and webhook addresses before enabling payment initiation.");
        return new(batch, approval, connection, consent, bankAccount, mapping, credentialBundle, provider, instructions);
    }

    private static PaymentExecutionException Error(string code, string message, bool conflict = false,
        long? version = null) => new(code, message, conflict, version);
}
