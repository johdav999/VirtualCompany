using System.Text.Json.Nodes;
using System.Text.Json;
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

public sealed class SupplierInvoicePaymentProposalServiceTests
{
    [Fact]
    public async Task RequestPaymentProposalAsync_creates_proposal_task_and_approval()
    {
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync();
        var bill = await fixture.AddSupplierBillAsync(amount: 2410m, settlementStatus: FinanceSettlementStatuses.Unpaid);

        var result = await fixture.Service.RequestPaymentProposalAsync(
            new RequestSupplierInvoicePaymentProposalCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
            CancellationToken.None);

        Assert.Equal(bill.Id, result.BillId);
        Assert.Equal(SupplierInvoicePaymentProposalStatuses.AwaitingApproval, result.Status);
        Assert.Equal(2410m, result.Amount);
        Assert.Equal("SEK", result.Currency);
        Assert.NotNull(result.TaskId);
        Assert.NotNull(result.ApprovalRequestId);

        var task = await fixture.Db.WorkTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == result.TaskId);
        Assert.Equal("finance.supplier_invoice_payment_proposal", task.Type);
        Assert.Equal(WorkTaskStatus.AwaitingApproval, task.Status);
        Assert.True(task.InputPayload.TryGetValue("doesNotInitiatePayment", out var paymentFlag) && paymentFlag?.GetValue<bool>() == true);
    }

    [Fact]
    public async Task RequestPaymentProposalAsync_prevents_duplicate_proposals_for_same_bill()
    {
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync();
        var bill = await fixture.AddSupplierBillAsync(amount: 188m, settlementStatus: FinanceSettlementStatuses.Unpaid);
        var command = new RequestSupplierInvoicePaymentProposalCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId);

        var first = await fixture.Service.RequestPaymentProposalAsync(command, CancellationToken.None);
        var second = await fixture.Service.RequestPaymentProposalAsync(command, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await fixture.Db.SupplierInvoicePaymentProposals.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await fixture.Db.WorkTasks.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task RequestPaymentProposalAsync_allows_follow_up_after_exported_proposal_when_balance_remains()
    {
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync();
        var bill = await fixture.AddSupplierBillAsync(
            amount: 22000m,
            settlementStatus: FinanceSettlementStatuses.PartiallyPaid,
            paidAmount: 12000m);
        var command = new RequestSupplierInvoicePaymentProposalCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId);
        var first = await fixture.Service.RequestPaymentProposalAsync(command, CancellationToken.None);
        var firstProposal = await fixture.Db.SupplierInvoicePaymentProposals
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == first.Id);
        firstProposal.MarkReadyForPayment(fixture.ActorUserId, DateTime.UtcNow, "Approved for test.");
        firstProposal.MarkPaymentExport(
            SupplierInvoicePaymentExportModes.RegisterPayment,
            SupplierInvoicePaymentExportStatuses.Exported,
            FinanceIntegrationProviderKeys.Fortnox,
            fixture.ConnectionId,
            fixture.ActorUserId,
            "Fortnox booked supplier invoice payment 77.",
            new JsonObject { ["booked"] = true },
            DateTime.UtcNow);
        await fixture.Db.SaveChangesAsync();

        var second = await fixture.Service.RequestPaymentProposalAsync(command, CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await fixture.Db.SupplierInvoicePaymentProposals.IgnoreQueryFilters().CountAsync());
        Assert.Equal(10000m, second.Amount);
        Assert.Equal(SupplierInvoicePaymentProposalStatuses.AwaitingApproval, second.Status);
    }

    [Fact]
    public async Task RequestPaymentProposalAsync_uses_remaining_amount_for_partially_paid_bill()
    {
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync();
        var bill = await fixture.AddSupplierBillAsync(
            amount: 22000m,
            settlementStatus: FinanceSettlementStatuses.PartiallyPaid,
            paidAmount: 12000m);

        var result = await fixture.Service.RequestPaymentProposalAsync(
            new RequestSupplierInvoicePaymentProposalCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
            CancellationToken.None);

        Assert.Equal(10000m, result.Amount);
        Assert.Equal(SupplierInvoicePaymentProposalStatuses.AwaitingApproval, result.Status);
    }

    [Fact]
    public async Task RequestPaymentProposalAsync_rejects_fully_allocated_partially_paid_bill()
    {
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync();
        var bill = await fixture.AddSupplierBillAsync(
            amount: 22000m,
            settlementStatus: FinanceSettlementStatuses.PartiallyPaid,
            paidAmount: 22000m);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RequestPaymentProposalAsync(
                new RequestSupplierInvoicePaymentProposalCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
                CancellationToken.None));

        Assert.Equal("Supplier bills with no remaining amount cannot receive payment proposals.", exception.Message);
    }

    [Fact]
    public void MarkReadyForPayment_records_approval_state_and_audit()
    {
        var now = new DateTime(2026, 5, 22, 8, 0, 0, DateTimeKind.Utc);
        var actorId = Guid.NewGuid();
        var proposal = CreateProposal(now);

        proposal.AttachApprovalWorkflow(Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(1));
        proposal.MarkReadyForPayment(actorId, now.AddMinutes(2), "Approved by finance.");

        Assert.Equal(SupplierInvoicePaymentProposalStatuses.ReadyForPayment, proposal.Status);
        Assert.Equal(actorId, proposal.DecidedByUserId);
        Assert.Equal(now.AddMinutes(2), proposal.DecidedUtc);
        Assert.True(proposal.AuditTrail.TryGetValue("events", out var events) && events is JsonArray { Count: >= 3 });
    }

    [Fact]
    public void MarkRejected_records_rejection_state_and_audit()
    {
        var now = new DateTime(2026, 5, 22, 8, 0, 0, DateTimeKind.Utc);
        var actorId = Guid.NewGuid();
        var proposal = CreateProposal(now);

        proposal.AttachApprovalWorkflow(Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(1));
        proposal.MarkRejected(actorId, now.AddMinutes(2), "Wrong due date.");

        Assert.Equal(SupplierInvoicePaymentProposalStatuses.Rejected, proposal.Status);
        Assert.Equal(actorId, proposal.DecidedByUserId);
        Assert.Equal(now.AddMinutes(2), proposal.DecidedUtc);
        Assert.True(proposal.AuditTrail.TryGetValue("events", out var events) && events is JsonArray { Count: >= 3 });
    }

    [Fact]
    public async Task RequestPaymentProposalAsync_works_without_simulated_finance_seed_state()
    {
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync();
        var bill = await fixture.AddSupplierBillAsync(
            amount: 999m,
            settlementStatus: FinanceSettlementStatuses.Unpaid,
            processingStatus: FinanceDocumentProcessingStatuses.None);

        var result = await fixture.Service.RequestPaymentProposalAsync(
            new RequestSupplierInvoicePaymentProposalCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
            CancellationToken.None);

        Assert.Equal(SupplierInvoicePaymentProposalStatuses.AwaitingApproval, result.Status);
        Assert.NotNull(result.ApprovalRequestId);
    }

    [Fact]
    public async Task ExportPaymentInstructionAsync_registers_supplier_invoice_payment_in_fortnox()
    {
        var apiClient = new CapturingFortnoxApiClient();
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync(new FortnoxSupplierInvoicePaymentExportProvider(apiClient));
        var bill = await fixture.AddSupplierBillAsync(amount: 22000m, settlementStatus: FinanceSettlementStatuses.PartiallyPaid, paidAmount: 12000m);
        await fixture.CreateReadyProposalAsync(bill.Id);

        var result = await fixture.Service.ExportPaymentInstructionAsync(
            new ExportSupplierInvoicePaymentInstructionCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
            CancellationToken.None);

        Assert.Equal(SupplierInvoicePaymentExportStatuses.Exported, result.ExportStatus);
        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, result.ExportProviderKey);
        Assert.Equal(fixture.ConnectionId, result.ExportConnectionId);
        Assert.Equal(10000m, result.Amount);
        Assert.Contains("supplierinvoicepayments", apiClient.Paths);
        Assert.Contains("supplierinvoicepayments/77/bookkeep", apiClient.Paths);
        Assert.Equal(bill.BillNumber, apiClient.LastPayload?["SupplierInvoicePayment"]?["InvoiceNumber"]?.ToString());
        Assert.Equal(10000m, apiClient.LastPayload?["SupplierInvoicePayment"]?["Amount"]?.GetValue<decimal>());
        Assert.Contains("registered and booked supplier invoice payment", result.ExportResponseSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No bank payment", result.ExportResponseSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportPaymentInstructionAsync_blocks_pending_proposal()
    {
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync();
        var bill = await fixture.AddSupplierBillAsync(amount: 1000m, settlementStatus: FinanceSettlementStatuses.Unpaid);
        await fixture.Service.RequestPaymentProposalAsync(
            new RequestSupplierInvoicePaymentProposalCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ExportPaymentInstructionAsync(
                new ExportSupplierInvoicePaymentInstructionCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
                CancellationToken.None));

        Assert.Equal("Only approved payment proposals can be exported.", exception.Message);
    }

    [Fact]
    public async Task ExportPaymentInstructionAsync_blocks_rejected_proposal()
    {
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync();
        var bill = await fixture.AddSupplierBillAsync(amount: 1000m, settlementStatus: FinanceSettlementStatuses.Unpaid);
        var proposal = await fixture.CreateReadyProposalAsync(bill.Id);
        proposal.MarkRejected(fixture.ActorUserId, DateTime.UtcNow, "Rejected for test.");
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ExportPaymentInstructionAsync(
                new ExportSupplierInvoicePaymentInstructionCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
                CancellationToken.None));

        Assert.Equal("Only approved payment proposals can be exported.", exception.Message);
    }

    [Fact]
    public async Task ExportPaymentInstructionAsync_prevents_duplicate_exports()
    {
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync();
        var bill = await fixture.AddSupplierBillAsync(amount: 1000m, settlementStatus: FinanceSettlementStatuses.Unpaid);
        await fixture.CreateReadyProposalAsync(bill.Id);
        var command = new ExportSupplierInvoicePaymentInstructionCommand(
            fixture.CompanyId,
            bill.Id,
            fixture.ActorUserId,
            "Alice Admin",
            SupplierInvoicePaymentExportModes.ManualExport);

        var first = await fixture.Service.ExportPaymentInstructionAsync(command, CancellationToken.None);
        var second = await fixture.Service.ExportPaymentInstructionAsync(command, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(SupplierInvoicePaymentExportStatuses.ExportRequested, second.ExportStatus);
        Assert.Equal(1, await fixture.Db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().CountAsync(x => x.InternalRecordId == first.Id));
    }

    [Fact]
    public async Task ExportPaymentInstructionAsync_blocks_fully_paid_bill()
    {
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync();
        var bill = await fixture.AddSupplierBillAsync(amount: 1000m, settlementStatus: FinanceSettlementStatuses.Unpaid);
        var proposal = await fixture.CreateReadyProposalAsync(bill.Id);
        bill.ApplySettlementStatus(FinanceSettlementStatuses.Paid);
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ExportPaymentInstructionAsync(
                new ExportSupplierInvoicePaymentInstructionCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin"),
                CancellationToken.None));

        Assert.Equal("Paid, credited, or cancelled supplier bills cannot be exported for payment.", exception.Message);
        Assert.Equal(SupplierInvoicePaymentExportStatuses.NotExported, proposal.ExportStatus);
    }

    [Fact]
    public async Task ExportPaymentInstructionAsync_allows_retry_after_failed_export()
    {
        var provider = new QueueingPaymentExportProvider(
            SupplierInvoicePaymentExportStatuses.Failed,
            SupplierInvoicePaymentExportStatuses.ExportRequested);
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(amount: 1000m, settlementStatus: FinanceSettlementStatuses.Unpaid);
        await fixture.CreateReadyProposalAsync(bill.Id);
        var command = new ExportSupplierInvoicePaymentInstructionCommand(fixture.CompanyId, bill.Id, fixture.ActorUserId, "Alice Admin");

        var failed = await fixture.Service.ExportPaymentInstructionAsync(command, CancellationToken.None);
        var retried = await fixture.Service.ExportPaymentInstructionAsync(command, CancellationToken.None);

        Assert.Equal(SupplierInvoicePaymentExportStatuses.Failed, failed.ExportStatus);
        Assert.Equal(SupplierInvoicePaymentExportStatuses.ExportRequested, retried.ExportStatus);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task ExportPaymentInstructionAsync_prepares_payment_file_without_bank_payment()
    {
        var apiClient = new CapturingFortnoxApiClient();
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync(new FortnoxSupplierInvoicePaymentExportProvider(apiClient));
        var bill = await fixture.AddSupplierBillAsync(amount: 12000m, settlementStatus: FinanceSettlementStatuses.Unpaid);
        await fixture.CreateReadyProposalAsync(bill.Id);

        var result = await fixture.Service.ExportPaymentInstructionAsync(
            new ExportSupplierInvoicePaymentInstructionCommand(
                fixture.CompanyId,
                bill.Id,
                fixture.ActorUserId,
                "Alice Admin",
                SupplierInvoicePaymentExportModes.PreparePaymentFile),
            CancellationToken.None);

        Assert.Equal(SupplierInvoicePaymentExportModes.PreparePaymentFile, result.ExportMode);
        Assert.Equal(SupplierInvoicePaymentExportStatuses.ExportRequested, result.ExportStatus);
        Assert.Empty(apiClient.Paths);

        var proposal = await fixture.Db.SupplierInvoicePaymentProposals
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == result.Id);
        Assert.True(proposal.ExportProviderMetadata.TryGetPropertyValue("manualPaymentFileRequired", out var manualNode) &&
            manualNode?.GetValue<bool>() == true);
        Assert.True(proposal.ExportProviderMetadata.TryGetPropertyValue("doesNotInitiateBankPayment", out var bankNode) &&
            bankNode?.GetValue<bool>() == true);
    }

    [Fact]
    public async Task ExportPaymentInstructionAsync_can_replace_manual_payment_file_request_with_fortnox_registration()
    {
        var apiClient = new CapturingFortnoxApiClient();
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync(new FortnoxSupplierInvoicePaymentExportProvider(apiClient));
        var bill = await fixture.AddSupplierBillAsync(amount: 12000m, settlementStatus: FinanceSettlementStatuses.Unpaid);
        await fixture.CreateReadyProposalAsync(bill.Id);

        await fixture.Service.ExportPaymentInstructionAsync(
            new ExportSupplierInvoicePaymentInstructionCommand(
                fixture.CompanyId,
                bill.Id,
                fixture.ActorUserId,
                "Alice Admin",
                SupplierInvoicePaymentExportModes.PreparePaymentFile),
            CancellationToken.None);

        var result = await fixture.Service.ExportPaymentInstructionAsync(
            new ExportSupplierInvoicePaymentInstructionCommand(
                fixture.CompanyId,
                bill.Id,
                fixture.ActorUserId,
                "Alice Admin",
                SupplierInvoicePaymentExportModes.RegisterPayment),
            CancellationToken.None);

        Assert.Equal(SupplierInvoicePaymentExportModes.RegisterPayment, result.ExportMode);
        Assert.Equal(SupplierInvoicePaymentExportStatuses.Exported, result.ExportStatus);
        Assert.Contains("supplierinvoicepayments", apiClient.Paths);
        Assert.Contains("supplierinvoicepayments/77/bookkeep", apiClient.Paths);
    }

    [Fact]
    public async Task ExportPaymentInstructionAsync_prevents_duplicate_payment_file_preparation()
    {
        var provider = new QueueingPaymentExportProvider(SupplierInvoicePaymentExportStatuses.ExportRequested);
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(amount: 12000m, settlementStatus: FinanceSettlementStatuses.Unpaid);
        await fixture.CreateReadyProposalAsync(bill.Id);
        var command = new ExportSupplierInvoicePaymentInstructionCommand(
            fixture.CompanyId,
            bill.Id,
            fixture.ActorUserId,
            "Alice Admin",
            SupplierInvoicePaymentExportModes.PreparePaymentFile);

        var first = await fixture.Service.ExportPaymentInstructionAsync(command, CancellationToken.None);
        var second = await fixture.Service.ExportPaymentInstructionAsync(command, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(SupplierInvoicePaymentExportModes.PreparePaymentFile, second.ExportMode);
        Assert.Equal(SupplierInvoicePaymentExportStatuses.ExportRequested, second.ExportStatus);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task ExportPaymentInstructionAsync_allows_retry_after_failed_payment_file_preparation()
    {
        var provider = new QueueingPaymentExportProvider(
            SupplierInvoicePaymentExportStatuses.Failed,
            SupplierInvoicePaymentExportStatuses.ExportRequested);
        await using var fixture = await SupplierPaymentProposalFixture.CreateAsync(provider);
        var bill = await fixture.AddSupplierBillAsync(amount: 12000m, settlementStatus: FinanceSettlementStatuses.Unpaid);
        await fixture.CreateReadyProposalAsync(bill.Id);
        var command = new ExportSupplierInvoicePaymentInstructionCommand(
            fixture.CompanyId,
            bill.Id,
            fixture.ActorUserId,
            "Alice Admin",
            SupplierInvoicePaymentExportModes.PreparePaymentFile);

        var failed = await fixture.Service.ExportPaymentInstructionAsync(command, CancellationToken.None);
        var retried = await fixture.Service.ExportPaymentInstructionAsync(command, CancellationToken.None);

        Assert.Equal(SupplierInvoicePaymentExportModes.PreparePaymentFile, failed.ExportMode);
        Assert.Equal(SupplierInvoicePaymentExportStatuses.Failed, failed.ExportStatus);
        Assert.Equal(SupplierInvoicePaymentExportModes.PreparePaymentFile, retried.ExportMode);
        Assert.Equal(SupplierInvoicePaymentExportStatuses.ExportRequested, retried.ExportStatus);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task Fortnox_provider_prepare_payment_file_falls_back_without_bank_payment()
    {
        var provider = new FortnoxSupplierInvoicePaymentExportProvider(new CapturingFortnoxApiClient());
        var result = await provider.ExportAsync(
            new SupplierInvoicePaymentExportProviderRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "3",
                Guid.NewGuid(),
                "OpenAI",
                12000m,
                "SEK",
                new DateTime(2026, 5, 23, 0, 0, 0, DateTimeKind.Utc),
                "3",
                Guid.NewGuid(),
                Guid.NewGuid(),
                SupplierInvoicePaymentExportModes.PreparePaymentFile),
            CancellationToken.None);

        Assert.Equal(SupplierInvoicePaymentExportModes.PreparePaymentFile, result.ExportMode);
        Assert.Equal(SupplierInvoicePaymentExportStatuses.ExportRequested, result.ExportStatus);
        Assert.True(result.ProviderMetadata.TryGetPropertyValue("manualPaymentFileRequired", out var manualNode) &&
            manualNode?.GetValue<bool>() == true);
        Assert.True(result.ProviderMetadata.TryGetPropertyValue("doesNotInitiateBankPayment", out var bankNode) &&
            bankNode?.GetValue<bool>() == true);
    }

    private static SupplierInvoicePaymentProposal CreateProposal(DateTime now) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Nordic IT Solutions AB",
            188m,
            "SEK",
            now.AddDays(10),
            "BILL-1",
            Guid.NewGuid(),
            now);

    private sealed class SupplierPaymentProposalFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SupplierPaymentProposalFixture(SqliteConnection connection, VirtualCompanyDbContext db, ISupplierInvoicePaymentExportProvider exportProvider)
        {
            _connection = connection;
            Db = db;
            ApprovalService = new PersistingApprovalRequestService(Db);
            Service = new SupplierInvoicePaymentProposalService(
                Db,
                ApprovalService,
                TimeProvider.System,
                exportProviders: [exportProvider]);
        }

        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid ActorUserId { get; } = Guid.NewGuid();
        public Guid ConnectionId { get; } = Guid.NewGuid();
        public VirtualCompanyDbContext Db { get; }
        public PersistingApprovalRequestService ApprovalService { get; }
        public SupplierInvoicePaymentProposalService Service { get; }

        public static async Task<SupplierPaymentProposalFixture> CreateAsync(ISupplierInvoicePaymentExportProvider? exportProvider = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new SupplierPaymentProposalFixture(connection, db, exportProvider ?? new FortnoxSupplierInvoicePaymentExportProvider());

            db.Users.Add(new User(
                fixture.ActorUserId,
                "finance-actor@example.test",
                "Finance Actor",
                "test",
                fixture.ActorUserId.ToString("N")));
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

        public async Task<SupplierInvoicePaymentProposal> CreateReadyProposalAsync(Guid billId)
        {
            var result = await Service.RequestPaymentProposalAsync(
                new RequestSupplierInvoicePaymentProposalCommand(CompanyId, billId, ActorUserId),
                CancellationToken.None);
            var proposal = await Db.SupplierInvoicePaymentProposals
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == result.Id);
            proposal.MarkReadyForPayment(ActorUserId, DateTime.UtcNow, "Approved for test.");
            await Db.SaveChangesAsync();
            return proposal;
        }

        public async Task<FinanceBill> AddSupplierBillAsync(
            decimal amount,
            string settlementStatus,
            decimal paidAmount = 0m,
            string processingStatus = FinanceDocumentProcessingStatuses.None)
        {
            var now = new DateTime(2026, 5, 22, 8, 0, 0, DateTimeKind.Utc);
            var bill = new FinanceBill(
                Guid.NewGuid(),
                CompanyId,
                SupplierId,
                $"BILL-{Guid.NewGuid():N}"[..16],
                now.AddDays(-1),
                now.AddDays(8),
                amount,
                "SEK",
                "approved",
                settlementStatus: settlementStatus,
                postingStatus: FinanceDocumentPostingStatuses.Booked,
                dueStatus: FinanceDocumentDueStatuses.NotDue,
                documentKind: FinanceDocumentKinds.SupplierInvoice,
                processingStatus: processingStatus,
                paidAmount: paidAmount);
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

    private sealed class QueueingPaymentExportProvider : ISupplierInvoicePaymentExportProvider
    {
        private readonly Queue<string> _statuses;

        public QueueingPaymentExportProvider(params string[] statuses) =>
            _statuses = new Queue<string>(statuses);

        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
        public int CallCount { get; private set; }

        public Task<SupplierInvoicePaymentExportProviderResult> ExportAsync(
            SupplierInvoicePaymentExportProviderRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var status = _statuses.Count == 0 ? SupplierInvoicePaymentExportStatuses.ExportRequested : _statuses.Dequeue();
            return Task.FromResult(new SupplierInvoicePaymentExportProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoicePaymentExportModes.Normalize(request.ExportMode),
                status,
                status == SupplierInvoicePaymentExportStatuses.Failed ? "Temporary export failure." : "Manual payment/export required.",
                new JsonObject
                {
                    ["callCount"] = CallCount,
                    ["exportMode"] = SupplierInvoicePaymentExportModes.Normalize(request.ExportMode),
                    ["doesNotInitiateBankPayment"] = true
                }));
        }
    }

    private sealed class CapturingFortnoxApiClient : IFortnoxApiClient
    {
        public string? LastPath { get; private set; }
        public JsonObject? LastPayload { get; private set; }
        public List<string> Paths { get; } = [];

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

        public Task<TResponse?> PostDirectAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken)
        {
            LastPath = path;
            Paths.Add(path);
            var parsedPayload = JsonNode.Parse(JsonSerializer.Serialize(payload))?.AsObject();
            if (path == "supplierinvoicepayments")
            {
                LastPayload = parsedPayload;
            }

            if (path.EndsWith("/bookkeep", StringComparison.OrdinalIgnoreCase))
            {
                var bookkeepResponse = new JsonObject
                {
                    ["SupplierInvoicePayment"] = new JsonObject
                    {
                        ["Number"] = 77,
                        ["InvoiceNumber"] = LastPayload?["SupplierInvoicePayment"]?["InvoiceNumber"]?.ToString(),
                        ["Amount"] = LastPayload?["SupplierInvoicePayment"]?["Amount"]?.GetValue<decimal>() ?? 0m,
                        ["AmountCurrency"] = LastPayload?["SupplierInvoicePayment"]?["AmountCurrency"]?.GetValue<decimal>() ?? 0m,
                        ["Booked"] = true,
                        ["PaymentDate"] = LastPayload?["SupplierInvoicePayment"]?["PaymentDate"]?.ToString(),
                        ["Source"] = "manual"
                    }
                };

                return Task.FromResult((TResponse?)(object)bookkeepResponse);
            }

            var response = new JsonObject
            {
                ["SupplierInvoicePayment"] = new JsonObject
                {
                    ["Number"] = 77,
                    ["InvoiceNumber"] = LastPayload?["SupplierInvoicePayment"]?["InvoiceNumber"]?.ToString(),
                    ["Amount"] = LastPayload?["SupplierInvoicePayment"]?["Amount"]?.GetValue<decimal>() ?? 0m,
                    ["AmountCurrency"] = LastPayload?["SupplierInvoicePayment"]?["AmountCurrency"]?.GetValue<decimal>() ?? 0m,
                    ["Currency"] = LastPayload?["SupplierInvoicePayment"]?["Currency"]?.ToString(),
                    ["Booked"] = false,
                    ["PaymentDate"] = LastPayload?["SupplierInvoicePayment"]?["PaymentDate"]?.ToString(),
                    ["Source"] = "manual"
                }
            };

            return Task.FromResult((TResponse?)(object)response);
        }

        public Task<TResponse?> PutAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TResponse?> PutDirectAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken)
        {
            LastPath = path;
            Paths.Add(path);

            var bookkeepResponse = new JsonObject
            {
                ["SupplierInvoicePayment"] = new JsonObject
                {
                    ["Number"] = 77,
                    ["InvoiceNumber"] = LastPayload?["SupplierInvoicePayment"]?["InvoiceNumber"]?.ToString(),
                    ["Amount"] = LastPayload?["SupplierInvoicePayment"]?["Amount"]?.GetValue<decimal>() ?? 0m,
                    ["AmountCurrency"] = LastPayload?["SupplierInvoicePayment"]?["AmountCurrency"]?.GetValue<decimal>() ?? 0m,
                    ["Booked"] = true,
                    ["PaymentDate"] = LastPayload?["SupplierInvoicePayment"]?["PaymentDate"]?.ToString(),
                    ["Source"] = "manual"
                }
            };

            return Task.FromResult((TResponse?)(object)bookkeepResponse);
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
                approval.DecisionSummary,
                string.Empty,
                string.Empty,
                [],
                null,
                approval.CreatedUtc);
        }
    }
}
