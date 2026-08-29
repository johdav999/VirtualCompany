using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class PaymentExecutionAuthorizationTests
{
    [Fact]
    public void Execution_reads_are_company_scoped_and_money_movement_or_recovery_requires_finance_approval()
    {
        var type = typeof(InternalPaymentExecutionsController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(),
            x => x.Policy == CompanyPolicies.FinanceView);
        Assert.NotNull(type.GetCustomAttribute<RequireCompanyContextAttribute>());

        foreach (var methodName in new[]
                 {
                     nameof(InternalPaymentExecutionsController.QueueAsync),
                     nameof(InternalPaymentExecutionsController.CancelAsync),
                     nameof(InternalPaymentExecutionsController.ReconcileAsync),
                     nameof(InternalPaymentExecutionsController.SettleAsync),
                     nameof(InternalPaymentExecutionsController.RetryRemittanceAsync)
                 })
        {
            var method = Assert.Single(type.GetMethods(), x => x.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                x => x.Policy == CompanyPolicies.FinanceApproval);
        }
    }

    [Fact]
    public void Provider_webhook_is_public_only_at_the_signed_provider_specific_ingress()
    {
        var type = typeof(PaymentProviderWebhooksController);
        Assert.NotNull(type.GetCustomAttribute<AllowAnonymousAttribute>());
        var route = type.GetCustomAttribute<RouteAttribute>();
        Assert.Equal("webhooks/finance/payment-initiation", route?.Template);
        var method = Assert.Single(type.GetMethods(), x => x.Name == nameof(PaymentProviderWebhooksController.ReceiveAsync));
        Assert.Equal("{providerKey}", method.GetCustomAttribute<HttpPostAttribute>()?.Template);
    }
}
