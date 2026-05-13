namespace VirtualCompany.Web.Services;

public static class FortnoxIntegrationDisplayMapper
{
    public static string NormalizeConnectionState(string? connectionStatus, bool isSyncing) =>
        connectionStatus?.Trim().ToLowerInvariant() switch
        {
            null or "" => "not_connected",
            "pending" => "connecting",
            "connected" when isSyncing => "syncing",
            "connected" => "connected",
            "needs_reconnect" or "needs-reconnect" => "needs_reconnect",
            "error" => "error",
            "revoked" or "disconnected" => "not_connected",
            _ => "error"
        };

    public static string FormatConnectionLabel(string normalizedState) =>
        normalizedState switch
        {
            "not_connected" => "Not connected",
            "connecting" => "Connecting",
            "connected" => "Connected",
            "syncing" => "Syncing",
            "needs_reconnect" => "Needs reconnect",
            "error" => "Needs attention",
            _ => "Needs attention"
        };

    public static string FormatConnectionDescription(string providerName, string normalizedState, string? lastErrorSummary) =>
        normalizedState switch
        {
            "not_connected" => $"Connect {providerName} to sync customers, suppliers, invoices, bills, accounts, and payments.",
            "connecting" => $"{providerName} authorization has started and is waiting for completion.",
            "connected" => $"{providerName} is connected and ready to sync finance records.",
            "syncing" => $"{providerName} sync is running. You can review history when it completes.",
            "needs_reconnect" => $"{providerName} needs a fresh authorization before syncing can continue.",
            "error" => FormatSafeText(lastErrorSummary, $"{providerName} reported an issue. Reconnect to restore the integration."),
            _ => "Connection state is unavailable."
        };

    public static string GetConnectionBadgeClass(string normalizedState) =>
        normalizedState switch
        {
            "connected" => "badge text-bg-success",
            "syncing" => "badge text-bg-primary",
            "needs_reconnect" => "badge text-bg-warning",
            "error" => "badge text-bg-danger",
            "connecting" => "badge text-bg-info",
            _ => "badge text-bg-secondary"
        };

    public static string FormatSyncStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "succeeded" or "completed" => "Completed",
            "failed" => "Failed",
            "partial" or "completed_with_errors" or "completed-with-errors" => "Completed with issues",
            "running" or "in_progress" or "in-progress" => "Running",
            "pending" => "Waiting to start",
            null or "" => "Unknown",
            _ => "Needs review"
        };

    public static string GetHistoryBadgeClass(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "succeeded" or "completed" => "badge text-bg-success",
            "failed" => "badge text-bg-danger",
            "partial" or "completed_with_errors" or "completed-with-errors" => "badge text-bg-warning",
            "running" or "in_progress" or "in-progress" => "badge text-bg-primary",
            _ => "badge text-bg-secondary"
        };

    public static string FormatImportCounts(int created, int updated, int skipped, int errors) =>
        $"Created {created}, updated {updated}, skipped {skipped}, issues {errors}.";

    public static string FormatEntityType(string? entityType) =>
        entityType?.Trim().ToLowerInvariant() switch
        {
            "company_information" => "Company information",
            "accounts" => "Accounts",
            "customers" => "Customers",
            "suppliers" => "Suppliers",
            "articles" => "Articles",
            "projects" => "Projects",
            "invoices" => "Invoices",
            "supplier_invoices" => "Supplier invoices",
            "vouchers" => "Vouchers",
            "payments" => "Payments",
            null or "" => "Records",
            _ => FormatTitle(entityType)
        };

    public static string FormatSafeHistoryError(FinanceIntegrationSyncHistoryItemResponse item)
    {
        if (!string.IsNullOrWhiteSpace(item.ErrorSummary))
        {
            return FormatSafeText(item.ErrorSummary, "Review the sync details.");
        }

        return item.Errors > 0
            ? $"{item.Errors} records need attention."
            : "No issues reported.";
    }

    public static string FormatSafeText(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || ContainsUnsafeDetail(trimmed))
        {
            return fallback;
        }

        return trimmed.Length <= 180 ? trimmed : $"{trimmed[..177]}...";
    }

    private static bool ContainsUnsafeDetail(string value) =>
        value.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("refresh_token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("client_secret", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("authorization:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("bearer ", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("System.", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(" stack ", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("trace", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(" at ", StringComparison.OrdinalIgnoreCase) ||
        value.Contains('\n') ||
        value.Contains('\r') ||
        LooksLikeJson(value) ||
        LooksLikeInternalIdentifier(value);

    private static bool LooksLikeJson(string value) =>
        (value.Contains('{') && value.Contains('}')) ||
        (value.Contains('[') && value.Contains(']'));

    private static bool LooksLikeInternalIdentifier(string value)
    {
        var words = value.Split([' ', '.', ',', ';', ':', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word =>
            word.Length > 3 &&
            word.Contains('_', StringComparison.Ordinal) &&
            word.Any(char.IsLetter));
    }

    private static string FormatTitle(string value) =>
        string.Join(
            " ",
            value.Replace("-", "_", StringComparison.Ordinal)
                .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Length == 0 ? part : string.Concat(char.ToUpperInvariant(part[0]), part[1..].ToLowerInvariant())));
}
