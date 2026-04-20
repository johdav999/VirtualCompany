using VirtualCompany.Web.Services;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAccessResolverTests
{
    private readonly FinanceAccessResolver _resolver = new();

    [Fact]
    public void Resolve_uses_active_company_context_for_finance_access()
    {
        var companyId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(
            memberships: [CreateMembership(companyId, "Company A", "owner", "active")],
            activeCompany: CreateActiveCompany(companyId, "Company A", "owner", "active"));

        var state = _resolver.Resolve(currentUser, null);

        Assert.True(state.IsAllowed);
        Assert.Equal(companyId, state.CompanyId);
        Assert.Equal("Company A", state.CompanyName);
        Assert.Equal($"/finance?companyId={companyId}", _resolver.BuildNavigationHref(currentUser));
    }

    [Fact]
    public void Resolve_allows_single_active_membership_without_preselected_company()
    {
        var companyId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(
            memberships: [CreateMembership(companyId, "Company A", "finance_approver", "active")],
            activeCompany: null);

        var state = _resolver.Resolve(currentUser, null);

        Assert.True(state.IsAllowed);
        Assert.Equal(companyId, state.CompanyId);
        Assert.Equal("finance_approver", state.MembershipRole);
    }

    [Fact]
    public void Resolve_hides_navigation_when_active_company_membership_lacks_finance_view_permission()
    {
        var companyId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(
            memberships: [CreateMembership(companyId, "Company A", "employee", "active")],
            activeCompany: CreateActiveCompany(companyId, "Company A", "employee", "active"));

        var state = _resolver.Resolve(currentUser, null);

        Assert.True(state.IsForbidden);
        Assert.False(_resolver.CanShowNavigation(currentUser));
        Assert.Null(_resolver.BuildNavigationHref(currentUser));
    }

    [Fact]
    public void Resolve_forbids_requested_company_when_membership_lacks_finance_view_permission()
    {
        var companyId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(
            memberships: [CreateMembership(companyId, "Company A", "employee", "active")],
            activeCompany: null);

        var state = _resolver.Resolve(currentUser, companyId);

        Assert.True(state.IsForbidden);
    }

    [Fact]
    public void Resolve_allows_requested_company_when_user_has_finance_view_access()
    {
        var companyId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(
            memberships:
            [
                CreateMembership(companyId, "Company A", "manager", "active"),
                CreateMembership(Guid.NewGuid(), "Company B", "employee", "active")
            ],
            activeCompany: null);

        var state = _resolver.Resolve(currentUser, companyId);
        Assert.True(state.IsAllowed);
        Assert.Equal(companyId, state.CompanyId);
    }

    [Fact]
    public void Resolve_requires_company_selection_when_multiple_active_memberships_exist()
    {
        var currentUser = CreateCurrentUser(
            memberships:
            [
                CreateMembership(Guid.NewGuid(), "Company A", "owner", "active"),
                CreateMembership(Guid.NewGuid(), "Company B", "admin", "active")
            ],
            activeCompany: null);

        var state = _resolver.Resolve(currentUser, null);

        Assert.True(state.RequiresCompanySelection);
        Assert.False(_resolver.CanShowNavigation(currentUser));
        Assert.Null(_resolver.BuildNavigationHref(currentUser));
    }

    [Fact]
    public void Finance_edit_permissions_distinguish_view_only_roles_from_edit_roles()
    {
        Assert.True(FinanceAccess.CanView("finance_approver"));
        Assert.True(FinanceAccess.CanView("tester"));
        Assert.False(FinanceAccess.CanEdit("finance_approver"));
        Assert.False(FinanceAccess.CanEdit("tester"));
        Assert.True(FinanceAccess.CanEdit("manager"));
        Assert.True(FinanceAccess.CanEdit("owner"));
        Assert.False(FinanceAccess.CanEdit("employee"));
    }

    [Fact]
    public void Finance_sandbox_admin_permissions_only_allow_admin_owner_and_tester_roles()
    {
        Assert.True(FinanceAccess.CanAccessSandboxAdmin("admin"));
        Assert.True(FinanceAccess.CanAccessSandboxAdmin("owner"));
        Assert.True(FinanceAccess.CanAccessSandboxAdmin("tester"));
        Assert.False(FinanceAccess.CanAccessSandboxAdmin("manager"));
        Assert.False(FinanceAccess.CanAccessSandboxAdmin("finance_approver"));
        Assert.False(FinanceAccess.CanAccessSandboxAdmin("employee"));
    }

    [Fact]
    public void Finance_approval_permissions_distinguish_approvers_from_non_finance_roles()
    {
        Assert.True(FinanceAccess.CanApproveInvoices("finance_approver"));
        Assert.True(FinanceAccess.CanApproveInvoices("manager"));
        Assert.True(FinanceAccess.CanApproveInvoices("owner"));
        Assert.False(FinanceAccess.CanApproveInvoices("employee"));
    }

    [Fact]
    public void Resolve_forbids_requested_company_outside_active_memberships()
    {
        var currentUser = CreateCurrentUser(
            memberships: [CreateMembership(Guid.NewGuid(), "Company A", "employee", "active")],
            activeCompany: null);

        var state = _resolver.Resolve(currentUser, Guid.NewGuid());

        Assert.True(state.IsForbidden);
    }

    [Fact]
    public void Resolve_forbids_finance_access_without_active_membership()
    {
        var currentUser = CreateCurrentUser(
            memberships: [CreateMembership(Guid.NewGuid(), "Company A", "employee", "pending")],
            activeCompany: null);

        var state = _resolver.Resolve(currentUser, null);

        Assert.True(state.IsForbidden);
        Assert.False(_resolver.CanShowNavigation(currentUser));
    }

    private static CurrentUserContextViewModel CreateCurrentUser(
        List<CompanyMembershipViewModel> memberships,
        ResolvedCompanyContextViewModel? activeCompany) =>
        new()
        {
            Memberships = memberships,
            ActiveCompany = activeCompany
        };

    private static CompanyMembershipViewModel CreateMembership(
        Guid companyId,
        string companyName,
        string role,
        string status) =>
        new()
        {
            CompanyId = companyId,
            CompanyName = companyName,
            MembershipRole = role,
            Status = status
        };

    private static ResolvedCompanyContextViewModel CreateActiveCompany(Guid companyId, string companyName, string role, string status) =>
        new() { CompanyId = companyId, CompanyName = companyName, MembershipRole = role, Status = status };
}
