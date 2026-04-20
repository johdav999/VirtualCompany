using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceTransactionDetailMappingIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceTransactionDetailMappingIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Transaction_list_and_detail_map_summary_fields_and_available_document_metadata_end_to_end()
    {
        var seed = await FinanceDetailTestScenarioFactory.CreateAsync(_factory);
        using var client = FinanceDetailTestScenarioFactory.CreateAuthenticatedClient(
            _factory,
            seed.OwnerSubject,
            seed.OwnerEmail,
            seed.OwnerDisplayName);

        var listResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/transactions?limit=60");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var transactions = await listResponse.Content.ReadFromJsonAsync<List<FinanceTransactionResponse>>();
        Assert.NotNull(transactions);

        var transaction = Assert.Single(transactions!, item => item.Id == seed.AccessibleTransactionId);
        Assert.Equal("Operating Cash", transaction.AccountName);
        Assert.Equal("Northwind Analytics", transaction.CounterpartyName);
        Assert.Equal(seed.ReviewInvoiceId, transaction.InvoiceId);
        Assert.Null(transaction.BillId);
        Assert.Equal(new DateTime(2025, 11, 13, 0, 0, 0, DateTimeKind.Utc), transaction.TransactionUtc);
        Assert.Equal("customer_payment", transaction.TransactionType);
        Assert.Equal(450m, transaction.Amount);
        Assert.Equal("USD", transaction.Currency);
        Assert.Equal("Customer receipt", transaction.Description);
        Assert.Equal($"FIN-{seed.CompanyId:N}-0000", transaction.ExternalReference);
        Assert.True(transaction.IsFlagged);
        Assert.Equal("needs_review", transaction.AnomalyState);
        Assert.NotNull(transaction.LinkedDocument);
        Assert.Equal("Finance supporting document 001", transaction.LinkedDocument!.Title);
        Assert.Equal("finance-document-001.pdf", transaction.LinkedDocument.OriginalFileName);
        Assert.Equal("application/pdf", transaction.LinkedDocument.ContentType);

        var detailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/transactions/{seed.AccessibleTransactionId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<FinanceTransactionDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(seed.AccessibleTransactionId, detail!.Id);
        Assert.Equal(transaction.AccountId, detail.AccountId);
        Assert.Equal(transaction.AccountName, detail.AccountName);
        Assert.Equal(transaction.CounterpartyId, detail.CounterpartyId);
        Assert.Equal(transaction.CounterpartyName, detail.CounterpartyName);
        Assert.Equal(transaction.InvoiceId, detail.InvoiceId);
        Assert.Equal(transaction.BillId, detail.BillId);
        Assert.Equal(transaction.TransactionUtc, detail.TransactionUtc);
        Assert.Equal(transaction.TransactionType, detail.Category);
        Assert.Equal(transaction.Amount, detail.Amount);
        Assert.Equal(transaction.Currency, detail.Currency);
        Assert.Equal(transaction.Description, detail.Description);
        Assert.Equal(transaction.ExternalReference, detail.ExternalReference);
        Assert.True(detail.IsFlagged);
        Assert.NotNull(detail.Permissions);
        Assert.True(detail.Permissions.CanEditTransactionCategory);
        Assert.True(detail.Permissions.CanChangeInvoiceApprovalStatus);
        Assert.True(detail.Permissions.CanManagePolicyConfiguration);
        Assert.Equal("needs_review", detail.AnomalyState);
        Assert.Equal(["missing_receipt"], detail.Flags);
        Assert.Equal("available", detail.LinkedDocument.Availability);
        Assert.Equal("Linked document available.", detail.LinkedDocument.Message);
        Assert.True(detail.LinkedDocument.CanNavigate);
        Assert.NotNull(detail.LinkedDocument.Document);
        Assert.Equal(transaction.LinkedDocument.Id, detail.LinkedDocument.Document!.Id);
        Assert.Equal(transaction.LinkedDocument.Title, detail.LinkedDocument.Document.Title);
        Assert.Equal(transaction.LinkedDocument.OriginalFileName, detail.LinkedDocument.Document.OriginalFileName);
        Assert.Equal(transaction.LinkedDocument.ContentType, detail.LinkedDocument.Document.ContentType);
    }

    [Fact]
    public async Task Transaction_detail_surfaces_missing_and_inaccessible_document_states_without_blocking_rendering()
    {
        var seed = await FinanceDetailTestScenarioFactory.CreateAsync(_factory);

        using var ownerClient = FinanceDetailTestScenarioFactory.CreateAuthenticatedClient(
            _factory,
            seed.OwnerSubject,
            seed.OwnerEmail,
            seed.OwnerDisplayName);

        var missingDetailResponse = await ownerClient.GetAsync($"/internal/companies/{seed.CompanyId}/finance/transactions/{seed.MissingTransactionId}");
        Assert.Equal(HttpStatusCode.OK, missingDetailResponse.StatusCode);

        var missingDetail = await missingDetailResponse.Content.ReadFromJsonAsync<FinanceTransactionDetailResponse>();
        Assert.NotNull(missingDetail);
        Assert.Equal(seed.MissingTransactionId, missingDetail!.Id);
        Assert.Equal("software", missingDetail.Category);
        Assert.Equal(-89.42m, missingDetail.Amount);
        Assert.Equal("USD", missingDetail.Currency);
        Assert.Equal("Missing linked document transaction", missingDetail.Description);
        Assert.False(missingDetail.IsFlagged);
        Assert.Equal("clear", missingDetail.AnomalyState);
        Assert.Empty(missingDetail.Flags);
        Assert.Equal("missing", missingDetail.LinkedDocument.Availability);
        Assert.Equal("Linked document is no longer available.", missingDetail.LinkedDocument.Message);
        Assert.False(missingDetail.LinkedDocument.CanNavigate);
        Assert.Null(missingDetail.LinkedDocument.Document);

        using var approverClient = FinanceDetailTestScenarioFactory.CreateAuthenticatedClient(
            _factory,
            seed.ApproverSubject,
            seed.ApproverEmail,
            seed.ApproverDisplayName);

        var inaccessibleDetailResponse = await approverClient.GetAsync($"/internal/companies/{seed.CompanyId}/finance/transactions/{seed.RestrictedTransactionId}");
        Assert.Equal(HttpStatusCode.OK, inaccessibleDetailResponse.StatusCode);

        var inaccessibleDetail = await inaccessibleDetailResponse.Content.ReadFromJsonAsync<FinanceTransactionDetailResponse>();
        Assert.NotNull(inaccessibleDetail);
        Assert.Equal(seed.RestrictedTransactionId, inaccessibleDetail!.Id);
        Assert.Equal("customer_payment", inaccessibleDetail.Category);
        Assert.Equal(520m, inaccessibleDetail.Amount);
        Assert.Equal("USD", inaccessibleDetail.Currency);
        Assert.Equal("Customer receipt", inaccessibleDetail.Description);
        Assert.False(inaccessibleDetail.Permissions.CanEditTransactionCategory);
        Assert.True(inaccessibleDetail.Permissions.CanChangeInvoiceApprovalStatus);
        Assert.False(inaccessibleDetail.Permissions.CanManagePolicyConfiguration);
        Assert.False(inaccessibleDetail.IsFlagged);
        Assert.Equal("clear", inaccessibleDetail.AnomalyState);
        Assert.Empty(inaccessibleDetail.Flags);
        Assert.Equal("inaccessible", inaccessibleDetail.LinkedDocument.Availability);
        Assert.Equal("Linked document unavailable or you do not have access.", inaccessibleDetail.LinkedDocument.Message);
        Assert.False(inaccessibleDetail.LinkedDocument.CanNavigate);
        Assert.Null(inaccessibleDetail.LinkedDocument.Document);
    }

    [Fact]
    public async Task Transaction_detail_is_tenant_scoped()
    {
        var seed = await FinanceDetailTestScenarioFactory.CreateAsync(_factory);
        using var client = FinanceDetailTestScenarioFactory.CreateAuthenticatedClient(
            _factory,
            seed.OwnerSubject,
            seed.OwnerEmail,
            seed.OwnerDisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/transactions/{seed.OtherCompanyTransactionId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
