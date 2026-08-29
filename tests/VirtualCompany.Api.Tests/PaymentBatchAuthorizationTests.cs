using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class PaymentBatchAuthorizationTests
{
    [Fact]
    public void Payment_batch_api_requires_company_finance_view_and_separates_edit_from_approval()
    {
        var type = typeof(InternalPaymentBatchesController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(),
            x => x.Policy == CompanyPolicies.FinanceView);
        Assert.NotNull(type.GetCustomAttribute<RequireCompanyContextAttribute>());

        foreach (var methodName in new[]
                 {
                     nameof(InternalPaymentBatchesController.CreateAsync),
                     nameof(InternalPaymentBatchesController.AddObligationAsync),
                     nameof(InternalPaymentBatchesController.RemoveObligationAsync),
                     nameof(InternalPaymentBatchesController.ValidateAsync),
                     nameof(InternalPaymentBatchesController.SubmitAsync),
                     nameof(InternalPaymentBatchesController.CancelAsync),
                     nameof(InternalPaymentBatchesController.RegenerateAsync)
                 })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                x => x.Policy == CompanyPolicies.FinanceEdit);
        }

        foreach (var methodName in new[]
                 {
                     nameof(InternalPaymentBatchesController.RegisterBeneficiaryAsync),
                     nameof(InternalPaymentBatchesController.ApproveAsync),
                     nameof(InternalPaymentBatchesController.RejectAsync)
                 })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                x => x.Policy == CompanyPolicies.FinanceApproval);
        }
    }

    [Fact]
    public void Prompt_6_api_exposes_readiness_but_no_bank_send_mutation()
    {
        var methods = typeof(InternalPaymentBatchesController).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        var postTemplates = methods.SelectMany(x => x.GetCustomAttributes<HttpPostAttribute>())
            .Select(x => x.Template ?? string.Empty)
            .ToArray();

        Assert.Contains(methods, x => x.Name == nameof(InternalPaymentBatchesController.SendReadinessAsync));
        Assert.DoesNotContain(postTemplates, template =>
            template.Contains("send", StringComparison.OrdinalIgnoreCase) ||
            template.Contains("execute", StringComparison.OrdinalIgnoreCase));
    }
}
