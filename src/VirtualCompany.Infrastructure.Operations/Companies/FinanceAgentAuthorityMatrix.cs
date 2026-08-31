using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public static class FinanceAgentAuthorityMatrix
{
    public const string CoverageTest =
        "VirtualCompany.Api.Tests.FinanceAgentAuthorityMatrixTests.Registered_finance_tool_has_complete_authorization_and_risk_metadata";

    public static IReadOnlyList<FinanceAgentAuthorityMatrixEntry> Build(ICompanyToolRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return registry.ListTools()
            .Where(static registration => registration.Scopes.Contains("finance"))
            .SelectMany(registration => registration.SupportedActions.Select(action => BuildEntry(registration, action)))
            .OrderBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ActionType, StringComparer.Ordinal)
            .ToArray();
    }

    private static FinanceAgentAuthorityMatrixEntry BuildEntry(
        TrustedToolRegistration registration,
        ToolActionType action)
    {
        var requirements = FinanceAgentAuthorizationService.ResolveRequirements(registration.ToolName, action);
        var risk = action == ToolActionType.Execute
            ? registration.FinanceRiskClassification ?? FinanceToolRiskPolicyCatalog.GetRequired(registration.ToolName)
            : null;

        return new FinanceAgentAuthorityMatrixEntry(
            registration.ToolName,
            registration.Version,
            action.ToStorageValue(),
            "finance",
            requirements.Policies,
            requirements.Permissions,
            $"effective_authority:{registration.ToolName}:{action.ToStorageValue()}:finance",
            risk?.RiskTier ?? (action == ToolActionType.Read ? "read_only" : "advisory"),
            risk?.DefaultApprovalBehavior ?? "not_applicable",
            risk?.ExternalSideEffectClassification ?? "none",
            CoverageTest);
    }
}
