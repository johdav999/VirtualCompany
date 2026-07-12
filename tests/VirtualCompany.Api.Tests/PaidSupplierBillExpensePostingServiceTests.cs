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

public sealed class PaidSupplierBillExpensePostingServiceTests
{
    [Fact]
    public async Task PostAsync_rejects_supplier_default_liability_account()
    {
        var provider = new CapturingDraftActionProvider();
        await using var fixture = await PaidExpenseFixture.CreateAsync(provider, supplierDefaultAccount: "2000");
        var bill = await fixture.AddPaidSupplierBillAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.PostAsync(
                new PostPaidSupplierBillExpenseCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
                CancellationToken.None));

        Assert.Contains("expense account", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.BookkeepCallCount);
    }

    [Fact]
    public async Task PostAsync_rejects_duplicate_supplier_payments()
    {
        var provider = new CapturingDraftActionProvider();
        await using var fixture = await PaidExpenseFixture.CreateAsync(provider);
        var bill = await fixture.AddPaidSupplierBillAsync();
        await fixture.AddPaymentTransactionsAsync(bill.Id, 2);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.PostAsync(
                new PostPaidSupplierBillExpenseCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
                CancellationToken.None));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.BookkeepCallCount);
    }

    [Fact]
    public async Task PostAsync_rejects_exported_payment_proposal_mismatch()
    {
        var provider = new CapturingDraftActionProvider();
        await using var fixture = await PaidExpenseFixture.CreateAsync(provider);
        var bill = await fixture.AddPaidSupplierBillAsync();
        await fixture.AddExportedPaymentProposalAsync(bill.Id, 120m);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.PostAsync(
                new PostPaidSupplierBillExpenseCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
                CancellationToken.None));

        Assert.Contains("payment proposal amount", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.BookkeepCallCount);
    }

    [Fact]
    public async Task PostAsync_books_valid_paid_supplier_expense()
    {
        var provider = new CapturingDraftActionProvider();
        await using var fixture = await PaidExpenseFixture.CreateAsync(provider);
        var bill = await fixture.AddPaidSupplierBillAsync();

        var result = await fixture.Service.PostAsync(
            new PostPaidSupplierBillExpenseCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);

        Assert.True(result.Posted);
        Assert.Equal(1, provider.BookkeepCallCount);
        Assert.Equal("6540", provider.LastRequest?.AccountCode);
    }

    private sealed class PaidExpenseFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private PaidExpenseFixture(
            SqliteConnection connection,
            VirtualCompanyDbContext db,
            CapturingDraftActionProvider provider)
        {
            _connection = connection;
            Db = db;
            DraftActionService = new SupplierInvoiceDraftActionService(
                Db,
                TimeProvider.System,
                providers: [provider]);
            Service = new PaidSupplierBillExpensePostingService(Db, DraftActionService);
        }

        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid AccountId { get; } = Guid.NewGuid();
        public Guid ActorUserId { get; } = Guid.NewGuid();
        public Guid ConnectionId { get; } = Guid.NewGuid();
        public VirtualCompanyDbContext Db { get; }
        public SupplierInvoiceDraftActionService DraftActionService { get; }
        public PaidSupplierBillExpensePostingService Service { get; }

        public static async Task<PaidExpenseFixture> CreateAsync(
            CapturingDraftActionProvider provider,
            string supplierDefaultAccount = "6540")
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new PaidExpenseFixture(connection, db, provider);
            var now = DateTime.UtcNow;

            db.Companies.Add(new Company(fixture.CompanyId, "Paid expense test company"));
            db.FinanceAccounts.Add(new FinanceAccount(
                fixture.AccountId,
                fixture.CompanyId,
                "1930",
                "Bank",
                "asset",
                "SEK",
                0m,
                now));
            db.FinanceCounterparties.Add(new FinanceCounterparty(
                fixture.SupplierId,
                fixture.CompanyId,
                "OpenAI",
                FinanceCounterpartyTypes.Supplier,
                defaultAccountMapping: supplierDefaultAccount));
            db.FinanceIntegrationConnections.Add(new FinanceIntegrationConnection(
                fixture.ConnectionId,
                fixture.CompanyId,
                FinanceIntegrationProviderKeys.Fortnox,
                FinanceIntegrationConnectionStatuses.Connected,
                fixture.ActorUserId,
                now));
            db.FinanceExternalReferences.Add(new FinanceExternalReference(
                Guid.NewGuid(),
                fixture.CompanyId,
                fixture.ConnectionId,
                FinanceIntegrationProviderKeys.Fortnox,
                "supplier",
                fixture.SupplierId,
                "1",
                "1",
                now,
                now));
            await db.SaveChangesAsync();

            return fixture;
        }

        public async Task<FinanceBill> AddPaidSupplierBillAsync(decimal paidAmount = 1000m)
        {
            var now = DateTime.UtcNow;
            var bill = new FinanceBill(
                Guid.NewGuid(),
                CompanyId,
                SupplierId,
                "3",
                now.AddDays(-10),
                now.AddDays(-1),
                1000m,
                "SEK",
                "paid",
                settlementStatus: FinanceSettlementStatuses.Paid,
                postingStatus: FinanceDocumentPostingStatuses.Draft,
                dueStatus: FinanceDocumentDueStatuses.NotDue,
                documentKind: FinanceDocumentKinds.SupplierInvoice,
                providerStatus: "booked=false;balance=0",
                processingStatus: FinanceDocumentProcessingStatuses.Synced,
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
                    -500m,
                    "SEK",
                    $"Supplier payment {i + 1}",
                    $"PAY-{i + 1}"));
            }

            await Db.SaveChangesAsync();
        }

        public async Task AddExportedPaymentProposalAsync(Guid billId, decimal amount)
        {
            var now = DateTime.UtcNow;
            var proposal = new SupplierInvoicePaymentProposal(
                Guid.NewGuid(),
                CompanyId,
                billId,
                SupplierId,
                "OpenAI",
                amount,
                "SEK",
                now.AddDays(-1),
                "3",
                ActorUserId,
                now);
            proposal.MarkReadyForPayment(ActorUserId, now, "Approved for test.");
            proposal.MarkPaymentExport(
                SupplierInvoicePaymentExportModes.RegisterPayment,
                SupplierInvoicePaymentExportStatuses.Exported,
                FinanceIntegrationProviderKeys.Fortnox,
                ConnectionId,
                ActorUserId,
                "Payment booked in Fortnox.",
                new JsonObject(),
                now);
            Db.SupplierInvoicePaymentProposals.Add(proposal);
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class CapturingDraftActionProvider : ISupplierInvoiceDraftActionProvider
    {
        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
        public int BookkeepCallCount { get; private set; }
        public SupplierInvoiceDraftActionProviderRequest? LastRequest { get; private set; }

        public Task<SupplierInvoiceDraftActionProviderResult> UpdateDraftAsync(
            SupplierInvoiceDraftActionProviderRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SupplierInvoiceDraftActionProviderResult> BookkeepAsync(
            SupplierInvoiceDraftActionProviderRequest request,
            CancellationToken cancellationToken)
        {
            BookkeepCallCount++;
            LastRequest = request;
            return Task.FromResult(new SupplierInvoiceDraftActionProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceDraftActionStatuses.Booked,
                "Booked for test.",
                new JsonObject()));
        }
    }
}
