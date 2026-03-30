using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Authorization;

public static class CompanyAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddCompanyAuthorization(this IServiceCollection services, IHostEnvironment hostEnvironment)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(CompanyPolicies.AuthenticatedUser, policy =>
            {
                if (hostEnvironment.IsDevelopment())
                {
                    policy.RequireAssertion(_ => true);
                }
                else
                {
                    policy.RequireAuthenticatedUser();
                }
            });

            options.AddPolicy(CompanyPolicies.CompanyMember, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CompanyMembershipRequirement()));

            // Company membership policies authorize human users inside a tenant.
            // They must stay separate from any future agent tool or execution policy.
            options.AddPolicy(CompanyPolicies.CompanyManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CompanyMembershipRoleRequirement(
                        CompanyMembershipRole.Owner,
                        CompanyMembershipRole.Admin,
                        CompanyMembershipRole.Manager)));

            options.AddPolicy(CompanyPolicies.CompanyOwnerOrAdmin, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CompanyMembershipRoleRequirement(
                        CompanyMembershipRole.Owner,
                        CompanyMembershipRole.Admin)));
        });

        return services;
    }
}
