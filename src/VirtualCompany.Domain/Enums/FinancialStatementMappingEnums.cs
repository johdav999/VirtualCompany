namespace VirtualCompany.Domain.Enums;

public enum FinancialStatementType
{
    BalanceSheet = 1,
    ProfitAndLoss = 2,
    CashFlow = 3
}

public enum FinancialStatementReportSection
{
    BalanceSheetAssets = 1,
    BalanceSheetLiabilities = 2,
    BalanceSheetEquity = 3,
    ProfitAndLossRevenue = 4,
    ProfitAndLossCostOfSales = 5,
    ProfitAndLossOperatingExpenses = 6,
    ProfitAndLossOtherIncomeExpense = 7,
    ProfitAndLossTaxes = 8,
    CashFlowOperatingActivities = 9,
    CashFlowInvestingActivities = 10,
    CashFlowFinancingActivities = 11,
    CashFlowSupplementalDisclosures = 12
}

public enum FinancialStatementLineClassification
{
    CurrentAsset = 1,
    NonCurrentAsset = 2,
    CurrentLiability = 3,
    NonCurrentLiability = 4,
    Equity = 5,
    Revenue = 6,
    ContraRevenue = 7,
    CostOfSales = 8,
    OperatingExpense = 9,
    NonOperatingIncome = 10,
    NonOperatingExpense = 11,
    IncomeTax = 12,
    DepreciationAndAmortization = 13,
    WorkingCapital = 14,
    NonCashAdjustment = 15,
    CashReceipt = 16,
    CashDisbursement = 17,
    InvestingCashInflow = 18,
    InvestingCashOutflow = 19,
    FinancingCashInflow = 20,
    FinancingCashOutflow = 21,
    SupplementalDisclosure = 22
}

public static class FinancialStatementTypeValues
{
    private static readonly IReadOnlyDictionary<FinancialStatementType, string> Values = new Dictionary<FinancialStatementType, string>
    {
        [FinancialStatementType.BalanceSheet] = "balance_sheet",
        [FinancialStatementType.ProfitAndLoss] = "profit_and_loss",
        [FinancialStatementType.CashFlow] = "cash_flow"
    };

    private static readonly IReadOnlyDictionary<string, FinancialStatementType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(static value => value).ToArray();

    public static string ToStorageValue(this FinancialStatementType type) =>
        Values.TryGetValue(type, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported financial statement type.");

    public static bool TryParse(string? value, out FinancialStatementType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (ReverseValues.TryGetValue(trimmed, out type))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out type) && Values.ContainsKey(type);
    }

    public static FinancialStatementType Parse(string value) =>
        TryParse(value, out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported financial statement type. Allowed values: {string.Join(", ", AllowedValues)}.");

    public static void EnsureSupported(FinancialStatementType type, string paramName) => _ = ToStorageValue(type);

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", AllowedValues.Select(static value => $"'{value}'"))})";
}

public static class FinancialStatementReportSectionValues
{
    private static readonly IReadOnlyDictionary<FinancialStatementReportSection, string> Values = new Dictionary<FinancialStatementReportSection, string>
    {
        [FinancialStatementReportSection.BalanceSheetAssets] = "balance_sheet_assets",
        [FinancialStatementReportSection.BalanceSheetLiabilities] = "balance_sheet_liabilities",
        [FinancialStatementReportSection.BalanceSheetEquity] = "balance_sheet_equity",
        [FinancialStatementReportSection.ProfitAndLossRevenue] = "profit_and_loss_revenue",
        [FinancialStatementReportSection.ProfitAndLossCostOfSales] = "profit_and_loss_cost_of_sales",
        [FinancialStatementReportSection.ProfitAndLossOperatingExpenses] = "profit_and_loss_operating_expenses",
        [FinancialStatementReportSection.ProfitAndLossOtherIncomeExpense] = "profit_and_loss_other_income_expense",
        [FinancialStatementReportSection.ProfitAndLossTaxes] = "profit_and_loss_taxes",
        [FinancialStatementReportSection.CashFlowOperatingActivities] = "cash_flow_operating_activities",
        [FinancialStatementReportSection.CashFlowInvestingActivities] = "cash_flow_investing_activities",
        [FinancialStatementReportSection.CashFlowFinancingActivities] = "cash_flow_financing_activities",
        [FinancialStatementReportSection.CashFlowSupplementalDisclosures] = "cash_flow_supplemental_disclosures"
    };

    private static readonly IReadOnlyDictionary<string, FinancialStatementReportSection> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(static value => value).ToArray();

    public static string ToStorageValue(this FinancialStatementReportSection section) =>
        Values.TryGetValue(section, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(section), section, "Unsupported financial statement report section.");

    public static bool TryParse(string? value, out FinancialStatementReportSection section)
    {
        section = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (ReverseValues.TryGetValue(trimmed, out section))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out section) && Values.ContainsKey(section);
    }

    public static FinancialStatementReportSection Parse(string value) =>
        TryParse(value, out var section)
            ? section
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported financial statement report section. Allowed values: {string.Join(", ", AllowedValues)}.");

    public static void EnsureSupported(FinancialStatementReportSection section, string paramName) => _ = ToStorageValue(section);

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", AllowedValues.Select(static value => $"'{value}'"))})";
}

public static class FinancialStatementLineClassificationValues
{
    private static readonly IReadOnlyDictionary<FinancialStatementLineClassification, string> Values = new Dictionary<FinancialStatementLineClassification, string>
    {
        [FinancialStatementLineClassification.CurrentAsset] = "current_asset",
        [FinancialStatementLineClassification.NonCurrentAsset] = "non_current_asset",
        [FinancialStatementLineClassification.CurrentLiability] = "current_liability",
        [FinancialStatementLineClassification.NonCurrentLiability] = "non_current_liability",
        [FinancialStatementLineClassification.Equity] = "equity",
        [FinancialStatementLineClassification.Revenue] = "revenue",
        [FinancialStatementLineClassification.ContraRevenue] = "contra_revenue",
        [FinancialStatementLineClassification.CostOfSales] = "cost_of_sales",
        [FinancialStatementLineClassification.OperatingExpense] = "operating_expense",
        [FinancialStatementLineClassification.NonOperatingIncome] = "non_operating_income",
        [FinancialStatementLineClassification.NonOperatingExpense] = "non_operating_expense",
        [FinancialStatementLineClassification.IncomeTax] = "income_tax",
        [FinancialStatementLineClassification.DepreciationAndAmortization] = "depreciation_and_amortization",
        [FinancialStatementLineClassification.WorkingCapital] = "working_capital",
        [FinancialStatementLineClassification.NonCashAdjustment] = "non_cash_adjustment",
        [FinancialStatementLineClassification.CashReceipt] = "cash_receipt",
        [FinancialStatementLineClassification.CashDisbursement] = "cash_disbursement",
        [FinancialStatementLineClassification.InvestingCashInflow] = "investing_cash_inflow",
        [FinancialStatementLineClassification.InvestingCashOutflow] = "investing_cash_outflow",
        [FinancialStatementLineClassification.FinancingCashInflow] = "financing_cash_inflow",
        [FinancialStatementLineClassification.FinancingCashOutflow] = "financing_cash_outflow",
        [FinancialStatementLineClassification.SupplementalDisclosure] = "supplemental_disclosure"
    };

    private static readonly IReadOnlyDictionary<string, FinancialStatementLineClassification> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(static value => value).ToArray();

    public static string ToStorageValue(this FinancialStatementLineClassification classification) =>
        Values.TryGetValue(classification, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(classification), classification, "Unsupported financial statement line classification.");

    public static bool TryParse(string? value, out FinancialStatementLineClassification classification)
    {
        classification = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (ReverseValues.TryGetValue(trimmed, out classification))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out classification) && Values.ContainsKey(classification);
    }

    public static FinancialStatementLineClassification Parse(string value) =>
        TryParse(value, out var classification)
            ? classification
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported financial statement line classification. Allowed values: {string.Join(", ", AllowedValues)}.");

    public static void EnsureSupported(FinancialStatementLineClassification classification, string paramName) => _ = ToStorageValue(classification);

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", AllowedValues.Select(static value => $"'{value}'"))})";
}

public static class FinancialStatementMappingCompatibility
{
    public static bool IsCompatible(
        FinancialStatementType statementType,
        FinancialStatementReportSection reportSection,
        FinancialStatementLineClassification lineClassification) =>
        SupportsSection(statementType, reportSection) &&
        SupportsLineClassification(statementType, lineClassification);

    public static void EnsureCompatible(
        FinancialStatementType statementType,
        FinancialStatementReportSection reportSection,
        FinancialStatementLineClassification lineClassification)
    {
        if (!SupportsSection(statementType, reportSection))
        {
            throw new ArgumentException(
                $"Report section '{reportSection}' is not valid for statement type '{statementType}'.",
                nameof(reportSection));
        }

        if (!SupportsLineClassification(statementType, lineClassification))
        {
            throw new ArgumentException(
                $"Line classification '{lineClassification}' is not valid for statement type '{statementType}'.",
                nameof(lineClassification));
        }
    }

    private static bool SupportsSection(FinancialStatementType statementType, FinancialStatementReportSection reportSection) =>
        statementType switch
        {
            FinancialStatementType.BalanceSheet => reportSection is
                FinancialStatementReportSection.BalanceSheetAssets or
                FinancialStatementReportSection.BalanceSheetLiabilities or
                FinancialStatementReportSection.BalanceSheetEquity,
            FinancialStatementType.ProfitAndLoss => reportSection is
                FinancialStatementReportSection.ProfitAndLossRevenue or
                FinancialStatementReportSection.ProfitAndLossCostOfSales or
                FinancialStatementReportSection.ProfitAndLossOperatingExpenses or
                FinancialStatementReportSection.ProfitAndLossOtherIncomeExpense or
                FinancialStatementReportSection.ProfitAndLossTaxes,
            FinancialStatementType.CashFlow => reportSection is
                FinancialStatementReportSection.CashFlowOperatingActivities or
                FinancialStatementReportSection.CashFlowInvestingActivities or
                FinancialStatementReportSection.CashFlowFinancingActivities or
                FinancialStatementReportSection.CashFlowSupplementalDisclosures,
            _ => false
        };

    private static bool SupportsLineClassification(FinancialStatementType statementType, FinancialStatementLineClassification lineClassification) =>
        statementType switch
        {
            FinancialStatementType.BalanceSheet => lineClassification is
                FinancialStatementLineClassification.CurrentAsset or
                FinancialStatementLineClassification.NonCurrentAsset or
                FinancialStatementLineClassification.CurrentLiability or
                FinancialStatementLineClassification.NonCurrentLiability or
                FinancialStatementLineClassification.Equity,
            FinancialStatementType.ProfitAndLoss => lineClassification is
                FinancialStatementLineClassification.Revenue or
                FinancialStatementLineClassification.ContraRevenue or
                FinancialStatementLineClassification.CostOfSales or
                FinancialStatementLineClassification.OperatingExpense or
                FinancialStatementLineClassification.NonOperatingIncome or
                FinancialStatementLineClassification.NonOperatingExpense or
                FinancialStatementLineClassification.IncomeTax,
            FinancialStatementType.CashFlow => lineClassification is
                FinancialStatementLineClassification.DepreciationAndAmortization or
                FinancialStatementLineClassification.WorkingCapital or
                FinancialStatementLineClassification.NonCashAdjustment or
                FinancialStatementLineClassification.CashReceipt or
                FinancialStatementLineClassification.CashDisbursement or
                FinancialStatementLineClassification.InvestingCashInflow or
                FinancialStatementLineClassification.InvestingCashOutflow or
                FinancialStatementLineClassification.FinancingCashInflow or
                FinancialStatementLineClassification.FinancingCashOutflow or
                FinancialStatementLineClassification.SupplementalDisclosure,
            _ => false
        };
}

public static class FinancialStatementMappingValidationErrorCodes
{
    public const string ConflictDuplicateActive = "ACCOUNT_MAPPING_CONFLICT_DUPLICATE_ACTIVE";
    public const string UnmappedActiveReportableAccount = "ACCOUNT_MAPPING_UNMAPPED_ACTIVE_REPORTABLE";
    public const string AccountNotFound = "ACCOUNT_MAPPING_ACCOUNT_NOT_FOUND";
    public const string AccountCompanyMismatch = "ACCOUNT_MAPPING_ACCOUNT_COMPANY_MISMATCH";
    public const string InvalidStatementType = "ACCOUNT_MAPPING_INVALID_STATEMENT_TYPE";
    public const string InvalidReportSection = "ACCOUNT_MAPPING_INVALID_REPORT_SECTION";
    public const string InvalidLineClassification = "ACCOUNT_MAPPING_INVALID_LINE_CLASSIFICATION";
    public const string MappingNotFound = "ACCOUNT_MAPPING_MAPPING_NOT_FOUND";
    public const string ValidationFailed = "ACCOUNT_MAPPING_VALIDATION_FAILED";
}
