using VirtualCompany.Shared;

namespace VirtualCompany.Web.Services;

public sealed record FinanceRouteDefinition(
    string Title,
    string Path,
    string Description,
    bool RequiresSandboxAdminAccess = false)
{
    public string BuildPath(Guid? companyId) => FinanceRoutes.WithCompanyContext(Path, companyId);
    public bool IsVisibleTo(string? membershipRole) => !RequiresSandboxAdminAccess || FinanceAccess.CanAccessSandboxAdmin(membershipRole);
}

public static class FinanceRoutes
{
    public const string CompanyIdQueryKey = "companyId";
    public const string Home = "/finance";
    public const string CashPosition = "/finance/cash-position";
    public const string Transactions = "/finance/transactions";
    public const string Payments = "/finance/payments";
    public const string PaymentDetail = "/finance/payments/{paymentId:guid}";
    public const string Bills = "/finance/bills";
    public const string BillDetail = "/finance/bills/{billId:guid}";
    public const string TransactionDetail = "/finance/transactions/{transactionId:guid}";
    public const string BillInbox = "/finance/bill-inbox";
    public const string BillInboxDetail = "/finance/bill-inbox/{billId:guid}";
    public const string Invoices = "/finance/invoices";
    public const string Reviews = "/finance/reviews";
    public const string ReviewDetail = "/finance/reviews/{invoiceId:guid}";
    public const string InvoiceDetail = "/finance/invoices/{invoiceId:guid}";
    public const string Counterparties = "/finance/admin/counterparties";
    public const string Mailbox = "/finance/mailbox";
    public const string Balances = "/finance/balances";
    public const string MonthlySummary = "/finance/monthly-summary";
    public const string Anomalies = "/finance/anomalies";
    public const string AnomalyDetail = "/finance/anomalies/{anomalyId:guid}";
    public const string TransparencyEvents = "/finance/admin/transparency/events";
    public const string TransparencyEventDetail = "/finance/admin/transparency/events/{eventId:guid}";
    public const string TransparencyToolRegistry = "/finance/admin/transparency/tools";
    public const string TransparencyToolExecutions = "/finance/admin/transparency/tool-executions";
    public const string TransparencyToolExecutionDetail = "/finance/admin/transparency/tool-executions/{executionId:guid}";
    public const string SandboxAdmin = "/finance/sandbox-admin";
    public const string Settings = "/finance/settings";
    public const string EmailSettings = "/finance/settings/email-settings";
    public const string AlertDetail = "/finance/alerts/{alertId:guid}";

    public static FinanceRouteDefinition HomePage { get; } =
        new("Finance", Home, "Open the tenant-scoped finance workspace.");

    public static IReadOnlyList<FinanceRouteDefinition> SummaryPages { get; } =
        [
            new("Cash position", CashPosition, "Track company cash coverage and liquidity in the active tenant context."),
            new("Balances", Balances, "Browse account balances with explicit tenant-scoped routing."),
            new("Monthly summary", MonthlySummary, "Summarize the current month's finance posture for the active company."),
            new("Anomalies", Anomalies, "Review finance anomalies and follow-up work in the selected tenant.")
        ];

    public static IReadOnlyList<FinanceRouteDefinition> SectionPages { get; } =
        [
            new("Cash position", CashPosition, "Review cash coverage and company liquidity in the active tenant context."),
            new("Transactions", Transactions, "Inspect transaction activity and categorization work for the selected company."),
            new("Payments", Payments, "Inspect incoming and outgoing cash movement records for the selected company."),
            new("Bills", Bills, "Inspect supplier bills and tenant-scoped bill detail workflows."),
            new("Bill inbox", BillInbox, "Review detected supplier bills, evidence, warnings, and approval proposals."),
            new("Invoice reviews", Reviews, "Review finance workflow recommendations and actions for invoice workbench items."),
            new("Invoices", Invoices, "Track invoice review and collection workflows inside the active company."),
            new("Mailbox", Mailbox, "Connect a mailbox and trigger tenant-scoped bill inbox scans."),
            new("Balances", Balances, "Browse account balances with explicit tenant-scoped routing."),
            new("Transparency events", TransparencyEvents, "Inspect finance domain events, payload summaries, and trigger trace coverage.", true),
            new("Counterparties", Counterparties, "Manage customer and supplier master data used by finance documents.", true),
            new("Tool registry", TransparencyToolRegistry, "Inspect finance tool manifests, schema summaries, and provider metadata.", true),
            new("Tool executions", TransparencyToolExecutions, "Review finance tool execution history and related approval links.", true),
            new("Sandbox admin", SandboxAdmin, "Inspect sandbox dataset generation, anomaly injection, simulation controls, tool execution visibility, and domain events.", true),
            new("Settings", Settings, "Configure finance integration settings for the active company.", true),
            new("Monthly summary", MonthlySummary, "Summarize the current month's finance posture for the active company."),
            new("Anomalies", Anomalies, "Review finance anomalies and follow-up work in the selected tenant.")
        ];

    public static string BuildTransactionDetailPath(Guid transactionId, Guid? companyId) =>
        WithCompanyContext($"/finance/transactions/{transactionId:D}", companyId);

    public static string BuildPaymentDetailPath(Guid paymentId, Guid? companyId) =>
        WithCompanyContext($"/finance/payments/{paymentId:D}", companyId);

    public static string BuildBillDetailPath(Guid billId, Guid? companyId) =>
        WithCompanyContext($"/finance/bills/{billId:D}", companyId);

    public static string BuildBillInboxDetailPath(Guid billId, Guid? companyId) =>
        WithCompanyContext($"/finance/bill-inbox/{billId:D}", companyId);

    public static string BuildInvoiceDetailPath(Guid invoiceId, Guid? companyId) =>
        WithCompanyContext($"/finance/invoices/{invoiceId:D}", companyId);

    public static string BuildInvoiceReviewDetailPath(Guid invoiceId, Guid? companyId) =>
        WithCompanyContext($"/finance/reviews/{invoiceId:D}", companyId);

    public static string BuildAnomalyDetailPath(Guid anomalyId, Guid? companyId) =>
        WithCompanyContext($"/finance/anomalies/{anomalyId:D}", companyId);

    public static string BuildTransparencyEventDetailPath(Guid eventId, Guid? companyId) =>
        WithCompanyContext($"/finance/admin/transparency/events/{eventId:D}", companyId);

    public static string BuildTransparencyToolExecutionDetailPath(Guid executionId, Guid? companyId) =>
        WithCompanyContext($"/finance/admin/transparency/tool-executions/{executionId:D}", companyId);

    public static string BuildAlertDetailPath(Guid alertId, Guid? companyId) =>
        WithCompanyContext($"/finance/alerts/{alertId:D}", companyId);

    public static string WithCompanyContext(string path, Guid? companyId)
    {
        if (companyId is not Guid resolvedCompanyId)
        {
            return path;
        }

        var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{path}{separator}{CompanyIdQueryKey}={resolvedCompanyId}";
    }
}
