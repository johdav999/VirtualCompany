using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SupplierInvoiceSourceDocumentAttachmentServiceTests
{
    [Fact]
    public async Task RequestAttachmentAsync_attaches_source_document_and_records_audit()
    {
        var provider = new QueueingSourceDocumentAttachmentProvider(SupplierInvoiceSourceDocumentAttachmentStatuses.Attached);
        await using var fixture = await SourceDocumentAttachmentFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(withDocument: true);

        var result = await fixture.Service.RequestAttachmentAsync(
            new RequestSupplierInvoiceSourceDocumentAttachmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Test User"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceSourceDocumentAttachmentStatuses.Attached, result.Status);
        Assert.Equal(bill.DocumentId, result.DocumentId);
        Assert.Equal(fixture.ConnectionId, result.ConnectionId);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal("invoice.pdf", provider.LastFileName);
        Assert.Equal("PDF bytes", provider.LastContent);
        Assert.Equal(1, await fixture.Db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().CountAsync(x => x.InternalRecordId == result.Id));
    }

    [Fact]
    public async Task RequestAttachmentAsync_marks_not_available_when_bill_has_no_source_document()
    {
        var provider = new QueueingSourceDocumentAttachmentProvider(SupplierInvoiceSourceDocumentAttachmentStatuses.Attached);
        await using var fixture = await SourceDocumentAttachmentFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(withDocument: false);

        var result = await fixture.Service.RequestAttachmentAsync(
            new RequestSupplierInvoiceSourceDocumentAttachmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Test User"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceSourceDocumentAttachmentStatuses.NotAvailable, result.Status);
        Assert.Equal("No source document available.", result.ResponseSummary);
        Assert.Equal(0, provider.CallCount);
        Assert.Equal(1, await fixture.Db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().CountAsync(x => x.InternalRecordId == result.Id));
    }

    [Fact]
    public async Task RequestAttachmentAsync_prevents_duplicate_attempts_after_successful_attachment()
    {
        var provider = new QueueingSourceDocumentAttachmentProvider(SupplierInvoiceSourceDocumentAttachmentStatuses.Attached);
        await using var fixture = await SourceDocumentAttachmentFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(withDocument: true);
        var command = new RequestSupplierInvoiceSourceDocumentAttachmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Test User");

        var first = await fixture.Service.RequestAttachmentAsync(command, CancellationToken.None);
        var second = await fixture.Service.RequestAttachmentAsync(command, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(SupplierInvoiceSourceDocumentAttachmentStatuses.Attached, second.Status);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(1, await fixture.Db.SupplierInvoiceSourceDocumentAttachments.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task RequestAttachmentAsync_allows_retry_after_failed_attachment()
    {
        var provider = new QueueingSourceDocumentAttachmentProvider(
            SupplierInvoiceSourceDocumentAttachmentStatuses.Failed,
            SupplierInvoiceSourceDocumentAttachmentStatuses.Attached);
        await using var fixture = await SourceDocumentAttachmentFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(withDocument: true);
        var command = new RequestSupplierInvoiceSourceDocumentAttachmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Test User");

        var failed = await fixture.Service.RequestAttachmentAsync(command, CancellationToken.None);
        var retried = await fixture.Service.RequestAttachmentAsync(command, CancellationToken.None);

        Assert.Equal(SupplierInvoiceSourceDocumentAttachmentStatuses.Failed, failed.Status);
        Assert.Equal(SupplierInvoiceSourceDocumentAttachmentStatuses.Attached, retried.Status);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task Fortnox_provider_uploads_archive_file_and_connects_it_to_supplier_invoice()
    {
        var apiClient = new CapturingSourceDocumentFortnoxApiClient();
        var provider = new FortnoxSupplierInvoiceSourceDocumentAttachmentProvider(apiClient);
        await using var fixture = await SourceDocumentAttachmentFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(withDocument: true, billNumber: "3");

        var result = await fixture.Service.RequestAttachmentAsync(
            new RequestSupplierInvoiceSourceDocumentAttachmentCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Test User"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoiceSourceDocumentAttachmentStatuses.Attached, result.Status);
        Assert.Contains("archive", apiClient.Paths);
        Assert.Contains("supplierinvoicefileconnections", apiClient.Paths);
        Assert.Equal("file", apiClient.MultipartFieldName);
        Assert.Equal("invoice.pdf", apiClient.MultipartFileName);
        Assert.Equal("application/pdf", apiClient.MultipartContentType);
        Assert.Equal("3", apiClient.LastPayload?["SupplierInvoiceFileConnection"]?["SupplierInvoiceNumber"]?.ToString());
        Assert.Equal("archive-file-1", apiClient.LastPayload?["SupplierInvoiceFileConnection"]?["FileId"]?.ToString());
    }

    private sealed class SourceDocumentAttachmentFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SourceDocumentAttachmentFixture(
            SqliteConnection connection,
            VirtualCompanyDbContext db,
            InMemoryCompanyDocumentStorage storage,
            ISupplierInvoiceSourceDocumentAttachmentProvider provider)
        {
            _connection = connection;
            Db = db;
            Storage = storage;
            Service = new SupplierInvoiceSourceDocumentAttachmentService(
                Db,
                Storage,
                TimeProvider.System,
                providers: [provider]);
        }

        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid ActorUserId { get; } = Guid.NewGuid();
        public Guid ConnectionId { get; } = Guid.NewGuid();
        public VirtualCompanyDbContext Db { get; }
        public InMemoryCompanyDocumentStorage Storage { get; }
        public SupplierInvoiceSourceDocumentAttachmentService Service { get; }

        public static async Task<SourceDocumentAttachmentFixture> CreateAsync(ISupplierInvoiceSourceDocumentAttachmentProvider provider)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();
            var storage = new InMemoryCompanyDocumentStorage();
            var fixture = new SourceDocumentAttachmentFixture(connection, db, storage, provider);

            db.Users.Add(new User(fixture.ActorUserId, "attachment-actor@example.test", "Attachment Actor", "test", fixture.ActorUserId.ToString("N")));
            db.Companies.Add(new Company(fixture.CompanyId, "Fortnox-only company"));
            db.FinanceCounterparties.Add(new FinanceCounterparty(
                fixture.SupplierId,
                fixture.CompanyId,
                "Nordic IT Solutions AB",
                FinanceCounterpartyTypes.Supplier,
                email: "billing@nordic.se"));
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

        public async Task<FinanceBill> AddSupplierBillAsync(bool withDocument, string? billNumber = null)
        {
            var now = new DateTime(2026, 5, 24, 8, 0, 0, DateTimeKind.Utc);
            Guid? documentId = null;
            if (withDocument)
            {
                documentId = Guid.NewGuid();
                Storage.Seed("docs/invoice.pdf", "PDF bytes");
                Db.CompanyKnowledgeDocuments.Add(new CompanyKnowledgeDocument(
                    documentId.Value,
                    CompanyId,
                    "Invoice PDF",
                    CompanyKnowledgeDocumentType.Reference,
                    "docs/invoice.pdf",
                    null,
                    "invoice.pdf",
                    "application/pdf",
                    ".pdf",
                    9,
                    accessScope: new CompanyKnowledgeDocumentAccessScope(CompanyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility)));
            }

            var bill = new FinanceBill(
                Guid.NewGuid(),
                CompanyId,
                SupplierId,
                billNumber ?? $"BILL-{Guid.NewGuid():N}"[..16],
                now.AddDays(-1),
                now.AddDays(7),
                1000m,
                "SEK",
                "approved",
                documentId: documentId,
                settlementStatus: FinanceSettlementStatuses.Unpaid,
                postingStatus: FinanceDocumentPostingStatuses.Booked,
                dueStatus: FinanceDocumentDueStatuses.NotDue,
                documentKind: FinanceDocumentKinds.SupplierInvoice,
                processingStatus: FinanceDocumentProcessingStatuses.None);
            Db.FinanceBills.Add(bill);
            await Db.SaveChangesAsync();
            return bill;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class QueueingSourceDocumentAttachmentProvider : ISupplierInvoiceSourceDocumentAttachmentProvider
    {
        private readonly Queue<string> _statuses;

        public QueueingSourceDocumentAttachmentProvider(params string[] statuses) =>
            _statuses = new Queue<string>(statuses);

        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
        public int CallCount { get; private set; }
        public string? LastFileName { get; private set; }
        public string? LastContent { get; private set; }

        public async Task<SupplierInvoiceSourceDocumentAttachmentProviderResult> AttachAsync(
            SupplierInvoiceSourceDocumentAttachmentProviderRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastFileName = request.OriginalFileName;
            using var reader = new StreamReader(request.Content, Encoding.UTF8, leaveOpen: true);
            LastContent = await reader.ReadToEndAsync(cancellationToken);

            var status = _statuses.Count == 0
                ? SupplierInvoiceSourceDocumentAttachmentStatuses.Attached
                : _statuses.Dequeue();
            return new SupplierInvoiceSourceDocumentAttachmentProviderResult(
                ProviderKey,
                request.ConnectionId,
                status,
                status == SupplierInvoiceSourceDocumentAttachmentStatuses.Failed
                    ? "Attachment failed for test."
                    : "Source document attached.",
                new JsonObject { ["callCount"] = CallCount });
        }
    }

    private sealed class InMemoryCompanyDocumentStorage : ICompanyDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.OrdinalIgnoreCase);

        public void Seed(string storageKey, string content) =>
            _objects[storageKey] = Encoding.UTF8.GetBytes(content);

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_objects.TryGetValue(storageKey, out var bytes))
            {
                throw new FileNotFoundException("Document not found.", storageKey);
            }

            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public async Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken)
        {
            await using var buffer = new MemoryStream();
            await request.Content.CopyToAsync(buffer, cancellationToken);
            _objects[request.StorageKey] = buffer.ToArray();
            return new DocumentStorageWriteResult(request.StorageKey, null);
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            _objects.Remove(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingSourceDocumentFortnoxApiClient : IFortnoxApiClient
    {
        public List<string> Paths { get; } = [];
        public string? MultipartFieldName { get; private set; }
        public string? MultipartFileName { get; private set; }
        public string? MultipartContentType { get; private set; }
        public JsonObject? LastPayload { get; private set; }

        public Task<TResponse?> PostMultipartFileDirectAsync<TResponse>(
            FortnoxRequestContext context,
            string path,
            string formFieldName,
            string fileName,
            string? contentType,
            Stream content,
            CancellationToken cancellationToken)
        {
            Paths.Add(path);
            MultipartFieldName = formFieldName;
            MultipartFileName = fileName;
            MultipartContentType = contentType;
            var response = new JsonObject
            {
                ["File"] = new JsonObject
                {
                    ["Id"] = "archive-file-1"
                }
            };
            return Task.FromResult((TResponse?)(object)response);
        }

        public Task<TResponse?> PostDirectAsync<TRequest, TResponse>(
            FortnoxRequestContext context,
            string path,
            TRequest payload,
            CancellationToken cancellationToken)
        {
            Paths.Add(path);
            LastPayload = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(payload))?.AsObject();
            var response = new JsonObject
            {
                ["SupplierInvoiceFileConnection"] = new JsonObject
                {
                    ["FileId"] = LastPayload?["SupplierInvoiceFileConnection"]?["FileId"]?.ToString(),
                    ["SupplierInvoiceNumber"] = LastPayload?["SupplierInvoiceFileConnection"]?["SupplierInvoiceNumber"]?.ToString()
                }
            };
            return Task.FromResult((TResponse?)(object)response);
        }

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
        public Task<TResponse?> PutAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TResponse?> PutDirectAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(FortnoxRequestContext context, string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
