using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class InternalCompanyToolRegistryTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

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
        Assert.Contains(tools, tool => tool.ToolName == "documents.list" && tool.Supports(ToolActionType.Read, "knowledge"));
        Assert.Contains(tools, tool => tool.ToolName == "documents.read" && tool.Supports(ToolActionType.Read, "knowledge"));
        Assert.Contains(tools, tool => tool.ToolName == DocumentPublicationToolNames.PrepareCreate && tool.Supports(ToolActionType.Recommend, "knowledge"));
        Assert.Contains(tools, tool => tool.ToolName == DocumentPublicationToolNames.Create && tool.Supports(ToolActionType.Execute, "knowledge"));
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

    [Fact]
    public void Repository_read_tools_have_strict_bounded_schemas_without_identity_or_url_inputs()
    {
        var registry = new StaticCompanyToolRegistry();

        foreach (var toolName in DocumentKnowledgeToolNames.RepositoryGrantTools)
        {
            Assert.True(registry.TryGetToolDefinition(toolName, out var definition));
            Assert.Equal(ToolActionType.Read, definition.ActionType);
            Assert.False(definition.InputSchema["additionalProperties"]!.GetValue<bool>());
            var properties = definition.InputSchema["properties"]!.AsObject();
            Assert.DoesNotContain("companyId", properties.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("agentId", properties.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("actorUserId", properties.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("url", properties.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("credential", properties.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        }

        Assert.True(registry.TryGetToolDefinition(DocumentKnowledgeToolNames.List, out var list));
        Assert.Equal(50, list.InputSchema["properties"]!["pageSize"]!["maximum"]!.GetValue<int>());
        Assert.True(registry.TryGetToolDefinition(DocumentKnowledgeToolNames.Read, out var read));
        Assert.Equal(8000, read.InputSchema["properties"]!["maxCharacters"]!["maximum"]!.GetValue<int>());
        Assert.Contains("documentHandle", read.InputSchema["required"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public void Publication_tools_separate_staging_from_sensitive_exact_file_execution()
    {
        var registry = new StaticCompanyToolRegistry();

        Assert.True(registry.TryGetToolDefinition(DocumentPublicationToolNames.PrepareCreate, out var prepare));
        Assert.Equal(ToolActionType.Recommend, prepare.ActionType);
        Assert.False(prepare.SensitiveAction);
        Assert.False(prepare.InputSchema["additionalProperties"]!.GetValue<bool>());
        Assert.DoesNotContain("url", prepare.InputSchema["properties"]!.AsObject().Select(x => x.Key), StringComparer.OrdinalIgnoreCase);

        Assert.True(registry.TryGetToolDefinition(DocumentPublicationToolNames.Create, out var create));
        Assert.Equal(ToolActionType.Execute, create.ActionType);
        Assert.True(create.SensitiveAction);
        Assert.False(create.InputSchema["additionalProperties"]!.GetValue<bool>());
        var required = create.InputSchema["required"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        Assert.Contains("publicationRequestId", required);
        Assert.Contains("targetFolderItemId", required);
        Assert.Contains("fileName", required);
        Assert.Contains("sizeBytes", required);
        Assert.Contains("contentSha256", required);
        Assert.DoesNotContain("contentBase64", create.InputSchema["properties"]!.AsObject().Select(x => x.Key));
        Assert.DoesNotContain("url", create.InputSchema["properties"]!.AsObject().Select(x => x.Key), StringComparer.OrdinalIgnoreCase);

        Assert.True(registry.TryGetToolDefinition(DocumentPublicationToolNames.PrepareUpdate, out var prepareUpdate));
        Assert.Equal(ToolActionType.Recommend, prepareUpdate.ActionType);
        Assert.False(prepareUpdate.SensitiveAction);
        var prepareUpdateRequired = prepareUpdate.InputSchema["required"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        Assert.Contains("itemId", prepareUpdateRequired);
        Assert.Contains("expectedRemoteVersion", prepareUpdateRequired);
        Assert.Contains("originalEvidenceVersion", prepareUpdateRequired);
        Assert.Contains("contentBase64", prepareUpdateRequired);

        Assert.True(registry.TryGetToolDefinition(DocumentPublicationToolNames.Update, out var update));
        Assert.Equal(ToolActionType.Execute, update.ActionType);
        Assert.True(update.SensitiveAction);
        Assert.False(update.InputSchema["additionalProperties"]!.GetValue<bool>());
        var updateRequired = update.InputSchema["required"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        Assert.Contains("publicationRequestId", updateRequired);
        Assert.Contains("targetItemId", updateRequired);
        Assert.Contains("expectedRemoteVersion", updateRequired);
        Assert.Contains("originalEvidenceVersion", updateRequired);
        Assert.Contains("contentSha256", updateRequired);
        Assert.DoesNotContain("contentBase64", update.InputSchema["properties"]!.AsObject().Select(x => x.Key));
        Assert.DoesNotContain("url", update.InputSchema["properties"]!.AsObject().Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Repository_tool_schema_rejects_forged_context_before_contract_routing()
    {
        var contract = new CountingContract();
        var executor = new NoOpCompanyToolExecutor(new StaticCompanyToolRegistry(), contract);

        var result = await executor.ExecuteAsync(new ToolExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), DocumentKnowledgeToolNames.List,
            ToolActionType.Read, "knowledge",
            new Dictionary<string, JsonNode?>
            {
                ["pageSize"] = JsonValue.Create(10),
                ["companyId"] = JsonValue.Create(Guid.NewGuid()),
                ["actorUserId"] = JsonValue.Create(Guid.NewGuid())
            }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("input_payload_schema_validation_failed", result.ErrorCode);
        Assert.Equal(0, contract.Count);
    }

    private sealed class CountingContract : IInternalCompanyToolContract
    {
        public int Count { get; private set; }

        public Task<InternalToolExecutionResponse> ExecuteAsync(
            InternalToolExecutionRequest request,
            CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(InternalToolExecutionResponse.Succeeded(
                "ok",
                new Dictionary<string, JsonNode?>()));
        }
    }
}
