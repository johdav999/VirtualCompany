using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Shared;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceCommandService : IFinanceCommandService, IFinancePolicyConfigurationService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IServiceProvider? _serviceProvider;

    public CompanyFinanceCommandService(VirtualCompanyDbContext dbContext)
        : this(dbContext, null, null)
    {
    }

    public CompanyFinanceCommandService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor)
        : this(dbContext, companyContextAccessor, null)
    {
    }

    public CompanyFinanceCommandService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor,
        IServiceProvider? serviceProvider)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _serviceProvider = serviceProvider;
    }

    public async Task<FinanceInvoiceDto> UpdateInvoiceApprovalStatusAsync(
        UpdateFinanceInvoiceApprovalStatusCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        if (command.InvoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice id is required.", nameof(command));
        }

        var normalizedStatus = FinanceInvoiceApprovalStatuses.Normalize(command.Status);
        if (!FinanceInvoiceApprovalStatuses.IsSupported(normalizedStatus))
        {
            throw CreateValidationException(
                nameof(command.Status),
                $"Unsupported invoice status '{command.Status}'. Allowed values: {string.Join(", ", FinanceInvoiceApprovalStatuses.EditableValues)}.");
        }

        var workflowService = _serviceProvider?.GetService<IInvoiceReviewWorkflowService>();
        var invoice = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.Id == command.InvoiceId, cancellationToken);

        EnsureRecordTenant(invoice, command.CompanyId, "invoice");
        var reviewResult = workflowService is null
            ? null
            : string.Equals(normalizedStatus, "pending_approval", StringComparison.OrdinalIgnoreCase) ? null : await workflowService.ExecuteAsync(
                new ReviewFinanceInvoiceWorkflowCommand(
                    command.CompanyId,
                    command.InvoiceId,
                    null,
                    null,
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["trigger"] = JsonValue.Create("invoice_status_update_requested"),
                        ["requestedStatus"] = JsonValue.Create(normalizedStatus),
                        ["currentStatus"] = JsonValue.Create(invoice!.Status)
                    }),
                cancellationToken);

        await EnsureInvoiceApprovalLimitAsync(invoice!, normalizedStatus, cancellationToken);
        EnsureWorkflowAllowsStatusUpdate(normalizedStatus, reviewResult);
        try
        {
            invoice!.ChangeApprovalStatus(normalizedStatus);
        }
        catch (InvalidOperationException ex)
        {
            throw CreateValidationException(nameof(command.Status), ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = MapInvoice(invoice);
        await TriggerInvoiceReviewWorkflowIfAwaitingApprovalAsync(
            command with { Status = normalizedStatus },
            dto,
            cancellationToken);
        return dto;
    }

    public async Task<FinanceTransactionDto> UpdateTransactionCategoryAsync(
        UpdateFinanceTransactionCategoryCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        if (command.TransactionId == Guid.Empty)
        {
            throw new ArgumentException("Transaction id is required.", nameof(command));
        }

        var normalizedCategory = FinanceTransactionCategories.Normalize(command.Category);
        if (!FinanceTransactionCategories.IsSupported(normalizedCategory))
        {
            throw CreateValidationException(
                nameof(command.Category),
                $"Unsupported transaction category '{command.Category}'. Allowed values: {string.Join(", ", FinanceTransactionCategories.AllowedValues)}.");
        }

        var transaction = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .Include(x => x.Account)
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.Id == command.TransactionId, cancellationToken);

        EnsureRecordTenant(transaction, command.CompanyId, "transaction");
        transaction!.ChangeCategory(normalizedCategory);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapTransaction(transaction);
    }

    public async Task<FinancePolicyConfigurationDto> GetPolicyConfigurationAsync(
        GetFinancePolicyConfigurationQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);

        var configuration = await _dbContext.FinancePolicyConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);

        return configuration is null
            ? CreateDefaultPolicy(query.CompanyId)
            : MapPolicy(configuration);
    }

    public async Task<FinancePolicyConfigurationDto> UpsertPolicyConfigurationAsync(
        UpsertFinancePolicyConfigurationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        if (command.Configuration.CompanyId != command.CompanyId)
        {
            throw new UnauthorizedAccessException("Finance policy writes are scoped to the active company context.");
        }

        var configuration = await _dbContext.FinancePolicyConfigurations
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken);

        if (configuration is null)
        {
            configuration = new FinancePolicyConfiguration(
                Guid.NewGuid(),
                command.CompanyId,
                command.Configuration.ApprovalCurrency,
                command.Configuration.InvoiceApprovalThreshold,
                command.Configuration.BillApprovalThreshold,
                command.Configuration.RequireCounterpartyForTransactions,
                command.Configuration.AnomalyDetectionLowerBound,
                command.Configuration.AnomalyDetectionUpperBound,
                command.Configuration.CashRunwayWarningThresholdDays,
                command.Configuration.CashRunwayCriticalThresholdDays);

            _dbContext.FinancePolicyConfigurations.Add(configuration);
        }
        else
        {
            configuration.Update(
                command.Configuration.ApprovalCurrency,
                command.Configuration.InvoiceApprovalThreshold,
                command.Configuration.BillApprovalThreshold,
                command.Configuration.RequireCounterpartyForTransactions,
                command.Configuration.AnomalyDetectionLowerBound,
                command.Configuration.AnomalyDetectionUpperBound,
                command.Configuration.CashRunwayWarningThresholdDays,
                command.Configuration.CashRunwayCriticalThresholdDays);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapPolicy(configuration);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Finance writes are scoped to the active company context.");
        }
    }

    private async Task EnsureInvoiceApprovalLimitAsync(
        FinanceInvoice invoice,
        string status,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(status?.Trim(), "approved", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var policy = await _dbContext.FinancePolicyConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == invoice.CompanyId, cancellationToken);

        var approvalCurrency = policy?.ApprovalCurrency ?? "USD";
        if (!string.Equals(invoice.Currency, approvalCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw CreateValidationException(
                "Status",
                $"Invoice currency '{invoice.Currency}' does not match approval policy currency '{approvalCurrency}'.");
        }

        var approvalThreshold = policy?.InvoiceApprovalThreshold ?? 10000m;
        if (invoice.Amount > approvalThreshold)
        {
            throw CreateValidationException(
                "Status",
                $"Invoice amount {invoice.Amount} exceeds the approval threshold {approvalThreshold}.");
        }
    }

    private static void EnsureWorkflowAllowsStatusUpdate(
        string normalizedStatus,
        FinanceInvoiceReviewWorkflowResultDto? reviewResult)
    {
        if (!string.Equals(normalizedStatus, "approved", StringComparison.OrdinalIgnoreCase) ||
            reviewResult is null)
        {
            return;
        }

        if (reviewResult.RequiresHumanApproval)
        {
            throw CreateValidationException("Status", "Invoice approval is blocked until the finance review workflow completes the required approval step.");
        }
    }

    private async Task TriggerInvoiceReviewWorkflowIfAwaitingApprovalAsync(
        UpdateFinanceInvoiceApprovalStatusCommand command,
        FinanceInvoiceDto invoice,
        CancellationToken cancellationToken)
    {
        if (!IsAwaitingApprovalStatus(invoice.Status))
        {
            return;
        }

        var workflowService = _serviceProvider?.GetService<IInvoiceReviewWorkflowService>();
        if (workflowService is null)
        {
            return;
        }

        await workflowService.ExecuteAsync(
            new ReviewFinanceInvoiceWorkflowCommand(
                command.CompanyId,
                command.InvoiceId,
                null,
                null,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["trigger"] = JsonValue.Create("invoice_status_awaiting_approval"),
                    ["invoiceStatus"] = JsonValue.Create(invoice.Status)
                }),
            cancellationToken);
    }

    private static bool IsAwaitingApprovalStatus(string? status) =>
        string.Equals(status?.Trim(), "awaiting_approval", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status?.Trim(), "awaiting-approval", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status?.Trim(), "awaiting approval", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status?.Trim(), "pending_approval", StringComparison.OrdinalIgnoreCase);

    private static void EnsureRecordTenant(ICompanyOwnedEntity? entity, Guid companyId, string recordName)
    {
        if (entity is null)
        {
            throw new KeyNotFoundException($"Finance {recordName} was not found.");
        }

        if (entity.CompanyId != companyId)
        {
            throw new UnauthorizedAccessException($"Finance {recordName} belongs to a different company.");
        }
    }

    private static FinanceValidationException CreateValidationException(string field, string message) =>
        new(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [field] = [message]
            },
            message);

    private static FinanceInvoiceDto MapInvoice(FinanceInvoice invoice) =>
        new(
            invoice.Id,
            invoice.CounterpartyId,
            invoice.Counterparty.Name,
            invoice.InvoiceNumber,
            invoice.IssuedUtc,
            invoice.DueUtc,
            invoice.Amount,
            invoice.Currency,
            invoice.Status,
            null);

    private static FinanceTransactionDto MapTransaction(FinanceTransaction transaction) =>
        new(
            transaction.Id,
            transaction.AccountId,
            transaction.Account.Name,
            transaction.CounterpartyId,
            transaction.Counterparty?.Name,
            transaction.InvoiceId,
            transaction.BillId,
            transaction.TransactionUtc,
            transaction.TransactionType,
            transaction.Amount,
            transaction.Currency,
            transaction.Description,
            transaction.ExternalReference,
            null);

    private static FinancePolicyConfigurationDto MapPolicy(FinancePolicyConfiguration configuration) =>
        new(
            configuration.CompanyId,
            configuration.ApprovalCurrency,
            configuration.InvoiceApprovalThreshold,
            configuration.BillApprovalThreshold,
            configuration.RequireCounterpartyForTransactions,
            configuration.AnomalyDetectionLowerBound,
            configuration.AnomalyDetectionUpperBound,
            configuration.CashRunwayWarningThresholdDays,
            configuration.CashRunwayCriticalThresholdDays);

    private static FinancePolicyConfigurationDto CreateDefaultPolicy(Guid companyId) =>
        new(
            companyId,
            "USD",
            10000m,
            5000m,
            true,
            -10000m,
            10000m,
            90,
            30);
}