using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingCloseApiSurfaceTests
{
    [Theory]
    [InlineData("CreateTemplateAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("VersionTemplateAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("CopyTemplateAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("ActivateTemplateAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("RetireTemplateAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("StartAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("AssignAsync", CompanyPolicies.FinanceEdit)]
    [InlineData("CompleteAsync", CompanyPolicies.FinanceEdit)]
    [InlineData("ReopenAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("CancelTaskAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("CancelAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("AddBlockerAsync", CompanyPolicies.FinanceEdit)]
    [InlineData("ResolveBlockerAsync", CompanyPolicies.AccountingAdmin)]
    public void Mutation_endpoints_require_the_expected_company_policy(string methodName, string policy)
    {
        var method = typeof(AccountingCloseController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.Contains(method!.GetCustomAttributes<AuthorizeAttribute>(), attribute => attribute.Policy == policy);
    }

    [Fact]
    public void All_close_entities_have_company_query_filters()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>().Model;
        var types = new[]
        {
            typeof(AccountingCloseTemplate), typeof(AccountingCloseTemplateVersion),
            typeof(AccountingCloseTemplateSection), typeof(AccountingCloseTaskDefinition),
            typeof(AccountingCloseTaskDefinitionDependency), typeof(AccountingCloseEvidenceRequirement),
            typeof(AccountingCloseTemplateHistory), typeof(AccountingCloseInstance), typeof(AccountingCloseTask),
            typeof(AccountingCloseTaskDependency), typeof(AccountingCloseTaskEvidence), typeof(AccountingCloseTaskNote),
            typeof(AccountingCloseTaskBlocker), typeof(AccountingCloseStatusHistory), typeof(AccountingCloseOperation)
        };
        foreach (var type in types)
            Assert.True(model.FindEntityType(type)?.GetQueryFilter() is not null, $"{type.Name} must have a company query filter.");
    }

    [Fact]
    public void Migration_contains_version_graph_instance_history_and_idempotency_storage()
    {
        var migrationPath = Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Persistence.Migrations",
            "Persistence", "Migrations"), "*AddAccountingCloseOrchestration.cs").Single();
        var migration = File.ReadAllText(migrationPath);
        Assert.Contains("accounting_close_template_versions", migration, StringComparison.Ordinal);
        Assert.Contains("accounting_close_task_definition_dependencies", migration, StringComparison.Ordinal);
        Assert.Contains("accounting_close_instances", migration, StringComparison.Ordinal);
        Assert.Contains("accounting_close_status_history", migration, StringComparison.Ordinal);
        Assert.Contains("accounting_close_operations", migration, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
