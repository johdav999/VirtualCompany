using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerCollectionsApiSurfaceTests
{
    [Theory]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.GetCustomerAgingAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.ListCustomerStatementsAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.GetCustomerStatementAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.DownloadCustomerStatementAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.GetCustomerCollectionPolicyAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.ListCustomerCollectionCasesAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.GetCustomerCollectionMetricsAsync), CompanyPolicies.AccountingView)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.GenerateCustomerStatementAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.UpsertCustomerCollectionPolicyAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.RecordCustomerDisputeAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.ResolveCustomerDisputeAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.RecordPromiseToPayAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.ResolvePromiseToPayAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.RecordCustomerCollectionResponseAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.PrepareCustomerReminderAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.SendCustomerReminderAsync), CompanyPolicies.AccountingAdmin)]
    [InlineData(nameof(InternalFinanceCustomerCollectionsController.RunCustomerCollectionWorkerAsync), CompanyPolicies.AccountingAdmin)]
    public void Collection_routes_enforce_accounting_authorization(string methodName, string policy)
    {
        var method = typeof(InternalFinanceCustomerCollectionsController).GetMethod(methodName)!;
        Assert.Equal(policy, Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);
        Assert.Single(method.GetCustomAttributes(true).OfType<HttpMethodAttribute>());
    }
}
