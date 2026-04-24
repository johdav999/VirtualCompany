using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class StaticFinanceWorkflowTriggerRegistry : IFinanceWorkflowTriggerRegistry
{
    private readonly IReadOnlyDictionary<string, FinanceWorkflowTriggerCheckRegistration> _registrations;

    public StaticFinanceWorkflowTriggerRegistry()
    {
        var registrations = new[]
        {
            Register(
                FinanceWorkflowTriggerTypes.Invoice,
                FinanceWorkflowExecutedChecks.RefreshInsightsSnapshot,
                FinanceWorkflowExecutedChecks.EvaluateCashPosition),
            Register(
                FinanceWorkflowTriggerTypes.Bill,
                FinanceWorkflowExecutedChecks.RefreshInsightsSnapshot,
                FinanceWorkflowExecutedChecks.EvaluateCashPosition,
                FinanceWorkflowExecutedChecks.EnsureApprovalTask),
            Register(
                FinanceWorkflowTriggerTypes.Payment,
                FinanceWorkflowExecutedChecks.RefreshInsightsSnapshot,
                FinanceWorkflowExecutedChecks.EvaluateCashPosition,
                FinanceWorkflowExecutedChecks.EnsureApprovalTask),
            Register(
                FinanceWorkflowTriggerTypes.Cash,
                FinanceWorkflowExecutedChecks.RefreshInsightsSnapshot,
                FinanceWorkflowExecutedChecks.EvaluateCashPosition),
            Register(
                FinanceWorkflowTriggerTypes.SimulationDayAdvanced,
                FinanceWorkflowExecutedChecks.RefreshInsightsSnapshot,
                FinanceWorkflowExecutedChecks.EvaluateCashPosition)
        };

        _registrations = registrations.ToDictionary(x => x.TriggerType, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetChecks(string triggerType)
    {
        var normalizedTriggerType = FinanceWorkflowTriggerTypes.Normalize(triggerType);
        return _registrations.TryGetValue(normalizedTriggerType, out var registration)
            ? registration.CheckCodes
            : [];
    }

    public IReadOnlyList<FinanceWorkflowTriggerCheckRegistration> ListRegistrations() =>
        _registrations.Values
            .OrderBy(x => x.TriggerType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public Task<FinanceWorkflowTriggerExecutionDto> ProcessAsync(
        ProcessFinanceWorkflowTriggerCommand command,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("StaticFinanceWorkflowTriggerRegistry only provides trigger registrations. Use IFinanceWorkflowTriggerService to process triggers.");

    private static FinanceWorkflowTriggerCheckRegistration Register(string triggerType, params string[] checkCodes) =>
        new(
            FinanceWorkflowTriggerTypes.Normalize(triggerType),
            checkCodes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray());
}
