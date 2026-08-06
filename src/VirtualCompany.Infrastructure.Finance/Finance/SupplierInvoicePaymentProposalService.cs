using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SupplierInvoicePaymentProposalService : IFinanceSupplierPaymentProposalService
{
    private const string ApprovalType = "supplier_invoice_payment_proposal";
    private const string TaskType = "finance.supplier_invoice_payment_proposal";
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly IReadOnlyDictionary<string, ISupplierInvoicePaymentExportProvider> _exportProviders;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SupplierInvoicePaymentProposalService>? _logger;

    public SupplierInvoicePaymentProposalService(
        VirtualCompanyDbContext dbContext,
        IApprovalRequestService approvalRequestService,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor = null,
        ILogger<SupplierInvoicePaymentProposalService>? logger = null,
        IEnumerable<ISupplierInvoicePaymentExportProvider>? exportProviders = null)
    {
        _dbContext = dbContext;
        _approvalRequestService = approvalRequestService;
        _exportProviders = (exportProviders ?? [])
            .GroupBy(provider => provider.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
        _logger = logger;
    }

    public async Task<SupplierInvoicePaymentProposalDto> RequestPaymentProposalAsync(
        RequestSupplierInvoicePaymentProposalCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);

        var existing = await _dbContext.SupplierInvoicePaymentProposals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.BillId == command.BillId)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null && !CanCreateFollowUpProposal(existing))
        {
            _logger?.LogInformation(
                "Supplier invoice payment proposal request returned existing active proposal. CompanyId: {CompanyId}. BillId: {BillId}. ProposalId: {ProposalId}. Status: {Status}. ExportStatus: {ExportStatus}.",
                command.CompanyId,
                command.BillId,
                existing.Id,
                existing.Status,
                existing.ExportStatus);
            return MapProposal(existing);
        }

        _logger?.LogInformation(
            "Supplier invoice payment proposal request loading bill. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}.",
            command.CompanyId,
            command.BillId,
            command.ActorUserId);

        var bill = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.BillId, cancellationToken)
            ?? throw new KeyNotFoundException("Supplier bill not found.");

        var proposalAmount = ResolveProposalAmount(bill);
        ValidateBillCanBeProposed(bill, proposalAmount);
        if (existing is not null)
        {
            _logger?.LogInformation(
                "Supplier invoice payment proposal request creating follow-up proposal. CompanyId: {CompanyId}. BillId: {BillId}. PreviousProposalId: {PreviousProposalId}. PreviousStatus: {PreviousStatus}. PreviousExportStatus: {PreviousExportStatus}. ProposalAmount: {ProposalAmount}.",
                command.CompanyId,
                bill.Id,
                existing.Id,
                existing.Status,
                existing.ExportStatus,
                proposalAmount);
        }

        _logger?.LogInformation(
            "Supplier invoice payment proposal request validated bill. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}. Amount: {Amount}. PaidAmount: {PaidAmount}. ProposalAmount: {ProposalAmount}. SettlementStatus: {SettlementStatus}. PostingStatus: {PostingStatus}. DocumentKind: {DocumentKind}.",
            command.CompanyId,
            bill.Id,
            bill.BillNumber,
            bill.Amount,
            bill.PaidAmount,
            proposalAmount,
            bill.SettlementStatus,
            bill.PostingStatus,
            bill.DocumentKind);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var actorId = command.ActorUserId is { } userId && userId != Guid.Empty ? userId : command.CompanyId;
        var proposal = new SupplierInvoicePaymentProposal(
            Guid.NewGuid(),
            command.CompanyId,
            bill.Id,
            bill.CounterpartyId,
            bill.Counterparty?.Name ?? "Supplier",
            proposalAmount,
            bill.Currency,
            bill.DueUtc,
            bill.BillNumber,
            command.ActorUserId,
            now);

        var task = new WorkTask(
            Guid.NewGuid(),
            command.CompanyId,
            TaskType,
            $"Approve payment proposal for {proposal.SupplierName}",
            $"Review payment proposal {proposal.PaymentReference} for {proposal.Currency} {proposal.Amount:N2}. Approval only marks it ready for payment/export; it does not initiate payment.",
            WorkTaskPriority.High,
            assignedAgentId: null,
            parentTaskId: null,
            createdByActorType: "human",
            createdByActorId: actorId,
            inputPayload: BuildTaskInput(proposal),
            rationaleSummary: "Supplier invoice payment proposal requires approval before payment/export.",
            correlationId: BuildCorrelationId(command.CompanyId, bill.Id),
            sourceType: WorkTaskSourceTypes.User,
            triggerSource: "finance_supplier_bill",
            creationReason: "Supplier bill was reviewed and requires payment approval.",
            triggerEventId: bill.Id.ToString("N"),
            status: WorkTaskStatus.AwaitingApproval);
        task.SetDueDate(bill.DueUtc);

        await ExecuteInTransactionAsync(async () =>
        {
            _dbContext.SupplierInvoicePaymentProposals.Add(proposal);
            _dbContext.WorkTasks.Add(task);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var approval = await _approvalRequestService.CreateAsync(
                command.CompanyId,
                new CreateApprovalRequestCommand(
                    ApprovalTargetEntityType.Task.ToStorageValue(),
                    task.Id,
                    "human",
                    actorId,
                    ApprovalType,
                    BuildApprovalContext(proposal),
                    RequiredRole: "finance_approver"),
                cancellationToken);

            proposal.AttachApprovalWorkflow(task.Id, approval.Id, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        _logger?.LogInformation(
            "Supplier invoice payment proposal request created proposal and approval workflow. CompanyId: {CompanyId}. BillId: {BillId}. ProposalId: {ProposalId}. TaskId: {TaskId}. ApprovalRequestId: {ApprovalRequestId}. Status: {Status}. Amount: {Amount}.",
            command.CompanyId,
            bill.Id,
            proposal.Id,
            proposal.TaskId,
            proposal.ApprovalRequestId,
            proposal.Status,
            proposal.Amount);

        return MapProposal(proposal);
    }

    public async Task<SupplierInvoicePaymentProposalDto> ExportPaymentInstructionAsync(
        ExportSupplierInvoicePaymentInstructionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);

        var providerKey = string.IsNullOrWhiteSpace(command.ProviderKey)
            ? FinanceIntegrationProviderKeys.Fortnox
            : command.ProviderKey.Trim().ToLowerInvariant();
        if (!_exportProviders.TryGetValue(providerKey, out var provider))
        {
            throw new InvalidOperationException($"Payment export provider '{providerKey}' is not available.");
        }

        var proposal = await _dbContext.SupplierInvoicePaymentProposals
            .IgnoreQueryFilters()
            .Include(x => x.Bill)
            .Where(x => x.CompanyId == command.CompanyId && x.BillId == command.BillId)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Supplier bill payment proposal was not found.");

        _logger?.LogInformation(
            "Supplier invoice payment export requested. CompanyId: {CompanyId}. BillId: {BillId}. ProposalId: {ProposalId}. ProposalStatus: {ProposalStatus}. ExportMode: {ExportMode}. ExportStatus: {ExportStatus}. ProviderKey: {ProviderKey}. Amount: {Amount}.",
            command.CompanyId,
            command.BillId,
            proposal.Id,
            proposal.Status,
            command.ExportMode,
            proposal.ExportStatus,
            providerKey,
            proposal.Amount);

        var shouldBookkeepExistingPayment = CanBookkeepExistingFortnoxPayment(proposal, providerKey);
        var exportMode = SupplierInvoicePaymentExportModes.Normalize(shouldBookkeepExistingPayment
            ? SupplierInvoicePaymentExportModes.RegisterPayment
            : command.ExportMode);
        ValidateProposalCanBeExported(proposal, providerKey, exportMode);

        if (proposal.ExportStatus is SupplierInvoicePaymentExportStatuses.Exported or SupplierInvoicePaymentExportStatuses.Cancelled ||
            (proposal.ExportStatus == SupplierInvoicePaymentExportStatuses.ExportRequested && !CanUpgradeManualExportRequest(proposal, exportMode)))
        {
            if (shouldBookkeepExistingPayment)
            {
                _logger?.LogInformation(
                    "Supplier invoice payment export will bookkeep existing Fortnox payment. CompanyId: {CompanyId}. BillId: {BillId}. ProposalId: {ProposalId}. ExistingPaymentNumber: {PaymentNumber}.",
                    command.CompanyId,
                    command.BillId,
                    proposal.Id,
                    ReadString(proposal.ExportProviderMetadata, "fortnoxSupplierInvoicePaymentNumber"));
            }
            else
            {
                _logger?.LogInformation(
                    "Supplier invoice payment export returned existing export state. CompanyId: {CompanyId}. BillId: {BillId}. ProposalId: {ProposalId}. ExportStatus: {ExportStatus}.",
                    command.CompanyId,
                    command.BillId,
                    proposal.Id,
                    proposal.ExportStatus);
                return MapProposal(proposal);
            }
        }

        var connection = await ResolveActiveConnectionAsync(command.CompanyId, providerKey, cancellationToken);
        var providerResult = await provider.ExportAsync(
            new SupplierInvoicePaymentExportProviderRequest(
                command.CompanyId,
                proposal.Id,
                proposal.BillId,
                proposal.Bill.BillNumber,
                proposal.SupplierId,
                proposal.SupplierName,
                proposal.Amount,
                proposal.Currency,
                proposal.DueUtc,
                proposal.PaymentReference,
                connection.Id,
                command.ActorUserId,
                exportMode,
                ExistingProviderPaymentNumber: shouldBookkeepExistingPayment
                    ? ReadString(proposal.ExportProviderMetadata, "fortnoxSupplierInvoicePaymentNumber")
                    : null,
                BookkeepExistingProviderPayment: shouldBookkeepExistingPayment),
            cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        proposal.MarkPaymentExport(
            providerResult.ExportMode,
            providerResult.ExportStatus,
            providerResult.ProviderKey,
            providerResult.ConnectionId ?? connection.Id,
            command.ActorUserId,
            providerResult.ResponseSummary,
            providerResult.ProviderMetadata,
            now);

        _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            command.CompanyId,
            providerResult.ConnectionId ?? connection.Id,
            providerResult.ProviderKey,
            "supplier_payment_export",
            providerResult.ExportStatus == SupplierInvoicePaymentExportStatuses.Failed
                ? FinanceIntegrationAuditOutcomes.Failed
                : FinanceIntegrationAuditOutcomes.Succeeded,
            "supplier_invoice_payment_proposal",
            proposal.Id,
            null,
            proposal.Id.ToString("N"),
            providerResult.ResponseSummary,
            now));

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger?.LogInformation(
            "Supplier invoice payment export state recorded. CompanyId: {CompanyId}. BillId: {BillId}. ProposalId: {ProposalId}. ExportStatus: {ExportStatus}. ProviderKey: {ProviderKey}. ConnectionId: {ConnectionId}.",
            command.CompanyId,
            command.BillId,
            proposal.Id,
            proposal.ExportStatus,
            proposal.ExportProviderKey,
            proposal.ExportConnectionId);

        return MapProposal(proposal);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (_companyContextAccessor?.CompanyId is { } currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("The requested company does not match the active tenant context.");
        }
    }

    private static void ValidateBillCanBeProposed(FinanceBill bill, decimal proposalAmount)
    {
        if (!string.Equals(bill.DocumentKind, FinanceDocumentKinds.SupplierInvoice, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only supplier invoices can receive payment proposals.");
        }

        if (string.Equals(bill.PostingStatus, FinanceDocumentPostingStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cancelled supplier bills cannot receive payment proposals.");
        }

        if (bill.SettlementStatus is FinanceSettlementStatuses.Paid or FinanceSettlementStatuses.Credited)
        {
            throw new InvalidOperationException("Paid or credited supplier bills do not need payment proposals.");
        }

        if (proposalAmount == 0m)
        {
            throw new InvalidOperationException("Supplier bills with no remaining amount cannot receive payment proposals.");
        }
    }

    private static bool CanCreateFollowUpProposal(SupplierInvoicePaymentProposal proposal) =>
        SupplierInvoicePaymentProposalStatuses.Normalize(proposal.Status) is SupplierInvoicePaymentProposalStatuses.Exported or
            SupplierInvoicePaymentProposalStatuses.Rejected or
            SupplierInvoicePaymentProposalStatuses.Cancelled ||
        SupplierInvoicePaymentExportStatuses.Normalize(proposal.ExportStatus) == SupplierInvoicePaymentExportStatuses.Cancelled;

    private static void ValidateProposalCanBeExported(SupplierInvoicePaymentProposal proposal, string providerKey, string requestedExportMode)
    {
        var canBookkeepExistingPayment = CanBookkeepExistingFortnoxPayment(proposal, providerKey);
        if (!string.Equals(proposal.Status, SupplierInvoicePaymentProposalStatuses.ReadyForPayment, StringComparison.OrdinalIgnoreCase) &&
            !canBookkeepExistingPayment)
        {
            throw new InvalidOperationException("Only approved payment proposals can be exported.");
        }

        if ((proposal.Bill.PostingStatus is FinanceDocumentPostingStatuses.Cancelled ||
             proposal.Bill.SettlementStatus is FinanceSettlementStatuses.Paid or FinanceSettlementStatuses.Credited) &&
            !canBookkeepExistingPayment)
        {
            throw new InvalidOperationException("Paid, credited, or cancelled supplier bills cannot be exported for payment.");
        }

        if (proposal.ExportStatus is SupplierInvoicePaymentExportStatuses.Exported or
            SupplierInvoicePaymentExportStatuses.Cancelled)
        {
            if (canBookkeepExistingPayment)
            {
                return;
            }

            return;
        }

        if (proposal.ExportStatus == SupplierInvoicePaymentExportStatuses.ExportRequested &&
            CanUpgradeManualExportRequest(proposal, requestedExportMode))
        {
            return;
        }

        if (proposal.ExportStatus == SupplierInvoicePaymentExportStatuses.ExportRequested)
        {
            return;
        }

        if (proposal.ExportStatus != SupplierInvoicePaymentExportStatuses.NotExported &&
            proposal.ExportStatus != SupplierInvoicePaymentExportStatuses.Failed)
        {
            throw new InvalidOperationException("This payment proposal cannot be exported from its current export state.");
        }
    }

    private static bool CanBookkeepExistingFortnoxPayment(SupplierInvoicePaymentProposal proposal, string providerKey)
    {
        if (!string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase) ||
            proposal.ExportStatus is not SupplierInvoicePaymentExportStatuses.Exported and not SupplierInvoicePaymentExportStatuses.Failed ||
            !string.Equals(proposal.ExportProviderKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ReadBool(proposal.ExportProviderMetadata, "booked") != true &&
               !string.IsNullOrWhiteSpace(ReadString(proposal.ExportProviderMetadata, "fortnoxSupplierInvoicePaymentNumber"));
    }

    private static bool CanUpgradeManualExportRequest(SupplierInvoicePaymentProposal proposal, string requestedExportMode)
    {
        if (!string.Equals(proposal.ExportStatus, SupplierInvoicePaymentExportStatuses.ExportRequested, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var currentMode = SupplierInvoicePaymentExportModes.Normalize(proposal.ExportMode);
        var requestedMode = SupplierInvoicePaymentExportModes.Normalize(requestedExportMode);
        if (currentMode == requestedMode)
        {
            return false;
        }

        if (ReadBool(proposal.ExportProviderMetadata, "manualPaymentRequired") == true ||
            ReadBool(proposal.ExportProviderMetadata, "manualPaymentFileRequired") == true)
        {
            return true;
        }

        return proposal.ExportResponseSummary?.Contains("no bank payment", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? ReadString(JsonObject? source, string propertyName) =>
        source is not null && source.TryGetPropertyValue(propertyName, out var node) && node is not null
            ? node.ToString()
            : null;

    private static bool? ReadBool(JsonObject? source, string propertyName)
    {
        if (source is not null &&
            source.TryGetPropertyValue(propertyName, out var node) &&
            node is JsonValue value &&
            value.TryGetValue<bool>(out var result))
        {
            return result;
        }

        return null;
    }

    private async Task<FinanceIntegrationConnection> ResolveActiveConnectionAsync(
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
        ?? throw new InvalidOperationException("No connected finance integration is available for payment export.");

    private static decimal ResolveProposalAmount(FinanceBill bill)
    {
        var remainingAmount = Math.Abs(bill.Amount) - Math.Abs(bill.PaidAmount);
        return decimal.Round(Math.Max(remainingAmount, 0m), 2, MidpointRounding.AwayFromZero);
    }

    private static Dictionary<string, JsonNode?> BuildTaskInput(SupplierInvoicePaymentProposal proposal) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["paymentProposalId"] = proposal.Id.ToString("D"),
            ["billId"] = proposal.BillId.ToString("D"),
            ["supplierId"] = proposal.SupplierId.ToString("D"),
            ["supplierName"] = proposal.SupplierName,
            ["amount"] = proposal.Amount,
            ["currency"] = proposal.Currency,
            ["dueUtc"] = proposal.DueUtc.ToString("O"),
            ["paymentReference"] = proposal.PaymentReference,
            ["doesNotInitiatePayment"] = true
        };

    private static Dictionary<string, JsonNode?> BuildApprovalContext(SupplierInvoicePaymentProposal proposal) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["paymentProposalId"] = proposal.Id.ToString("D"),
            ["billId"] = proposal.BillId.ToString("D"),
            ["supplier"] = proposal.SupplierName,
            ["amount"] = proposal.Amount,
            ["currency"] = proposal.Currency,
            ["dueUtc"] = proposal.DueUtc.ToString("O"),
            ["paymentReference"] = proposal.PaymentReference,
            ["summary"] = $"Approve payment proposal {proposal.PaymentReference} for {proposal.Currency} {proposal.Amount:N2}. No payment will be initiated automatically."
        };

    private static string BuildCorrelationId(Guid companyId, Guid billId) =>
        $"supplier-payment-proposal:{companyId:N}:{billId:N}";

    private async Task ExecuteInTransactionAsync(
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null)
        {
            await action();
            return;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await action();
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public static SupplierInvoicePaymentProposalDto MapProposal(SupplierInvoicePaymentProposal proposal) =>
        new(
            proposal.Id,
            proposal.BillId,
            proposal.SupplierId,
            proposal.SupplierName,
            proposal.Amount,
            proposal.Currency,
            proposal.DueUtc,
            proposal.PaymentReference,
            proposal.Status,
            proposal.TaskId,
            proposal.ApprovalRequestId,
            proposal.RequestedByUserId,
            proposal.DecidedByUserId,
            proposal.DecidedUtc,
            proposal.CreatedUtc,
            proposal.UpdatedUtc,
            proposal.ExportMode,
            proposal.ExportStatus,
            proposal.ExportProviderKey,
            proposal.ExportConnectionId,
            proposal.ExportRequestedByUserId,
            proposal.ExportRequestedUtc,
            proposal.ExportedUtc,
            proposal.ExportResponseSummary);
}

public sealed class FortnoxSupplierInvoicePaymentExportProvider : ISupplierInvoicePaymentExportProvider
{
    private readonly IFortnoxApiClient? _apiClient;
    private readonly ILogger<FortnoxSupplierInvoicePaymentExportProvider>? _logger;

    public FortnoxSupplierInvoicePaymentExportProvider(
        IFortnoxApiClient? apiClient = null,
        ILogger<FortnoxSupplierInvoicePaymentExportProvider>? logger = null)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public async Task<SupplierInvoicePaymentExportProviderResult> ExportAsync(
        SupplierInvoicePaymentExportProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedExportMode = SupplierInvoicePaymentExportModes.Normalize(request.ExportMode);
        if (normalizedExportMode == SupplierInvoicePaymentExportModes.PreparePaymentFile)
        {
            return CreateManualPaymentFileResult(request);
        }

        if (normalizedExportMode == SupplierInvoicePaymentExportModes.ManualExport)
        {
            return CreateManualExportResult(request);
        }

        if (_apiClient is null)
        {
            return CreateManualExportResult(request);
        }

        var paymentDate = DateOnly.FromDateTime(DateTime.SpecifyKind(request.DueUtc, DateTimeKind.Utc));
        if (request.BookkeepExistingProviderPayment)
        {
            return await BookkeepExistingPaymentAsync(request, paymentDate, cancellationToken);
        }

        var payload = new JsonObject
        {
            ["SupplierInvoicePayment"] = new JsonObject
            {
                ["InvoiceNumber"] = request.SourceBillNumber,
                ["Amount"] = request.Amount,
                ["AmountCurrency"] = request.Amount,
                ["PaymentDate"] = paymentDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["Information"] = $"Virtual Company approved payment proposal {request.ProposalId:N}"
            }
        };

        _logger?.LogInformation(
            "Registering supplier invoice payment in Fortnox. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ProposalId: {ProposalId}. BillId: {BillId}. FortnoxInvoiceNumber: {InvoiceNumber}. Amount: {Amount}. Currency: {Currency}. PaymentDate: {PaymentDate}.",
            request.CompanyId,
            request.ConnectionId,
            request.ProposalId,
            request.BillId,
            request.SourceBillNumber,
            request.Amount,
            request.Currency,
            paymentDate);

        try
        {
            var response = await _apiClient.PostDirectAsync<JsonObject, JsonObject?>(
                new FortnoxRequestContext(
                    request.CompanyId,
                    request.ConnectionId,
                    $"supplier-payment-registration:{request.ProposalId:N}",
                    ActorUserId: request.ActorUserId,
                    WriteRequestId: request.ProposalId),
                "supplierinvoicepayments",
                payload,
                cancellationToken);
            var responseMetadata = response is null
                ? new JsonObject()
                : JsonNode.Parse(response.ToJsonString())?.AsObject() ?? new JsonObject();
            var payment = response?["SupplierInvoicePayment"] as JsonObject;
            var paymentNumber = ReadString(payment, "Number");
            var booked = ReadBool(payment, "Booked");
            JsonObject? bookkeepResponseMetadata = null;
            if (!string.IsNullOrWhiteSpace(paymentNumber) && booked != true)
            {
                _logger?.LogInformation(
                    "Bookkeeping supplier invoice payment in Fortnox. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ProposalId: {ProposalId}. BillId: {BillId}. FortnoxPaymentNumber: {PaymentNumber}.",
                    request.CompanyId,
                    request.ConnectionId,
                    request.ProposalId,
                    request.BillId,
                    paymentNumber);

                var bookkeepResponse = await _apiClient.PutDirectAsync<JsonObject, JsonObject?>(
                    new FortnoxRequestContext(
                        request.CompanyId,
                        request.ConnectionId,
                        $"supplier-payment-bookkeep:{request.ProposalId:N}",
                        ActorUserId: request.ActorUserId,
                        WriteRequestId: request.ProposalId),
                    $"supplierinvoicepayments/{paymentNumber}/bookkeep",
                    BuildBookkeepPayload(request, paymentNumber, paymentDate),
                    cancellationToken);

                bookkeepResponseMetadata = bookkeepResponse is null
                    ? new JsonObject()
                    : JsonNode.Parse(bookkeepResponse.ToJsonString())?.AsObject() ?? new JsonObject();
                payment = bookkeepResponse?["SupplierInvoicePayment"] as JsonObject ?? payment;
                booked = ReadBool(payment, "Booked") ?? true;
            }

            var metadata = BuildBaseMetadata(request);
            metadata["paymentDate"] = paymentDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            metadata["fortnoxSupplierInvoicePaymentNumber"] = paymentNumber;
            metadata["booked"] = booked;
            metadata["doesNotInitiateBankPayment"] = true;
            metadata["fortnoxResponse"] = responseMetadata;
            metadata["fortnoxBookkeepResponse"] = bookkeepResponseMetadata;
            metadata["requestPayload"] = JsonNode.Parse(payload.ToJsonString())?.AsObject() ?? new JsonObject();

            var summary = string.IsNullOrWhiteSpace(paymentNumber)
                ? "Fortnox accepted and booked the supplier invoice payment registration. No bank payment was initiated automatically."
                : $"Fortnox registered and booked supplier invoice payment {paymentNumber}. No bank payment was initiated automatically.";

            return new SupplierInvoicePaymentExportProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoicePaymentExportModes.RegisterPayment,
                SupplierInvoicePaymentExportStatuses.Exported,
                summary,
                metadata);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            var safeSummary = exception is FortnoxApiException apiException
                ? apiException.SafeMessage
                : "Fortnox could not register this supplier invoice payment.";
            _logger?.LogWarning(
                exception,
                "Fortnox supplier invoice payment registration failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ProposalId: {ProposalId}. BillId: {BillId}. FortnoxInvoiceNumber: {InvoiceNumber}. SafeSummary: {SafeSummary}.",
                request.CompanyId,
                request.ConnectionId,
                request.ProposalId,
                request.BillId,
                request.SourceBillNumber,
                safeSummary);

            var metadata = BuildBaseMetadata(request);
            metadata["paymentDate"] = paymentDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            metadata["doesNotInitiateBankPayment"] = true;
            metadata["requestPayload"] = JsonNode.Parse(payload.ToJsonString())?.AsObject() ?? new JsonObject();
            metadata["failureCategory"] = exception is FortnoxApiException api ? api.Category : "transport";
            metadata["failureMessage"] = safeSummary;

            return new SupplierInvoicePaymentExportProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoicePaymentExportModes.RegisterPayment,
                SupplierInvoicePaymentExportStatuses.Failed,
                safeSummary,
                metadata);
        }
    }

    private async Task<SupplierInvoicePaymentExportProviderResult> BookkeepExistingPaymentAsync(
        SupplierInvoicePaymentExportProviderRequest request,
        DateOnly paymentDate,
        CancellationToken cancellationToken)
    {
        if (_apiClient is null)
        {
            return CreateManualExportResult(request);
        }

        if (string.IsNullOrWhiteSpace(request.ExistingProviderPaymentNumber))
        {
            return new SupplierInvoicePaymentExportProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoicePaymentExportModes.RegisterPayment,
                SupplierInvoicePaymentExportStatuses.Failed,
                "Fortnox payment could not be finalized because the supplier invoice payment number is missing.",
                BuildBaseMetadata(request));
        }

        _logger?.LogInformation(
            "Bookkeeping existing supplier invoice payment in Fortnox. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ProposalId: {ProposalId}. BillId: {BillId}. FortnoxPaymentNumber: {PaymentNumber}.",
            request.CompanyId,
            request.ConnectionId,
            request.ProposalId,
            request.BillId,
            request.ExistingProviderPaymentNumber);

        try
        {
            var response = await _apiClient.PutDirectAsync<JsonObject, JsonObject?>(
                new FortnoxRequestContext(
                    request.CompanyId,
                    request.ConnectionId,
                    $"supplier-payment-bookkeep-existing:{request.ProposalId:N}",
                    ActorUserId: request.ActorUserId,
                    WriteRequestId: request.ProposalId),
                $"supplierinvoicepayments/{request.ExistingProviderPaymentNumber}/bookkeep",
                BuildBookkeepPayload(request, request.ExistingProviderPaymentNumber, paymentDate),
                cancellationToken);
            var responseMetadata = response is null
                ? new JsonObject()
                : JsonNode.Parse(response.ToJsonString())?.AsObject() ?? new JsonObject();
            var payment = response?["SupplierInvoicePayment"] as JsonObject;
            var booked = ReadBool(payment, "Booked") ?? true;
            var metadata = BuildBaseMetadata(request);
            metadata["paymentDate"] = paymentDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            metadata["fortnoxSupplierInvoicePaymentNumber"] = request.ExistingProviderPaymentNumber;
            metadata["booked"] = booked;
            metadata["doesNotInitiateBankPayment"] = true;
            metadata["fortnoxBookkeepResponse"] = responseMetadata;
            metadata["bookkeepRequestPayload"] = BuildBookkeepPayload(request, request.ExistingProviderPaymentNumber, paymentDate);

            return new SupplierInvoicePaymentExportProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoicePaymentExportModes.RegisterPayment,
                SupplierInvoicePaymentExportStatuses.Exported,
                $"Fortnox booked supplier invoice payment {request.ExistingProviderPaymentNumber}. No bank payment was initiated automatically.",
                metadata);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            var safeSummary = exception is FortnoxApiException apiException
                ? apiException.SafeMessage
                : "Fortnox could not bookkeep this supplier invoice payment.";
            _logger?.LogWarning(
                exception,
                "Fortnox supplier invoice payment bookkeep failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ProposalId: {ProposalId}. BillId: {BillId}. FortnoxPaymentNumber: {PaymentNumber}. SafeSummary: {SafeSummary}.",
                request.CompanyId,
                request.ConnectionId,
                request.ProposalId,
                request.BillId,
                request.ExistingProviderPaymentNumber,
                safeSummary);

            var metadata = BuildBaseMetadata(request);
            metadata["paymentDate"] = paymentDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            metadata["fortnoxSupplierInvoicePaymentNumber"] = request.ExistingProviderPaymentNumber;
            metadata["booked"] = false;
            metadata["doesNotInitiateBankPayment"] = true;
            metadata["bookkeepRequestPayload"] = BuildBookkeepPayload(request, request.ExistingProviderPaymentNumber, paymentDate);
            metadata["failureCategory"] = exception is FortnoxApiException api ? api.Category : "transport";
            metadata["failureMessage"] = safeSummary;

            return new SupplierInvoicePaymentExportProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoicePaymentExportModes.RegisterPayment,
                SupplierInvoicePaymentExportStatuses.Failed,
                safeSummary,
                metadata);
        }
    }

    private SupplierInvoicePaymentExportProviderResult CreateManualPaymentFileResult(SupplierInvoicePaymentExportProviderRequest request)
    {
        var metadata = BuildBaseMetadata(request);
        metadata["doesNotInitiateBankPayment"] = true;
        metadata["manualPaymentFileRequired"] = true;
        metadata["paymentFilePrepared"] = false;
        metadata["reason"] = "Fortnox payment-file/payment-out preparation is recorded for manual handling because the current Fortnox API scope or endpoint is not configured.";

        return new SupplierInvoicePaymentExportProviderResult(
            ProviderKey,
            request.ConnectionId,
            SupplierInvoicePaymentExportModes.PreparePaymentFile,
            SupplierInvoicePaymentExportStatuses.ExportRequested,
            "Manual payment file required. The approved Fortnox payment instruction was recorded, but no bank payment was initiated automatically.",
            metadata);
    }

    private SupplierInvoicePaymentExportProviderResult CreateManualExportResult(SupplierInvoicePaymentExportProviderRequest request)
    {
        var metadata = BuildBaseMetadata(request);
        metadata["doesNotInitiateBankPayment"] = true;
        metadata["manualPaymentRequired"] = true;
        metadata["reason"] = "Fortnox supplier invoice payment export is recorded for manual handling because no Fortnox API client is available.";

        return new SupplierInvoicePaymentExportProviderResult(
            ProviderKey,
            request.ConnectionId,
            SupplierInvoicePaymentExportModes.ManualExport,
            SupplierInvoicePaymentExportStatuses.ExportRequested,
            "Manual payment/export required. The approved Fortnox payment instruction was recorded, but no bank payment was initiated automatically.",
            metadata);
    }

    private JsonObject BuildBaseMetadata(SupplierInvoicePaymentExportProviderRequest request) =>
        new()
        {
            ["provider"] = ProviderKey,
            ["exportMode"] = SupplierInvoicePaymentExportModes.Normalize(request.ExportMode),
            ["doesNotInitiateBankPayment"] = true,
            ["connectionId"] = request.ConnectionId.ToString("D"),
            ["proposalId"] = request.ProposalId.ToString("D"),
            ["billId"] = request.BillId.ToString("D"),
            ["fortnoxInvoiceNumber"] = request.SourceBillNumber,
            ["supplierId"] = request.SupplierId.ToString("D"),
            ["supplierName"] = request.SupplierName,
            ["amount"] = request.Amount,
            ["currency"] = request.Currency,
            ["dueUtc"] = request.DueUtc.ToString("O"),
            ["paymentReference"] = request.PaymentReference
        };

    private static JsonObject BuildBookkeepPayload(
        SupplierInvoicePaymentExportProviderRequest request,
        string paymentNumber,
        DateOnly paymentDate) =>
        new()
        {
            ["SupplierInvoicePayment"] = new JsonObject
            {
                ["Number"] = paymentNumber,
                ["InvoiceNumber"] = request.SourceBillNumber,
                ["Amount"] = request.Amount,
                ["AmountCurrency"] = request.Amount,
                ["PaymentDate"] = paymentDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["Information"] = $"Virtual Company approved payment proposal {request.ProposalId:N}"
            }
        };

    private static string? ReadString(JsonObject? source, string propertyName) =>
        source is not null && source.TryGetPropertyValue(propertyName, out var node) && node is not null
            ? node.ToString()
            : null;

    private static bool? ReadBool(JsonObject? source, string propertyName)
    {
        if (source is not null &&
            source.TryGetPropertyValue(propertyName, out var node) &&
            node is JsonValue value &&
            value.TryGetValue<bool>(out var result))
        {
            return result;
        }

        return null;
    }
}
