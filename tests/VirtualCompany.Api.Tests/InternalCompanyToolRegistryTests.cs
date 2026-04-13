using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class InternalCompanyToolRegistryTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public InternalCompanyToolRegistryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Registry_exposes_initial_internal_tool_set_with_typed_actions()
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ICompanyToolRegistry>();

        var tools = registry.ListTools();

        Assert.Contains(tools, tool => tool.ToolName == "tasks.get" && tool.Supports(ToolActionType.Read, "tasks"));
        Assert.Contains(tools, tool => tool.ToolName == "tasks.list" && tool.Supports(ToolActionType.Read, "tasks"));
        Assert.Contains(tools, tool => tool.ToolName == "tasks.update_status" && tool.Supports(ToolActionType.Execute, "tasks"));
        Assert.Contains(tools, tool => tool.ToolName == "approvals.create_request" && tool.Supports(ToolActionType.Execute, "approvals"));
        Assert.Contains(tools, tool => tool.ToolName == "knowledge.search" && tool.Supports(ToolActionType.Read, "knowledge"));
        Assert.Contains(tools, tool => tool.ToolName == "knowledge.search" && tool.Supports(ToolActionType.Recommend, "knowledge"));
    }

    [Fact]
    public void Registry_rejects_unregistered_external_tools()
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ICompanyToolRegistry>();

        Assert.False(registry.TryGetTool("wire_transfer", out _));
        Assert.False(registry.TryGetTool("external.crm.raw_http", out _));
        Assert.True(registry.TryGetTool("tasks.update_status", out var taskUpdate));
        Assert.False(taskUpdate.Supports(ToolActionType.Read, "tasks"));
    }
}
