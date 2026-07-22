using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SupplierInvoiceDraftActionServiceTests
{
    [Fact]
    public async Task UpdateDraftAsync_updates_draft_invoice_and_records_audit()
    {
        var provider = new QueueingDraftActionProvider(SupplierInvoiceDraftActionStatuses.Updated);
        await using var fixture = await DraftActionFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(FinanceDocumentPostingStatuses.Draft);

        var result = await fixture.Service.UpdateDraftAsync(
            new UpdateSupplierInvoiceDraftCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceDraftActionStatuses.Updated, result.Status);
        Assert.Equal(fixture.ConnectionId, result.ConnectionId);
        Assert.Equal(1, provider.UpdateCallCount);
        Assert.Equal("3", provider.LastRequest?.SourceBillNumber);
        Assert.Equal("1", provider.LastRequest?.SupplierNumber);
        Assert.Equal(1, await fixture.Db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().CountAsync(x => x.InternalRecordId == result.Id));
    }

    [Fact]
    public async Task UpdateDraftAsync_blocks_booked_invoice()
    {
        var provider = new QueueingDraftActionProvider(SupplierInvoiceDraftActionStatuses.Updated);
        await using var fixture = await DraftActionFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(FinanceDocumentPostingStatuses.Booked);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UpdateDraftAsync(
                new UpdateSupplierInvoiceDraftCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
                CancellationToken.None));

        Assert.Contains("Only draft", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.UpdateCallCount);
    }

    [Fact]
    public async Task BookkeepAsync_books_draft_invoice()
    {
        var provider = new QueueingDraftActionProvider(bookkeepStatus: SupplierInvoiceDraftActionStatuses.Booked);
        await using var fixture = await DraftActionFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(FinanceDocumentPostingStatuses.Draft);

        var result = await fixture.Service.BookkeepAsync(
            new BookkeepSupplierInvoiceDraftCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceDraftActionStatuses.Booked, result.Status);
        Assert.NotNull(result.BookedUtc);
        Assert.Equal(1, provider.BookkeepCallCount);
    }

    [Fact]
    public async Task BookkeepAsync_records_failed_provider_result()
    {
        var provider = new QueueingDraftActionProvider(bookkeepStatus: SupplierInvoiceDraftActionStatuses.Failed);
        await using var fixture = await DraftActionFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(FinanceDocumentPostingStatuses.Draft);

        var result = await fixture.Service.BookkeepAsync(
            new BookkeepSupplierInvoiceDraftCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceDraftActionStatuses.Failed, result.Status);
        Assert.Equal(1, provider.BookkeepCallCount);
        Assert.Equal(1, await fixture.Db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().CountAsync(x => x.Outcome == FinanceIntegrationAuditOutcomes.Failed));
    }

    [Fact]
    public async Task Fortnox_provider_updates_and_bookkeeps_through_provider_neutral_interface()
    {
        var apiClient = new CapturingDraftActionFortnoxApiClient();
        var provider = new FortnoxSupplierInvoiceDraftActionProvider(apiClient);
        var request = new SupplierInvoiceDraftActionProviderRequest(
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
            "INV-12",
            "OCR-12",
            4400m,
            "1012",
            "CC1",
            "P1",
            Guid.NewGuid(),
            Guid.NewGuid());

        var update = await provider.UpdateDraftAsync(request, CancellationToken.None);
        var bookkeep = await provider.BookkeepAsync(request, CancellationToken.None);

        Assert.Equal(SupplierInvoiceDraftActionStatuses.Updated, update.Status);
        Assert.Equal(SupplierInvoiceDraftActionStatuses.Booked, bookkeep.Status);
        Assert.Contains("supplierinvoices/3", apiClient.Paths);
        Assert.Contains("supplierinvoices/3/bookkeep", apiClient.Paths);
        Assert.Equal("INV-12", apiClient.LastPayload?["SupplierInvoice"]?["InvoiceNumber"]?.ToString());
        Assert.Equal("1012", apiClient.LastPayload?["SupplierInvoice"]?["SupplierInvoiceRows"]?[0]?["Account"]?.ToString());
    }

    private sealed class DraftActionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private DraftActionFixture(
            SqliteConnection connection,
            VirtualCompanyDbContext db,
            ISupplierInvoiceDraftActionProvider provider)
        {
            _connection = connection;
            Db = db;
            Service = new SupplierInvoiceDraftActionService(
                Db,
                TimeProvider.System,
                providers: [provider]);
        }

        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid ActorUserId { get; } = Guid.NewGuid();
        public Guid ConnectionId { get; } = Guid.NewGuid();
        public VirtualCompanyDbContext Db { get; }
        public SupplierInvoiceDraftActionService Service { get; }

        public static async Task<DraftActionFixture> CreateAsync(ISupplierInvoiceDraftActionProvider provider)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new DraftActionFixture(connection, db, provider);

            db.Users.Add(new User(fixture.ActorUserId, "draft-actor@example.test", "Draft Actor", "test", fixture.ActorUserId.ToString("N")));
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

        public async Task<FinanceBill> AddSupplierBillAsync(string postingStatus)
        {
            var now = new DateTime(2026, 5, 24, 8, 0, 0, DateTimeKind.Utc);
            var bill = new FinanceBill(
                Guid.NewGuid(),
                CompanyId,
                SupplierId,
                "INV-12",
                now.AddDays(-11),
                now.AddDays(-1),
                22000m,
                "SEK",
                "approved",
                settlementStatus: FinanceSettlementStatuses.Unpaid,
                postingStatus: postingStatus,
                dueStatus: FinanceDocumentDueStatuses.Overdue,
                documentKind: FinanceDocumentKinds.SupplierInvoice,
                providerStatus: "booked=false;balance=22000",
                processingStatus: FinanceDocumentProcessingStatuses.None);
            Db.FinanceBills.Add(bill);
            var invoiceReference = new FinanceExternalReference(
                Guid.NewGuid(),
                CompanyId,
                ConnectionId,
                FinanceIntegrationProviderKeys.Fortnox,
                "supplier_invoice",
                bill.Id,
                "3",
                "3",
                null,
                now);
            invoiceReference.ReplaceMetadata(new JsonObject
            {
                ["vatAmount"] = 4400m,
                ["costCenter"] = "CC1",
                ["project"] = "P1"
            }, now);
            Db.FinanceExternalReferences.Add(invoiceReference);
            await Db.SaveChangesAsync();
            return bill;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class QueueingDraftActionProvider : ISupplierInvoiceDraftActionProvider
    {
        private readonly string _updateStatus;
        private readonly string _bookkeepStatus;

        public QueueingDraftActionProvider(
            string updateStatus = SupplierInvoiceDraftActionStatuses.Updated,
            string bookkeepStatus = SupplierInvoiceDraftActionStatuses.Booked)
        {
            _updateStatus = updateStatus;
            _bookkeepStatus = bookkeepStatus;
        }

        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
        public int UpdateCallCount { get; private set; }
        public int BookkeepCallCount { get; private set; }
        public SupplierInvoiceDraftActionProviderRequest? LastRequest { get; private set; }

        public Task<SupplierInvoiceDraftActionProviderResult> UpdateDraftAsync(
            SupplierInvoiceDraftActionProviderRequest request,
            CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            LastRequest = request;
            return Task.FromResult(CreateResult(request, _updateStatus));
        }

        public Task<SupplierInvoiceDraftActionProviderResult> BookkeepAsync(
            SupplierInvoiceDraftActionProviderRequest request,
            CancellationToken cancellationToken)
        {
            BookkeepCallCount++;
            LastRequest = request;
            return Task.FromResult(CreateResult(request, _bookkeepStatus));
        }

        private static SupplierInvoiceDraftActionProviderResult CreateResult(
            SupplierInvoiceDraftActionProviderRequest request,
            string status) =>
            new(
                FinanceIntegrationProviderKeys.Fortnox,
                request.ConnectionId,
                status,
                status == SupplierInvoiceDraftActionStatuses.Failed ? "Draft action failed." : "Draft action completed.",
                new JsonObject { ["status"] = status });
    }

    private sealed class CapturingDraftActionFortnoxApiClient : IFortnoxApiClient
    {
        public List<string> Paths { get; } = [];
        public JsonObject? LastPayload { get; private set; }

        public Task<TResponse?> PutDirectAsync<TRequest, TResponse>(
            FortnoxRequestContext context,
            string path,
            TRequest payload,
            CancellationToken cancellationToken)
        {
            Paths.Add(path);
            LastPayload = JsonNode.Parse(JsonSerializer.Serialize(payload))?.AsObject();
            var response = new JsonObject
            {
                ["SupplierInvoice"] = new JsonObject
                {
                    ["GivenNumber"] = path.Contains("/bookkeep", StringComparison.OrdinalIgnoreCase) ? "D1" : null
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
}
