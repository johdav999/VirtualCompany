using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ReconciliationPersistenceTests
{
    [Fact]
    public async Task Create_and_query_open_suggestions_supports_payment_to_bank_invoice_to_payment_and_bill_to_payment()
    {
        var companyId = Guid.NewGuid();
        var creatorUserId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        await SeedUsersAsync(dbContext, (creatorUserId, "creator@reconciliation.example", "Creator"));
        var seed = await SeedCompanyFinanceScenarioAsync(dbContext, companyId);
        await SeedMembershipsAsync(dbContext, companyId, creatorUserId);

        var service = new CompanyReconciliationSuggestionService(dbContext, accessor);
        await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, ReconciliationRecordTypes.BankTransaction, seed.BankTransactionId, creatorUserId),
            CancellationToken.None);
        await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Invoice, seed.InvoiceId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, creatorUserId),
            CancellationToken.None);
        await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Bill, seed.BillId, ReconciliationRecordTypes.Payment, seed.OutgoingPaymentId, creatorUserId),
            CancellationToken.None);

        var openSuggestions = await service.GetOpenSuggestionsAsync(
            new GetOpenReconciliationSuggestionsQuery(companyId, Limit: 10),
            CancellationToken.None);

        var paymentToBank = Assert.Single(openSuggestions.Where(x =>
            x.SourceRecordType == ReconciliationRecordTypes.Payment &&
            x.TargetRecordType == ReconciliationRecordTypes.BankTransaction));
        var invoiceToPayment = Assert.Single(openSuggestions.Where(x =>
            x.SourceRecordType == ReconciliationRecordTypes.Invoice &&
            x.TargetRecordType == ReconciliationRecordTypes.Payment));
        var billToPayment = Assert.Single(openSuggestions.Where(x =>
            x.SourceRecordType == ReconciliationRecordTypes.Bill &&
            x.TargetRecordType == ReconciliationRecordTypes.Payment));

        Assert.Equal(3, openSuggestions.Count);
        Assert.Equal(ReconciliationSuggestionStatuses.Open, paymentToBank.Status);
        Assert.Equal(ReconciliationSuggestionStatuses.Open, invoiceToPayment.Status);
        Assert.Equal(ReconciliationSuggestionStatuses.Open, billToPayment.Status);
        Assert.Equal(creatorUserId, paymentToBank.CreatedByUserId);
        Assert.Equal(creatorUserId, paymentToBank.UpdatedByUserId);
        Assert.NotEmpty(paymentToBank.RuleBreakdown);
        Assert.Equal(companyId, invoiceToPayment.CompanyId);
        Assert.NotEmpty(invoiceToPayment.RuleBreakdown);
        Assert.Equal(companyId, billToPayment.CompanyId);
        Assert.NotEmpty(billToPayment.RuleBreakdown);
    }

    [Fact]
    public async Task Accept_payment_to_bank_suggestion_creates_result_and_supersedes_competing_suggestions()
    {
        var companyId = Guid.NewGuid();
        var creatorUserId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        await SeedUsersAsync(
            dbContext,
            (creatorUserId, "creator@payment-bank.example", "Creator"),
            (approverUserId, "approver@payment-bank.example", "Approver"));
        var seed = await SeedCompanyFinanceScenarioAsync(dbContext, companyId);
        await SeedMembershipsAsync(dbContext, companyId, creatorUserId, approverUserId);

        var service = new CompanyReconciliationSuggestionService(dbContext, accessor);
        var first = await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, ReconciliationRecordTypes.BankTransaction, seed.BankTransactionId, creatorUserId, ReconciliationMatchTypes.RuleBased, 0.98m),
            CancellationToken.None);
        await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.BankTransaction, seed.BankTransactionId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, creatorUserId, ReconciliationMatchTypes.Near, 0.86m),
            CancellationToken.None);

        var accepted = await service.AcceptSuggestionAsync(
            new AcceptReconciliationSuggestionCommand(companyId, first.Id, approverUserId),
            CancellationToken.None);

        Assert.Equal(first.Id, accepted.Suggestion.Id);
        Assert.Equal(ReconciliationSuggestionStatuses.Accepted, accepted.Suggestion.Status);
        Assert.Equal(1, accepted.SupersededSuggestionCount);
        Assert.Equal(first.Id, accepted.Result.AcceptedSuggestionId);
        Assert.Equal(ReconciliationRecordTypes.Payment, accepted.Result.SourceRecordType);
        Assert.Equal(ReconciliationRecordTypes.BankTransaction, accepted.Result.TargetRecordType);

        var persistedSuggestions = await dbContext.ReconciliationSuggestionRecords
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();
        var acceptedSuggestion = Assert.Single(persistedSuggestions.Where(x => x.Id == first.Id));
        var supersededSuggestion = Assert.Single(persistedSuggestions.Where(x => x.Id != first.Id));
        var result = await dbContext.ReconciliationResultRecords
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId);
        var paymentLink = await dbContext.BankTransactionPaymentLinks
            .IgnoreQueryFilters()
            .SingleAsync(x =>
                x.CompanyId == companyId &&
                x.BankTransactionId == seed.BankTransactionId &&
                x.PaymentId == seed.IncomingPaymentId);
        var bankTransaction = await dbContext.BankTransactions
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == seed.BankTransactionId);

        Assert.Equal(ReconciliationSuggestionStatuses.Accepted, acceptedSuggestion.Status);
        Assert.Equal(approverUserId, acceptedSuggestion.UpdatedByUserId);
        Assert.NotNull(acceptedSuggestion.AcceptedUtc);
        Assert.Equal(ReconciliationSuggestionStatuses.Superseded, supersededSuggestion.Status);
        Assert.Equal(approverUserId, supersededSuggestion.UpdatedByUserId);
        Assert.NotNull(supersededSuggestion.SupersededUtc);
        Assert.Equal(first.Id, result.AcceptedSuggestionId);
        Assert.Equal(250m, paymentLink.AllocatedAmount);
        Assert.Equal("USD", paymentLink.Currency);
        Assert.Equal(250m, bankTransaction.ReconciledAmount);
        Assert.Equal(BankTransactionReconciliationStatuses.Reconciled, bankTransaction.Status);

        var remainingOpen = await service.GetOpenSuggestionsAsync(
            new GetOpenReconciliationSuggestionsQuery(companyId, Limit: 10),
            CancellationToken.None);
        Assert.Empty(remainingOpen);
    }

    [Fact]
    public async Task Accept_invoice_to_payment_suggestion_creates_result_allocation_and_supersedes_competing_suggestions()
    {
        var companyId = Guid.NewGuid();
        var creatorUserId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        await SeedUsersAsync(
            dbContext,
            (creatorUserId, "creator@invoice-accept.example", "Creator"),
            (approverUserId, "approver@invoice-accept.example", "Approver"));
        var seed = await SeedCompanyFinanceScenarioAsync(dbContext, companyId);
        await SeedMembershipsAsync(dbContext, companyId, creatorUserId, approverUserId);

        var service = new CompanyReconciliationSuggestionService(dbContext, accessor);
        var first = await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Invoice, seed.InvoiceId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, creatorUserId, ReconciliationMatchTypes.RuleBased, 0.97m),
            CancellationToken.None);
        await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, ReconciliationRecordTypes.Invoice, seed.InvoiceId, creatorUserId, ReconciliationMatchTypes.Near, 0.83m),
            CancellationToken.None);

        var accepted = await service.AcceptSuggestionAsync(
            new AcceptReconciliationSuggestionCommand(companyId, first.Id, approverUserId),
            CancellationToken.None);

        var allocation = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .SingleAsync(x =>
                x.CompanyId == companyId &&
                x.PaymentId == seed.IncomingPaymentId &&
                x.InvoiceId == seed.InvoiceId);
        var invoice = await dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == seed.InvoiceId);
        var persistedSuggestions = await dbContext.ReconciliationSuggestionRecords
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();
        var result = await dbContext.ReconciliationResultRecords
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.AcceptedSuggestionId == first.Id);

        Assert.Equal(ReconciliationSuggestionStatuses.Accepted, accepted.Suggestion.Status);
        Assert.Equal(1, accepted.SupersededSuggestionCount);
        Assert.Equal(first.Id, accepted.Result.AcceptedSuggestionId);
        Assert.Equal(ReconciliationRecordTypes.Invoice, result.SourceRecordType);
        Assert.Equal(ReconciliationRecordTypes.Payment, result.TargetRecordType);
        Assert.Equal(250m, allocation.AllocatedAmount);
        Assert.Equal("USD", allocation.Currency);
        Assert.Equal(FinanceSettlementStatuses.Paid, invoice.SettlementStatus);

        var acceptedSuggestion = Assert.Single(persistedSuggestions.Where(x => x.Id == first.Id));
        var supersededSuggestion = Assert.Single(persistedSuggestions.Where(x => x.Id != first.Id));
        Assert.Equal(ReconciliationSuggestionStatuses.Accepted, acceptedSuggestion.Status);
        Assert.Equal(ReconciliationSuggestionStatuses.Superseded, supersededSuggestion.Status);
        Assert.NotNull(supersededSuggestion.SupersededUtc);

        Assert.Empty(await service.GetOpenSuggestionsAsync(
            new GetOpenReconciliationSuggestionsQuery(companyId, Limit: 10),
            CancellationToken.None));
    }

    [Fact]
    public async Task Reject_invoice_to_payment_suggestion_marks_rejected_and_excludes_from_open_queries()
    {
        var companyId = Guid.NewGuid();
        var creatorUserId = Guid.NewGuid();
        var reviewerUserId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        await SeedUsersAsync(
            dbContext,
            (creatorUserId, "creator@invoice-payment.example", "Creator"),
            (reviewerUserId, "reviewer@invoice-payment.example", "Reviewer"));
        var seed = await SeedCompanyFinanceScenarioAsync(dbContext, companyId);
        await SeedMembershipsAsync(dbContext, companyId, creatorUserId, reviewerUserId);

        var service = new CompanyReconciliationSuggestionService(dbContext, accessor);
        var created = await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Invoice, seed.InvoiceId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, creatorUserId),
            CancellationToken.None);

        var rejected = await service.RejectSuggestionAsync(
            new RejectReconciliationSuggestionCommand(companyId, created.Id, reviewerUserId),
            CancellationToken.None);

        Assert.Equal(ReconciliationSuggestionStatuses.Rejected, rejected.Status);
        Assert.Equal(reviewerUserId, rejected.UpdatedByUserId);
        Assert.NotNull(rejected.RejectedUtc);

        var persisted = await dbContext.ReconciliationSuggestionRecords
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == created.Id);
        Assert.Equal(ReconciliationSuggestionStatuses.Rejected, persisted.Status);
        Assert.Equal(creatorUserId, persisted.CreatedByUserId);
        Assert.Equal(reviewerUserId, persisted.UpdatedByUserId);

        var remainingOpen = await service.GetOpenSuggestionsAsync(
            new GetOpenReconciliationSuggestionsQuery(companyId, Limit: 10),
            CancellationToken.None);
        Assert.Empty(remainingOpen);
    }

    [Fact]
    public async Task Accept_bill_to_payment_suggestion_creates_result_allocation_and_supersedes_competing_suggestions()
    {
        var companyId = Guid.NewGuid();
        var creatorUserId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        await SeedUsersAsync(
            dbContext,
            (creatorUserId, "creator@bill-payment.example", "Creator"),
            (approverUserId, "approver@bill-payment.example", "Approver"));
        var seed = await SeedCompanyFinanceScenarioAsync(dbContext, companyId);
        await SeedMembershipsAsync(dbContext, companyId, creatorUserId, approverUserId);

        var service = new CompanyReconciliationSuggestionService(dbContext, accessor);
        var created = await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Bill, seed.BillId, ReconciliationRecordTypes.Payment, seed.OutgoingPaymentId, creatorUserId, ReconciliationMatchTypes.Exact, 1.00m),
            CancellationToken.None);
        await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Payment, seed.OutgoingPaymentId, ReconciliationRecordTypes.Bill, seed.BillId, creatorUserId, ReconciliationMatchTypes.Near, 0.82m),
            CancellationToken.None);

        var accepted = await service.AcceptSuggestionAsync(
            new AcceptReconciliationSuggestionCommand(companyId, created.Id, approverUserId),
            CancellationToken.None);

        var allocation = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .SingleAsync(x =>
                x.CompanyId == companyId &&
                x.PaymentId == seed.OutgoingPaymentId &&
                x.BillId == seed.BillId);
        var bill = await dbContext.FinanceBills
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == seed.BillId);
        var suggestions = await dbContext.ReconciliationSuggestionRecords
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();

        Assert.Equal(created.Id, accepted.Result.AcceptedSuggestionId);
        Assert.Equal(ReconciliationRecordTypes.Bill, accepted.Result.SourceRecordType);
        Assert.Equal(ReconciliationRecordTypes.Payment, accepted.Result.TargetRecordType);
        Assert.Equal(ReconciliationSuggestionStatuses.Accepted, accepted.Suggestion.Status);
        Assert.Equal(1, accepted.SupersededSuggestionCount);
        Assert.Equal(180m, allocation.AllocatedAmount);
        Assert.Equal("USD", allocation.Currency);
        Assert.Equal(FinanceSettlementStatuses.Paid, bill.SettlementStatus);

        var acceptedSuggestion = Assert.Single(suggestions.Where(x => x.Id == created.Id));
        var supersededSuggestion = Assert.Single(suggestions.Where(x => x.Id != created.Id));
        Assert.Equal(ReconciliationSuggestionStatuses.Accepted, acceptedSuggestion.Status);
        Assert.Equal(ReconciliationSuggestionStatuses.Superseded, supersededSuggestion.Status);
        Assert.NotNull(supersededSuggestion.SupersededUtc);

        Assert.Empty(await service.GetOpenSuggestionsAsync(
            new GetOpenReconciliationSuggestionsQuery(companyId, Limit: 10),
            CancellationToken.None));
    }

    [Fact]
    public async Task Tenant_scoping_prevents_cross_tenant_leakage()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var creatorAUserId = Guid.NewGuid();
        var creatorBUserId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();

        await using (var seedContext = CreateContext(connection))
        {
            await seedContext.Database.EnsureCreatedAsync();
            await SeedUsersAsync(
                seedContext,
                (creatorAUserId, "creator-a@tenant.example", "Creator A"),
                (creatorBUserId, "creator-b@tenant.example", "Creator B"));
            var seedA = await SeedCompanyFinanceScenarioAsync(seedContext, companyAId);
            var seedB = await SeedCompanyFinanceScenarioAsync(seedContext, companyBId);
            await SeedMembershipsAsync(seedContext, companyAId, creatorAUserId);
            await SeedMembershipsAsync(seedContext, companyBId, creatorBUserId);

            var seedService = new CompanyReconciliationSuggestionService(seedContext);
            await seedService.CreateSuggestionAsync(
                CreateCommand(companyAId, ReconciliationRecordTypes.Payment, seedA.IncomingPaymentId, ReconciliationRecordTypes.BankTransaction, seedA.BankTransactionId, creatorAUserId),
                CancellationToken.None);
            await seedService.CreateSuggestionAsync(
                CreateCommand(companyBId, ReconciliationRecordTypes.Payment, seedB.IncomingPaymentId, ReconciliationRecordTypes.BankTransaction, seedB.BankTransactionId, creatorBUserId),
                CancellationToken.None);
        }

        var accessor = new TestCompanyContextAccessor(companyAId);
        await using var scopedContext = CreateContext(connection, accessor);
        var scopedService = new CompanyReconciliationSuggestionService(scopedContext, accessor);

        var filteredRows = await scopedContext.ReconciliationSuggestionRecords
            .AsNoTracking()
            .ToListAsync();
        var visibleSuggestion = Assert.Single(filteredRows);
        Assert.Equal(companyAId, visibleSuggestion.CompanyId);

        var visibleOpenSuggestions = await scopedService.GetOpenSuggestionsAsync(
            new GetOpenReconciliationSuggestionsQuery(companyAId, Limit: 10),
            CancellationToken.None);
        var openSuggestion = Assert.Single(visibleOpenSuggestions);
        Assert.Equal(companyAId, openSuggestion.CompanyId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scopedService.GetOpenSuggestionsAsync(
                new GetOpenReconciliationSuggestionsQuery(companyBId, Limit: 10),
                CancellationToken.None));
    }

    [Fact]
    public async Task Reconciliation_write_paths_require_actor_membership_in_target_company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var creatorUserId = Guid.NewGuid();
        var outsiderUserId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        await SeedUsersAsync(
            dbContext,
            (creatorUserId, "creator@membership.example", "Creator"),
            (outsiderUserId, "outsider@membership.example", "Outsider"));
        var seed = await SeedCompanyFinanceScenarioAsync(dbContext, companyId);
        dbContext.Companies.Add(new Company(otherCompanyId, $"Other Company {otherCompanyId:N}"));
        await dbContext.SaveChangesAsync();
        await SeedMembershipsAsync(dbContext, companyId, creatorUserId);
        await SeedMembershipsAsync(dbContext, otherCompanyId, outsiderUserId);

        var service = new CompanyReconciliationSuggestionService(dbContext, accessor);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateSuggestionAsync(
                CreateCommand(companyId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, ReconciliationRecordTypes.BankTransaction, seed.BankTransactionId, outsiderUserId),
                CancellationToken.None));

        var created = await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, ReconciliationRecordTypes.BankTransaction, seed.BankTransactionId, creatorUserId),
            CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AcceptSuggestionAsync(
                new AcceptReconciliationSuggestionCommand(companyId, created.Id, outsiderUserId),
                CancellationToken.None));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RejectSuggestionAsync(
                new RejectReconciliationSuggestionCommand(companyId, created.Id, outsiderUserId),
                CancellationToken.None));

        var persisted = await dbContext.ReconciliationSuggestionRecords
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == created.Id);
        Assert.Equal(ReconciliationSuggestionStatuses.Open, persisted.Status);
        Assert.Equal(creatorUserId, persisted.CreatedByUserId);
        Assert.Equal(creatorUserId, persisted.UpdatedByUserId);
    }

    [Fact]
    public async Task Audit_metadata_is_persisted_and_updated_for_create_reject_and_accept_transitions()
    {
        var companyId = Guid.NewGuid();
        var creatorUserId = Guid.NewGuid();
        var reviewerUserId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        await SeedUsersAsync(
            dbContext,
            (creatorUserId, "creator@audit.example", "Creator"),
            (reviewerUserId, "reviewer@audit.example", "Reviewer"));
        var seed = await SeedCompanyFinanceScenarioAsync(dbContext, companyId);
        await SeedMembershipsAsync(dbContext, companyId, creatorUserId, reviewerUserId);

        var service = new CompanyReconciliationSuggestionService(dbContext, accessor);
        var rejectedCandidate = await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Invoice, seed.InvoiceId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, creatorUserId, ReconciliationMatchTypes.RuleBased, 0.72m),
            CancellationToken.None);
        var acceptedCandidate = await service.CreateSuggestionAsync(
            CreateCommand(companyId, ReconciliationRecordTypes.Payment, seed.IncomingPaymentId, ReconciliationRecordTypes.BankTransaction, seed.BankTransactionId, creatorUserId, ReconciliationMatchTypes.Exact, 1.00m),
            CancellationToken.None);

        await service.RejectSuggestionAsync(
            new RejectReconciliationSuggestionCommand(companyId, rejectedCandidate.Id, reviewerUserId),
            CancellationToken.None);
        var accepted = await service.AcceptSuggestionAsync(
            new AcceptReconciliationSuggestionCommand(companyId, acceptedCandidate.Id, reviewerUserId),
            CancellationToken.None);

        var rejected = await dbContext.ReconciliationSuggestionRecords
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == rejectedCandidate.Id);
        var acceptedSuggestion = await dbContext.ReconciliationSuggestionRecords
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == acceptedCandidate.Id);
        var result = await dbContext.ReconciliationResultRecords
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AcceptedSuggestionId == acceptedCandidate.Id);

        Assert.Equal(creatorUserId, rejected.CreatedByUserId);
        Assert.Equal(reviewerUserId, rejected.UpdatedByUserId);
        Assert.True(rejected.UpdatedUtc >= rejected.CreatedUtc);
        Assert.NotNull(rejected.RejectedUtc);

        Assert.Equal(creatorUserId, acceptedSuggestion.CreatedByUserId);
        Assert.Equal(reviewerUserId, acceptedSuggestion.UpdatedByUserId);
        Assert.True(acceptedSuggestion.UpdatedUtc >= acceptedSuggestion.CreatedUtc);
        Assert.NotNull(acceptedSuggestion.AcceptedUtc);

        Assert.Equal(reviewerUserId, result.CreatedByUserId);
        Assert.Equal(reviewerUserId, result.UpdatedByUserId);
        Assert.True(result.UpdatedUtc >= result.CreatedUtc);
        Assert.Equal(accepted.Result.Id, result.Id);
    }

    private static CreateReconciliationSuggestionCommand CreateCommand(
        Guid companyId,
        string sourceRecordType,
        Guid sourceRecordId,
        string targetRecordType,
        Guid targetRecordId,
        Guid actorUserId,
        string matchType = ReconciliationMatchTypes.RuleBased,
        decimal confidenceScore = 0.91m) =>
        new(
            companyId,
            sourceRecordType,
            sourceRecordId,
            targetRecordType,
            targetRecordId,
            matchType,
            confidenceScore,
            BuildRuleBreakdown(confidenceScore),
            actorUserId);

    private static Dictionary<string, JsonNode?> BuildRuleBreakdown(decimal confidenceScore) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["amount_exact"] = new JsonObject
            {
                ["matched"] = JsonValue.Create(true),
                ["score"] = JsonValue.Create(confidenceScore)
            },
            ["reference_similarity"] = new JsonObject
            {
                ["matched"] = JsonValue.Create(true),
                ["score"] = JsonValue.Create(confidenceScore)
            }
        };

    private static async Task SeedUsersAsync(VirtualCompanyDbContext dbContext, params (Guid Id, string Email, string DisplayName)[] users)
    {
        foreach (var user in users)
        {
            dbContext.Users.Add(new User(user.Id, user.Email, user.DisplayName, "dev-header", $"sub-{user.Id:N}"));
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedMembershipsAsync(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        params Guid[] userIds)
    {
        foreach (var userId in userIds)
        {
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Employee,
                CompanyMembershipStatus.Active));
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task<CompanyFinanceSeed> SeedCompanyFinanceScenarioAsync(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var cashAccountId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        var bankTransactionId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        var incomingPaymentId = Guid.NewGuid();
        var outgoingPaymentId = Guid.NewGuid();

        dbContext.Companies.Add(new Company(companyId, $"Reconciliation Company {companyId:N}"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            cashAccountId,
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            "USD",
            0m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.CompanyBankAccounts.Add(new CompanyBankAccount(
            bankAccountId,
            companyId,
            cashAccountId,
            "Operating Account",
            "Northwind Bank",
            "**** 0101",
            "USD",
            "operating",
            true,
            true,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(
            counterpartyId,
            companyId,
            "Contoso",
            "customer",
            "finance@contoso.example"));
        dbContext.BankTransactions.Add(new BankTransaction(
            bankTransactionId,
            companyId,
            bankAccountId,
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            250m,
            "USD",
            "INV-REC-001",
            "Contoso"));
        dbContext.FinanceInvoices.Add(new FinanceInvoice(
            invoiceId,
            companyId,
            counterpartyId,
            "INV-REC-001",
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            250m,
            "USD",
            "open"));
        dbContext.FinanceBills.Add(new FinanceBill(
            billId,
            companyId,
            counterpartyId,
            "BILL-REC-001",
            new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            180m,
            "USD",
            "open"));
        dbContext.Payments.AddRange(
            new Payment(
                incomingPaymentId,
                companyId,
                PaymentTypes.Incoming,
                250m,
                "USD",
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                "completed",
                "INV-REC-001"),
            new Payment(
                outgoingPaymentId,
                companyId,
                PaymentTypes.Outgoing,
                180m,
                "USD",
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                "ach",
                "completed",
                "BILL-REC-001"));

        await dbContext.SaveChangesAsync();
        return new CompanyFinanceSeed(bankTransactionId, invoiceId, billId, incomingPaymentId, outgoingPaymentId);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(
        SqliteConnection connection,
        ICompanyContextAccessor? accessor = null) =>
        new(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options,
            accessor);

    private sealed record CompanyFinanceSeed(
        Guid BankTransactionId,
        Guid InvoiceId,
        Guid BillId,
        Guid IncomingPaymentId,
        Guid OutgoingPaymentId);

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid companyId)
        {
            CompanyId = companyId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; private set; }
        public bool IsResolved => Membership is not null;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }

        public void SetCompanyId(Guid? companyId)
        {
            CompanyId = companyId;
            Membership = null;
            UserId = null;
        }

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }
}
