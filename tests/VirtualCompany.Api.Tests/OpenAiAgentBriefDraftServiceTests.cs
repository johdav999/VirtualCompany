using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class OpenAiAgentBriefDraftServiceTests
{
    [Fact]
    public void SelectGroundingDocuments_UsesOnlySelectedAgentsCategoryAttachments()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var otherAgentId = Guid.NewGuid();
        var matching = CreateDocument(
            companyId,
            "Virtual Company product catalog",
            agentId,
            AgentBriefingCategories.CompanyInformation,
            "Virtual Company provides AI-supported finance, sales, and customer support workflows for SMEs.");
        var seed = CreateSeedDocument(companyId, "Seed finance document 001");
        var wrongCategory = CreateDocument(
            companyId,
            "Support policy",
            agentId,
            AgentBriefingCategories.CustomerSupport,
            "Support policy content.");
        var wrongAgent = CreateDocument(
            companyId,
            "Other agent brief",
            otherAgentId,
            AgentBriefingCategories.CompanyInformation,
            "Other agent content.");

        var selected = OpenAiAgentBriefDraftService.SelectGroundingDocuments(
            [seed, wrongCategory, wrongAgent, matching],
            agentId,
            AgentBriefingCategories.CompanyInformation);

        var document = Assert.Single(selected);
        Assert.Equal(matching.Title, document.Title);
        Assert.Contains("finance, sales, and customer support", document.Excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectGroundingDocuments_IncludesCategoryAttachmentSharedByAnotherAgent()
    {
        var companyId = Guid.NewGuid();
        var selectedAgentId = Guid.NewGuid();
        var ownerAgentId = Guid.NewGuid();
        var shared = CreateDocument(
            companyId,
            "Shared company overview",
            ownerAgentId,
            AgentBriefingCategories.CompanyInformation,
            "Shared company facts.");
        shared.SetMetadataValue("shareWithAgentTeam", JsonValue.Create(true));

        var selected = OpenAiAgentBriefDraftService.SelectGroundingDocuments(
            [shared],
            selectedAgentId,
            AgentBriefingCategories.CompanyInformation);

        Assert.Equal(shared.Title, Assert.Single(selected).Title);
    }

    [Fact]
    public void SelectGroundingDocuments_CollapsesHistoricalDuplicateUploadsByChecksum()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var first = CreateDocument(
            companyId,
            "Product catalog first upload",
            agentId,
            AgentBriefingCategories.ProductsAndServices,
            "Same product catalog content.");
        var second = CreateDocument(
            companyId,
            "Product catalog retry",
            agentId,
            AgentBriefingCategories.ProductsAndServices,
            "Same product catalog content.");
        first.SetMetadataValue("checksum_sha256", JsonValue.Create("same-checksum"));
        second.SetMetadataValue("checksum_sha256", JsonValue.Create("same-checksum"));

        var selected = OpenAiAgentBriefDraftService.SelectGroundingDocuments(
            [first, second],
            agentId,
            AgentBriefingCategories.ProductsAndServices);

        Assert.Single(selected);
    }

    [Fact]
    public void BuildPrompt_CompanyInformation_DoesNotLeakLanguageComplianceOrSeedTitles()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var company = new Company(companyId, "VC");
        company.UpdateWorkspaceProfile(
            "VC",
            "Software",
            "SaaS",
            "Europe/Stockholm",
            "SEK",
            "sq",
            "Australia");
        var agent = new Agent(
            agentId,
            companyId,
            "finance-manager",
            "Laura",
            "Finance Manager",
            "Finance",
            null,
            AgentSeniority.Senior);
        var command = new GenerateAgentBriefDraftCommand(
            AgentBriefingCategories.CompanyInformation,
            "Kompania: VC\nRajoni i perputhshmerise: Australi");
        var grounding = new[]
        {
            new OpenAiAgentBriefDraftService.AgentBriefGroundingDocument(
                "Virtual Company product catalog",
                "Virtual Company provides AI-supported operational workflows for SMEs.")
        };

        var prompt = OpenAiAgentBriefDraftService.BuildPrompt(company, agent, command, grounding);

        Assert.Contains("Company name: VC", prompt, StringComparison.Ordinal);
        Assert.Contains("Virtual Company product catalog", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Language:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Compliance region:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Seed finance document", prompt, StringComparison.Ordinal);
        Assert.Contains("not authoritative", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static CompanyKnowledgeDocument CreateDocument(
        Guid companyId,
        string title,
        Guid agentId,
        string category,
        string extractedText)
    {
        var document = new CompanyKnowledgeDocument(
            Guid.NewGuid(),
            companyId,
            title,
            CompanyKnowledgeDocumentType.Reference,
            $"tests/{Guid.NewGuid():N}.md",
            null,
            $"{Guid.NewGuid():N}.md",
            "text/markdown",
            ".md",
            100,
            new Dictionary<string, JsonNode?>
            {
                ["purpose"] = JsonValue.Create("agent_brief"),
                ["agentId"] = JsonValue.Create(agentId.ToString("D")),
                ["briefingCategory"] = JsonValue.Create(category)
            },
            new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
        document.SetExtractedText(extractedText);
        return document;
    }

    private static CompanyKnowledgeDocument CreateSeedDocument(Guid companyId, string title)
    {
        var document = new CompanyKnowledgeDocument(
            Guid.NewGuid(),
            companyId,
            title,
            CompanyKnowledgeDocumentType.Report,
            $"seed/{Guid.NewGuid():N}.pdf",
            null,
            $"{Guid.NewGuid():N}.pdf",
            "application/pdf",
            ".pdf",
            100,
            new Dictionary<string, JsonNode?>
            {
                ["category"] = JsonValue.Create("finance"),
                ["seed"] = JsonValue.Create(true)
            },
            new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
        document.SetExtractedText("Generic deterministic finance seed content.");
        return document;
    }
}
