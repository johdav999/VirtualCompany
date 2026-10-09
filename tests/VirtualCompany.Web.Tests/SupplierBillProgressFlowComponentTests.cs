using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Localization.Formatting;

namespace VirtualCompany.Api.Tests;

public sealed class SupplierBillProgressFlowComponentTests
{
    [Fact]
    public void Flow_distinguishes_completed_current_and_upcoming_steps()
    {
        var progress = new SupplierBillProgressViewModel(
        [
            new("received", "Invoice received", "Invoice is available.", SupplierBillProgressStates.Completed, "Complete"),
            new("approval", "Payment approved", "A decision is required.", SupplierBillProgressStates.Current, "Action needed"),
            new("registered", "Payment registered", "Register after approval.", SupplierBillProgressStates.Upcoming, "Upcoming")
        ],
        "approval");

        using var context = CreateContext();
        var cut = context.RenderComponent<SupplierBillProgressFlow>(parameters => parameters
            .Add(component => component.Progress, progress)
            .Add(component => component.CurrentStepContent, builder => builder.AddMarkupContent(0, "<button>Approve payment</button>")));

        Assert.Contains("1 of 3 steps complete", cut.Markup);
        Assert.Contains("supplier-bill-progress__step--completed", cut.Markup);
        Assert.Contains("supplier-bill-progress__step--current", cut.Markup);
        Assert.Contains("supplier-bill-progress__step--upcoming", cut.Markup);
        Assert.Equal("step", cut.Find("[aria-current='step']").GetAttribute("aria-current"));
        Assert.Contains("Approve payment", cut.Find("[aria-current='step']").TextContent);
    }

    [Fact]
    public void Flow_exposes_failed_step_as_the_current_recovery_point()
    {
        var progress = new SupplierBillProgressViewModel(
        [
            new("received", "Invoice received", "Invoice is available.", SupplierBillProgressStates.Completed, "Complete"),
            new("fortnox", "Sent to Fortnox", "Retry the provider action.", SupplierBillProgressStates.Failed, "Retry needed"),
            new("approval", "Payment approved", "Approval follows.", SupplierBillProgressStates.Upcoming, "Upcoming")
        ],
        "fortnox");

        using var context = CreateContext();
        var cut = context.RenderComponent<SupplierBillProgressFlow>(parameters => parameters
            .Add(component => component.Progress, progress));

        var current = cut.Find("[aria-current='step']");
        Assert.Contains("supplier-bill-progress__step--failed", current.GetAttribute("class"));
        Assert.Contains("Retry needed", current.TextContent);
        Assert.Contains("Payment approved", cut.Markup);
    }

    private static TestContext CreateContext()
    {
        var context = new TestContext();
        context.Services.AddLocalization();
        var presentationContext = new CompanyPresentationContext();
        presentationContext.SetFormattingCulture("en-US");
        context.Services.AddSingleton<ICompanyPresentationContext>(presentationContext);
        context.Services.AddSingleton<ILocalDateTimeFormatter, LocalDateTimeFormatter>();
        context.Services.AddSingleton<INumberFormatter, NumberFormatter>();
        context.Services.AddSingleton<IMoneyFormatter, MoneyFormatter>();
        return context;
    }
}
