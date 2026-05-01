using VirtualCompany.Shared;

namespace VirtualCompany.Web.Services;

public sealed record FinanceRouteDefinition(
    string Title,
    string Path,
    string Description,
    bool RequiresSandboxAdminAccess = false,
    IReadOnlyList<string>? ActivePathPrefixes = null)
{
    public string BuildPath(Guid? companyId) => FinanceRoutes.WithCompanyContext(Path, companyId);
    public bool IsVisibleTo(string? membershipRole) => !RequiresSandboxAdminAccess || FinanceAccess.CanAccessSandboxAdmin(membershipRole);
}

public static class FinanceRoutes
{
    public const string CompanyIdQueryKey = "companyId";
    public const string Home = "/finance";
    public const string CashPosition = "/finance/cash-position";
    public const string Activity = "/finance/activity";
    public const string Transactions = "/finance/transactions";
    public const string Payments = "/finance/payments";
    public const string PaymentDetail = "/finance/payments/{PaymentId:guid}";
    public const string SupplierBills = "/finance/supplier-bills";
    public const string SupplierBillsReview = "/finance/supplier-bills/review";
    public const string SupplierBillDetail = "/finance/supplier-bills/{BillId:guid}";
    public const string SupplierBillsReviewDetail = "/finance/supplier-bills/review/{BillId:guid}";
    public const string Bills = "/finance/bills";
    public const string BillDetail = "/finance/bills/{BillId:guid}";
    public const string ActivityDetail = "/finance/activity/{TransactionId:guid}";
    public const string TransactionDetail = "/finance/transactions/{TransactionId:guid}";
    public const string BillInbox = "/finance/bill-inbox";
    public const string BillInboxDetail = "/finance/bill-inbox/{BillId:guid}";
    public const string Invoices = "/finance/invoices";
    public const string Reviews = "/finance/reviews";
    public const string ReviewDetail = "/finance/reviews/{InvoiceId:guid}";
    public const string InvoiceDetail = "/finance/invoices/{InvoiceId:guid}";
    public const string Counterparties = "/finance/counterparties";
    public const string Mailbox = "/finance/mailbox";
    public const string Balances = "/finance/balances";
    public const string MonthlySummary = "/finance/monthly-summary";
    public const string Issues = "/finance/issues";
    public const string IssueDetail = "/finance/issues/{AnomalyId:guid}";
    public const string Anomalies = "/finance/anomalies";
    public const string AnomalyDetail = "/finance/anomalies/{AnomalyId:guid}";
    public const string TransparencyEvents = "/system/admin/transparency-events";
    public const string TransparencyEventDetail = "/system/admin/transparency-events/{EventId:guid}";
    public const string TransparencyToolRegistry = "/system/admin/tool-registry";
    public const string TransparencyToolExecutions = "/system/admin/tool-executions";
    public const string TransparencyToolExecutionDetail = "/system/admin/tool-executions/{ExecutionId:guid}";
    public const string SandboxAdmin = "/simulation-lab";
    public const string Settings = "/finance/settings";
    public const string EmailSettings = "/finance/settings/email-settings";
    public const string FortnoxIntegrationSettings = "/finance/settings/integrations/fortnox";
    public const string AlertDetail = "/finance/alerts/{AlertId:guid}";

    public static FinanceRouteDefinition HomePage { get; } =
        new("Finance", Home, "Open the finance workspace.");

    public static IReadOnlyList<FinanceRouteDefinition> SummaryPages { get; } =
        [
            new("Cash & liquidity", CashPosition, "Track company cash coverage and liquidity."),
            new("Balances", Balances, "Browse account balances for the active company."),
            new("Monthly summary", MonthlySummary, "Summarize the current month's finance posture for the active company."),
            new("Issues", Issues, "Review finance items that need attention.", ActivePathPrefixes: [Issues, Anomalies])
        ];

    public static IReadOnlyList<FinanceRouteDefinition> SectionPages { get; } =
        [
            new("Overview", Home, "Review key finance actions and open the operational finance workspace."),
            new("Invoices", Invoices, "Track invoice review and collection workflows."),
            new("Supplier bills", SupplierBills, "Review supplier bills and bill intake work.", ActivePathPrefixes: [SupplierBills, Bills, BillInbox]),
            new("Payments", Payments, "Inspect incoming and outgoing cash movement records."),
            new("Activity", Activity, "Review money movements and finance activity.", ActivePathPrefixes: [Activity, Transactions]),
            new("Issues", Issues, "Review finance items that need attention.", ActivePathPrefixes: [Issues, Anomalies]),
            new("Settings", Settings, "Configure finance integration settings for the active company.")
        ];

    public static IReadOnlyList<FinanceRouteDefinition> SimulationLabPages { get; } =
        [
            new("Simulation control", SandboxAdmin, "Control company simulation timing and generated finance activity.", true),
            new("Dataset generation", SandboxAdmin, "Generate and inspect finance test datasets.", true),
            new("Anomaly injection", SandboxAdmin, "Register anomaly scenarios for simulation testing.", true),
            new("Simulation history", SandboxAdmin, "Review recent simulation runs and output.", true)
        ];

    public static IReadOnlyList<FinanceRouteDefinition> SystemAdminPages { get; } =
        [
            new("Tool registry", TransparencyToolRegistry, "Inspect finance tool manifests, schema summaries, and provider metadata.", true),
            new("Tool executions", TransparencyToolExecutions, "Review finance tool execution history and related approval links.", true),
            new("Transparency events", TransparencyEvents, "Inspect finance events, payload summaries, and trigger trace coverage.", true)
        ];

    public static string BuildTransactionDetailPath(Guid transactionId, Guid? companyId) =>
        WithCompanyContext($"/finance/activity/{transactionId:D}", companyId);

    public static string BuildPaymentDetailPath(Guid paymentId, Guid? companyId) =>
        WithCompanyContext($"/finance/payments/{paymentId:D}", companyId);

    public static string BuildBillDetailPath(Guid billId, Guid? companyId) =>
        WithCompanyContext($"/finance/supplier-bills/{billId:D}", companyId);

    public static string BuildBillInboxDetailPath(Guid billId, Guid? companyId) =>
        WithCompanyContext($"/finance/supplier-bills/review/{billId:D}", companyId);

    public static string BuildInvoiceDetailPath(Guid invoiceId, Guid? companyId) =>
        WithCompanyContext($"/finance/invoices/{invoiceId:D}", companyId);

    public static string BuildInvoiceReviewDetailPath(Guid invoiceId, Guid? companyId) =>
        WithCompanyContext($"/finance/reviews/{invoiceId:D}", companyId);

    public static string BuildAnomalyDetailPath(Guid anomalyId, Guid? companyId) =>
        WithCompanyContext($"/finance/issues/{anomalyId:D}", companyId);

    public static string BuildTransparencyEventDetailPath(Guid eventId, Guid? companyId) =>
        WithCompanyContext($"/system/admin/transparency-events/{eventId:D}", companyId);

    public static string BuildTransparencyToolExecutionDetailPath(Guid executionId, Guid? companyId) =>
        WithCompanyContext($"/system/admin/tool-executions/{executionId:D}", companyId);

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
