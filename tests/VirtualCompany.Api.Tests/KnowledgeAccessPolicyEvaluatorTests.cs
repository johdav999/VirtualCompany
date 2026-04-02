using System.Text.Json.Nodes;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Documents;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class KnowledgeAccessPolicyEvaluatorTests
{
    private readonly KnowledgeAccessPolicyEvaluator _evaluator = new();

    [Fact]
    public void Company_visible_document_is_accessible_to_same_company_member()
    {
        var companyId = Guid.NewGuid();
        var document = CreateDocument(companyId);
        var accessContext = new CompanyKnowledgeAccessContext(
            companyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "manager",
            Array.Empty<string>());

        var allowed = _evaluator.CanAccess(accessContext, document);

        Assert.True(allowed);
    }

    [Fact]
    public void Cross_company_document_is_denied()
    {
        var companyId = Guid.NewGuid();
        var document = CreateDocument(Guid.NewGuid());
        var accessContext = new CompanyKnowledgeAccessContext(
            companyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "manager",
            Array.Empty<string>());

        var allowed = _evaluator.CanAccess(accessContext, document);

        Assert.False(allowed);
    }

    [Fact]
    public void Restricted_document_is_denied_when_role_and_scope_do_not_match()
    {
        var companyId = Guid.NewGuid();
        var document = CreateDocument(
            companyId,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["restricted"] = JsonValue.Create(true),
                ["roles"] = new JsonArray(JsonValue.Create("owner")),
                ["scopes"] = new JsonArray(JsonValue.Create("finance"))
            });

        var accessContext = new CompanyKnowledgeAccessContext(
            companyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "manager",
            ["operations"]);

        var allowed = _evaluator.CanAccess(accessContext, document);

        Assert.False(allowed);
    }

    [Fact]
    public void Restricted_document_is_allowed_when_role_matches()
    {
        var companyId = Guid.NewGuid();
        var document = CreateDocument(
            companyId,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["private"] = JsonValue.Create(true),
                ["roles"] = new JsonArray(JsonValue.Create("manager"))
            });

        var accessContext = new CompanyKnowledgeAccessContext(
            companyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "manager",
            Array.Empty<string>());

        var allowed = _evaluator.CanAccess(accessContext, document);

        Assert.True(allowed);
    }

    private static CompanyKnowledgeDocument CreateDocument(
        Guid companyId,
        Dictionary<string, JsonNode?>? accessScopeProperties = null)
    {
        return new CompanyKnowledgeDocument(
            Guid.NewGuid(),
            companyId,
            "Finance Policy",
            CompanyKnowledgeDocumentType.Policy,
            $"companies/{companyId:N}/knowledge/{Guid.NewGuid():N}/finance-policy.txt",
            null,
            "finance-policy.txt",
            "text/plain",
            ".txt",
            128,
            accessScope: new CompanyKnowledgeDocumentAccessScope(
                companyId,
                CompanyKnowledgeDocumentAccessScope.CompanyVisibility,
                accessScopeProperties));
    }
}
