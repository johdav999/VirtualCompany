using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class StaticCompanyToolRegistry : ICompanyToolRegistry
{
    private static readonly IReadOnlySet<ToolActionType> StandardActions = new HashSet<ToolActionType>
    {
        ToolActionType.Read,
        ToolActionType.Recommend,
        ToolActionType.Execute
    };

    private readonly IReadOnlyDictionary<string, TrustedToolRegistration> _tools;

    public StaticCompanyToolRegistry()
    {
        var taskScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tasks" };
        var approvalScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "approvals" };
        var knowledgeScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "knowledge" };
        var paymentsScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payments" };

        var registrations = new[]
        {
            Register("tasks.get", new HashSet<ToolActionType> { ToolActionType.Read }, taskScopes),
            Register("tasks.list", new HashSet<ToolActionType> { ToolActionType.Read }, taskScopes),
            Register("tasks.update_status", new HashSet<ToolActionType> { ToolActionType.Execute }, taskScopes),
            Register("approvals.create_request", new HashSet<ToolActionType> { ToolActionType.Execute }, approvalScopes),
            Register("knowledge.search", new HashSet<ToolActionType> { ToolActionType.Read, ToolActionType.Recommend }, knowledgeScopes),
            Register("erp", new HashSet<ToolActionType> { ToolActionType.Execute }, paymentsScopes)
        };

        _tools = registrations.ToDictionary(x => x.ToolName, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetTool(string toolName, out TrustedToolRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            registration = default!;
            return false;
        }

        return _tools.TryGetValue(toolName.Trim(), out registration!);
    }

    public IReadOnlyList<TrustedToolRegistration> ListTools() =>
        _tools.Values.OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase).ToArray();

    private static TrustedToolRegistration Register(
        string toolName,
        IReadOnlySet<ToolActionType>? supportedActions = null,
        IReadOnlySet<string>? scopes = null) =>
        new(
            toolName,
            supportedActions ?? StandardActions,
            scopes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}