using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Platform;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Infrastructure.Support;

namespace VirtualCompany.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVirtualCompanyInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPlatformInfrastructure(configuration);
        services.AddOperationsInfrastructure(configuration);
        services.AddMailboxInfrastructure(configuration);
        services.AddFinanceInfrastructure(configuration);
        services.AddSalesInfrastructure(configuration);
        services.AddSupportInfrastructure(configuration);
        return services;
    }
}
