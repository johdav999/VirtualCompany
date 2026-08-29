using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BankConnectionsAuthorizationTests
{
    [Fact]
    public void Company_bank_connection_api_requires_finance_view_company_context_and_finance_edit_for_mutations()
    {
        var type = typeof(BankConnectionsController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.FinanceView);
        Assert.NotNull(type.GetCustomAttribute<RequireCompanyContextAttribute>());
        foreach (var methodName in new[] { "ConnectAsync", "RenewAsync", "MapAsync", "RefreshAsync", "SuspendAsync", "DisconnectAsync" })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.FinanceEdit);
        }
    }

    [Fact]
    public void Browser_callback_requires_an_authenticated_user()
    {
        var method = Assert.Single(typeof(BankConnectionCallbacksController).GetMethods(), x => x.Name == "CallbackAsync");
        Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.AuthenticatedUser);
    }

    [Fact]
    public void Bank_feed_health_requires_finance_view_and_mutations_require_finance_edit()
    {
        var type = typeof(BankFeedsController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.FinanceView);
        Assert.NotNull(type.GetCustomAttribute<RequireCompanyContextAttribute>());
        Assert.Empty(Assert.Single(type.GetMethods(), x => x.Name == "GetAsync")
            .GetCustomAttributes<AuthorizeAttribute>());
        foreach (var methodName in new[] { "SynchronizeAsync", "BackfillAsync" })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                x => x.Policy == CompanyPolicies.FinanceEdit);
        }
    }

    [Fact]
    public void Statement_import_center_requires_company_finance_view_and_edit_for_every_mutation()
    {
        var type = typeof(BankStatementImportsController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.FinanceView);
        Assert.NotNull(type.GetCustomAttribute<RequireCompanyContextAttribute>());
        foreach (var methodName in new[] { "PreviewAsync", "CommitAsync", "DecideAsync", "CreateCsvProfileAsync", "CreateCsvProfileVersionAsync" })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.FinanceEdit);
        }
    }

    [Fact]
    public void Advanced_reconciliation_requires_company_finance_view_and_separates_proposal_from_approval()
    {
        var type = typeof(InternalAdvancedReconciliationController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.FinanceView);
        Assert.NotNull(type.GetCustomAttribute<RequireCompanyContextAttribute>());

        var create = Assert.Single(type.GetMethods(), x => x.Name == "CreateAsync");
        Assert.Contains(create.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.FinanceEdit);

        foreach (var methodName in new[] { "CreateRuleAsync", "AcceptAsync", "RejectAsync", "ReverseAsync" })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                x => x.Policy == CompanyPolicies.FinanceApproval);
        }
    }

    [Fact]
    public void Treasury_sources_require_company_context_and_separate_edit_from_approval_authority()
    {
        var type = typeof(InternalTreasurySourcesController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.FinanceView);
        Assert.NotNull(type.GetCustomAttribute<RequireCompanyContextAttribute>());

        foreach (var methodName in new[]
                 {
                     "CreateTransferAsync", "CreateBankAdjustmentAsync", "CreateCardSettlementAsync",
                     "CreatePayoutSettlementAsync", "LinkBankEvidenceAsync", "PreviewAsync"
                 })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                x => x.Policy == CompanyPolicies.FinanceEdit);
        }

        foreach (var methodName in new[] { "BindApprovalAsync", "PostAsync", "ReverseAsync" })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                x => x.Policy == CompanyPolicies.FinanceApproval);
        }
    }
}
