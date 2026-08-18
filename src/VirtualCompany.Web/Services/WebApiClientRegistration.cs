using VirtualCompany.Web.Localization.Formatting;

namespace VirtualCompany.Web.Services;

public static class WebApiClientRegistration
{
    public static IServiceCollection AddVirtualCompanyApiClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ICompanyApiTransport, CompanyApiTransport>();
        services.AddScoped(sp => new OnboardingApiClient(
            sp.GetRequiredService<HttpClient>(),
            IsOffline(sp),
            sp.GetRequiredService<ICompanyPresentationContext>(),
            sp.GetRequiredService<IApiProblemMessageResolver>()));
        services.AddScoped(sp => new AgentApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp), sp.GetRequiredService<IApiProblemMessageResolver>()));
        services.AddScoped(sp => new WorkflowApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new ApprovalApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new InboxApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new AuditApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new TaskApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new DirectChatApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new ExecutiveCockpitApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new AgentStaffOverviewApiClient(
            sp.GetRequiredService<ICompanyApiTransport>(),
            IsOffline(sp),
            sp.GetRequiredService<IApiProblemMessageResolver>()));
        services.AddScoped(sp => new ActionInsightApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new TodayFocusApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new ActivityFeedApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped<CompanyOperationApiClient>();
        services.AddScoped(sp => new FinanceApiClient(
            sp.GetRequiredService<ICompanyApiTransport>(),
            sp.GetRequiredService<ILogger<FinanceApiClient>>(),
            IsOffline(sp),
            configuration["FinanceUi:SourceFilter"],
            sp.GetRequiredService<IApiProblemMessageResolver>()));
        services.AddScoped(sp => new DashboardSummaryApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new SalesApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp), sp.GetRequiredService<IApiProblemMessageResolver>()));
        services.AddScoped(sp => new MarketingApiClient(sp.GetRequiredService<ICompanyApiTransport>(), IsOffline(sp)));
        services.AddScoped(sp => new GuidedWorkApiClient(sp.GetRequiredService<ICompanyApiTransport>(), IsOffline(sp)));
        services.AddScoped(sp => new SalesAutomationApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp)));
        services.AddScoped(sp => new SupportApiClient(sp.GetRequiredService<HttpClient>(), IsOffline(sp), sp.GetRequiredService<IApiProblemMessageResolver>()));
        services.AddScoped<FinanceIntegrationApplicationApiClient>();
        return services;
    }

    private static bool IsOffline(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(configuration["ApiBaseUrl"]))
        {
            return false;
        }

        var context = provider.GetRequiredService<IHttpContextAccessor>().HttpContext;
        if (context is null)
        {
            return true;
        }

        return string.Equals(context.Request.Host.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Host.Host is "127.0.0.1" or "::1";
    }
}
