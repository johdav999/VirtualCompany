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
        Assert.Contains(tools, tool => tool.ToolName == "get_cash_balance" && tool.Version == "1.0.0" && tool.Supports(ToolActionType.Read, "finance"));
        Assert.Contains(tools, tool => tool.ToolName == "resolve_finance_agent_query" && tool.Version == "1.0.0" && tool.Supports(ToolActionType.Read, "finance"));
        Assert.Contains(tools, tool => tool.ToolName == "list_transactions" && tool.Version == "1.0.0" && tool.Supports(ToolActionType.Read, "finance"));
        Assert.Contains(tools, tool => tool.ToolName == "list_uncategorized_transactions" && tool.Version == "1.0.0" && tool.Supports(ToolActionType.Read, "finance"));
        Assert.Contains(tools, tool => tool.ToolName == "list_invoices_awaiting_approval" && tool.Version == "1.0.0" && tool.Supports(ToolActionType.Read, "finance"));
        Assert.Contains(tools, tool => tool.ToolName == "get_profit_and_loss_summary" && tool.Version == "1.0.0" && tool.Supports(ToolActionType.Read, "finance"));
        Assert.Contains(tools, tool => tool.ToolName == "recommend_transaction_category" && tool.Version == "1.0.0" && tool.Supports(ToolActionType.Recommend, "finance"));
        Assert.Contains(tools, tool => tool.ToolName == "recommend_invoice_approval_decision" && tool.Version == "1.0.0" && tool.Supports(ToolActionType.Recommend, "finance"));
        Assert.Contains(tools, tool => tool.ToolName == "categorize_transaction" && tool.Version == "1.0.0" && tool.Supports(ToolActionType.Execute, "finance"));
        Assert.Contains(tools, tool => tool.ToolName == "approve_invoice" && tool.Version == "1.0.0" && tool.Supports(ToolActionType.Execute, "finance"));
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
