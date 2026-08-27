using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerInvoiceDeliveryApiSurfaceTests
{
    [Fact]
    public void Preferred_delivery_requires_accounting_administration_authorization()
    {
        var method = typeof(CustomerInvoiceDeliveryController)
            .GetMethod(nameof(CustomerInvoiceDeliveryController.PreferredDelivery))!;

        Assert.Equal(CompanyPolicies.AccountingAdmin,
            Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>()).Policy);
        Assert.Single(method.GetCustomAttributes(true).OfType<HttpMethodAttribute>());
    }

    [Theory]
    [InlineData(nameof(CustomerInvoiceDeliveryController.Electronic))]
    [InlineData(nameof(CustomerInvoiceDeliveryController.RetryElectronic))]
    [InlineData(nameof(CustomerInvoiceDeliveryController.ReconcileElectronic))]
    public void Electronic_delivery_mutations_require_accounting_administration(string methodName)
    {
        var method = typeof(CustomerInvoiceDeliveryController).GetMethod(methodName)!;
        Assert.Equal(CompanyPolicies.AccountingAdmin,
            Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>()).Policy);
        Assert.Single(method.GetCustomAttributes(true).OfType<HttpMethodAttribute>());
    }

    [Fact]
    public void B2brouter_webhook_is_an_explicit_anonymous_signed_transport_endpoint()
    {
        Assert.NotNull(typeof(B2BRouterWebhookController).GetCustomAttributes(true)
            .SingleOrDefault(x => x is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute));
        var method = typeof(B2BRouterWebhookController).GetMethod(nameof(B2BRouterWebhookController.InvoiceState))!;
        Assert.IsType<Microsoft.AspNetCore.Mvc.HttpPostAttribute>(Assert.Single(method.GetCustomAttributes(true)
            .OfType<HttpMethodAttribute>()));
    }
}
