using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SupplierInvoiceCorrectionServiceTests
{
    [Fact]
    public async Task RequestCancellationAsync_cancels_eligible_supplier_invoice()
    {
        var provider = new QueueingCorrectionProvider(cancellationStatus: SupplierInvoiceCorrectionActionStatuses.Cancelled);
        await using var fixture = await CorrectionFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(FinanceDocumentPostingStatuses.Booked, FinanceSettlementStatuses.Unpaid);

        var result = await fixture.Service.RequestCancellationAsync(
            new RequestSupplierInvoiceCancellationCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceCorrectionActionStatuses.CancellationRequested, result.Status);
        Assert.NotNull(result.TaskId);
        Assert.NotNull(result.ApprovalRequestId);
        Assert.Equal(0, provider.CancelCallCount);

        await fixture.ApproveAsync(result.ApprovalRequestId!.Value);
        result = await fixture.Service.RequestCancellationAsync(
            new RequestSupplierInvoiceCancellationCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceCorrectionActionStatuses.Cancelled, result.Status);
        Assert.NotNull(result.ApprovedUtc);
        Assert.Equal(1, provider.CancelCallCount);
        Assert.Equal("3", provider.LastRequest?.SourceBillNumber);
        Assert.Equal(1, await fixture.Db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().CountAsync(x => x.InternalRecordId == result.Id));
    }

    [Fact]
    public async Task RequestCancellationAsync_blocks_paid_supplier_invoice()
    {
        var provider = new QueueingCorrectionProvider(cancellationStatus: SupplierInvoiceCorrectionActionStatuses.Cancelled);
        await using var fixture = await CorrectionFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(FinanceDocumentPostingStatuses.Booked, FinanceSettlementStatuses.Paid);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RequestCancellationAsync(
                new RequestSupplierInvoiceCancellationCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
                CancellationToken.None));

        Assert.Contains("Paid", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.CancelCallCount);
    }

    [Fact]
    public async Task RequestCreditNoteAsync_creates_credit_note()
    {
        var provider = new QueueingCorrectionProvider(creditNoteStatus: SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated);
        await using var fixture = await CorrectionFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(FinanceDocumentPostingStatuses.Booked, FinanceSettlementStatuses.Paid);

        var result = await fixture.Service.RequestCreditNoteAsync(
            new RequestSupplierInvoiceCreditNoteCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin", "Overbilling"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceCorrectionActionStatuses.CreditNoteRequested, result.Status);
        Assert.NotNull(result.TaskId);
        Assert.NotNull(result.ApprovalRequestId);
        Assert.Equal(0, provider.CreditNoteCallCount);

        await fixture.ApproveAsync(result.ApprovalRequestId!.Value);
        result = await fixture.Service.RequestCreditNoteAsync(
            new RequestSupplierInvoiceCreditNoteCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin", "Overbilling"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated, result.Status);
        Assert.NotNull(result.ApprovedUtc);
        Assert.Equal("CN-3", result.ProviderCreditNoteNumber);
        Assert.Equal(1, provider.CreditNoteCallCount);
    }

    [Fact]
    public async Task RequestCreditNoteAsync_prevents_duplicate_created_credit_note()
    {
        var provider = new QueueingCorrectionProvider(creditNoteStatus: SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated);
        await using var fixture = await CorrectionFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(FinanceDocumentPostingStatuses.Booked, FinanceSettlementStatuses.Paid);

        var requested = await fixture.Service.RequestCreditNoteAsync(
            new RequestSupplierInvoiceCreditNoteCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);
        await fixture.ApproveAsync(requested.ApprovalRequestId!.Value);
        await fixture.Service.RequestCreditNoteAsync(
            new RequestSupplierInvoiceCreditNoteCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);
        var second = await fixture.Service.RequestCreditNoteAsync(
            new RequestSupplierInvoiceCreditNoteCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated, second.Status);
        Assert.Equal(1, provider.CreditNoteCallCount);
    }

    [Fact]
    public async Task RequestCreditNoteAsync_allows_retry_after_failure()
    {
        var provider = new QueueingCorrectionProvider(
            creditNoteStatus: SupplierInvoiceCorrectionActionStatuses.CreditNoteFailed,
            nextCreditNoteStatus: SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated);
        await using var fixture = await CorrectionFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(FinanceDocumentPostingStatuses.Booked, FinanceSettlementStatuses.Paid);

        var requested = await fixture.Service.RequestCreditNoteAsync(
            new RequestSupplierInvoiceCreditNoteCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);
        await fixture.ApproveAsync(requested.ApprovalRequestId!.Value);
        var failed = await fixture.Service.RequestCreditNoteAsync(
            new RequestSupplierInvoiceCreditNoteCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);
        var retry = await fixture.Service.RequestCreditNoteAsync(
            new RequestSupplierInvoiceCreditNoteCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceCorrectionActionStatuses.CreditNoteFailed, failed.Status);
        Assert.Equal(SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated, retry.Status);
        Assert.Equal(2, provider.CreditNoteCallCount);
    }

    [Fact]
    public async Task Fortnox_provider_uses_supplier_invoice_correction_endpoints()
    {
        var apiClient = new CapturingCorrectionFortnoxApiClient();
        var provider = new FortnoxSupplierInvoiceCorrectionProvider(apiClient);
        var request = new SupplierInvoiceCorrectionProviderRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "3",
            Guid.NewGuid(),
            "OpenAI",
            "1",
            22000m,
            "SEK",
            new DateTime(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 23, 0, 0, 0, DateTimeKind.Utc),
            "12",
            "OCR-12",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Correction");

        var cancellation = await provider.CancelAsync(request, CancellationToken.None);
        var creditNote = await provider.CreateCreditNoteAsync(request, CancellationToken.None);

        Assert.Equal(SupplierInvoiceCorrectionActionStatuses.Cancelled, cancellation.Status);
        Assert.Equal(SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated, creditNote.Status);
        Assert.Contains("supplierinvoices/3/cancel", apiClient.Paths);
        Assert.Contains("supplierinvoices/3/credit", apiClient.Paths);
        Assert.Equal("CN-3", creditNote.ProviderCreditNoteNumber);
    }

    private sealed class CorrectionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private CorrectionFixture(
            SqliteConnection connection,
            VirtualCompanyDbContext db,
            ISupplierInvoiceCorrectionProvider provider)
        {
            _connection = connection;
            Db = db;
            ApprovalService = new PersistingApprovalRequestService(Db);
            Service = new SupplierInvoiceCorrectionService(
                Db,
                ApprovalService,
                TimeProvider.System,
                providers: [provider]);
        }

        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid ActorUserId { get; } = Guid.NewGuid();
        public Guid ConnectionId { get; } = Guid.NewGuid();
        public VirtualCompanyDbContext Db { get; }
        public PersistingApprovalRequestService ApprovalService { get; }
        public SupplierInvoiceCorrectionService Service { get; }

        public static async Task<CorrectionFixture> CreateAsync(ISupplierInvoiceCorrectionProvider provider)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new CorrectionFixture(connection, db, provider);

            db.Companies.Add(new Company(fixture.CompanyId, "Fortnox-only company"));
            db.FinanceCounterparties.Add(new FinanceCounterparty(
                fixture.SupplierId,
                fixture.CompanyId,
                "OpenAI",
                FinanceCounterpartyTypes.Supplier,
                defaultAccountMapping: "1012"));
            db.FinanceIntegrationConnections.Add(new FinanceIntegrationConnection(
                fixture.ConnectionId,
                fixture.CompanyId,
                FinanceIntegrationProviderKeys.Fortnox,
                FinanceIntegrationConnectionStatuses.Connected,
                fixture.ActorUserId,
                DateTime.UtcNow));
            db.FinanceExternalReferences.Add(new FinanceExternalReference(
                Guid.NewGuid(),
                fixture.CompanyId,
                fixture.ConnectionId,
                FinanceIntegrationProviderKeys.Fortnox,
                "supplier",
                fixture.SupplierId,
                "1",
                "1",
                null,
                DateTime.UtcNow));
            await db.SaveChangesAsync();

            return fixture;
        }

        public async Task<FinanceBill> AddSupplierBillAsync(string postingStatus, string settlementStatus)
        {
            var now = new DateTime(2026, 5, 24, 8, 0, 0, DateTimeKind.Utc);
            var bill = new FinanceBill(
                Guid.NewGuid(),
                CompanyId,
                SupplierId,
                "12",
                now.AddDays(-11),
                now.AddDays(-1),
                22000m,
                "SEK",
                "approved",
                settlementStatus: settlementStatus,
                postingStatus: postingStatus,
                dueStatus: FinanceDocumentDueStatuses.Overdue,
                documentKind: FinanceDocumentKinds.SupplierInvoice,
                providerStatus: "booked=true;balance=22000",
                processingStatus: FinanceDocumentProcessingStatuses.Synced);
            Db.FinanceBills.Add(bill);
            Db.FinanceExternalReferences.Add(new FinanceExternalReference(
                Guid.NewGuid(),
                CompanyId,
                ConnectionId,
                FinanceIntegrationProviderKeys.Fortnox,
                "supplier_invoice",
                bill.Id,
                "3",
                "3",
                null,
                now));
            await Db.SaveChangesAsync();
            return bill;
        }

        public async Task ApproveAsync(Guid approvalRequestId)
        {
            var approval = await Db.ApprovalRequests
                .IgnoreQueryFilters()
                .Include(x => x.Steps)
                .SingleAsync(x => x.Id == approvalRequestId);
            var step = approval.Steps.Single();
            approval.ApproveCurrentStep(step.Id, ActorUserId, "Approved for test.");
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class QueueingCorrectionProvider : ISupplierInvoiceCorrectionProvider
    {
        private readonly Queue<string> _cancellationStatuses = new();
        private readonly Queue<string> _creditNoteStatuses = new();

        public QueueingCorrectionProvider(
            string cancellationStatus = SupplierInvoiceCorrectionActionStatuses.Cancelled,
            string? nextCancellationStatus = null,
            string creditNoteStatus = SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated,
            string? nextCreditNoteStatus = null)
        {
            _cancellationStatuses.Enqueue(cancellationStatus);
            if (!string.IsNullOrWhiteSpace(nextCancellationStatus))
            {
                _cancellationStatuses.Enqueue(nextCancellationStatus);
            }

            _creditNoteStatuses.Enqueue(creditNoteStatus);
            if (!string.IsNullOrWhiteSpace(nextCreditNoteStatus))
            {
                _creditNoteStatuses.Enqueue(nextCreditNoteStatus);
            }
        }

        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
        public int CancelCallCount { get; private set; }
        public int CreditNoteCallCount { get; private set; }
        public SupplierInvoiceCorrectionProviderRequest? LastRequest { get; private set; }

        public Task<SupplierInvoiceCorrectionProviderResult> CancelAsync(
            SupplierInvoiceCorrectionProviderRequest request,
            CancellationToken cancellationToken)
        {
            CancelCallCount++;
            LastRequest = request;
            var status = _cancellationStatuses.Count > 1 ? _cancellationStatuses.Dequeue() : _cancellationStatuses.Peek();
            return Task.FromResult(CreateResult(request, status));
        }

        public Task<SupplierInvoiceCorrectionProviderResult> CreateCreditNoteAsync(
            SupplierInvoiceCorrectionProviderRequest request,
            CancellationToken cancellationToken)
        {
            CreditNoteCallCount++;
            LastRequest = request;
            var status = _creditNoteStatuses.Count > 1 ? _creditNoteStatuses.Dequeue() : _creditNoteStatuses.Peek();
            return Task.FromResult(CreateResult(request, status));
        }

        private static SupplierInvoiceCorrectionProviderResult CreateResult(
            SupplierInvoiceCorrectionProviderRequest request,
            string status) =>
            new(
                FinanceIntegrationProviderKeys.Fortnox,
                request.ConnectionId,
                status,
                status.Contains("failed", StringComparison.OrdinalIgnoreCase) ? "Correction failed." : "Correction completed.",
                new JsonObject { ["status"] = status },
                status == SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated ? "CN-3" : null);
    }

    private sealed class CapturingCorrectionFortnoxApiClient : IFortnoxApiClient
    {
        public List<string> Paths { get; } = [];

        public Task<TResponse?> PutDirectAsync<TRequest, TResponse>(
            FortnoxRequestContext context,
            string path,
            TRequest payload,
            CancellationToken cancellationToken)
        {
            Paths.Add(path);
            var response = new JsonObject
            {
                ["SupplierInvoice"] = new JsonObject
                {
                    ["GivenNumber"] = path.Contains("/credit", StringComparison.OrdinalIgnoreCase) ? "CN-3" : "3"
                }
            };
            return Task.FromResult((TResponse?)(object)response);
        }

        public Task<TResponse?> PostMultipartFileDirectAsync<TResponse>(FortnoxRequestContext context, string path, string formFieldName, string fileName, string? contentType, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxCompanyInformation> GetCompanyInformationAsync(FortnoxRequestContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxPagedResponse<FortnoxCustomer>> GetCustomersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxPagedResponse<FortnoxSupplier>> GetSuppliersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxPagedResponse<FortnoxInvoice>> GetInvoicesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxPagedResponse<FortnoxInvoicePayment>> GetInvoicePaymentsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxPagedResponse<FortnoxSupplierInvoice>> GetSupplierInvoicesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxPagedResponse<FortnoxSupplierInvoicePayment>> GetSupplierInvoicePaymentsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxPagedResponse<FortnoxVoucher>> GetVouchersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxPagedResponse<FortnoxAccount>> GetAccountsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxPagedResponse<FortnoxArticle>> GetArticlesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FortnoxPagedResponse<FortnoxProject>> GetProjectsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TResponse?> GetAsync<TResponse>(FortnoxRequestContext context, string path, FortnoxPageOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TResponse?> PostAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TResponse?> PostDirectAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TResponse?> PutAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(FortnoxRequestContext context, string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PersistingApprovalRequestService : IApprovalRequestService
    {
        private readonly VirtualCompanyDbContext _db;

        public PersistingApprovalRequestService(VirtualCompanyDbContext db) => _db = db;

        public Task<IReadOnlyList<ApprovalRequestDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApprovalRequestDto> GetAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<ApprovalRequestDto> CreateAsync(Guid companyId, CreateApprovalRequestCommand command, CancellationToken cancellationToken)
        {
            var approval = ApprovalRequest.CreateForTarget(
                Guid.NewGuid(),
                companyId,
                ApprovalTargetEntityTypeValues.Parse(command.TargetEntityType),
                command.TargetEntityId,
                command.RequestedByActorType,
                command.RequestedByActorId,
                command.ApprovalType,
                command.ThresholdContext ?? [],
                command.RequiredRole,
                command.RequiredUserId,
                []);
            _db.ApprovalRequests.Add(approval);
            await _db.SaveChangesAsync(cancellationToken);
            return Map(approval);
        }

        public Task<ApprovalDecisionResultDto> DecideAsync(Guid companyId, ApprovalDecisionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static ApprovalRequestDto Map(ApprovalRequest approval)
        {
            var steps = approval.Steps
                .OrderBy(step => step.SequenceNo)
                .Select(step => new ApprovalStepDto(
                    step.Id,
                    step.SequenceNo,
                    step.ApproverType.ToStorageValue(),
                    step.ApproverRef,
                    step.Status.ToStorageValue(),
                    step.DecidedByUserId,
                    step.DecidedUtc,
                    step.Comment))
                .ToArray();

            return new ApprovalRequestDto(
                approval.Id,
                approval.CompanyId,
                approval.TargetEntityType,
                approval.TargetEntityId,
                approval.RequestedByActorType,
                approval.RequestedByActorId,
                approval.ApprovalType,
                approval.RequiredRole,
                approval.RequiredUserId,
                approval.Status.ToStorageValue(),
                approval.ThresholdContext,
                steps,
                steps.FirstOrDefault(),
                approval.DecisionSummary,
                approval.RejectionComment,
                string.Empty,
                string.Empty,
                [],
                null,
                approval.CreatedUtc);
        }
    }
}
