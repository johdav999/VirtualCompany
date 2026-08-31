using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class CurrencyRevaluationAuthorizationTests
{
    [Fact]
    public void Revaluation_reads_require_accounting_view_and_mutations_require_accounting_admin()
    {
        var type = typeof(InternalFinanceController);
        foreach (var methodName in new[] { "ListCurrencyRevaluationsAsync", "GetCurrencyRevaluationAsync",
                     "ListCurrencyRevaluationAccountsAsync", "GetCurrencyRevaluationScheduleAsync" })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                x => x.Policy == CompanyPolicies.AccountingView);
        }

        foreach (var methodName in new[] { "PreviewCurrencyRevaluationAsync", "ReviewCurrencyRevaluationItemAsync",
                     "SubmitCurrencyRevaluationAsync", "PostCurrencyRevaluationAsync", "ReverseCurrencyRevaluationAsync",
                     "ConfigureCurrencyRevaluationAccountAsync", "ConfigureCurrencyRevaluationScheduleAsync" })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                x => x.Policy == CompanyPolicies.AccountingAdmin);
        }
    }
}
