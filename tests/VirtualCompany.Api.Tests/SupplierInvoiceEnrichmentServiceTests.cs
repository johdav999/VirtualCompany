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

public sealed class SupplierInvoiceEnrichmentServiceTests
{
    [Fact]
    public async Task SuggestAsync_creates_coding_suggestion_and_approval()
    {
        await using var fixture = await SupplierInvoiceEnrichmentFixture.CreateAsync(new CapturingEnrichmentProvider());
        var bill = await fixture.AddSupplierBillAsync();

        var result = await fixture.Service.SuggestAsync(
            new SuggestSupplierInvoiceEnrichmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceEnrichmentActionStatuses.AwaitingApproval, result.Status);
        Assert.Equal("4010", result.SuggestionPayload["coding"]?["ledgerAccount"]?.ToString());
        Assert.Equal("KST-10", result.SuggestionPayload["coding"]?["costCenter"]?.ToString());
        Assert.Equal("P-20", result.SuggestionPayload["coding"]?["project"]?.ToString());
        Assert.NotNull(result.TaskId);
        Assert.NotNull(result.ApprovalRequestId);
        Assert.Equal(1, await fixture.Db.WorkTasks.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await fixture.Db.ApprovalRequests.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SyncApprovedAsync_pushes_approved_changes_to_provider()
    {
        var provider = new CapturingEnrichmentProvider();
        await using var fixture = await SupplierInvoiceEnrichmentFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync();
        var suggestion = await fixture.Service.SuggestAsync(
            new SuggestSupplierInvoiceEnrichmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
            CancellationToken.None);
        await fixture.ApproveAsync(suggestion.ApprovalRequestId!.Value);

        var result = await fixture.Service.SyncApprovedAsync(
            new SyncSupplierInvoiceEnrichmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceEnrichmentActionStatuses.Synced, result.Status);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal("3", provider.LastRequest?.SourceBillNumber);
        Assert.Equal("1", provider.LastRequest?.SupplierNumber);
        Assert.Equal("4010", provider.LastRequest?.AccountCode);
        Assert.Equal("KST-10", provider.LastRequest?.CostCenter);
        Assert.Equal("P-20", provider.LastRequest?.Project);
        Assert.Equal(1, await fixture.Db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().CountAsync(x => x.InternalRecordId == result.Id));
    }

    [Fact]
    public async Task Fortnox_provider_updates_supplier_invoice_and_supplier_master_data()
    {
        var apiClient = new CapturingFortnoxApiClient();
        var provider = new FortnoxSupplierInvoiceEnrichmentProvider(apiClient);
        var request = new SupplierInvoiceEnrichmentProviderRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "3",
            Guid.NewGuid(),
            "OpenAI",
            "1",
            "billing@example.com",
            "559999-1234",
            "14",
            "bank_transfer",
            "4010",
            "KST-10",
            "P-20",
            "Reviewed by Laura.",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new JsonObject());

        var result = await provider.SyncAsync(request, CancellationToken.None);

        Assert.Equal(SupplierInvoiceEnrichmentActionStatuses.Synced, result.Status);
        Assert.Contains("supplierinvoices/3", apiClient.Paths);
        Assert.Contains("suppliers/1", apiClient.Paths);
        Assert.Equal("Reviewed by Laura.", apiClient.Payloads["supplierinvoices/3"]["SupplierInvoice"]?["Comments"]?.ToString());
        Assert.Equal("4010", apiClient.Payloads["supplierinvoices/3"]["SupplierInvoice"]?["SupplierInvoiceRows"]?[0]?["Account"]?.ToString());
        Assert.Equal("billing@example.com", apiClient.Payloads["suppliers/1"]["Supplier"]?["Email"]?.ToString());
        Assert.Equal("559999-1234", apiClient.Payloads["suppliers/1"]["Supplier"]?["OrganisationNumber"]?.ToString());
    }

    [Fact]
    public async Task ReconcileAsync_flags_duplicate_payment()
    {
        await using var fixture = await SupplierInvoiceEnrichmentFixture.CreateAsync(new CapturingEnrichmentProvider());
        var bill = await fixture.AddSupplierBillAsync(paidAmount: 100m, settlementStatus: FinanceSettlementStatuses.PartiallyPaid);
        await fixture.AddPaymentTransactionsAsync(bill.Id, count: 2);

        var result = await fixture.Service.ReconcileAsync(
            new ReconcileSupplierInvoiceCommand(fixture.CompanyId, bill.Id),
            CancellationToken.None);

        Assert.Contains(result.ReconciliationWarnings.OfType<JsonObject>(), warning => warning["code"]?.ToString() == "duplicate_payment");
    }

    [Fact]
    public async Task ReconcileAsync_flags_unpaid_overdue_invoice()
    {
        await using var fixture = await SupplierInvoiceEnrichmentFixture.CreateAsync(new CapturingEnrichmentProvider());
        var bill = await fixture.AddSupplierBillAsync(
            dueUtc: DateTime.UtcNow.AddDays(-3),
            settlementStatus: FinanceSettlementStatuses.Unpaid);

        var result = await fixture.Service.ReconcileAsync(
            new ReconcileSupplierInvoiceCommand(fixture.CompanyId, bill.Id),
            CancellationToken.None);

        Assert.Contains(result.ReconciliationWarnings.OfType<JsonObject>(), warning => warning["code"]?.ToString() == "unpaid_after_due_date");
    }

    [Fact]
    public async Task SuggestAsync_works_in_fortnox_only_mode_without_simulation_seed()
    {
        await using var fixture = await SupplierInvoiceEnrichmentFixture.CreateAsync(new CapturingEnrichmentProvider());
        var bill = await fixture.AddSupplierBillAsync(processingStatus: FinanceDocumentProcessingStatuses.Synced);

        var result = await fixture.Service.SuggestAsync(
            new SuggestSupplierInvoiceEnrichmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceEnrichmentActionStatuses.AwaitingApproval, result.Status);
        Assert.NotNull(result.ApprovalRequestId);
    }

    [Fact]
    public async Task SuggestAsync_ignores_supplier_default_liability_account()
    {
        await using var fixture = await SupplierInvoiceEnrichmentFixture.CreateAsync(new CapturingEnrichmentProvider(), supplierDefaultAccount: "2000");
        var bill = await fixture.AddSupplierBillAsync(metadataAccountCode: null);

        var result = await fixture.Service.SuggestAsync(
            new SuggestSupplierInvoiceEnrichmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
            CancellationToken.None);

        Assert.Equal("4010", result.SuggestionPayload["coding"]?["ledgerAccount"]?.ToString());
        Assert.Contains(result.ReconciliationWarnings.OfType<JsonObject>(), warning => warning["code"]?.ToString() == "invalid_expense_account");
    }

    [Fact]
    public async Task SuggestAsync_uses_valid_provider_metadata_when_supplier_default_is_invalid()
    {
        await using var fixture = await SupplierInvoiceEnrichmentFixture.CreateAsync(new CapturingEnrichmentProvider(), supplierDefaultAccount: "2000");
        var bill = await fixture.AddSupplierBillAsync(metadataAccountCode: "6540");

        var result = await fixture.Service.SuggestAsync(
            new SuggestSupplierInvoiceEnrichmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
            CancellationToken.None);

        Assert.Equal("6540", result.SuggestionPayload["coding"]?["ledgerAccount"]?.ToString());
        Assert.Contains(result.ReconciliationWarnings.OfType<JsonObject>(), warning => warning["code"]?.ToString() == "invalid_expense_account");
    }

    private sealed class SupplierInvoiceEnrichmentFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SupplierInvoiceEnrichmentFixture(
            SqliteConnection connection,
            VirtualCompanyDbContext db,
            ISupplierInvoiceEnrichmentProvider provider)
        {
            _connection = connection;
            Db = db;
            ApprovalService = new PersistingApprovalRequestService(Db);
            Service = new SupplierInvoiceEnrichmentService(
                Db,
                ApprovalService,
                TimeProvider.System,
                providers: [provider]);
        }

        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid AccountId { get; } = Guid.NewGuid();
        public Guid ActorUserId { get; } = Guid.NewGuid();
        public Guid ConnectionId { get; } = Guid.NewGuid();
        public VirtualCompanyDbContext Db { get; }
        public PersistingApprovalRequestService ApprovalService { get; }
        public SupplierInvoiceEnrichmentService Service { get; }

        public static async Task<SupplierInvoiceEnrichmentFixture> CreateAsync(
            ISupplierInvoiceEnrichmentProvider provider,
            string supplierDefaultAccount = "4010")
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new SupplierInvoiceEnrichmentFixture(connection, db, provider);

            db.Companies.Add(new Company(fixture.CompanyId, "Fortnox-only company"));
            db.FinanceAccounts.Add(new FinanceAccount(
                fixture.AccountId,
                fixture.CompanyId,
                "1930",
                "Bank",
                "asset",
                "SEK",
                0m,
                DateTime.UtcNow));
            db.FinanceCounterparties.Add(new FinanceCounterparty(
                fixture.SupplierId,
                fixture.CompanyId,
                "OpenAI",
                FinanceCounterpartyTypes.Supplier,
                email: "billing@example.com",
                paymentTerms: "14",
                taxId: "559999-1234",
                preferredPaymentMethod: "bank_transfer",
                defaultAccountMapping: supplierDefaultAccount));
            db.FinanceIntegrationConnections.Add(new FinanceIntegrationConnection(
                fixture.ConnectionId,
                fixture.CompanyId,
                FinanceIntegrationProviderKeys.Fortnox,
                FinanceIntegrationConnectionStatuses.Connected,
                fixture.ActorUserId,
                DateTime.UtcNow));
            await db.SaveChangesAsync();

            return fixture;
        }

        public async Task<FinanceBill> AddSupplierBillAsync(
            DateTime? dueUtc = null,
            string settlementStatus = FinanceSettlementStatuses.Unpaid,
            decimal paidAmount = 0m,
            string processingStatus = FinanceDocumentProcessingStatuses.None,
            string? metadataAccountCode = "4010")
        {
            var now = DateTime.UtcNow;
            var bill = new FinanceBill(
                Guid.NewGuid(),
                CompanyId,
                SupplierId,
                "3",
                now.AddDays(-10),
                dueUtc ?? now.AddDays(10),
                1000m,
                "SEK",
                "approved",
                settlementStatus: settlementStatus,
                postingStatus: FinanceDocumentPostingStatuses.Booked,
                dueStatus: dueUtc.HasValue && dueUtc.Value.Date < now.Date ? FinanceDocumentDueStatuses.Overdue : FinanceDocumentDueStatuses.NotDue,
                documentKind: FinanceDocumentKinds.SupplierInvoice,
                processingStatus: processingStatus,
                paidAmount: paidAmount);
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
                now,
                now));
            var invoiceReference = Db.FinanceExternalReferences.Local.Last();
            invoiceReference.ReplaceMetadata(
                new JsonObject
                {
                    ["accountCode"] = metadataAccountCode,
                    ["costCenter"] = "KST-10",
                    ["project"] = "P-20"
                },
                now);
            Db.FinanceExternalReferences.Add(new FinanceExternalReference(
                Guid.NewGuid(),
                CompanyId,
                ConnectionId,
                FinanceIntegrationProviderKeys.Fortnox,
                "supplier",
                SupplierId,
                "1",
                "1",
                now,
                now));
            await Db.SaveChangesAsync();
            return bill;
        }

        public async Task AddPaymentTransactionsAsync(Guid billId, int count)
        {
            for (var i = 0; i < count; i++)
            {
                Db.FinanceTransactions.Add(new FinanceTransaction(
                    Guid.NewGuid(),
                    CompanyId,
                    AccountId,
                    SupplierId,
                    invoiceId: null,
                    billId,
                    DateTime.UtcNow.AddMinutes(i),
                    "supplier_payment",
                    -50m,
                    "SEK",
                    $"Supplier payment {i + 1}",
                    $"PAY-{i + 1}"));
            }

            await Db.SaveChangesAsync();
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

    private sealed class CapturingEnrichmentProvider : ISupplierInvoiceEnrichmentProvider
    {
        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
        public int CallCount { get; private set; }
        public SupplierInvoiceEnrichmentProviderRequest? LastRequest { get; private set; }

        public Task<SupplierInvoiceEnrichmentProviderResult> SyncAsync(
            SupplierInvoiceEnrichmentProviderRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new SupplierInvoiceEnrichmentProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceEnrichmentActionStatuses.Synced,
                "Synced for test.",
                new JsonObject
                {
                    ["accountCode"] = request.AccountCode,
                    ["supplierNumber"] = request.SupplierNumber
                }));
        }
    }

    private sealed class CapturingFortnoxApiClient : IFortnoxApiClient
    {
        public List<string> Paths { get; } = [];
        public Dictionary<string, JsonObject> Payloads { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<FortnoxCompanyInformation> GetCompanyInformationAsync(FortnoxRequestContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxPagedResponse<FortnoxCustomer>> GetCustomersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxPagedResponse<FortnoxSupplier>> GetSuppliersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxPagedResponse<FortnoxInvoice>> GetInvoicesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxPagedResponse<FortnoxInvoicePayment>> GetInvoicePaymentsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxPagedResponse<FortnoxSupplierInvoice>> GetSupplierInvoicesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxPagedResponse<FortnoxSupplierInvoicePayment>> GetSupplierInvoicePaymentsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxPagedResponse<FortnoxVoucher>> GetVouchersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxPagedResponse<FortnoxAccount>> GetAccountsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxPagedResponse<FortnoxArticle>> GetArticlesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxPagedResponse<FortnoxProject>> GetProjectsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TResponse?> GetAsync<TResponse>(FortnoxRequestContext context, string path, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TResponse?> PostAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TResponse?> PostDirectAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TResponse?> PutAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TResponse?> PutDirectAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken)
        {
            Paths.Add(path);
            Payloads[path] = JsonNode.Parse(JsonSerializer.Serialize(payload))?.AsObject() ?? new JsonObject();
            return Task.FromResult((TResponse?)(object)new JsonObject { ["ok"] = true });
        }

        public Task DeleteAsync(FortnoxRequestContext context, string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TResponse?> PostMultipartFileDirectAsync<TResponse>(
            FortnoxRequestContext context,
            string path,
            string formFieldName,
            string fileName,
            string? contentType,
            Stream content,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
