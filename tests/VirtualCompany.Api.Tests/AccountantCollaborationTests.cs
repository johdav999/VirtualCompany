using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Shared;

namespace VirtualCompany.Api.Tests;

public sealed class AccountantCollaborationTests
{
    [Fact]
    public void Accountant_role_is_read_only_in_the_general_finance_boundary()
    {
        Assert.True(FinanceAccess.CanView("accountant"));
        Assert.False(FinanceAccess.CanEdit("accountant"));
        Assert.False(FinanceAccess.CanApproveInvoices("accountant"));
        Assert.False(FinanceAccess.CanManageAccounting("accountant"));
    }

    [Fact]
    public void Grant_requires_independent_approval_and_revocation_stops_access()
    {
        var inviter = Guid.NewGuid();
        var grant = new AccountantCompanyGrant(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            AccountantGrantScopes.AccountingReview, true, true, true, DateTime.UtcNow.AddMinutes(-1), null, inviter, DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => grant.Approve(inviter, DateTime.UtcNow));
        grant.Approve(Guid.NewGuid(), DateTime.UtcNow);
        Assert.True(grant.IsEffectiveAt(DateTime.UtcNow));

        grant.Revoke(Guid.NewGuid(), "Engagement ended", DateTime.UtcNow);
        Assert.False(grant.IsEffectiveAt(DateTime.UtcNow));
        Assert.NotNull(grant.RevokedUtc);
    }

    [Fact]
    public void Collaboration_routes_require_the_explicit_policy_and_grant_management_is_admin_only()
    {
        var controllerPolicy = Assert.Single(typeof(AccountantCollaborationController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(CompanyPolicies.AccountantCollaboration, controllerPolicy.Policy);

        var createGrant = typeof(AccountantCollaborationController).GetMethod(nameof(AccountantCollaborationController.CreateGrantAsync))!;
        var managementPolicy = Assert.Single(createGrant.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(CompanyPolicies.CompanyOwnerOrAdmin, managementPolicy.Policy);
    }

    [Fact]
    public void Migration_contains_only_the_seven_collaboration_tables()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Persistence.Migrations", "Persistence", "Migrations", "20260830230000_AddExternalAccountantCollaboration.cs"));
        var tables = new[] { "accountant_company_grants", "accountant_review_engagements", "accountant_review_items",
            "accountant_evidence_requests", "accountant_evidence_responses", "accountant_engagement_signoffs", "accountant_review_history" };
        foreach (var table in tables) Assert.Contains($"name: \"{table}\"", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("vat_filing_periods", migration, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
