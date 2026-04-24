using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Shared;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceCommandService : IFinanceCommandService, IFinancePolicyConfigurationService, IFinancePaymentCommandService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IServiceProvider? _serviceProvider;
    private readonly FinancePaymentAllocationService _paymentAllocationService;
    private readonly IFinanceApprovalTaskService _approvalTaskService;
    private readonly IFinanceCashSettlementPostingService _cashSettlementPostingService;
    private readonly ICompanyOutboxEnqueuer? _outboxEnqueuer;

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
        _cashSettlementPostingService = serviceProvider?.GetService<IFinanceCashSettlementPostingService>() ?? new CompanyCashSettlementPostingService(dbContext, companyContextAccessor);
        _paymentAllocationService = new FinancePaymentAllocationService(dbContext, _cashSettlementPostingService);
        _outboxEnqueuer = serviceProvider?.GetService<ICompanyOutboxEnqueuer>();
        _approvalTaskService = serviceProvider?.GetService<IFinanceApprovalTaskService>() ??
            new CompanyFinanceApprovalTaskService(dbContext, companyContextAccessor, NullLogger<CompanyFinanceApprovalTaskService>.Instance);
    }

    public async Task<FinancePaymentDto> CreatePaymentAsync(
        CreateFinancePaymentCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        ValidatePayment(command.Payment);

        var payment = new Payment(
            Guid.NewGuid(),
            command.CompanyId,
            command.Payment.PaymentType,
            command.Payment.Amount,
            command.Payment.Currency,
            command.Payment.PaymentDate,
            command.Payment.Method,
            command.Payment.Status,
            command.Payment.CounterpartyReference);

        _dbContext.Payments.Add(payment);
        FinanceDomainEvents.EnqueuePaymentCreated(_outboxEnqueuer, payment);
        await _approvalTaskService.EnsureTaskAsync(
            new EnsureFinanceApprovalTaskCommand(
                command.CompanyId,
                ApprovalTargetType.Payment,
                payment.Id,
                payment.Amount,
                payment.Currency,
                payment.PaymentDate),
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapPayment(payment);
    }

    public Task<FinancePaymentAllocationDto> CreateAllocationAsync(
        CreateFinancePaymentAllocationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        return _paymentAllocationService.CreateAsync(command, cancellationToken);
    }

    public Task<FinancePaymentAllocationDto> UpdateAllocationAsync(
        UpdateFinancePaymentAllocationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        return _paymentAllocationService.UpdateAsync(command, cancellationToken);
    }

    public Task DeleteAllocationAsync(
        DeleteFinancePaymentAllocationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        return _paymentAllocationService.DeleteAsync(command, cancellationToken);
    }

    public Task<FinancePaymentAllocationBackfillResultDto> BackfillAllocationsAsync(
        BackfillFinancePaymentAllocationsCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        return _paymentAllocationService.BackfillAsync(command, cancellationToken);
    }

    private static FinancePaymentDto MapPayment(Payment payment) =>
        new(
            payment.Id,
            payment.CompanyId,
            payment.PaymentType,
            payment.Amount,
            payment.Currency,
            payment.PaymentDate,
            payment.Method,
            payment.Status,
            payment.CounterpartyReference,
            payment.CreatedUtc,
            payment.UpdatedUtc,
            Array.Empty<NormalizedFinanceInsightDto>());

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

    public async Task<FinanceCounterpartyDto> CreateCounterpartyAsync(
        CreateFinanceCounterpartyCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        ValidateCounterparty(command.Counterparty);

        var normalizedType = NormalizeCounterpartyType(command.CounterpartyType);
        var counterparty = new FinanceCounterparty(
            Guid.NewGuid(),
            command.CompanyId,
            command.Counterparty.Name,
            normalizedType,
            command.Counterparty.Email,
            command.Counterparty.PaymentTerms,
            command.Counterparty.TaxId,
            command.Counterparty.CreditLimit,
            command.Counterparty.PreferredPaymentMethod,
            command.Counterparty.DefaultAccountMapping);

        _dbContext.FinanceCounterparties.Add(counterparty);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapCounterparty(counterparty);
    }

    public async Task<FinanceCounterpartyDto> UpdateCounterpartyAsync(
        UpdateFinanceCounterpartyCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);

        ValidateCounterparty(command.Counterparty);
        var normalizedType = NormalizeCounterpartyType(command.CounterpartyType);
        var counterparty = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == command.CounterpartyId, cancellationToken);

        EnsureRecordTenant(counterparty, command.CompanyId, "counterparty");
        EnsureCounterpartyType(counterparty!, normalizedType);

        counterparty!.UpdateMasterData(
            command.Counterparty.Name,
            normalizedType,
            command.Counterparty.Email,
            command.Counterparty.PaymentTerms,
            command.Counterparty.TaxId,
            command.Counterparty.CreditLimit,
            command.Counterparty.PreferredPaymentMethod,
            command.Counterparty.DefaultAccountMapping);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapCounterparty(counterparty);
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

    private static void EnsureCounterpartyType(FinanceCounterparty counterparty, string requestedType)
    {
        var actualType = NormalizeCounterpartyType(counterparty.CounterpartyType);
        if (!string.Equals(actualType, requestedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyNotFoundException($"Finance {requestedType} was not found.");
        }
    }

    private static void ValidateCounterparty(FinanceCounterpartyUpsertDto counterparty)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        AddRequired(errors, nameof(FinanceCounterpartyUpsertDto.Name), counterparty.Name);
        AddMaxLength(errors, nameof(FinanceCounterpartyUpsertDto.Name), counterparty.Name, 200);
        AddMaxLength(errors, nameof(FinanceCounterpartyUpsertDto.Email), counterparty.Email, 256);
        AddMaxLength(errors, nameof(FinanceCounterpartyUpsertDto.PaymentTerms), counterparty.PaymentTerms, 64);
        AddMaxLength(errors, nameof(FinanceCounterpartyUpsertDto.TaxId), counterparty.TaxId, 64);
        AddMaxLength(errors, nameof(FinanceCounterpartyUpsertDto.PreferredPaymentMethod), counterparty.PreferredPaymentMethod, 64);
        AddMaxLength(errors, nameof(FinanceCounterpartyUpsertDto.DefaultAccountMapping), counterparty.DefaultAccountMapping, 64);

        if (counterparty.CreditLimit is decimal creditLimit && creditLimit < 0m)
        {
            errors[nameof(FinanceCounterpartyUpsertDto.CreditLimit)] = ["Credit limit cannot be negative."];
        }

        if (errors.Count > 0)
        {
            throw new FinanceValidationException(errors);
        }
    }

    private static void AddRequired(IDictionary<string, string[]> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{field} is required."];
        }
    }

    private static void AddMaxLength(IDictionary<string, string[]> errors, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"{field} must be {maxLength} characters or fewer."];
        }
    }

    private static FinanceValidationException CreateValidationException(string field, string message) =>
        new(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [field] = [message]
            },
            message);

    private static void ValidatePayment(CreateFinancePaymentDto payment)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        AddRequired(errors, nameof(CreateFinancePaymentDto.PaymentType), payment.PaymentType);
        AddRequired(errors, nameof(CreateFinancePaymentDto.Currency), payment.Currency);
        AddRequired(errors, nameof(CreateFinancePaymentDto.Method), payment.Method);
        AddRequired(errors, nameof(CreateFinancePaymentDto.Status), payment.Status);
        AddRequired(errors, nameof(CreateFinancePaymentDto.CounterpartyReference), payment.CounterpartyReference);
        AddMaxLength(errors, nameof(CreateFinancePaymentDto.CounterpartyReference), payment.CounterpartyReference, 200);

        if (!string.IsNullOrWhiteSpace(payment.Currency))
        {
            var normalizedCurrency = payment.Currency.Trim();
            if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsLetter))
            {
                errors[nameof(CreateFinancePaymentDto.Currency)] = ["Currency must be a three-letter ISO code."];
            }
        }

        if (!string.IsNullOrWhiteSpace(payment.PaymentType) && !PaymentTypes.IsSupported(payment.PaymentType))
        {
            errors[nameof(CreateFinancePaymentDto.PaymentType)] = [$"Unsupported payment type '{payment.PaymentType}'. Allowed values: {string.Join(", ", PaymentTypes.AllowedValues)}."];
        }

        if (!string.IsNullOrWhiteSpace(payment.Method) && !PaymentMethods.IsSupported(payment.Method))
        {
            errors[nameof(CreateFinancePaymentDto.Method)] = [$"Unsupported payment method '{payment.Method}'. Allowed values: {string.Join(", ", PaymentMethods.AllowedValues)}."];
        }

        if (!string.IsNullOrWhiteSpace(payment.Status) && !PaymentStatuses.IsSupported(payment.Status))
        {
            errors[nameof(CreateFinancePaymentDto.Status)] = [$"Unsupported payment status '{payment.Status}'. Allowed values: {string.Join(", ", PaymentStatuses.AllowedValues)}."];
        }

        if (payment.Amount <= 0m)
        {
            errors[nameof(CreateFinancePaymentDto.Amount)] = ["Amount must be greater than zero."];
        }

        if (payment.PaymentDate == default)
        {
            errors[nameof(CreateFinancePaymentDto.PaymentDate)] = ["Payment date is required."];
        }

        if (errors.Count > 0)
        {
            throw new FinanceValidationException(errors);
        }
    }

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

    private static FinanceCounterpartyDto MapCounterparty(FinanceCounterparty counterparty) =>
        new(
            counterparty.Id,
            counterparty.CompanyId,
            NormalizeCounterpartyType(counterparty.CounterpartyType),
            counterparty.Name,
            counterparty.Email,
            counterparty.PaymentTerms,
            counterparty.TaxId,
            counterparty.CreditLimit,
            counterparty.PreferredPaymentMethod,
            counterparty.DefaultAccountMapping,
            counterparty.CreatedUtc,
            counterparty.UpdatedUtc);

    private static string NormalizeCounterpartyType(string value) =>
        FinanceCounterparty.NormalizeCounterpartyKind(value);

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
