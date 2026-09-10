using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using VirtualCompany.Web.Localization.Shared;
using VirtualCompany.Web.Localization.Formatting;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Api.Tests;

internal static class WebTestContextServiceRegistration
{
    public static TestContext AddVirtualCompanyWebPresentationServices(this TestContext context)
    {
        context.Services.AddLocalization();
        var presentationContext = new CompanyPresentationContext();
        presentationContext.SetFormattingCulture("en-US");
        context.Services.AddSingleton<ICompanyPresentationContext>(presentationContext);
        context.Services.AddSingleton<ILocalDateTimeFormatter, LocalDateTimeFormatter>();
        context.Services.AddSingleton<INumberFormatter, NumberFormatter>();
        context.Services.AddSingleton<IMoneyFormatter, MoneyFormatter>();
        context.Services.AddSingleton<IApiProblemMessageResolver>(serviceProvider => new ApiProblemMessageResolver(
            serviceProvider.GetRequiredService<IStringLocalizer<CommonResources>>()));
        context.Services.AddSingleton(new AgentApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }, useOfflineMode: true));
        context.Services.AddSingleton<AgentStaffOverviewApiClient>(serviceProvider => new AgentStaffOverviewApiClient(
            new CompanyApiTransport(new HttpClient { BaseAddress = new Uri("http://localhost/") }),
            useOfflineMode: true,
            serviceProvider.GetRequiredService<IApiProblemMessageResolver>()));
        context.Services.AddSingleton(new DashboardSummaryApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }, useOfflineMode: true));
        context.Services.AddSingleton(new GuidedWorkApiClient(
            new CompanyApiTransport(new HttpClient { BaseAddress = new Uri("http://localhost/") }),
            offline: true));
        var presentationHttp = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        context.Services.AddSingleton(new SalesPresentationRuntimeClient(
            new CompanyApiTransport(presentationHttp), presentationHttp, useOfflineMode: true));
        context.Services.AddSingleton(new SalesNarrationApiClient(new CompanyApiTransport(new HttpClient(new NarrationUnavailableHandler()) { BaseAddress = new Uri("http://localhost/") })));
        return context;
    }
    private sealed class NarrationUnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) {
                Content = System.Net.Http.Json.JsonContent.Create(new { detail = "Narration is unavailable in this isolated page fixture." }) });
    }
}
