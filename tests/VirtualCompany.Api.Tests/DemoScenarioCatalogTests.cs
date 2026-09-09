using System.Text.Json;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class DemoScenarioCatalogTests
{
    [Fact]
    public void Embedded_specification_is_versioned_bounded_and_synthetic()
    {
        var definition = Assert.Single(new DemoScenarioCatalog().List());

        Assert.Equal(1, definition.SchemaVersion);
        Assert.Equal("northstar-sales", definition.ScenarioKey);
        Assert.Equal(1, definition.ScenarioVersion);
        Assert.EndsWith(".invalid", definition.InitialState.ContactEmail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([1, 2, 3], definition.Actions.Select(x => x.StepNumber));
        Assert.All(definition.Actions, x => Assert.StartsWith("demo.sales.", x.CommandName, StringComparison.Ordinal));
        Assert.DoesNotContain(definition.Actions, x => x.CommandName.Contains("http", StringComparison.OrdinalIgnoreCase) ||
            x.CommandName.Contains("sql", StringComparison.OrdinalIgnoreCase) ||
            x.CommandName.Contains("script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parser_rejects_commands_outside_the_allowlist()
    {
        var definition = Assert.Single(new DemoScenarioCatalog().List());
        var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json = json.Replace("demo.sales.qualify_lead", "arbitrary.http", StringComparison.Ordinal);

        var exception = Assert.Throws<DemoScenarioException>(() => DemoScenarioCatalog.Parse(json));

        Assert.Equal(DemoScenarioProblemCodes.InvalidSpecification, exception.Code);
    }
}

