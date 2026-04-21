using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ReconciliationSuggestionApiIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ReconciliationSuggestionApiIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_endpoint_filters_open_suggestions_by_entity_type_confidence_and_paginates()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Finance Approver");

        var firstResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions?entityType=payment&minConfidence=0.90&page=1&pageSize=2");
        var secondResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions?entityType=payment&minConfidence=0.90&page=2&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var firstPage = await firstResponse.Content.ReadFromJsonAsync<ReconciliationSuggestionPageDto>();
        var secondPage = await secondResponse.Content.ReadFromJsonAsync<ReconciliationSuggestionPageDto>();

        Assert.NotNull(firstPage);
        Assert.NotNull(secondPage);
        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, firstPage.PageSize);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.All(firstPage.Items.Concat(secondPage.Items), item =>
        {
            Assert.Equal(seed.CompanyId, item.CompanyId);
            Assert.Equal(ReconciliationSuggestionStatuses.Open, item.Status);
            Assert.True(item.ConfidenceScore >= 0.90m);
            Assert.True(
                item.SourceRecordType == ReconciliationRecordTypes.Payment ||
                item.TargetRecordType == ReconciliationRecordTypes.Payment);
        });

        Assert.Equal(
            new[] { seed.OpenHighestSuggestionId, seed.OpenMidSuggestionId, seed.OpenLowSuggestionId },
            firstPage.Items.Concat(secondPage.Items).Select(x => x.Id));
        Assert.DoesNotContain(firstPage.Items.Concat(secondPage.Items), x => x.Id == seed.RejectedSuggestionId);
        Assert.DoesNotContain(firstPage.Items.Concat(secondPage.Items), x => x.Id == seed.OtherCompanySuggestionId);
    }

    [Fact]
    public async Task List_endpoint_supports_status_filter()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Finance Approver");

        var response = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions?status=rejected&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReconciliationSuggestionPageDto>();
        Assert.NotNull(payload);
        var rejected = Assert.Single(payload!.Items);
        Assert.Equal(1, payload.TotalCount);
        Assert.Equal(seed.RejectedSuggestionId, rejected.Id);
        Assert.Equal(ReconciliationSuggestionStatuses.Rejected, rejected.Status);
    }

    [Fact]
    public async Task Endpoints_require_authentication()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateClient();

        var listResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions?page=1&pageSize=10");
        var acceptResponse = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.OpenHighestSuggestionId:D}/accept",
            null);
        var rejectResponse = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.OpenHighestSuggestionId:D}/reject",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, acceptResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectResponse.StatusCode);
    }

    [Fact]
    public async Task Endpoints_enforce_finance_authorization_for_authenticated_users_without_required_role()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.EmployeeSubject, seed.EmployeeEmail, "Employee");

        var listResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions?page=1&pageSize=10");

        var acceptResponse = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.OpenHighestSuggestionId:D}/accept",
            null);

        var rejectResponse = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.OpenHighestSuggestionId:D}/reject",
            null);

        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, acceptResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rejectResponse.StatusCode);
    }

    [Fact]
    public async Task Accept_endpoint_persists_result_and_returns_linked_record_identifiers()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Finance Approver");

        var response = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.OpenHighestSuggestionId:D}/accept",
            null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AcceptedReconciliationSuggestionDto>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!.Suggestion.CompanyId);
        Assert.Equal(seed.OpenHighestSuggestionId, payload!.Suggestion.Id);
        Assert.Equal(ReconciliationSuggestionStatuses.Accepted, payload.Suggestion.Status);
        Assert.NotNull(payload.Suggestion.AcceptedUtc);
        Assert.Null(payload.Suggestion.RejectedUtc);
        Assert.Equal(seed.CompanyId, payload.Result.CompanyId);
        Assert.NotEqual(Guid.Empty, payload.Result.Id);
        Assert.Equal(seed.OpenHighestSuggestionId, payload.Result.AcceptedSuggestionId);
        Assert.Equal(ReconciliationRecordTypes.Payment, payload.Result.SourceRecordType);
        Assert.Equal(seed.IncomingPaymentId, payload.Result.SourceRecordId);
        Assert.Equal(ReconciliationRecordTypes.BankTransaction, payload.Result.TargetRecordType);
        Assert.Equal(seed.BankTransactionId, payload.Result.TargetRecordId);
        Assert.NotEmpty(payload.Result.RuleBreakdown);
        Assert.Equal(0, payload.SupersededSuggestionCount);

        var persisted = await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var suggestion = await dbContext.ReconciliationSuggestionRecords
                .IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == seed.CompanyId && x.Id == seed.OpenHighestSuggestionId);
            var result = await dbContext.ReconciliationResultRecords
                .IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == seed.CompanyId && x.AcceptedSuggestionId == seed.OpenHighestSuggestionId);
            var paymentLink = await dbContext.BankTransactionPaymentLinks
                .IgnoreQueryFilters()
                .SingleAsync(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.BankTransactionId == seed.BankTransactionId &&
                    x.PaymentId == seed.IncomingPaymentId);

            return new
            {
                suggestion.Status,
                result.SourceRecordId,
                result.TargetRecordId,
                paymentLink.AllocatedAmount
            };
        });

        Assert.Equal(ReconciliationSuggestionStatuses.Accepted, persisted.Status);
        Assert.Equal(seed.IncomingPaymentId, persisted.SourceRecordId);
        Assert.Equal(seed.BankTransactionId, persisted.TargetRecordId);
        Assert.Equal(250m, persisted.AllocatedAmount);
    }

    [Fact]
    public async Task Reject_endpoint_updates_status_and_returns_rejected_payload()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Finance Approver");

        var response = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.OpenMidSuggestionId:D}/reject",
            null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReconciliationSuggestionRecordDto>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!.CompanyId);
        Assert.Equal(seed.OpenMidSuggestionId, payload!.Id);
        Assert.Equal(ReconciliationSuggestionStatuses.Rejected, payload.Status);
        Assert.Null(payload.AcceptedUtc);
        Assert.NotNull(payload.RejectedUtc);

        var persistedStatus = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.ReconciliationSuggestionRecords
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.Id == seed.OpenMidSuggestionId)
                .Select(x => x.Status)
                .SingleAsync());

        Assert.Equal(ReconciliationSuggestionStatuses.Rejected, persistedStatus);
    }

    [Fact]
    public async Task Accept_endpoint_returns_deterministic_validation_error_for_already_accepted_or_rejected_suggestions()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Finance Approver");

        var acceptedResponse = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.AcceptedSuggestionId:D}/accept",
            null);
        var rejectedResponse = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.RejectedSuggestionId:D}/accept",
            null);

        Assert.Equal(HttpStatusCode.BadRequest, acceptedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);

        var acceptedProblem = await acceptedResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        var rejectedProblem = await rejectedResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.NotNull(acceptedProblem);
        Assert.NotNull(rejectedProblem);
        Assert.Equal("Finance validation failed", acceptedProblem!.Title);
        Assert.Equal("Finance validation failed", rejectedProblem!.Title);
        Assert.Equal(
            $"Suggestion '{seed.AcceptedSuggestionId}' cannot be accepted because it is already accepted.",
            Assert.Single(acceptedProblem.Errors[nameof(AcceptReconciliationSuggestionCommand.SuggestionId)]));
        Assert.Equal(
            $"Suggestion '{seed.RejectedSuggestionId}' cannot be accepted because it is already rejected.",
            Assert.Single(rejectedProblem.Errors[nameof(AcceptReconciliationSuggestionCommand.SuggestionId)]));
    }

    [Fact]
    public async Task Reject_endpoint_returns_deterministic_validation_error_for_already_accepted_or_rejected_suggestions()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Finance Approver");

        var acceptedResponse = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.AcceptedSuggestionId:D}/reject",
            null);
        var rejectedResponse = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.RejectedSuggestionId:D}/reject",
            null);

        Assert.Equal(HttpStatusCode.BadRequest, acceptedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);

        var acceptedProblem = await acceptedResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        var rejectedProblem = await rejectedResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.NotNull(acceptedProblem);
        Assert.NotNull(rejectedProblem);
        Assert.Equal("Finance validation failed", acceptedProblem!.Title);
        Assert.Equal("Finance validation failed", rejectedProblem!.Title);
        Assert.Equal(
            $"Suggestion '{seed.AcceptedSuggestionId}' cannot be rejected because it is already accepted.",
            Assert.Single(acceptedProblem.Errors[nameof(RejectReconciliationSuggestionCommand.SuggestionId)]));
        Assert.Equal(
            $"Suggestion '{seed.RejectedSuggestionId}' cannot be rejected because it is already rejected.",
            Assert.Single(rejectedProblem.Errors[nameof(RejectReconciliationSuggestionCommand.SuggestionId)]));
    }

    [Fact]
    public async Task Accept_endpoint_does_not_expose_suggestions_from_other_companies()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, "Owner");

        var response = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.OtherCompanySuggestionId:D}/accept",
            null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var status = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.ReconciliationSuggestionRecords
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.OtherCompanyId && x.Id == seed.OtherCompanySuggestionId)
                .Select(x => x.Status)
                .SingleAsync());

        Assert.Equal(ReconciliationSuggestionStatuses.Open, status);
    }

    [Fact]
    public async Task Reject_endpoint_does_not_expose_suggestions_from_other_companies()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, "Owner");

        var response = await client.PostAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/reconciliation-suggestions/{seed.OtherCompanySuggestionId:D}/reject",
            null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var status = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.ReconciliationSuggestionRecords
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.OtherCompanyId && x.Id == seed.OtherCompanySuggestionId)
                .Select(x => x.Status)
                .SingleAsync());

        Assert.Equal(ReconciliationSuggestionStatuses.Open, status);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private async Task<ReconciliationSuggestionApiSeed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var ownerSubject = $"reconciliation-owner-{Guid.NewGuid():N}";
        var approverSubject = $"reconciliation-approver-{Guid.NewGuid():N}";
        var employeeSubject = $"reconciliation-employee-{Guid.NewGuid():N}";
        var ownerEmail = $"{ownerSubject}@example.com";
        var approverEmail = $"{approverSubject}@example.com";
        var employeeEmail = $"{employeeSubject}@example.com";

        var bankTransactionId = Guid.Empty;
        var invoiceId = Guid.Empty;
        var billId = Guid.Empty;
        var incomingPaymentId = Guid.Empty;
        var outgoingPaymentId = Guid.Empty;
        var otherCompanySuggestionId = Guid.Empty;
        var openHighestSuggestionId = Guid.Empty;
        var openMidSuggestionId = Guid.Empty;
        var openLowSuggestionId = Guid.Empty;
        var rejectedSuggestionId = Guid.Empty;
        var acceptedSuggestionId = Guid.Empty;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(ownerUserId, ownerEmail, "Owner", "dev-header", ownerSubject),
                new User(approverUserId, approverEmail, "Finance Approver", "dev-header", approverSubject),
                new User(employeeUserId, employeeEmail, "Employee", "dev-header", employeeSubject));
            dbContext.Companies.AddRange(
                new Company(companyId, "Reconciliation API Company"),
                new Company(otherCompanyId, "Other Reconciliation API Company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, approverUserId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            var primaryFinance = SeedCompanyFinanceScenario(dbContext, companyId);
            var otherFinance = SeedCompanyFinanceScenario(dbContext, otherCompanyId);
            bankTransactionId = primaryFinance.BankTransactionId;
            invoiceId = primaryFinance.InvoiceId;
            billId = primaryFinance.BillId;
            incomingPaymentId = primaryFinance.IncomingPaymentId;
            outgoingPaymentId = primaryFinance.OutgoingPaymentId;

            var now = new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc);
            var openHighest = new ReconciliationSuggestionRecord(
                Guid.NewGuid(),
                companyId,
                ReconciliationRecordTypes.Payment,
                primaryFinance.IncomingPaymentId,
                ReconciliationRecordTypes.BankTransaction,
                primaryFinance.BankTransactionId,
                ReconciliationMatchTypes.RuleBased,
                0.98m,
                BuildRuleBreakdown(0.98m),
                ownerUserId,
                now.AddMinutes(-10));
            var openMid = new ReconciliationSuggestionRecord(
                Guid.NewGuid(),
                companyId,
                ReconciliationRecordTypes.Invoice,
                primaryFinance.InvoiceId,
                ReconciliationRecordTypes.Payment,
                primaryFinance.IncomingPaymentId,
                ReconciliationMatchTypes.Near,
                0.93m,
                BuildRuleBreakdown(0.93m),
                ownerUserId,
                now.AddMinutes(-9));
            var openLow = new ReconciliationSuggestionRecord(
                Guid.NewGuid(),
                companyId,
                ReconciliationRecordTypes.Bill,
                primaryFinance.BillId,
                ReconciliationRecordTypes.Payment,
                primaryFinance.OutgoingPaymentId,
                ReconciliationMatchTypes.Exact,
                0.91m,
                BuildRuleBreakdown(0.91m),
                ownerUserId,
                now.AddMinutes(-8));
            var rejected = new ReconciliationSuggestionRecord(
                Guid.NewGuid(),
                companyId,
                ReconciliationRecordTypes.Payment,
                primaryFinance.IncomingPaymentId,
                ReconciliationRecordTypes.Invoice,
                primaryFinance.InvoiceId,
                ReconciliationMatchTypes.RuleBased,
                0.87m,
                BuildRuleBreakdown(0.87m),
                ownerUserId,
                now.AddMinutes(-7));
            rejected.Reject(approverUserId, now.AddMinutes(-6));
            var accepted = new ReconciliationSuggestionRecord(
                Guid.NewGuid(),
                companyId,
                ReconciliationRecordTypes.Payment,
                primaryFinance.OutgoingPaymentId,
                ReconciliationRecordTypes.Bill,
                primaryFinance.BillId,
                ReconciliationMatchTypes.Exact,
                0.79m,
                BuildRuleBreakdown(0.79m),
                ownerUserId,
                now.AddMinutes(-5));
            accepted.Accept(approverUserId, now.AddMinutes(-4));
            var otherOpen = new ReconciliationSuggestionRecord(
                Guid.NewGuid(),
                otherCompanyId,
                ReconciliationRecordTypes.Payment,
                otherFinance.IncomingPaymentId,
                ReconciliationRecordTypes.BankTransaction,
                otherFinance.BankTransactionId,
                ReconciliationMatchTypes.RuleBased,
                0.97m,
                BuildRuleBreakdown(0.97m),
                ownerUserId,
                now.AddMinutes(-3));

            dbContext.ReconciliationSuggestionRecords.AddRange(openHighest, openMid, openLow, rejected, accepted, otherOpen);

            openHighestSuggestionId = openHighest.Id;
            openMidSuggestionId = openMid.Id;
            openLowSuggestionId = openLow.Id;
            rejectedSuggestionId = rejected.Id;
            acceptedSuggestionId = accepted.Id;
            otherCompanySuggestionId = otherOpen.Id;

            return Task.CompletedTask;
        });

        return new ReconciliationSuggestionApiSeed(
            companyId,
            otherCompanyId,
            ownerSubject,
            ownerEmail,
            approverSubject,
            approverEmail,
            employeeSubject,
            employeeEmail,
            bankTransactionId,
            invoiceId,
            billId,
            incomingPaymentId,
            outgoingPaymentId,
            openHighestSuggestionId,
            openMidSuggestionId,
            openLowSuggestionId,
            rejectedSuggestionId,
            acceptedSuggestionId,
            otherCompanySuggestionId);
    }

    private static CompanyFinanceSeed SeedCompanyFinanceScenario(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var cashAccountId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        var bankTransactionId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        var incomingPaymentId = Guid.NewGuid();
        var outgoingPaymentId = Guid.NewGuid();

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

        return new CompanyFinanceSeed(bankTransactionId, invoiceId, billId, incomingPaymentId, outgoingPaymentId);
    }

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

    private sealed record CompanyFinanceSeed(
        Guid BankTransactionId,
        Guid InvoiceId,
        Guid BillId,
        Guid IncomingPaymentId,
        Guid OutgoingPaymentId);

    private sealed record ReconciliationSuggestionApiSeed(
        Guid CompanyId,
        Guid OtherCompanyId,
        string OwnerSubject,
        string OwnerEmail,
        string ApproverSubject,
        string ApproverEmail,
        string EmployeeSubject,
        string EmployeeEmail,
        Guid BankTransactionId,
        Guid InvoiceId,
        Guid BillId,
        Guid IncomingPaymentId,
        Guid OutgoingPaymentId,
        Guid OpenHighestSuggestionId,
        Guid OpenMidSuggestionId,
        Guid OpenLowSuggestionId,
        Guid RejectedSuggestionId,
        Guid AcceptedSuggestionId,
        Guid OtherCompanySuggestionId);
}