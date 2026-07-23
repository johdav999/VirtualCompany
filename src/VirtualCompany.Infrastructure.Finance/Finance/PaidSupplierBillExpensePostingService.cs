using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class PaidSupplierBillExpensePostingService : IPaidSupplierBillExpensePostingService
{
    private static readonly IReadOnlyList<string> RequiredFortnoxScopes = ["bookkeeping", "supplierinvoice"];

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceSupplierInvoiceDraftActionService _draftActionService;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ILogger<PaidSupplierBillExpensePostingService>? _logger;

    public PaidSupplierBillExpensePostingService(
        VirtualCompanyDbContext dbContext,
        IFinanceSupplierInvoiceDraftActionService draftActionService,
        ICompanyContextAccessor? companyContextAccessor = null,
        ILogger<PaidSupplierBillExpensePostingService>? logger = null)
    {
        _dbContext = dbContext;
        _draftActionService = draftActionService;
        _companyContextAccessor = companyContextAccessor;
        _logger = logger;
    }

    public async Task<PaidSupplierBillExpensePostingDto> PostAsync(
        PostPaidSupplierBillExpenseCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var providerKey = NormalizeProviderKey(command.ProviderKey);
        var bill = await LoadBillAsync(command.CompanyId, command.BillId, cancellationToken);
        await ValidateBillCanBePostedAsPaidExpenseAsync(bill, cancellationToken);
        var connection = await ResolveConnectedFinanceIntegrationAsync(command.CompanyId, providerKey, cancellationToken);
        await EnsureFortnoxScopesAsync(command.CompanyId, connection.Id, cancellationToken);

        _logger?.LogInformation(
            "Posting paid supplier bill expense. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}. ProviderKey: {ProviderKey}. ConnectionId: {ConnectionId}.",
            command.CompanyId,
            bill.Id,
            bill.BillNumber,
            providerKey,
            connection.Id);

        var draftAction = await _draftActionService.BookkeepAsync(
            new BookkeepSupplierInvoiceDraftCommand(
                command.CompanyId,
                command.BillId,
                command.ActorUserId,
                command.ActorDisplayName,
                providerKey,
                AllowPaidBill: true),
            cancellationToken);

        return Map(command.BillId, providerKey, draftAction);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (_companyContextAccessor?.CompanyId is { } currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("The requested company does not match the active tenant context.");
        }
    }

    private static string NormalizeProviderKey(string? providerKey) =>
        string.IsNullOrWhiteSpace(providerKey)
            ? FinanceIntegrationProviderKeys.Fortnox
            : providerKey.Trim().ToLowerInvariant();

    private async Task<FinanceBill> LoadBillAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId, cancellationToken)
        ?? throw new KeyNotFoundException("Supplier bill not found.");

    private async Task ValidateBillCanBePostedAsPaidExpenseAsync(FinanceBill bill, CancellationToken cancellationToken)
    {
        if (!string.Equals(bill.DocumentKind, FinanceDocumentKinds.SupplierInvoice, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only supplier bills can be posted as expenses.");
        }

        if (string.Equals(bill.PostingStatus, FinanceDocumentPostingStatuses.Booked, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This supplier bill expense is already posted.");
        }

        if (string.Equals(bill.PostingStatus, FinanceDocumentPostingStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cancelled supplier bills cannot be posted as expenses.");
        }

        if (string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Credited, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Credited supplier bills cannot be posted as expenses.");
        }

        if (!string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The supplier bill must be paid before Laura can post it as an expense.");
        }

        var accountCode = await ResolveExpenseAccountCodeAsync(bill, cancellationToken);
        if (!FinanceAccountCodePolicy.IsSupplierExpenseAccount(accountCode))
        {
            throw new InvalidOperationException("Choose a supplier expense account before Laura posts this bill.");
        }

        await EnsurePaymentReviewIsCleanAsync(bill, cancellationToken);
    }

    private async Task<string?> ResolveExpenseAccountCodeAsync(FinanceBill bill, CancellationToken cancellationToken)
    {
        var enrichmentAction = await _dbContext.SupplierInvoiceEnrichmentActions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == bill.CompanyId && x.BillId == bill.Id)
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var coding = enrichmentAction?.SuggestionPayload["coding"] as JsonObject;
        var suggestedAccount = ReadString(coding, "ledgerAccount");
        if (FinanceAccountCodePolicy.IsSupplierExpenseAccount(suggestedAccount))
        {
            return suggestedAccount;
        }

        return bill.Counterparty is not null &&
            FinanceAccountCodePolicy.IsSupplierExpenseAccount(bill.Counterparty.DefaultAccountMapping)
                ? bill.Counterparty.DefaultAccountMapping
                : await ResolveProviderMetadataExpenseAccountCodeAsync(bill, cancellationToken);
    }

    private async Task<string?> ResolveProviderMetadataExpenseAccountCodeAsync(FinanceBill bill, CancellationToken cancellationToken)
    {
        var invoiceReference = await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == bill.CompanyId &&
                x.InternalRecordId == bill.Id &&
                x.EntityType == "supplier_invoice")
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var metadataAccount = ReadString(invoiceReference?.Metadata, "accountCode")
            ?? ReadString(invoiceReference?.Metadata, "Account")
            ?? ReadString(invoiceReference?.Metadata, "account");
        return FinanceAccountCodePolicy.IsSupplierExpenseAccount(metadataAccount)
            ? FinanceAccountCodePolicy.NormalizeAccountCode(metadataAccount)
            : null;
    }

    private async Task EnsurePaymentReviewIsCleanAsync(FinanceBill bill, CancellationToken cancellationToken)
    {
        var paymentTransactionCount = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x =>
                x.CompanyId == bill.CompanyId &&
                x.BillId == bill.Id &&
                (x.TransactionType == "supplier_payment" || x.TransactionType == "payment"),
                cancellationToken);

        if (paymentTransactionCount > 1)
        {
            throw new InvalidOperationException("Resolve the duplicate supplier payment warning before Laura posts this expense.");
        }

        var paidAmount = decimal.Round(Math.Abs(bill.PaidAmount), 2, MidpointRounding.AwayFromZero);
        var totalAmount = decimal.Round(Math.Abs(bill.Amount), 2, MidpointRounding.AwayFromZero);
        if (totalAmount > 0m && paidAmount != totalAmount)
        {
            throw new InvalidOperationException("Resolve the paid amount mismatch before Laura posts this expense.");
        }

        var exportedProposalAmounts = await _dbContext.SupplierInvoicePaymentProposals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == bill.CompanyId &&
                x.BillId == bill.Id &&
                x.ExportStatus == SupplierInvoicePaymentExportStatuses.Exported)
            .Select(x => x.Amount)
            .ToListAsync(cancellationToken);
        var exportedProposalAmount = exportedProposalAmounts.Sum(Math.Abs);

        if (exportedProposalAmount > 0m &&
            decimal.Round(exportedProposalAmount, 2, MidpointRounding.AwayFromZero) != paidAmount)
        {
            throw new InvalidOperationException("Resolve the payment proposal amount mismatch before Laura posts this expense.");
        }
    }

    private static string? ReadString(JsonObject? node, string propertyName) =>
        node is not null &&
        node.TryGetPropertyValue(propertyName, out var value) &&
        value is not null
            ? value.ToString()
            : null;

    private async Task<FinanceIntegrationConnection> ResolveConnectedFinanceIntegrationAsync(
        Guid companyId,
        string providerKey,
        CancellationToken cancellationToken) =>
        await _dbContext.FinanceIntegrationConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == providerKey &&
                x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("Fortnox is not connected for this company. Connect Fortnox and try again.");

    private async Task EnsureFortnoxScopesAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken)
    {
        var scopes = await _dbContext.FortnoxConnections
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == connectionId)
            .Select(x => x.GrantedScopes)
            .SingleOrDefaultAsync(cancellationToken);

        if (scopes is not { Count: > 0 })
        {
            return;
        }

        var grantedScopes = scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingScopes = RequiredFortnoxScopes
            .Where(scope => !grantedScopes.Contains(scope))
            .ToArray();
        if (missingScopes.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Fortnox did not grant the {string.Join(" and ", missingScopes)} permission. Enable the missing permission in the Fortnox Developer Portal, reconnect Fortnox, and try again.");
    }

    private static PaidSupplierBillExpensePostingDto Map(
        Guid billId,
        string providerKey,
        SupplierInvoiceDraftActionDto draftAction)
    {
        var posted = string.Equals(draftAction.Status, SupplierInvoiceDraftActionStatuses.Booked, StringComparison.OrdinalIgnoreCase);
        var summary = string.IsNullOrWhiteSpace(draftAction.ResponseSummary)
            ? posted
                ? "Laura posted the paid supplier bill as an expense in Fortnox."
                : "Laura recorded the Fortnox expense posting request."
            : draftAction.ResponseSummary;

        return new PaidSupplierBillExpensePostingDto(
            billId,
            draftAction.Id,
            draftAction.Status,
            posted,
            draftAction.ProviderKey ?? providerKey,
            draftAction.ConnectionId,
            summary,
            draftAction.RequestedUtc,
            draftAction.BookedUtc,
            draftAction);
    }
}
