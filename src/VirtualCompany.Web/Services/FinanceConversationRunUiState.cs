namespace VirtualCompany.Web.Services;

public static class FinanceConversationRunUiState
{
    private static readonly IReadOnlySet<string> TerminalStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "completed", "partially_completed", "cancelled", "stale", "failed" };

    public static bool IsTerminal(string? status) =>
        !string.IsNullOrWhiteSpace(status) && TerminalStates.Contains(status);

    public static bool ShouldPoll(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        !IsTerminal(status) &&
        status is not ("awaiting_clarification" or "awaiting_confirmation" or "awaiting_approval");

    public static bool CanCancel(string? status) =>
        !string.IsNullOrWhiteSpace(status) && !IsTerminal(status);

    public static bool CanConfirm(string? stepStatus) =>
        string.Equals(stepStatus, "awaiting_confirmation", StringComparison.OrdinalIgnoreCase);

    public static bool CanOpenApproval(string? stepStatus, Guid? approvalRequestId) =>
        approvalRequestId.HasValue && string.Equals(stepStatus, "awaiting_approval", StringComparison.OrdinalIgnoreCase);
}
