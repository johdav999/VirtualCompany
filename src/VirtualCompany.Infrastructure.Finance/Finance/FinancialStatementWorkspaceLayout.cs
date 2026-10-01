using VirtualCompany.Application.Finance;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

/// <summary>Management layout over retained statement classifications. Never changes ledger facts.</summary>
public static class FinancialStatementWorkspaceLayout
{
    public const string Version = "management-statements/1.0";

    public static decimal? ChangePercent(decimal current, decimal? comparison) =>
        comparison is > 0m ? decimal.Round((current - comparison.Value) / comparison.Value * 100m, 1) : null;

    public static IReadOnlyList<StatementWorkspaceRow> Build(string kind,
        IReadOnlyList<FinanceStatementLineDto> current, IReadOnlyList<FinanceStatementLineDto>? comparison,
        IAccountingChartCatalog? basCatalog)
    {
        string Group(FinanceStatementLineDto line)
        {
            // BAS subdivisions are used only for a Swedish policy and verified catalog membership.
            var bas = basCatalog is not null && basCatalog.TryGetAccount(line.AccountCode, out var account) ? account : null;
            var group = bas?.GroupCode ?? "";
            if (kind == "profit-loss") return line.LineClassification switch
            {
                "revenue" or "contra_revenue" => group is "38" or "39" ? "other_revenue" : "sales",
                "cost_of_sales" => "cost_of_sales",
                "depreciation_and_amortization" => "depreciation",
                "operating_expense" => group == "78" ? "depreciation" : group is "70" or "71" or "72" or "73" or "74" or "75" or "76" ? "staff" : "external",
                "non_operating_income" => "financial_income",
                "non_operating_expense" => "financial_expense",
                "income_tax" => "tax",
                _ => "unclassified"
            };
            if (line.AccountCode == "current_earnings") return "earnings";
            return line.LineClassification switch
            {
                "non_current_asset" => "fixed_assets",
                "current_asset" => group == "14" ? "inventory" : group == "15" ? "receivables" : group == "19" ? "cash" : "other_assets",
                "non_current_liability" => "long_debt",
                "current_liability" => bas?.Code.StartsWith("244", StringComparison.Ordinal) == true || bas?.Code.StartsWith("246", StringComparison.Ordinal) == true || bas?.Code.StartsWith("247", StringComparison.Ordinal) == true ? "payables" : "short_debt",
                "equity" => bas?.Code.StartsWith("2081", StringComparison.Ordinal) == true ? "capital" :
                    bas?.Code.StartsWith("2099", StringComparison.Ordinal) == true ? "earnings" : "retained",
                _ => "unclassified"
            };
        }
        decimal Display(FinanceStatementLineDto line) => kind == "profit-loss" &&
            line.ReportSection != "profit_and_loss_revenue" && line.LineClassification != "non_operating_income"
            ? -line.Amount : line.Amount;
        var now = current.GroupBy(Group).ToDictionary(x => x.Key, x => x.ToArray());
        var prior = comparison?.GroupBy(Group).ToDictionary(x => x.Key, x => x.ToArray());
        var result = new List<StatementWorkspaceRow>();
        StatementWorkspaceRow Detail(string key, string sv, string en)
        {
            var a = now.GetValueOrDefault(key, []);
            var b = prior?.GetValueOrDefault(key, []);
            var identities = a.Concat(b ?? []).Select(x => (x.FinanceAccountId, x.AccountCode)).Distinct().ToArray();
            var accounts = identities.Select(id =>
            {
                var lines = a.Where(x => (x.FinanceAccountId, x.AccountCode) == id).ToArray();
                var previous = b?.Where(x => (x.FinanceAccountId, x.AccountCode) == id).ToArray();
                var source = lines.FirstOrDefault() ?? previous!.First();
                return new StatementWorkspaceAccount(id.FinanceAccountId, id.AccountCode, source.AccountName,
                    lines.Sum(Display), b is null ? null : previous!.Sum(Display), source.Currency);
            }).OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
            var amount = a.Sum(Display); decimal? compare = b is null ? null : b.Sum(Display);
            return new(key, sv, en, "detail", amount, compare, ChangePercent(amount, compare), accounts);
        }
        void Add(string key, string sv, string en) => result.Add(Detail(key, sv, en));
        void Total(string key, string sv, string en, string[] keys, string type = "total")
        {
            var rows = result.Where(x => keys.Contains(x.Key)).ToArray();
            var amount = rows.Sum(x => x.Amount);
            decimal? previous = comparison is null ? null : rows.Sum(x => x.ComparisonAmount ?? 0m);
            result.Add(new(key, sv, en, type, amount, previous, ChangePercent(amount, previous),
                rows.SelectMany(x => x.Accounts).ToArray()));
        }
        void Heading(string key, string sv, string en) => result.Add(new(key, sv, en, "heading", 0, null, null, []));
        if (kind == "profit-loss")
        {
            Heading("revenue_heading", "Rörelseintäkter", "Operating income");
            Add("sales", "Nettoomsättning", "Net sales"); Add("other_revenue", "Övriga rörelseintäkter", "Other operating income");
            Total("revenue", "Summa rörelseintäkter", "Total operating income", ["sales", "other_revenue"]);
            Heading("cost_heading", "Rörelsekostnader", "Operating expenses");
            Add("cost_of_sales", "Råvaror och handelsvaror", "Raw materials and goods");
            Add("external", "Övriga externa kostnader", "Other external expenses");
            Add("staff", "Personalkostnader", "Personnel expenses"); Add("depreciation", "Avskrivningar", "Depreciation");
            Total("operating", "Rörelseresultat", "Operating profit", ["revenue", "cost_of_sales", "external", "staff", "depreciation"]);
            Heading("financial_heading", "Finansiella poster", "Financial items");
            Add("financial_income", "Finansiella intäkter", "Financial income"); Add("financial_expense", "Finansiella kostnader", "Financial expenses");
            Total("before_tax", "Resultat före skatt", "Profit before tax", ["operating", "financial_income", "financial_expense"]);
            Add("tax", "Skatt", "Income tax");
            if (now.ContainsKey("unclassified") || prior?.ContainsKey("unclassified") == true) Add("unclassified", "Ej klassificerat", "Unclassified");
            Total("net", "Periodens resultat", "Period profit", ["before_tax", "tax", "unclassified"], "result");
            foreach (var (heading, keys) in new[] {
                ("revenue_heading", new[] { "sales", "other_revenue" }),
                ("cost_heading", new[] { "cost_of_sales", "external", "staff", "depreciation" }),
                ("financial_heading", new[] { "financial_income", "financial_expense" }) })
            {
                var index = result.FindIndex(x => x.Key == heading);
                var children = result.Where(x => keys.Contains(x.Key)).ToArray();
                var amount = children.Sum(x => x.Amount);
                decimal? baseline = comparison is null ? null : children.Sum(x => x.ComparisonAmount ?? 0m);
                result[index] = result[index] with { Amount = amount, ComparisonAmount = baseline,
                    ChangePercent = ChangePercent(amount, baseline), Accounts = children.SelectMany(x => x.Accounts).ToArray() };
            }
        }
        else
        {
            Heading("assets_heading", "TILLGÅNGAR", "ASSETS");
            Add("fixed_assets", "Anläggningstillgångar", "Non-current assets"); Add("inventory", "Varulager", "Inventory");
            Add("receivables", "Kundfordringar", "Trade receivables"); Add("cash", "Kassa och bank", "Cash and bank");
            if (now.ContainsKey("other_assets") || prior?.ContainsKey("other_assets") == true) Add("other_assets", "Övriga omsättningstillgångar", "Other current assets");
            Total("assets", "Summa tillgångar", "Total assets", ["fixed_assets", "inventory", "receivables", "cash", "other_assets"]);
            Heading("equity_heading", "EGET KAPITAL", "EQUITY");
            Add("capital", "Aktiekapital", "Share capital"); Add("retained", "Balanserat resultat och övrigt eget kapital", "Retained earnings and other equity");
            Add("earnings", "Periodens resultat", "Period profit"); Total("equity", "Summa eget kapital", "Total equity", ["capital", "retained", "earnings"]);
            Heading("debt_heading", "SKULDER", "LIABILITIES");
            Add("long_debt", "Långfristiga skulder", "Non-current liabilities"); Add("payables", "Leverantörsskulder", "Trade payables");
            Add("short_debt", "Övriga kortfristiga skulder", "Other current liabilities");
            Total("debt", "Summa skulder", "Total liabilities", ["long_debt", "payables", "short_debt"]);
            if (now.ContainsKey("unclassified") || prior?.ContainsKey("unclassified") == true) Add("unclassified", "Ej klassificerat", "Unclassified");
            Total("equity_debt", "Summa eget kapital och skulder", "Total equity and liabilities", ["equity", "debt"]);
        }
        return result;
    }
}
