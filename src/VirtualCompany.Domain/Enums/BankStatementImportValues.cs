namespace VirtualCompany.Domain.Enums;

public static class BankStatementImportJobStatuses
{
    public const string PendingScan = "pending_scan";
    public const string PreviewReady = "preview_ready";
    public const string AttentionRequired = "attention_required";
    public const string ReadyToImport = "ready_to_import";
    public const string Importing = "importing";
    public const string PartiallyImported = "partially_imported";
    public const string Completed = "completed";
    public const string StatusOnly = "status_only";
    public const string Failed = "failed";
}

public static class BankStatementImportRowOutcomes
{
    public const string Accepted = "accepted";
    public const string Duplicate = "duplicate";
    public const string Error = "error";
    public const string PaymentStatus = "payment_status";
    public const string Imported = "imported";
    public const string Skipped = "skipped";
}

public static class BankStatementImportFormats
{
    public const string Camt052 = "camt.052";
    public const string Camt053 = "camt.053";
    public const string Camt054 = "camt.054";
    public const string Pain002 = "pain.002";
    public const string Csv = "csv";
}

public static class BankStatementImportIssueSeverities
{
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Information = "information";
}

public static class BankStatementImportConflictDecisions
{
    public const string Skip = "skip";
}
