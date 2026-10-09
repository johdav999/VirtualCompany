using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using VirtualCompany.Shared;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class WireContractAuthorityTests
{
    [Fact]
    public void Document_scope_uses_one_wire_converter_while_domain_retains_tenant_validation()
    {
        var companyId = Guid.NewGuid();
        var scope = new VirtualCompany.Domain.Entities.CompanyKnowledgeDocumentAccessScope(companyId, "company",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["source"] = System.Text.Json.Nodes.JsonValue.Create("library"), ["optional"] = null });
        var domainJson = JsonSerializer.Serialize(scope);
        var wireJson = JsonSerializer.Serialize<VirtualCompany.Shared.Contracts.Documents.CompanyKnowledgeDocumentAccessScopeDto>(scope);
        Assert.Equal(domainJson, wireJson);
        using var document = JsonDocument.Parse(wireJson);
        Assert.Equal(companyId, document.RootElement.GetProperty("company_id").GetGuid());
        Assert.Equal("company", document.RootElement.GetProperty("visibility").GetString());
        Assert.Equal("library", document.RootElement.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("optional").ValueKind);
        Assert.False(document.RootElement.TryGetProperty("AdditionalProperties", out _));
        var restored = JsonSerializer.Deserialize<VirtualCompany.Domain.Entities.CompanyKnowledgeDocumentAccessScope>(wireJson)!;
        Assert.Equal(companyId, restored.CompanyId);
        Assert.Equal("library", restored.AdditionalProperties["source"]!.GetValue<string>());
        Assert.Throws<ArgumentException>(() => restored.NormalizeForCompany(Guid.NewGuid()));
    }

    private static readonly Assembly Contracts = typeof(WireEnumJsonConverter).Assembly;

    public static IEnumerable<object[]> OriginalSchemas() => Load<SchemaBaseline>("WireContractSchemas.json")
        .Select(schema => new object[] { schema.Name, schema.PropertiesHash, schema.ConstructorHashes, schema.OriginalName ?? schema.Name });

    [Theory]
    [MemberData(nameof(OriginalSchemas))]
    public void Relocated_contract_has_one_definition_and_preserves_its_original_schema(
        string name, string propertiesHash, string[] constructorHashes, string originalName)
    {
        var type = Contracts.GetType(name, throwOnError: true)!;
        Assert.Null(typeof(VirtualCompany.Api.Controllers.InternalFinanceInvoicesController).Assembly.GetType(originalName));
        Assert.Null(Assembly.Load("VirtualCompany.Application").GetType(name));
        Assert.Null(typeof(VirtualCompany.Web.Services.FinanceApiClient).Assembly.GetType(name));

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}|{FormatType(property.PropertyType)}|{string.Join(",", property.GetCustomAttributesData().Where(attribute => attribute.AttributeType.Namespace == "System.Text.Json.Serialization").Select(attribute => attribute.ToString()))}");
        var description = string.Join("\n", properties);
        Assert.True(propertiesHash == Hash(description), $"Serialized properties changed for {name}:\n{description}");

        var constructors = type.GetConstructors().Where(constructor => constructor.GetParameters().Length > 0)
            .Select(constructor => Hash(string.Join("\n", constructor.GetParameters().Select(parameter =>
                $"{parameter.Name}|{FormatType(parameter.ParameterType)}|{parameter.HasDefaultValue}|{(parameter.HasDefaultValue ? parameter.DefaultValue?.ToString() : null)}"))))
            .ToArray();
        foreach (var original in constructorHashes) Assert.Contains(original, constructors);
        Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name is "ToCommand" or "ToCreateCommand" or "ToUpdateCommand" or "ToInput" or "ToDto");
    }

    [Fact]
    public void Client_compatibility_names_do_not_declare_a_second_transport_shape()
    {
        var web = typeof(VirtualCompany.Web.Services.FinanceApiClient).Assembly;
        foreach (var alias in Load<ClientAlias>("WireContractAliases.json"))
        {
            Assert.NotNull(Contracts.GetType(alias.Contract));
            Assert.Null(web.GetType(alias.Original));
        }
    }

    [Fact]
    public void Shared_contracts_remain_independent_and_Web_does_not_depend_on_backend_implementations()
    {
        var root = FindRepositoryRoot();
        var shared = XDocument.Load(Path.Combine(root, "src/VirtualCompany.Shared/VirtualCompany.Shared.csproj"));
        Assert.Empty(shared.Descendants("ProjectReference"));
        Assert.Empty(shared.Descendants("PackageReference"));
        var web = XDocument.Load(Path.Combine(root, "src/VirtualCompany.Web/VirtualCompany.Web.csproj"));
        Assert.All(web.Descendants("ProjectReference"), reference => Assert.Contains("VirtualCompany.Shared", reference.Attribute("Include")!.Value));
        Assert.DoesNotContain(Contracts.GetReferencedAssemblies(), assembly => assembly.Name!.StartsWith("VirtualCompany.", StringComparison.Ordinal));
    }

    private static T[] Load<T>(string name) => JsonSerializer.Deserialize<T[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)))!;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    // The access-scope wire data moved to a shared base. Domain still derives from
    // it for validation; compare against the original CLR name and test JSON above.
    private static string FormatType(Type type) => type == typeof(VirtualCompany.Shared.Contracts.Documents.CompanyKnowledgeDocumentAccessScopeDto)
        ? "VirtualCompany.Domain.Entities.CompanyKnowledgeDocumentAccessScope"
        : type.IsGenericType
        ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(FormatType)) + ">"
        : type.FullName!;
    private static string FindRepositoryRoot()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "src/VirtualCompany.Shared/VirtualCompany.Shared.csproj"))) return path.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed record SchemaBaseline(string Name, string PropertiesHash, string[] ConstructorHashes, string? OriginalName = null);
    private sealed record ClientAlias(string Original, string Contract);
}
