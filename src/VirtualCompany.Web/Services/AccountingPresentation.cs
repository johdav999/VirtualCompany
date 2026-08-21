namespace VirtualCompany.Web.Services;

public static class AccountingPresentation
{
    public static string PeriodStatusClass(AccountingPeriodResponse period) =>
        period.IsReportingLocked ? "accounting-badge accounting-badge--warning" : period.IsClosed ? "accounting-badge accounting-badge--neutral" : "accounting-badge accounting-badge--success";

    public static string AccountStatusClass(bool isPostingEnabled) =>
        isPostingEnabled ? "accounting-badge accounting-badge--success" : "accounting-badge accounting-badge--neutral";

    public static string DefaultNormalBalance(string accountClass) =>
        accountClass.Trim().ToLowerInvariant() is "asset" or "expense" ? "debit" : "credit";

    public static string AccountClassResourceKey(string accountClass) =>
        accountClass.Trim().ToLowerInvariant() switch
        {
            "asset" => "Assets",
            "liability" => "Liabilities",
            "equity" => "Equity",
            "income" or "revenue" => "Income",
            "expense" => "Expenses",
            _ => "NotClassified"
        };

    public static string NormalBalanceResourceKey(string normalBalance) =>
        normalBalance.Trim().ToLowerInvariant() switch
        {
            "debit" => "Debit",
            "credit" => "Credit",
            _ => "NotSet"
        };

    public static string FiscalYearLabel(AccountingFiscalYearResponse fiscalYear) =>
        fiscalYear.StartDate.Year == fiscalYear.EndDate.Year
            ? fiscalYear.StartDate.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{fiscalYear.StartDate.Year}/{fiscalYear.EndDate.Year}";

    public static string JournalStatusClass(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "posted" or "approved" => "accounting-badge accounting-badge--success",
        "awaiting_approval" or "pending" => "accounting-badge accounting-badge--warning",
        "rejected" or "discarded" or "approval_expired" => "accounting-badge accounting-badge--danger",
        _ => "accounting-badge accounting-badge--neutral"
    };

    public static string JournalStatusResourceKey(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "posted" => "Posted", "approved" => "Approved", "awaiting_approval" or "pending" => "AwaitingApproval",
        "rejected" => "Rejected", "discarded" => "Discarded", "approval_expired" or "expired" => "ApprovalExpired",
        _ => "Draft"
    };

    public static string EntryTypeResourceKey(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "reversal" => "Reversal", "adjustment" => "Adjustment", "manual" => "Manual", _ => "JournalEntry"
    };

    public static string JournalSourceResourceKey(string? source) => source?.Trim().ToLowerInvariant() switch
    {
        "manual_journal_draft" => "ManualJournalSource", "reversal" => "Reversal", _ => "AutomatedSource"
    };

    public static string Humanize(string value) => string.Join(' ', value.Split(['_', '-', '.'], StringSplitOptions.RemoveEmptyEntries)) switch
    {
        { Length: 0 } => value,
        var words => char.ToUpperInvariant(words[0]) + words[1..]
    };
}
