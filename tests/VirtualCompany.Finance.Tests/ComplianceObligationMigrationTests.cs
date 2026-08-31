namespace VirtualCompany.Finance.Tests;

public sealed class ComplianceObligationMigrationTests
{
    [Fact]
    public void Migration_contains_tenant_indexes_and_no_unrelated_report_schema()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!Directory.Exists(Path.Combine(directory.FullName,"src")))directory=directory.Parent;var root=directory?.FullName??throw new DirectoryNotFoundException("Repository root was not found.");
        var path=Directory.GetFiles(Path.Combine(root,"src","VirtualCompany.Persistence.Migrations","Persistence","Migrations"),"*AddComplianceObligationCalendar.cs").Single();
        var migration=File.ReadAllText(path);
        Assert.Contains("compliance_obligation_instances_origin",migration,StringComparison.Ordinal);
        Assert.Contains("CompanyId",migration,StringComparison.Ordinal);
        Assert.Contains("due_date",migration,StringComparison.Ordinal);
        Assert.DoesNotContain("report_definitions",migration,StringComparison.Ordinal);
    }

    [Fact]
    public void Accountant_fixture_keeps_pack_hash_and_human_review_boundary()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!Directory.Exists(Path.Combine(directory.FullName,"src")))directory=directory.Parent;var root=directory?.FullName??throw new DirectoryNotFoundException("Repository root was not found.");
        var fixture=File.ReadAllText(Path.Combine(root,"tests","VirtualCompany.Finance.Tests","Fixtures","Compliance","swedish-vat-obligation-explicit-deadline.json"));
        Assert.Contains("f7dd2403535ebd51e5e97137cff2aa629da09768cc45cc6a37fbf667d53b3eb6",fixture,StringComparison.Ordinal);
        Assert.Contains("operator_supplied_authority_source_for_fixture_only",fixture,StringComparison.Ordinal);
        Assert.Contains("human_accountant_review_pending",fixture,StringComparison.Ordinal);
    }

    [Fact]
    public void Service_keeps_generation_tenant_idempotency_and_authority_boundaries_explicit()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!Directory.Exists(Path.Combine(directory.FullName,"src")))directory=directory.Parent;var root=directory?.FullName??throw new DirectoryNotFoundException("Repository root was not found.");
        var service=File.ReadAllText(Path.Combine(root,"src","VirtualCompany.Infrastructure.Finance","Finance","ComplianceObligationService.cs"));
        Assert.Contains("explicit_due_date_required",service,StringComparison.Ordinal);
        Assert.Contains("idempotency_payload_mismatch",service,StringComparison.Ordinal);
        Assert.Contains("x.CompanyId==command.CompanyId",service,StringComparison.Ordinal);
        Assert.Contains("submission_evidence_review_required",service,StringComparison.Ordinal);
        Assert.Contains("RequireApproveAsync",service,StringComparison.Ordinal);
        Assert.Contains("AuthorityReceived",service,StringComparison.Ordinal);
    }
}
