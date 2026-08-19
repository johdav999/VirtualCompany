using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Marketing;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Infrastructure.Support;

namespace VirtualCompany.Api.Tests;

public sealed class GuidedArtifactDefinitionContractTests
{
    [Fact]
    public void Artifact_types_and_field_paths_are_stable_and_unique()
    {
        var definitions=Definitions();
        Assert.Equal(definitions.Length,definitions.Select(x=>x.ArtifactType).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach(var definition in definitions)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.SchemaVersion));
            Assert.NotEmpty(definition.Fields);
            Assert.Equal(definition.Fields.Count,definition.Fields.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(definition.Fields,field=>{Assert.Matches("^[a-z][a-z0-9_]*$",field.Path);Assert.False(string.IsNullOrWhiteSpace(field.Label));Assert.False(string.IsNullOrWhiteSpace(field.Description));});
        }
    }

    [Fact]
    public void Every_artifact_has_required_fields_and_bounded_text()
    {
        foreach(var definition in Definitions())
        {
            Assert.Contains(definition.Fields,x=>x.IsRequired);
            Assert.All(definition.Fields.Where(x=>x.ValueType==GuidedFieldValueTypes.Text),field=>Assert.True(field.MaxLength is >0 and <=12000));
        }
    }

    [Fact]
    public void Supported_artifact_catalog_is_complete()
    {
        Assert.Equal(new[]{GuidedArtifactTypes.AgentOperatingBrief,GuidedArtifactTypes.CompanyOnboarding,GuidedArtifactTypes.FinanceBudget,GuidedArtifactTypes.MarketingPlan,GuidedArtifactTypes.MarketingSegment,GuidedArtifactTypes.MarketingStrategy,GuidedArtifactTypes.SalesCampaignPlan,GuidedArtifactTypes.SupportSlaPolicy},Definitions().Select(x=>x.ArtifactType).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Marketing_strategy_workshop_covers_stp_four_ps_execution_and_measurement()
    {
        var definition = new MarketingStrategyGuidedArtifactDefinition(null!, null!);
        var paths = definition.Fields.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("1.1", definition.SchemaVersion);
        Assert.Contains("segmentation", paths);
        Assert.Contains("targeting_rationale", paths);
        Assert.Contains("positioning", paths);
        Assert.Contains("product", paths);
        Assert.Contains("price", paths);
        Assert.Contains("place", paths);
        Assert.Contains("promotion", paths);
        Assert.Contains("implementation_roadmap", paths);
        Assert.Contains("success_metrics", paths);
        Assert.Contains("governance", paths);
    }

    [Fact]
    public void Marketing_plan_workshop_covers_grounding_audiences_budget_evidence_and_governed_commit()
    {
        var definition = new MarketingPlanGuidedArtifactDefinition(null!, null!, null!);
        var paths = definition.Fields.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("1.0", definition.SchemaVersion);
        Assert.True(definition.Capabilities.SupportsDocumentAttachments);
        Assert.Contains("strategy_id", paths);
        Assert.Contains("strategy_version", paths);
        Assert.Contains("objective_ids", paths);
        Assert.Contains("primary_segment_version_id", paths);
        Assert.Contains("planned_budget", paths);
        Assert.Contains("evidence_references", paths);
        Assert.Contains("missing_evidence", paths);
    }

    [Fact]
    public void Company_onboarding_exposes_documents_research_and_the_required_company_foundation()
    {
        var definition=new CompanyOnboardingGuidedArtifactDefinition(null!,null!,null!);
        var paths=definition.Fields.Select(x=>x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(definition.Capabilities.SupportsDocumentAttachments);
        Assert.True(definition.Capabilities.SupportsVoiceDocumentSearch);
        Assert.True(definition.Capabilities.SupportsExternalResearch);
        Assert.Contains("company_summary",paths);
        Assert.Contains("target_customers",paths);
        Assert.Contains("products_and_services",paths);
        Assert.Contains("initial_priorities",paths);
    }

    [Fact]
    public void Workshop_resume_requires_the_current_artifact_schema_version()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "VirtualCompany.Infrastructure.Operations", "Companies", "GuidedWorkSessionService.cs"));
        Assert.Contains("x.SchemaVersion == definition.SchemaVersion", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static IGuidedArtifactDefinition[] Definitions()=>
    [
        new AgentOperatingBriefGuidedArtifactDefinition(null!),
        new CompanyOnboardingGuidedArtifactDefinition(null!,null!,null!),
        new MarketingStrategyGuidedArtifactDefinition(null!,null!),
        new MarketingSegmentGuidedArtifactDefinition(null!,null!),
        new MarketingPlanGuidedArtifactDefinition(null!,null!,null!),
        new FinanceBudgetGuidedArtifactDefinition(null!,null!,null!),
        new SalesCampaignGuidedArtifactDefinition(null!,null!,null!),
        new SupportSlaGuidedArtifactDefinition(null!,null!)
    ];
}
