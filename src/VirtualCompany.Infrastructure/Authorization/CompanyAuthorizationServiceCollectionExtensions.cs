using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Shared;

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

            options.AddPolicy(CompanyPolicies.AuditReview, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CompanyMembershipRoleRequirement(
                        CompanyMembershipRole.Owner,
                        CompanyMembershipRole.Admin,
                        CompanyMembershipRole.Manager,
                        CompanyMembershipRole.FinanceApprover)));

            options.AddPolicy(CompanyPolicies.FinanceView, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CompanyPermissionRequirement(FinancePermissions.View)));

            options.AddPolicy(CompanyPolicies.FinanceEdit, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CompanyPermissionRequirement(FinancePermissions.Edit)));

            options.AddPolicy(CompanyPolicies.FinanceApproval, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CompanyPermissionRequirement(FinancePermissions.Approve)));

            options.AddPolicy(CompanyPolicies.FinanceSandboxAdmin, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CompanyPermissionRequirement(FinancePermissions.SandboxAdmin)));

            options.AddPolicy(CompanyPolicies.CompanyOwnerOrAdmin, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CompanyMembershipRoleRequirement(
                        CompanyMembershipRole.Owner,
                        CompanyMembershipRole.Admin)));
        });

        return services;
    }
}
