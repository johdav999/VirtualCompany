using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceInvoiceDetailMappingIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceInvoiceDetailMappingIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Invoice_list_and_detail_map_summary_fields_and_available_document_metadata_end_to_end()
    {
        var seed = await FinanceDetailTestScenarioFactory.CreateAsync(_factory);
        using var client = FinanceDetailTestScenarioFactory.CreateAuthenticatedClient(
            _factory,
            seed.OwnerSubject,
            seed.OwnerEmail,
            seed.OwnerDisplayName);

        var listResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices?limit=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var invoices = await listResponse.Content.ReadFromJsonAsync<List<FinanceInvoiceResponse>>();
        Assert.NotNull(invoices);

        var invoice = Assert.Single(invoices!, item => item.Id == seed.ReviewInvoiceId);
        Assert.Equal("Adventure Works", invoice.CounterpartyName);
        Assert.Equal("INV-202601-001", invoice.InvoiceNumber);
        Assert.Equal(new DateTime(2025, 11, 18, 0, 0, 0, DateTimeKind.Utc), invoice.IssuedUtc);
        Assert.Equal(new DateTime(2025, 12, 18, 0, 0, 0, DateTimeKind.Utc), invoice.DueUtc);
        Assert.Equal(2725m, invoice.Amount);
        Assert.Equal("USD", invoice.Currency);
        Assert.Equal("open", invoice.Status);
        Assert.NotNull(invoice.LinkedDocument);
        Assert.Equal("Finance supporting document 001", invoice.LinkedDocument!.Title);
        Assert.Equal("finance-document-001.pdf", invoice.LinkedDocument.OriginalFileName);
        Assert.Equal("application/pdf", invoice.LinkedDocument.ContentType);

        var detailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.ReviewInvoiceId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<FinanceInvoiceDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(seed.ReviewInvoiceId, detail!.Id);
        Assert.Equal(invoice.CounterpartyId, detail.CounterpartyId);
        Assert.Equal(invoice.CounterpartyName, detail.CounterpartyName);
        Assert.Equal(invoice.InvoiceNumber, detail.InvoiceNumber);
        Assert.Equal(invoice.IssuedUtc, detail.IssuedUtc);
        Assert.Equal(invoice.DueUtc, detail.DueUtc);
        Assert.Equal(invoice.Amount, detail.Amount);
        Assert.Equal(invoice.Currency, detail.Currency);
        Assert.Equal(invoice.Status, detail.Status);
        Assert.NotNull(detail.Permissions);
        Assert.True(detail.Permissions.CanEditTransactionCategory);
        Assert.True(detail.Permissions.CanChangeInvoiceApprovalStatus);
        Assert.True(detail.Permissions.CanManagePolicyConfiguration);
        Assert.Null(detail.WorkflowContext);
        Assert.Equal("available", detail.LinkedDocument.Availability);
        Assert.Equal("Linked document available.", detail.LinkedDocument.Message);
        Assert.True(detail.LinkedDocument.CanNavigate);
        Assert.NotNull(detail.LinkedDocument.Document);
        Assert.Equal(invoice.LinkedDocument.Id, detail.LinkedDocument.Document!.Id);
        Assert.Equal(invoice.LinkedDocument.Title, detail.LinkedDocument.Document.Title);
        Assert.Equal(invoice.LinkedDocument.OriginalFileName, detail.LinkedDocument.Document.OriginalFileName);
        Assert.Equal(invoice.LinkedDocument.ContentType, detail.LinkedDocument.Document.ContentType);
    }

    [Fact]
    public async Task Invoice_detail_maps_workflow_context_and_missing_document_status_without_blocking_rendering()
    {
        var seed = await FinanceDetailTestScenarioFactory.CreateAsync(_factory);
        using var client = FinanceDetailTestScenarioFactory.CreateAuthenticatedClient(
            _factory,
            seed.OwnerSubject,
            seed.OwnerEmail,
            seed.OwnerDisplayName);

        var reviewResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.MissingInvoiceId}/review-workflow",
            new { });
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);

        var detailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.MissingInvoiceId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<FinanceInvoiceDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(seed.MissingInvoiceId, detail!.Id);
        Assert.Equal("Adventure Works", detail.CounterpartyName);
        Assert.Equal("INV-202601-001-MISSING", detail.InvoiceNumber);
        Assert.Equal(new DateTime(2025, 11, 21, 0, 0, 0, DateTimeKind.Utc), detail.IssuedUtc);
        Assert.Equal(new DateTime(2025, 12, 21, 0, 0, 0, DateTimeKind.Utc), detail.DueUtc);
        Assert.Equal(2767m, detail.Amount);
        Assert.Equal("USD", detail.Currency);
        Assert.Equal("pending_approval", detail.Status);
        Assert.True(detail.Permissions.CanChangeInvoiceApprovalStatus);
        Assert.True(detail.Permissions.CanManagePolicyConfiguration);
        Assert.Equal("missing", detail.LinkedDocument.Availability);
        Assert.NotNull(detail.RecommendationDetails);
        Assert.Equal("overdue_invoice", detail.RecommendationDetails!.Classification);
        Assert.Equal("medium", detail.RecommendationDetails.Risk);
        Assert.Equal("request_human_approval", detail.RecommendationDetails.RecommendedAction);
        Assert.Equal("awaiting_approval", detail.RecommendationDetails.CurrentWorkflowStatus);
        Assert.NotNull(detail.WorkflowHistory);
        Assert.NotEmpty(detail.WorkflowHistory);
        Assert.Equal(
            detail.WorkflowHistory.Select(x => x.EventId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            detail.WorkflowHistory.Count(x => !string.IsNullOrWhiteSpace(x.EventId)));
        Assert.True(detail.WorkflowHistory.SequenceEqual(
            InvoiceWorkflowPresentation.NormalizeWorkflowHistory(detail.WorkflowHistory)));
        Assert.Contains(detail.WorkflowHistory, item => item.RelatedApprovalId == detail.WorkflowContext!.ApprovalRequestId);
        Assert.Equal("Linked document is no longer available.", detail.LinkedDocument.Message);
        Assert.False(detail.LinkedDocument.CanNavigate);
        Assert.Null(detail.LinkedDocument.Document);

        Assert.NotNull(detail.WorkflowContext);
        Assert.Equal("Invoice review workflow", detail.WorkflowContext!.WorkflowName);
        Assert.Equal("awaiting_approval", detail.WorkflowContext.ReviewTaskStatus);
        Assert.Equal("overdue_invoice", detail.WorkflowContext.Classification);
        Assert.Equal("medium", detail.WorkflowContext.RiskLevel);
        Assert.Equal("request_human_approval", detail.WorkflowContext.RecommendedAction);
        Assert.Equal(0.76m, detail.WorkflowContext.Confidence);
        Assert.True(detail.WorkflowContext.RequiresHumanApproval);
        Assert.True(detail.WorkflowContext.ApprovalRequestId.HasValue);
        Assert.Equal("pending", detail.WorkflowContext.ApprovalStatus);
        Assert.Equal("Awaiting finance_approver approval.", detail.WorkflowContext.ApprovalAssigneeSummary);
        Assert.False(detail.WorkflowContext.CanNavigateToWorkflow);
        Assert.True(detail.WorkflowContext.CanNavigateToApproval);
    }

    [Fact]
    public async Task Invoice_detail_hides_inaccessible_navigation_and_remains_tenant_scoped()
    {
        var seed = await FinanceDetailTestScenarioFactory.CreateAsync(_factory);

        using var approverClient = FinanceDetailTestScenarioFactory.CreateAuthenticatedClient(
            _factory,
            seed.ApproverSubject,
            seed.ApproverEmail,
            seed.ApproverDisplayName);

        var restrictedDetailResponse = await approverClient.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.RestrictedInvoiceId}");
        Assert.Equal(HttpStatusCode.OK, restrictedDetailResponse.StatusCode);

        var restrictedDetail = await restrictedDetailResponse.Content.ReadFromJsonAsync<FinanceInvoiceDetailResponse>();
        Assert.NotNull(restrictedDetail);
        Assert.Equal(seed.RestrictedInvoiceId, restrictedDetail!.Id);
        Assert.Equal("Adventure Works", restrictedDetail.CounterpartyName);
        Assert.Equal("INV-202601-005", restrictedDetail.InvoiceNumber);
        Assert.Equal(new DateTime(2025, 11, 22, 0, 0, 0, DateTimeKind.Utc), restrictedDetail.IssuedUtc);
        Assert.Equal(new DateTime(2025, 12, 22, 0, 0, 0, DateTimeKind.Utc), restrictedDetail.DueUtc);
        Assert.Equal(4025m, restrictedDetail.Amount);
        Assert.Equal("USD", restrictedDetail.Currency);
        Assert.Equal("open", restrictedDetail.Status);
        Assert.False(restrictedDetail.Permissions.CanEditTransactionCategory);
        Assert.True(restrictedDetail.Permissions.CanChangeInvoiceApprovalStatus);
        Assert.False(restrictedDetail.Permissions.CanManagePolicyConfiguration);
        Assert.Null(restrictedDetail.WorkflowContext);
        Assert.Equal("inaccessible", restrictedDetail.LinkedDocument.Availability);
        Assert.Equal("Linked document unavailable or you do not have access.", restrictedDetail.LinkedDocument.Message);
        Assert.False(restrictedDetail.LinkedDocument.CanNavigate);
        Assert.Null(restrictedDetail.LinkedDocument.Document);

        using var ownerClient = FinanceDetailTestScenarioFactory.CreateAuthenticatedClient(
            _factory,
            seed.OwnerSubject,
            seed.OwnerEmail,
            seed.OwnerDisplayName);

        var crossTenantDetailResponse = await ownerClient.GetAsync(
            $"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.OtherCompanyInvoiceId}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantDetailResponse.StatusCode);
    }
}
