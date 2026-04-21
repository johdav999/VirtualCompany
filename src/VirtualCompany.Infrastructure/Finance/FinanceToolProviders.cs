using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceToolProviderOptions
{
    public const string SectionName = "FinanceTools";
    public const string InternalProvider = "internal";
    public const string MockProvider = "mock";

    public string Provider { get; set; } = InternalProvider;
}

public sealed class InternalFinanceToolProvider : IFinanceToolProvider
{
    private readonly IFinanceReadService _readService;
    private readonly IFinanceCommandService _commandService;

    public InternalFinanceToolProvider(
        IFinanceReadService readService,
        IFinanceCommandService commandService)
    {
        _readService = readService;
        _commandService = commandService;
    }

    public Task<FinanceCashBalanceDto> GetCashBalanceAsync(
        GetFinanceCashBalanceQuery query,
        CancellationToken cancellationToken) =>
        _readService.GetCashBalanceAsync(query, cancellationToken);

    public Task<IReadOnlyList<FinanceTransactionDto>> GetTransactionsAsync(
        GetFinanceTransactionsQuery query,
        CancellationToken cancellationToken) =>
        _readService.GetTransactionsAsync(query, cancellationToken);

    public Task<IReadOnlyList<FinanceInvoiceDto>> GetInvoicesAsync(
        GetFinanceInvoicesQuery query,
        CancellationToken cancellationToken) =>
        _readService.GetInvoicesAsync(query, cancellationToken);

    public Task<FinanceMonthlyProfitAndLossDto> GetMonthlyProfitAndLossAsync(
        GetFinanceMonthlyProfitAndLossQuery query,
        CancellationToken cancellationToken) =>
        _readService.GetMonthlyProfitAndLossAsync(query, cancellationToken);

    public Task<FinanceExpenseBreakdownDto> GetExpenseBreakdownAsync(
        GetFinanceExpenseBreakdownQuery query,
        CancellationToken cancellationToken) =>
        _readService.GetExpenseBreakdownAsync(query, cancellationToken);

    public Task<IReadOnlyList<FinanceBillDto>> GetBillsAsync(
        GetFinanceBillsQuery query,
        CancellationToken cancellationToken) =>
        _readService.GetBillsAsync(query, cancellationToken);

    public Task<IReadOnlyList<FinanceAccountBalanceDto>> GetBalancesAsync(
        GetFinanceBalancesQuery query,
        CancellationToken cancellationToken) =>
        _readService.GetBalancesAsync(query, cancellationToken);

    public Task<FinanceAgentQueryResultDto> ResolveAgentQueryAsync(
        GetFinanceAgentQueryQuery query,
        CancellationToken cancellationToken) =>
        _readService.ResolveAgentQueryAsync(query, cancellationToken);

    public Task<FinanceTransactionCategoryRecommendationDto> RecommendTransactionCategoryAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var transactionId = ReadGuid(request.Payload, "transactionId")!.Value;
        var category = ReadString(request.Payload, "candidateCategory") ?? "review_required";
        return Task.FromResult(new FinanceTransactionCategoryRecommendationDto(transactionId, category, 0.75m));
    }

    public Task<FinanceInvoiceApprovalRecommendationDto> RecommendInvoiceApprovalDecisionAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var invoiceId = ReadGuid(request.Payload, "invoiceId")!.Value;
        var status = ReadString(request.Payload, "candidateStatus") ?? "approved";
        return Task.FromResult(new FinanceInvoiceApprovalRecommendationDto(invoiceId, status, 0.75m));
    }

    public Task<FinanceTransactionDto> UpdateTransactionCategoryAsync(
        UpdateFinanceTransactionCategoryCommand command,
        CancellationToken cancellationToken) =>
        _commandService.UpdateTransactionCategoryAsync(command, cancellationToken);

    public Task<FinanceInvoiceDto> UpdateInvoiceApprovalStatusAsync(
        UpdateFinanceInvoiceApprovalStatusCommand command,
        CancellationToken cancellationToken) =>
        _commandService.UpdateInvoiceApprovalStatusAsync(command, cancellationToken);

    private static string? ReadString(IReadOnlyDictionary<string, System.Text.Json.Nodes.JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not System.Text.Json.Nodes.JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, System.Text.Json.Nodes.JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not System.Text.Json.Nodes.JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<Guid>(out var guid) && guid != Guid.Empty)
        {
            return guid;
        }

        return value.TryGetValue<string>(out var text) && Guid.TryParse(text, out guid) && guid != Guid.Empty
            ? guid
            : null;
    }
}

public sealed class MockFinanceToolProvider : IFinanceToolProvider
{
    private static readonly DateTime DefaultAsOfUtc = new(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid CashAccountId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid TransactionId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid RevenueTransactionId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid CounterpartyId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid InvoiceId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid BillId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid AllocationId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid PriorTransactionId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid CurrentTransactionId = Guid.Parse("70000000-0000-0000-0000-000000000002");
    private static readonly Guid PayrollTransactionId = Guid.Parse("70000000-0000-0000-0000-000000000003");
    private static readonly Guid SoftwareTransactionId = Guid.Parse("70000000-0000-0000-0000-000000000004");

    public Task<FinanceCashBalanceDto> GetCashBalanceAsync(
        GetFinanceCashBalanceQuery query,
        CancellationToken cancellationToken)
    {
        var asOfUtc = query.AsOfUtc ?? DefaultAsOfUtc;
        return Task.FromResult(new FinanceCashBalanceDto(
            query.CompanyId,
            asOfUtc,
            125000m,
            "USD",
            [
                new FinanceAccountBalanceDto(
                    CashAccountId,
                    "1000",
                    "Mock Operating Cash",
                    "asset",
                    125000m,
                    "USD",
                    asOfUtc)
            ]));
    }

    public Task<IReadOnlyList<FinanceTransactionDto>> GetTransactionsAsync(
        GetFinanceTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FinanceTransactionDto> transactions =
        [
            new FinanceTransactionDto(
                TransactionId,
                CashAccountId,
                "Mock Operating Cash",
                CounterpartyId,
                "Mock Vendor",
                null,
                null,
                DefaultAsOfUtc.AddDays(-1),
                "uncategorized",
                -250m,
                "USD",
                "Mock cloud tools",
                "mock-txn-001",
                null),
            new FinanceTransactionDto(
                RevenueTransactionId,
                CashAccountId,
                "Mock Operating Cash",
                CounterpartyId,
                "Mock Customer",
                InvoiceId,
                null,
                DefaultAsOfUtc.AddDays(-2),
                "revenue",
                1500m,
                "USD",
                "Mock subscription revenue",
                "mock-txn-002",
                null)
        ];

        return Task.FromResult(transactions);
    }

    public Task<IReadOnlyList<FinanceInvoiceDto>> GetInvoicesAsync(
        GetFinanceInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FinanceInvoiceDto> invoices =
        [
            new FinanceInvoiceDto(
                InvoiceId,
                CounterpartyId,
                "Mock Customer",
                "MOCK-INV-001",
                DefaultAsOfUtc.AddDays(-15),
                DefaultAsOfUtc.AddDays(15),
                1500m,
                "USD",
                "awaiting_approval",
                null)
        ];

        return Task.FromResult(invoices);
    }

    public Task<FinanceMonthlyProfitAndLossDto> GetMonthlyProfitAndLossAsync(
        GetFinanceMonthlyProfitAndLossQuery query,
        CancellationToken cancellationToken)
    {
        var periodStartUtc = new DateTime(query.Year, query.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return Task.FromResult(new FinanceMonthlyProfitAndLossDto(
            query.CompanyId,
            query.Year,
            query.Month,
            periodStartUtc,
            periodStartUtc.AddMonths(1),
            25000m,
            8500m,
            16500m,
            "USD"));
    }

    public Task<FinanceExpenseBreakdownDto> GetExpenseBreakdownAsync(
        GetFinanceExpenseBreakdownQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FinanceExpenseCategoryDto> categories =
        [
            new FinanceExpenseCategoryDto("software", 3200m, "USD"),
            new FinanceExpenseCategoryDto("payroll", 5300m, "USD")
        ];

        return Task.FromResult(new FinanceExpenseBreakdownDto(
            query.CompanyId,
            query.StartUtc,
            query.EndUtc,
            8500m,
            "USD",
            categories));
    }

    public Task<IReadOnlyList<FinanceBillDto>> GetBillsAsync(
        GetFinanceBillsQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FinanceBillDto> bills =
        [
            new FinanceBillDto(
                BillId,
                CounterpartyId,
                "Mock Vendor",
                "MOCK-BILL-001",
                DefaultAsOfUtc.AddDays(-10),
                DefaultAsOfUtc.AddDays(20),
                250m,
                "USD",
                "open",
                null)
        ];

        return Task.FromResult(bills);
    }

    public Task<IReadOnlyList<FinanceAccountBalanceDto>> GetBalancesAsync(
        GetFinanceBalancesQuery query,
        CancellationToken cancellationToken)
    {
        var asOfUtc = query.AsOfUtc ?? DefaultAsOfUtc;
        IReadOnlyList<FinanceAccountBalanceDto> balances =
        [
            new FinanceAccountBalanceDto(
                CashAccountId,
                "1000",
                "Mock Operating Cash",
                "asset",
                125000m,
                "USD",
                asOfUtc),
            new FinanceAccountBalanceDto(
                Guid.Parse("10000000-0000-0000-0000-000000000002"),
                "2000",
                "Mock Accounts Payable",
                "liability",
                -250m,
                "USD",
                asOfUtc),
            new FinanceAccountBalanceDto(
                Guid.Parse("10000000-0000-0000-0000-000000000003"),
                "4000",
                "Mock Subscription Revenue",
                "revenue",
                25000m,
                "USD",
                asOfUtc)
        ];

        return Task.FromResult(balances);
    }

    public Task<FinanceAgentQueryResultDto> ResolveAgentQueryAsync(
        GetFinanceAgentQueryQuery query,
        CancellationToken cancellationToken)
    {
        var asOfUtc = query.AsOfUtc ?? DefaultAsOfUtc;
        if (!FinanceAgentQueryRouting.TryResolveIntent(query.QueryText, out var intent))
        {
            throw new ArgumentException(
                $"Unsupported finance agent query '{query.QueryText}'. Supported queries: {string.Join(", ", FinanceAgentQueryRouting.SupportedPhrases)}.",
                nameof(query));
        }

        return Task.FromResult(intent switch
        {
            var value when string.Equals(value, FinanceAgentQueryIntents.WhatShouldIPayThisWeek, StringComparison.Ordinal) =>
                new FinanceAgentQueryResultDto(
                    query.CompanyId,
                    value,
                    FinanceAgentQueryRouting.NormalizeQueryText(query.QueryText),
                    "Selected 1 payable item for this company week.",
                    "USD",
                    asOfUtc,
                    new FinanceAgentQueryPeriodDto(
                        asOfUtc,
                        new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                        null,
                        null,
                        "UTC"),
                    [
                        new FinanceAgentQueryItemDto(
                            BillId,
                            "bill",
                            CounterpartyId,
                            "Mock Vendor",
                            "MOCK-BILL-001",
                            new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                            250m,
                            "USD",
                            "Due within the current company week.",
                            1,
                            null,
                            null,
                            [BillId, AllocationId],
                            [
                                new FinanceAgentMetricComponentDto("original_amount", "Original amount", 250m, null, 250m, "USD", [BillId]),
                                new FinanceAgentMetricComponentDto("scheduled_outgoing_this_week", "Scheduled outgoing this week", 125m, null, 125m, "USD", [AllocationId])
                            ])
                    ],
                    [
                        new FinanceAgentMetricComponentDto("recommended_payables_total", "Recommended payables total", 250m, null, 250m, "USD", [BillId]),
                        new FinanceAgentMetricComponentDto("scheduled_outgoing_this_week", "Scheduled outgoing this week", 125m, null, 125m, "USD", [AllocationId])
                    ],
                    [AllocationId, BillId]),
            var value when string.Equals(value, FinanceAgentQueryIntents.WhichCustomersAreOverdue, StringComparison.Ordinal) =>
                new FinanceAgentQueryResultDto(
                    query.CompanyId,
                    value,
                    FinanceAgentQueryRouting.NormalizeQueryText(query.QueryText),
                    "Selected 1 overdue customer receivable.",
                    "USD",
                    asOfUtc,
                    new FinanceAgentQueryPeriodDto(asOfUtc, null, asOfUtc, null, null, "UTC"),
                    [
                        new FinanceAgentQueryItemDto(
                            InvoiceId,
                            "invoice",
                            CounterpartyId,
                            "Mock Customer",
                            "MOCK-INV-001",
                            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                            1500m,
                            "USD",
                            "1-30 days overdue.",
                            1,
                            15,
                            "1-30",
                            [InvoiceId],
                            [new FinanceAgentMetricComponentDto("remaining_balance", "Remaining balance", 1500m, null, 1500m, "USD", [InvoiceId])])
                    ],
                    [new FinanceAgentMetricComponentDto("overdue_receivables_total", "Overdue receivables total", 1500m, null, 1500m, "USD", [InvoiceId])],
                    [InvoiceId]),
            _ =>
                new FinanceAgentQueryResultDto(
                    query.CompanyId,
                    FinanceAgentQueryIntents.WhyIsCashDownThisMonth,
                    FinanceAgentQueryRouting.NormalizeQueryText(query.QueryText),
                    "Net cash movement is down by 1,100.00 USD month-to-date versus the same number of days in the prior month. Largest drivers: revenue (-800.00 USD) and payroll (-300.00 USD).",
                    "USD",
                    asOfUtc,
                    new FinanceAgentQueryPeriodDto(asOfUtc, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), asOfUtc.AddTicks(1), new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc), "UTC"),
                    [new FinanceAgentQueryItemDto(null, "cash_movement_category", null, null, "revenue", null, -800m, "USD", "Cash inflows for revenue are down by 800.00 USD versus the prior comparable period.", 1, null, null, [PriorTransactionId, CurrentTransactionId], [new FinanceAgentMetricComponentDto("revenue", "Revenue", 1200m, 2000m, -800m, "USD", [PriorTransactionId, CurrentTransactionId])])],
                    [
                        new FinanceAgentMetricComponentDto("net_cash_movement", "Net cash movement", -250m, 850m, -1100m, "USD", [PriorTransactionId, CurrentTransactionId, PayrollTransactionId, SoftwareTransactionId]),
                        new FinanceAgentMetricComponentDto("revenue", "Revenue", 1200m, 2000m, -800m, "USD", [PriorTransactionId, CurrentTransactionId]),
                        new FinanceAgentMetricComponentDto("payroll", "Payroll", -900m, -600m, -300m, "USD", [PayrollTransactionId])
                    ],
                    [CurrentTransactionId, PayrollTransactionId, PriorTransactionId, SoftwareTransactionId])
        });
    }

    public Task<FinanceTransactionCategoryRecommendationDto> RecommendTransactionCategoryAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var transactionId = ReadGuid(request.Payload, "transactionId") ?? TransactionId;
        var category = ReadString(request.Payload, "candidateCategory") ?? "software";
        return Task.FromResult(new FinanceTransactionCategoryRecommendationDto(transactionId, category, 0.8m));
    }

    public Task<FinanceInvoiceApprovalRecommendationDto> RecommendInvoiceApprovalDecisionAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var invoiceId = ReadGuid(request.Payload, "invoiceId") ?? InvoiceId;
        var status = ReadString(request.Payload, "candidateStatus") ?? "approved";
        return Task.FromResult(new FinanceInvoiceApprovalRecommendationDto(invoiceId, status, 0.8m));
    }

    public Task<FinanceTransactionDto> UpdateTransactionCategoryAsync(
        UpdateFinanceTransactionCategoryCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(new FinanceTransactionDto(
            command.TransactionId,
            CashAccountId,
            "Mock Operating Cash",
            CounterpartyId,
            "Mock Vendor",
            null,
            null,
            DefaultAsOfUtc,
            command.Category,
            -250m,
            "USD",
            "Mock categorized transaction",
            "mock-txn-001",
            null));

    public Task<FinanceInvoiceDto> UpdateInvoiceApprovalStatusAsync(
        UpdateFinanceInvoiceApprovalStatusCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(new FinanceInvoiceDto(
            command.InvoiceId,
            CounterpartyId,
            "Mock Customer",
            "MOCK-INV-001",
            DefaultAsOfUtc.AddDays(-15),
            DefaultAsOfUtc.AddDays(15),
            1500m,
            "USD",
            command.Status,
            null));

    private static string? ReadString(IReadOnlyDictionary<string, System.Text.Json.Nodes.JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not System.Text.Json.Nodes.JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, System.Text.Json.Nodes.JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not System.Text.Json.Nodes.JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<Guid>(out var guid) && guid != Guid.Empty)
        {
            return guid;
        }

        return value.TryGetValue<string>(out var text) && Guid.TryParse(text, out guid) && guid != Guid.Empty
            ? guid
            : null;
    }
}