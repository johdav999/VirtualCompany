using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerInvoiceDraftApiSurfaceTests
{
    [Theory]
    [InlineData(nameof(InternalFinanceController.ListCustomerInvoiceDraftsAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.GetCustomerInvoiceDraftAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.PreviewCustomerInvoiceDraftAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.GetCustomerInvoiceDraftReadinessAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.CreateCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.UpdateCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.CopyCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.DiscardCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.SubmitCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.IssueCustomerInvoiceDraftAsync), CompanyPolicies.AccountingAdmin)]
    public void Draft_routes_enforce_accounting_authorization(string methodName, string policy)
    {
        var method = typeof(InternalFinanceController).GetMethod(methodName)!;
        Assert.Equal(policy, Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Policy);
        Assert.Single(method.GetCustomAttributes(true).OfType<HttpMethodAttribute>());
    }

    [Fact]
    public void Prompt_three_surface_exposes_only_the_atomic_issue_action()
    {
        var draftMethods = typeof(InternalFinanceController).GetMethods()
            .Where(method => method.Name.Contains("CustomerInvoiceDraft", StringComparison.Ordinal))
            .Select(method => method.Name).ToArray();

        Assert.Contains(nameof(InternalFinanceController.IssueCustomerInvoiceDraftAsync), draftMethods);
        Assert.DoesNotContain(draftMethods, name => name.Contains("Post", StringComparison.Ordinal));
        Assert.DoesNotContain(draftMethods, name => name.Contains("Render", StringComparison.Ordinal));
        Assert.DoesNotContain(draftMethods, name => name.Contains("Send", StringComparison.Ordinal));
    }
}
