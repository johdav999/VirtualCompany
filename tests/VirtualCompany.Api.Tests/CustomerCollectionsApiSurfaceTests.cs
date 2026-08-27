using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerCollectionsApiSurfaceTests
{
    [Theory]
    [InlineData(nameof(InternalFinanceController.GetCustomerAgingAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.ListCustomerStatementsAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.GetCustomerStatementAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.DownloadCustomerStatementAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.GetCustomerCollectionPolicyAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.ListCustomerCollectionCasesAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.GetCustomerCollectionMetricsAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceController.GenerateCustomerStatementAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.UpsertCustomerCollectionPolicyAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.RecordCustomerDisputeAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.ResolveCustomerDisputeAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.RecordPromiseToPayAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.ResolvePromiseToPayAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.RecordCustomerCollectionResponseAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.PrepareCustomerReminderAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.SendCustomerReminderAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceController.RunCustomerCollectionWorkerAsync), CompanyPolicies.AccountingAdmin)]
    public void Collection_routes_enforce_accounting_authorization(string methodName, string policy)
    {
        var method = typeof(InternalFinanceController).GetMethod(methodName)!;
        Assert.Equal(policy, Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);
        Assert.Single(method.GetCustomAttributes(true).OfType<HttpMethodAttribute>());
    }
}
