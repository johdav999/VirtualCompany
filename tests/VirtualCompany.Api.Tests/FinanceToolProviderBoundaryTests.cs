using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceToolProviderBoundaryTests
{
    private static readonly IReadOnlyDictionary<string, string> RegisteredFinanceToolProviderOperations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["get_cash_balance"] = nameof(IFinanceToolProvider.GetCashBalanceAsync),
        ["list_transactions"] = nameof(IFinanceToolProvider.GetTransactionsAsync),
        ["list_uncategorized_transactions"] = nameof(IFinanceToolProvider.GetTransactionsAsync),
        ["list_invoices_awaiting_approval"] = nameof(IFinanceToolProvider.GetInvoicesAsync),
        ["get_profit_and_loss_summary"] = nameof(IFinanceToolProvider.GetMonthlyProfitAndLossAsync),
        ["recommend_transaction_category"] = nameof(IFinanceToolProvider.RecommendTransactionCategoryAsync),
        ["recommend_invoice_approval_decision"] = nameof(IFinanceToolProvider.RecommendInvoiceApprovalDecisionAsync),
        ["categorize_transaction"] = nameof(IFinanceToolProvider.UpdateTransactionCategoryAsync),
        ["approve_invoice"] = nameof(IFinanceToolProvider.UpdateInvoiceApprovalStatusAsync)
    };

    private static readonly string[] ForbiddenOrchestrationFinanceReferences =
    [
        "IFinanceReadService",
        "IFinanceCommandService",
        "CompanyFinanceReadService",
        "CompanyFinanceCommandService",
        "VirtualCompany.Infrastructure.Finance",
        "VirtualCompany.Domain.Entities",
        "FinanceAccount",
        "FinanceBalance",
        "FinanceBill",
        "FinanceCounterparty",
        "FinanceInvoice",
        "FinancePolicyConfiguration",
        "FinanceTransaction"
    ];

    [Fact]
    public void Orchestration_tool_contract_does_not_depend_on_internal_finance_services()
    {
        var checkedFiles = GetOrchestrationBoundaryFiles();

        Assert.NotEmpty(checkedFiles);

        var violations = new List<string>();
        foreach (var file in checkedFiles)
        {
            var source = File.ReadAllText(file);
            violations.AddRange(ForbiddenOrchestrationFinanceReferences
                .Where(reference => source.Contains(reference, StringComparison.Ordinal))
                .Select(reference => $"{Path.GetFileName(file)} directly references {reference}."));
        }

        Assert.False(
            violations.Count > 0,
            "Finance tool orchestration must depend on IFinanceToolProvider only. Violations: " + string.Join(" ", violations));
    }

    [Fact]
    public void Internal_company_tool_contract_dispatches_registered_finance_tools_through_provider_boundary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "src", "VirtualCompany.Infrastructure", "Companies", "InternalCompanyToolContract.cs"));

        foreach (var (toolName, providerMethod) in RegisteredFinanceToolProviderOperations)
        {
            Assert.Contains($"\"{toolName}\"", source, StringComparison.Ordinal);
            Assert.Contains($"_financeToolProvider.{providerMethod}", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Finance_tool_provider_contract_exposes_all_registered_finance_operations()
    {
        var providerMethods = typeof(VirtualCompany.Application.Finance.IFinanceToolProvider)
            .GetMethods()
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(RegisteredFinanceToolProviderOperations.Values.Distinct(StringComparer.Ordinal), methodName => Assert.Contains(methodName, providerMethods));
        Assert.Contains("GetExpenseBreakdownAsync", providerMethods);
        Assert.Contains("GetBillsAsync", providerMethods);
        Assert.Contains("GetBalancesAsync", providerMethods);
    }

    [Fact]
    public void Finance_tool_provider_selection_can_use_mock_adapter()
    {
        var services = new ServiceCollection();
        services.AddOptions<FinanceToolProviderOptions>()
            .Configure(options => options.Provider = FinanceToolProviderOptions.MockProvider);
        services.AddScoped<InternalFinanceToolProvider>();
        services.AddScoped<MockFinanceToolProvider>();
        services.AddScoped<IFinanceToolProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FinanceToolProviderOptions>>().Value;
            return options.Provider.Equals(FinanceToolProviderOptions.MockProvider, StringComparison.OrdinalIgnoreCase)
                ? provider.GetRequiredService<MockFinanceToolProvider>()
                : provider.GetRequiredService<InternalFinanceToolProvider>();
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<MockFinanceToolProvider>(scope.ServiceProvider.GetRequiredService<IFinanceToolProvider>());
    }

    [Fact]
    public async Task Mock_finance_provider_implements_registered_finance_tool_operations()
    {
        var provider = new MockFinanceToolProvider();
        var companyId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        var cashBalance = await provider.GetCashBalanceAsync(
            new GetFinanceCashBalanceQuery(companyId, new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);
        Assert.Equal(companyId, cashBalance.CompanyId);
        Assert.True(cashBalance.Amount > 0);
        Assert.False(string.IsNullOrWhiteSpace(cashBalance.Currency));
        Assert.NotEmpty(cashBalance.Accounts);

        var transactions = await provider.GetTransactionsAsync(
            new GetFinanceTransactionsQuery(companyId, Limit: 25),
            CancellationToken.None);
        Assert.NotEmpty(transactions);
        Assert.Contains(transactions, transaction => string.Equals(transaction.TransactionType, "uncategorized", StringComparison.OrdinalIgnoreCase));

        var invoices = await provider.GetInvoicesAsync(
            new GetFinanceInvoicesQuery(companyId, Limit: 25),
            CancellationToken.None);
        Assert.NotEmpty(invoices);
        Assert.Contains(invoices, invoice => string.Equals(invoice.Status, "awaiting_approval", StringComparison.OrdinalIgnoreCase));

        var profitAndLoss = await provider.GetMonthlyProfitAndLossAsync(
            new GetFinanceMonthlyProfitAndLossQuery(companyId, 2026, 4),
            CancellationToken.None);
        Assert.Equal(companyId, profitAndLoss.CompanyId);
        Assert.Equal(profitAndLoss.Revenue - profitAndLoss.Expenses, profitAndLoss.NetResult);
        Assert.False(string.IsNullOrWhiteSpace(profitAndLoss.Currency));

        var expenseBreakdown = await provider.GetExpenseBreakdownAsync(
            new GetFinanceExpenseBreakdownQuery(
                companyId,
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);
        Assert.Equal(companyId, expenseBreakdown.CompanyId);
        Assert.True(expenseBreakdown.TotalExpenses > 0);
        Assert.NotEmpty(expenseBreakdown.Categories);

        var bills = await provider.GetBillsAsync(
            new GetFinanceBillsQuery(companyId, Limit: 25),
            CancellationToken.None);
        Assert.NotEmpty(bills);
        Assert.All(bills, bill =>
        {
            Assert.False(string.IsNullOrWhiteSpace(bill.BillNumber));
            Assert.False(string.IsNullOrWhiteSpace(bill.Status));
        });

        var balances = await provider.GetBalancesAsync(
            new GetFinanceBalancesQuery(companyId, new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);
        Assert.NotEmpty(balances);
        Assert.All(balances, balance =>
        {
            Assert.False(string.IsNullOrWhiteSpace(balance.AccountCode));
            Assert.False(string.IsNullOrWhiteSpace(balance.Currency));
        });

        var categoryRecommendation = await provider.RecommendTransactionCategoryAsync(
            CreateFinanceToolRequest(
                companyId,
                "recommend_transaction_category",
                ToolActionType.Recommend,
                ("transactionId", JsonValue.Create(transactionId)),
                ("candidateCategory", JsonValue.Create("software"))),
            CancellationToken.None);
        Assert.Equal(transactionId, categoryRecommendation.TransactionId);
        Assert.Equal("software", categoryRecommendation.RecommendedCategory);
        Assert.InRange(categoryRecommendation.Confidence, 0m, 1m);

        var approvalRecommendation = await provider.RecommendInvoiceApprovalDecisionAsync(
            CreateFinanceToolRequest(
                companyId,
                "recommend_invoice_approval_decision",
                ToolActionType.Recommend,
                ("invoiceId", JsonValue.Create(invoiceId)),
                ("candidateStatus", JsonValue.Create("approved"))),
            CancellationToken.None);
        Assert.Equal(invoiceId, approvalRecommendation.InvoiceId);
        Assert.Equal("approved", approvalRecommendation.RecommendedStatus);
        Assert.InRange(approvalRecommendation.Confidence, 0m, 1m);

        var categorizedTransaction = await provider.UpdateTransactionCategoryAsync(
            new UpdateFinanceTransactionCategoryCommand(companyId, transactionId, "software"),
            CancellationToken.None);
        Assert.Equal(transactionId, categorizedTransaction.Id);
        Assert.Equal("software", categorizedTransaction.TransactionType);

        var approvedInvoice = await provider.UpdateInvoiceApprovalStatusAsync(
            new UpdateFinanceInvoiceApprovalStatusCommand(companyId, invoiceId, "approved"),
            CancellationToken.None);
        Assert.Equal(invoiceId, approvedInvoice.Id);
        Assert.Equal("approved", approvedInvoice.Status);
    }

    private static InternalToolExecutionRequest CreateFinanceToolRequest(
        Guid companyId,
        string toolName,
        ToolActionType actionType,
        params (string Key, JsonNode? Value)[] payload) =>
        new(
            toolName,
            new InternalToolExecutionContext(
                companyId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                actionType,
                "finance"),
            payload.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase));

    private static string[] GetOrchestrationBoundaryFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        return Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src", "VirtualCompany.Application", "Orchestration"), "*.cs", SearchOption.AllDirectories)
            .Concat(
            [
                Path.Combine(repositoryRoot, "src", "VirtualCompany.Application", "Agents", "AgentToolOrchestrationExecutor.cs"),
                Path.Combine(repositoryRoot, "src", "VirtualCompany.Application", "Agents", "CompanyAgentToolExecutionContracts.cs"),
                Path.Combine(repositoryRoot, "src", "VirtualCompany.Infrastructure", "Companies", "InternalCompanyToolContract.cs")
            ])
            .Where(File.Exists)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}