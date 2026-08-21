using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Localization.Formatting;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanySelectionRequiredStateComponentTests
{
    [Fact]
    public void Company_options_preserve_the_current_finance_page_and_query_context()
    {
        var companyAId = Guid.Parse("f9753f1c-26d7-4c24-bba1-b8f66443676f");
        var companyBId = Guid.Parse("091eed2d-5296-4526-8d24-2e04b3b0b225");

        using var context = new TestContext();
        context.Services.AddLocalization();
        var presentationContext = new CompanyPresentationContext();
        presentationContext.SetFormattingCulture("en-US");
        context.Services.AddSingleton<ICompanyPresentationContext>(presentationContext);
        context.Services.AddSingleton<ILocalDateTimeFormatter, LocalDateTimeFormatter>();
        context.Services.AddSingleton<INumberFormatter, NumberFormatter>();
        context.Services.AddSingleton<IMoneyFormatter, MoneyFormatter>();
        context.Services
            .GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/finance/accounting/setup?source=sidebar");

        var cut = context.RenderComponent<CompanySelectionRequiredState>(parameters => parameters
            .Add(component => component.Companies,
            [
                new FinanceCompanyOption(companyAId, "Company A", "owner"),
                new FinanceCompanyOption(companyBId, "Company B", "finance_approver")
            ]));

        var links = cut.FindAll("[data-testid='finance-company-selector'] a");
        Assert.Equal(2, links.Count);
        Assert.Equal(
            $"http://localhost/finance/accounting/setup?source=sidebar&companyId={companyAId:D}",
            links[0].GetAttribute("href"));
        Assert.Equal(
            $"http://localhost/finance/accounting/setup?source=sidebar&companyId={companyBId:D}",
            links[1].GetAttribute("href"));
        Assert.Equal("Open Finance for Company A with Owner access", links[0].GetAttribute("aria-label"));
        Assert.Equal("Open Finance for Company B with Finance approver access", links[1].GetAttribute("aria-label"));
        Assert.Contains("Owner access · Finance workspace", links[0].TextContent);
        Assert.Contains("Finance approver access · Finance workspace", links[1].TextContent);
        Assert.DoesNotContain("Open dashboard", cut.Markup);
    }
}
