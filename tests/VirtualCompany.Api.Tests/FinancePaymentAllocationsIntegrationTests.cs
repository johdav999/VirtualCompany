using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePaymentAllocationsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinancePaymentAllocationsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Allocation_endpoints_create_and_list_allocations_by_payment_and_document()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var invoiceAPartialResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments/{seed.IncomingPaymentId}/allocations",
            new AllocationCreateRequest
            {
                InvoiceId = seed.InvoiceAId,
                AllocatedAmount = 40.25m,
                Currency = "usd"
            });

        Assert.Equal(HttpStatusCode.OK, invoiceAPartialResponse.StatusCode);
        var invoiceAPartial = await invoiceAPartialResponse.Content.ReadFromJsonAsync<FinancePaymentAllocationResponse>();
        Assert.NotNull(invoiceAPartial);
        Assert.Equal(seed.CompanyId, invoiceAPartial!.CompanyId);
        Assert.Equal(seed.IncomingPaymentId, invoiceAPartial.PaymentId);
        Assert.Equal(seed.InvoiceAId, invoiceAPartial.InvoiceId);
        Assert.Equal(40.25m, invoiceAPartial.AllocatedAmount);
        Assert.Equal("USD", invoiceAPartial.Currency);

        var invoiceARemainingResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments/{seed.IncomingPaymentBId}/allocations",
            new AllocationCreateRequest
            {
                InvoiceId = seed.InvoiceAId,
                AllocatedAmount = 29.75m,
                Currency = "USD"
            });

        Assert.Equal(HttpStatusCode.OK, invoiceARemainingResponse.StatusCode);
        var invoiceARemaining = await invoiceARemainingResponse.Content.ReadFromJsonAsync<FinancePaymentAllocationResponse>();
        Assert.NotNull(invoiceARemaining);

        var invoiceBResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments/{seed.IncomingPaymentId}/allocations",
            new AllocationCreateRequest
            {
                InvoiceId = seed.InvoiceBId,
                AllocatedAmount = 30m,
                Currency = "USD"
            });

        Assert.Equal(HttpStatusCode.OK, invoiceBResponse.StatusCode);
        var invoiceBAllocation = await invoiceBResponse.Content.ReadFromJsonAsync<FinancePaymentAllocationResponse>();
        Assert.NotNull(invoiceBAllocation);

        var billResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments/{seed.OutgoingPaymentId}/allocations",
            new AllocationCreateRequest
            {
                BillId = seed.BillId,
                AllocatedAmount = 50m,
                Currency = "usd"
            });

        Assert.Equal(HttpStatusCode.OK, billResponse.StatusCode);
        var billAllocation = await billResponse.Content.ReadFromJsonAsync<FinancePaymentAllocationResponse>();
        Assert.NotNull(billAllocation);
        Assert.Equal(seed.BillId, billAllocation!.BillId);

        var paymentAllocationsResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments/{seed.IncomingPaymentId}/allocations");
        Assert.Equal(HttpStatusCode.OK, paymentAllocationsResponse.StatusCode);
        var paymentAllocations = await paymentAllocationsResponse.Content.ReadFromJsonAsync<List<FinancePaymentAllocationResponse>>();
        Assert.NotNull(paymentAllocations);
        Assert.Equal(2, paymentAllocations!.Count);
        Assert.Contains(paymentAllocations, allocation => allocation.Id == invoiceAPartial.Id);
        Assert.Contains(paymentAllocations, allocation => allocation.Id == invoiceBAllocation!.Id);

        var invoiceAllocationsResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.InvoiceAId}/allocations");
        Assert.Equal(HttpStatusCode.OK, invoiceAllocationsResponse.StatusCode);
        var invoiceAllocations = await invoiceAllocationsResponse.Content.ReadFromJsonAsync<List<FinancePaymentAllocationResponse>>();
        Assert.NotNull(invoiceAllocations);
        Assert.Equal(2, invoiceAllocations!.Count);
        Assert.Contains(invoiceAllocations, allocation => allocation.Id == invoiceAPartial.Id);
        Assert.Contains(invoiceAllocations, allocation => allocation.Id == invoiceARemaining!.Id);

        var billAllocationsResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bills/{seed.BillId}/allocations");
        Assert.Equal(HttpStatusCode.OK, billAllocationsResponse.StatusCode);
        var billAllocations = await billAllocationsResponse.Content.ReadFromJsonAsync<List<FinancePaymentAllocationResponse>>();
        Assert.NotNull(billAllocations);
        Assert.Single(billAllocations!);
        Assert.Equal(billAllocation.Id, billAllocations[0].Id);

        var persistedAllocations = await _factory.ExecuteDbContextAsync(async dbContext =>
            await dbContext.PaymentAllocations
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId)
                .Select(x => new PersistedAllocationRow(x.Id, x.PaymentId, x.InvoiceId, x.BillId, x.AllocatedAmount, x.Currency))
                .ToListAsync());

        Assert.Equal(4, persistedAllocations.Count);
        Assert.Contains(persistedAllocations, allocation => allocation.Id == invoiceAPartial.Id && allocation.AllocatedAmount == 40.25m && allocation.Currency == "USD");
        Assert.Contains(persistedAllocations, allocation => allocation.Id == invoiceARemaining!.Id && allocation.AllocatedAmount == 29.75m && allocation.Currency == "USD");
        Assert.Contains(persistedAllocations, allocation => allocation.Id == invoiceBAllocation!.Id && allocation.AllocatedAmount == 30m && allocation.Currency == "USD");
        Assert.Contains(persistedAllocations, allocation => allocation.Id == billAllocation.Id && allocation.AllocatedAmount == 50m && allocation.Currency == "USD");

        var snapshot = await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var invoiceASettlementStatus = await dbContext.FinanceInvoices
                .IgnoreQueryFilters()
                .Where(x => x.Id == seed.InvoiceAId)
                .Select(x => x.SettlementStatus)
                .SingleAsync();
            var invoiceBSettlementStatus = await dbContext.FinanceInvoices
                .IgnoreQueryFilters()
                .Where(x => x.Id == seed.InvoiceBId)
                .Select(x => x.SettlementStatus)
                .SingleAsync();
            var billSettlementStatus = await dbContext.FinanceBills
                .IgnoreQueryFilters()
                .Where(x => x.Id == seed.BillId)
                .Select(x => x.SettlementStatus)
                .SingleAsync();
            var allocatedOnPrimaryPayment = await dbContext.PaymentAllocations
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.PaymentId == seed.IncomingPaymentId)
                .SumAsync(x => x.AllocatedAmount);

            return new AllocationSnapshot(invoiceASettlementStatus, invoiceBSettlementStatus, billSettlementStatus, allocatedOnPrimaryPayment);
        });

        Assert.Equal(FinanceSettlementStatuses.Paid, snapshot.InvoiceASettlementStatus);
        Assert.Equal(FinanceSettlementStatuses.PartiallyPaid, snapshot.InvoiceBSettlementStatus);
        Assert.Equal(FinanceSettlementStatuses.Paid, snapshot.BillSettlementStatus);
        Assert.Equal(70.25m, snapshot.AllocatedOnPrimaryPayment);
    }

    [Fact]
    public async Task Allocation_endpoints_reject_over_allocation_and_hide_foreign_records()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var firstResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments/{seed.IncomingPaymentId}/allocations",
            new AllocationCreateRequest
            {
                InvoiceId = seed.InvoiceBId,
                AllocatedAmount = 60m,
                Currency = "USD"
            });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var overAllocationResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments/{seed.IncomingPaymentId}/allocations",
            new AllocationCreateRequest
            {
                InvoiceId = seed.InvoiceAId,
                AllocatedAmount = 50m,
                Currency = "USD"
            });

        Assert.Equal(HttpStatusCode.BadRequest, overAllocationResponse.StatusCode);
        var problem = await overAllocationResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("AllocatedAmount", problem!.Errors.Keys);

        var documentOverAllocationResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments/{seed.IncomingPaymentBId}/allocations",
            new AllocationCreateRequest
            {
                InvoiceId = seed.InvoiceBId,
                AllocatedAmount = 0.50m,
                Currency = "USD"
            });

        Assert.Equal(HttpStatusCode.BadRequest, documentOverAllocationResponse.StatusCode);
        var documentOverAllocationProblem = await documentOverAllocationResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(documentOverAllocationProblem);
        Assert.Contains("AllocatedAmount", documentOverAllocationProblem!.Errors.Keys);

        var currencyMismatchResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments/{seed.OutgoingPaymentId}/allocations",
            new AllocationCreateRequest
            {
                BillId = seed.BillId,
                AllocatedAmount = 10m,
                Currency = "EUR"
            });

        Assert.Equal(HttpStatusCode.BadRequest, currencyMismatchResponse.StatusCode);
        var currencyMismatchProblem = await currencyMismatchResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(currencyMismatchProblem);
        Assert.Contains("Currency", currencyMismatchProblem!.Errors.Keys);

        var validationSnapshot = await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var invoiceBAllocated = await dbContext.PaymentAllocations
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.InvoiceId == seed.InvoiceBId)
                .SumAsync(x => (decimal?)x.AllocatedAmount) ?? 0m;
            var billAllocated = await dbContext.PaymentAllocations
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.BillId == seed.BillId)
                .SumAsync(x => (decimal?)x.AllocatedAmount) ?? 0m;

            return new ValidationSnapshot(invoiceBAllocated, billAllocated);
        });

        Assert.Equal(60m, validationSnapshot.InvoiceBAllocatedAmount);
        Assert.Equal(0m, validationSnapshot.BillAllocatedAmount);

        var foreignPaymentResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments/{seed.OtherCompanyPaymentId}/allocations");
        Assert.Equal(HttpStatusCode.NotFound, foreignPaymentResponse.StatusCode);
    }

    private async Task<AllocationEndpointSeed> SeedAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var subject = $"finance-payment-allocations-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Allocation Owner";
        var counterpartyId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var invoiceAId = Guid.NewGuid();
        var invoiceBId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        var incomingPaymentId = Guid.NewGuid();
        var incomingPaymentBId = Guid.NewGuid();
        var outgoingPaymentId = Guid.NewGuid();
        var otherCompanyPaymentId = Guid.Empty;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.AddRange(
                new Company(companyId, "Allocation Endpoint Company"),
                new Company(otherCompanyId, "Other Allocation Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            FinanceSeedData.AddMockFinanceData(dbContext, companyId);
            FinanceSeedData.AddMockFinanceData(dbContext, otherCompanyId);

            dbContext.FinanceCounterparties.AddRange(
                new FinanceCounterparty(counterpartyId, companyId, "Allocation Customer", "customer", "customer@example.com"),
                new FinanceCounterparty(supplierId, companyId, "Allocation Supplier", "vendor", "supplier@example.com"));

            dbContext.FinanceInvoices.AddRange(
                new FinanceInvoice(
                    invoiceAId,
                    companyId,
                    counterpartyId,
                    "INV-API-ALLOC-001",
                    new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                    70m,
                    "USD",
                    "open"),
                new FinanceInvoice(
                    invoiceBId,
                    companyId,
                    counterpartyId,
                    "INV-API-ALLOC-002",
                    new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
                    60m,
                    "USD",
                    "open"));

            dbContext.FinanceBills.Add(new FinanceBill(
                billId,
                companyId,
                supplierId,
                "BILL-API-ALLOC-001",
                new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                50m,
                "USD",
                "open"));

            dbContext.Payments.AddRange(
                new Payment(
                    incomingPaymentId,
                    companyId,
                    PaymentTypes.Incoming,
                    100m,
                    "USD",
                    new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                    "bank_transfer",
                    "completed",
                    "ALLOC-API-INCOMING-A"),
                new Payment(
                    incomingPaymentBId,
                    companyId,
                    PaymentTypes.Incoming,
                    30m,
                    "USD",
                    new DateTime(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc),
                    "bank_transfer",
                    "completed",
                    "ALLOC-API-INCOMING-B"),
                new Payment(
                    outgoingPaymentId,
                    companyId,
                    PaymentTypes.Outgoing,
                    50m,
                    "USD",
                    new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc),
                    "ach",
                    "completed",
                    "ALLOC-API-OUTGOING-A"));

            var foreignPayment = new Payment(
                Guid.NewGuid(),
                otherCompanyId,
                PaymentTypes.Incoming,
                125m,
                "USD",
                new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                "completed",
                "FOREIGN-ALLOC-PAYMENT");
            dbContext.Payments.Add(foreignPayment);
            otherCompanyPaymentId = foreignPayment.Id;

            return Task.CompletedTask;
        });

        return new AllocationEndpointSeed(
            companyId,
            subject,
            email,
            displayName,
            invoiceAId,
            invoiceBId,
            billId,
            incomingPaymentId,
            incomingPaymentBId,
            outgoingPaymentId,
            otherCompanyPaymentId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record AllocationEndpointSeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName,
        Guid InvoiceAId,
        Guid InvoiceBId,
        Guid BillId,
        Guid IncomingPaymentId,
        Guid IncomingPaymentBId,
        Guid OutgoingPaymentId,
        Guid OtherCompanyPaymentId);

    private sealed record AllocationSnapshot(
        string InvoiceASettlementStatus,
        string InvoiceBSettlementStatus,
        string BillSettlementStatus,
        decimal AllocatedOnPrimaryPayment);

    private sealed record ValidationSnapshot(
        decimal InvoiceBAllocatedAmount,
        decimal BillAllocatedAmount);

    private sealed record PersistedAllocationRow(
        Guid Id,
        Guid PaymentId,
        Guid? InvoiceId,
        Guid? BillId,
        decimal AllocatedAmount,
        string Currency);

    private sealed class FinancePaymentAllocationResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public Guid PaymentId { get; set; }
        public Guid? InvoiceId { get; set; }
        public Guid? BillId { get; set; }
        public decimal AllocatedAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class AllocationCreateRequest
    {
        public Guid? InvoiceId { get; set; }
        public Guid? BillId { get; set; }
        public decimal AllocatedAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
}