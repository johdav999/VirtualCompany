using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerInvoiceDraftApiSurfaceTests
{
    [Theory]
    [InlineData(nameof(InternalFinanceCustomerInvoiceDraftsController.ListCustomerInvoiceDraftsAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceDraftsController.GetCustomerInvoiceDraftAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceDraftsController.PreviewCustomerInvoiceDraftAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceDraftsController.GetCustomerInvoiceDraftReadinessAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceDraftsController.CreateCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceDraftsController.UpdateCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceDraftsController.CopyCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceDraftsController.DiscardCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceDraftsController.SubmitCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceDraftsController.IssueCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    public void Draft_routes_enforce_accounting_authorization(string methodName, string policy)
    {
        var method = typeof(InternalFinanceCustomerInvoiceDraftsController).GetMethod(methodName)!;
        Assert.Equal(policy, Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Policy);
        Assert.Single(method.GetCustomAttributes(true).OfType<HttpMethodAttribute>());
    }

    [Fact]
    public void Prompt_three_surface_exposes_only_the_atomic_issue_action()
    {
        var draftMethods = typeof(InternalFinanceCustomerInvoiceDraftsController).GetMethods()
            .Where(method => method.Name.Contains("CustomerInvoiceDraft", StringComparison.Ordinal))
            .Select(method => method.Name).ToArray();

        Assert.Contains(nameof(InternalFinanceCustomerInvoiceDraftsController.IssueCustomerInvoiceDraftAsync), draftMethods);
        Assert.DoesNotContain(draftMethods, name => name.Contains("Post", StringComparison.Ordinal));
        Assert.DoesNotContain(draftMethods, name => name.Contains("Render", StringComparison.Ordinal));
        Assert.DoesNotContain(draftMethods, name => name.Contains("Send", StringComparison.Ordinal));
    }
}
