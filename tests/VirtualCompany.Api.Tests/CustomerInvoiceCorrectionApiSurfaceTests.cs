using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerInvoiceCorrectionApiSurfaceTests
{
    [Theory]
    [InlineData(nameof(InternalFinanceCustomerInvoiceCorrectionsController.EvaluateCustomerInvoiceCorrectionAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceCorrectionsController.ListCustomerInvoiceCorrectionsAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceCorrectionsController.GetCustomerInvoiceCorrectionAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceCorrectionsController.ProposeCustomerInvoiceCorrectionAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceCorrectionsController.ExecuteCustomerInvoiceCorrectionAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerInvoiceCorrectionsController.ReconcileCustomerInvoiceRefundAsync), CompanyPolicies.AccountingAdmin)]
    public void Correction_routes_enforce_accounting_authorization(string methodName, string policy)
    {
        var method = typeof(InternalFinanceCustomerInvoiceCorrectionsController).GetMethod(methodName)!;
        Assert.Equal(policy, Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Policy);
        Assert.Single(method.GetCustomAttributes(true).OfType<HttpMethodAttribute>());
    }
}
