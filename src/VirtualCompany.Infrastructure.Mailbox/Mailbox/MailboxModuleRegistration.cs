using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Mailbox;

namespace VirtualCompany.Infrastructure.Mailbox;

public static class MailboxModuleRegistration
{
    public static IServiceCollection AddMailboxInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MailboxIntegrationOptions>()
            .Bind(configuration.GetSection(MailboxIntegrationOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MailboxIntegrationOptions>, MailboxIntegrationOptionsValidator>());
        services.AddHttpClient(GmailMailboxProviderClient.ClientName);
        services.AddHttpClient(Microsoft365MailboxProviderClient.ClientName);
        services.AddSingleton<IMailboxOAuthStateProtector, DataProtectionMailboxOAuthStateProtector>();
        services.AddSingleton<ICalendarOAuthStateProtector, DataProtectionCalendarOAuthStateProtector>();
        services.AddScoped<IMailboxOAuthReplayGuard, MailboxOAuthReplayGuard>();
        services.AddScoped<GmailMailboxProviderClient>();
        services.AddScoped<Microsoft365MailboxProviderClient>();
        services.AddScoped<StandardMailboxProviderClient>();
        services.AddScoped<IMailboxProviderClient>(provider => provider.GetRequiredService<GmailMailboxProviderClient>());
        services.AddScoped<IMailboxProviderClient>(provider => provider.GetRequiredService<Microsoft365MailboxProviderClient>());
        services.AddScoped<IMailboxProviderClient>(provider => provider.GetRequiredService<StandardMailboxProviderClient>());
        services.AddScoped<IMailboxProviderRegistry, MailboxProviderRegistry>();
        services.AddSingleton<IMailboxConnectionProfileRegistry, StandardMailboxConnectionProfileRegistry>();
        services.AddSingleton<IMailboxEndpointPolicy, SecureMailboxEndpointPolicy>();
        services.AddSingleton<IMailboxOperationConcurrencyGate, MailboxOperationConcurrencyGate>();
        services.AddScoped<MailKitMailboxTransport>();
        services.AddScoped<IMailboxTransport>(provider => provider.GetRequiredService<MailKitMailboxTransport>());
        services.AddScoped<IMailboxTransportRegistry, MailboxTransportRegistry>();
        services.AddScoped<ApplicationPasswordMailboxAuthenticationStrategy>();
        services.AddScoped<OAuthMailboxAuthenticationStrategy>();
        services.AddScoped<IMailboxAuthenticationStrategy>(provider => provider.GetRequiredService<ApplicationPasswordMailboxAuthenticationStrategy>());
        services.AddScoped<IMailboxAuthenticationStrategy>(provider => provider.GetRequiredService<OAuthMailboxAuthenticationStrategy>());
        services.AddScoped<IStandardMailboxConnectionService, StandardMailboxConnectionService>();
        services.AddScoped<IMailboxConnectionService, CompanyMailboxConnectionService>();
        services.AddScoped<ICalendarConnectionService, CalendarConnectionService>();
        services.AddScoped<IMailboxOAuthAccessTokenLeaseService, MailboxOAuthAccessTokenLeaseService>();
        services.AddScoped<ICalendarOAuthAccessTokenLeaseService, CalendarOAuthAccessTokenLeaseService>();
        services.AddScoped<MailboxConnectionCredentialRefresher>();
        services.AddHostedService<MailboxConnectionStartupRefreshBackgroundService>();
        services.AddHostedService<StandardMailboxInboundSyncBackgroundService>();
        services.AddScoped<IBillDetectionService, BillDetectionService>();
        services.AddScoped<IEmailClassificationService, HybridEmailClassificationService>();
        services.AddScoped<IManualInboxBillScanOrchestrator, CompanyManualInboxBillScanOrchestrator>();
        services.AddScoped<IConnectedMailboxInboxScanOrchestrator, CompanyConnectedMailboxInboxScanOrchestrator>();
        services.AddScoped<IConnectedMailboxInboxScanJobScheduler, ScopedConnectedMailboxInboxScanJobScheduler>();
        services.AddScoped<IManualInboxBillScanJobScheduler, InlineManualInboxBillScanJobScheduler>();
        return services;
    }
}
