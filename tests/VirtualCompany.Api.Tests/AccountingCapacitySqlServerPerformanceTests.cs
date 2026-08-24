using System.Data;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit.Abstractions;

namespace VirtualCompany.Api.Tests;

[Trait("Category", "AccountingPerformance")]
public sealed class AccountingCapacitySqlServerPerformanceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);
    private readonly ITestOutputHelper _output;

    public AccountingCapacitySqlServerPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AccountingPerformanceFact]
    public async Task Bounded_accounting_reports_meet_profile_budgets_and_use_tenant_leading_plan()
    {
        var profile = AccountingLoadProfile.Resolve(
            Environment.GetEnvironmentVariable(AccountingPerformanceFactAttribute.ProfileVariable)!);
        using var factory = TestWebApplicationFactory.CreateSqlServer(TimeProvider.System);
        var seed = await SeedCompanyAsync(factory, profile);
        var connectionString = await factory.ExecuteDbContextAsync(db =>
            Task.FromResult(db.Database.GetConnectionString()!));
        await AccountingSqlServerVolumeGenerator.GenerateAsync(connectionString, seed, profile,
            CancellationToken.None);

        var plan = await CapturePlanAsync(connectionString, seed, CancellationToken.None);
        Assert.Contains("company_id", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_ledger_entries_company_id_fiscal_period_id_status_entry_at_entry_number",
            plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_ledger_entry_lines_company_id_finance_account_id",
            plan, StringComparison.OrdinalIgnoreCase);

        // The factory owns created clients and disposes them after its SQL Server cleanup.
        // Disposing this client first would tear down the provider required by the factory's
        // isolated-database cleanup.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, seed.Subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, seed.Email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, "Accounting performance owner");

        var accountRoute = $"/internal/companies/{seed.CompanyId:D}/finance/accounting/reports/general-ledger" +
            $"?fiscalPeriodId={seed.PeriodId:D}&accountId={seed.AccountIds[0]:D}&page=1&pageSize=100";
        var trialRoute = $"/internal/companies/{seed.CompanyId:D}/finance/accounting/reports/trial-balance" +
            $"?fiscalPeriodId={seed.PeriodId:D}";

        await AssertBoundedLedgerPageAsync(client, accountRoute, profile);
        var ledgerP95 = await MeasureP95Async(() => AssertBoundedLedgerPageAsync(client, accountRoute, profile), 7);
        var trialP95 = await MeasureP95Async(() => AssertSuccessfulAsync(client, trialRoute), 7);

        _output.WriteLine(
            "Profile {0}: {1:N0} journals, {2:N0} lines; general-ledger p95 {3:N1} ms; trial-balance p95 {4:N1} ms; tenant-leading index present in actual SQL plan.",
            profile.Key, profile.JournalCount, profile.LineCount, ledgerP95.TotalMilliseconds,
            trialP95.TotalMilliseconds);

        Assert.True(ledgerP95 <= TimeSpan.FromMilliseconds(1_200),
            $"{profile.Key} general-ledger page p95 was {ledgerP95.TotalMilliseconds:N0} ms (budget 1,200 ms).\nPlan: {plan}");
        Assert.True(trialP95 <= TimeSpan.FromMilliseconds(1_500),
            $"{profile.Key} trial-balance p95 was {trialP95.TotalMilliseconds:N0} ms (budget 1,500 ms).\nPlan: {plan}");
    }

    private static async Task AssertBoundedLedgerPageAsync(HttpClient client, string route,
        AccountingLoadProfile profile)
    {
        using var response = await client.GetAsync(route);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(100, root.GetProperty("pageSize").GetInt32());
        Assert.True(root.GetProperty("totalLineCount").GetInt64() >= profile.LineCount / profile.AccountCount - 1);
        var account = Assert.Single(root.GetProperty("accounts").EnumerateArray());
        Assert.InRange(account.GetProperty("lines").GetArrayLength(), 1, 100);
        Assert.True(account.GetProperty("totalLineCount").GetInt32() > account.GetProperty("lines").GetArrayLength());
    }

    private static async Task AssertSuccessfulAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<TimeSpan> MeasureP95Async(Func<Task> operation, int repetitions)
    {
        await operation();
        var samples = new long[repetitions];
        for (var index = 0; index < repetitions; index++)
        {
            var started = Stopwatch.GetTimestamp();
            await operation();
            samples[index] = Stopwatch.GetElapsedTime(started).Ticks;
        }
        Array.Sort(samples);
        var p95Index = Math.Clamp((int)Math.Ceiling(samples.Length * 0.95) - 1, 0, samples.Length - 1);
        return TimeSpan.FromTicks(samples[p95Index]);
    }

    private static async Task<PerformanceSeed> SeedCompanyAsync(TestWebApplicationFactory factory,
        AccountingLoadProfile profile)
    {
        var companyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var actorId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var periodId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        const string subject = "accounting-performance-owner";
        const string email = "accounting-performance-owner@example.com";
        var accountIds = Enumerable.Range(1, profile.AccountCount)
            .Select(index => DeterministicGuid(10, index)).ToArray();

        await factory.SeedAsync(db =>
        {
            db.Users.Add(new User(actorId, email, "Accounting performance owner", "dev-header", subject));
            db.Companies.Add(new Company(companyId, $"{profile.Key} accounting performance company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, actorId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "Capacity year 2026",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            db.FinanceAccounts.AddRange(accountIds.Select((id, index) => new FinanceAccount(
                id, companyId, (1000 + index).ToString(), $"Capacity account {index + 1}",
                index < profile.AccountCount / 2 ? FinanceAccountClassValues.Asset : FinanceAccountClassValues.Liability,
                "USD", 0m, NowUtc,
                accountClass: index < profile.AccountCount / 2 ? FinanceAccountClassValues.Asset : FinanceAccountClassValues.Liability,
                normalBalance: index < profile.AccountCount / 2 ? FinanceNormalBalanceValues.Debit : FinanceNormalBalanceValues.Credit,
                effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true)));
            return Task.CompletedTask;
        });

        return new PerformanceSeed(companyId, actorId, periodId, accountIds, subject, email);
    }

    private static async Task<string> CapturePlanAsync(string connectionString, PerformanceSeed seed,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var enable = connection.CreateCommand())
        {
            // STATISTICS PROFILE executes the representative query and returns its actual
            // operator tree as a final tabular result set. The tabular shape is stable in
            // SqlClient for parameterized batches and exposes the selected index by name.
            enable.CommandText = "SET STATISTICS PROFILE ON";
            await enable.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT l.finance_account_id, SUM(l.debit_amount) AS debit, SUM(l.credit_amount) AS credit, COUNT_BIG(*) AS line_count
                FROM ledger_entry_lines AS l
                INNER JOIN ledger_entries AS e ON e.company_id = l.company_id AND e.id = l.ledger_entry_id
                WHERE l.company_id = @company_id AND e.fiscal_period_id = @period_id AND e.status = 'posted'
                GROUP BY l.finance_account_id
                """;
            command.Parameters.Add(new SqlParameter("@company_id", SqlDbType.UniqueIdentifier) { Value = seed.CompanyId });
            command.Parameters.Add(new SqlParameter("@period_id", SqlDbType.UniqueIdentifier) { Value = seed.PeriodId });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var plan = new StringBuilder();
            do
            {
                var statementOrdinal = Enumerable.Range(0, reader.FieldCount)
                    .FirstOrDefault(ordinal => string.Equals(reader.GetName(ordinal), "StmtText",
                        StringComparison.OrdinalIgnoreCase), -1);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (statementOrdinal >= 0 && !await reader.IsDBNullAsync(statementOrdinal, cancellationToken))
                        plan.AppendLine(reader.GetValue(statementOrdinal).ToString());
                }
            }
            while (await reader.NextResultAsync(cancellationToken));

            return plan.Length > 0
                ? plan.ToString()
                : throw new InvalidOperationException("SQL Server did not return a query plan.");
        }
        finally
        {
            await using var disable = connection.CreateCommand();
            disable.CommandText = "SET STATISTICS PROFILE OFF";
            await disable.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static Guid DeterministicGuid(int kind, int index)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(kind).CopyTo(bytes, 0);
        BitConverter.GetBytes(index).CopyTo(bytes, 4);
        bytes[15] = 0x5A;
        return new Guid(bytes);
    }

    private sealed record PerformanceSeed(Guid CompanyId, Guid ActorId, Guid PeriodId,
        Guid[] AccountIds, string Subject, string Email);

    private sealed record AccountingLoadProfile(string Key, int AccountCount, int JournalCount, int LineCount)
    {
        public static AccountingLoadProfile Resolve(string key) => key switch
        {
            "small" => new("small", 300, 100_000, 400_000),
            "medium" => new("medium", 1_000, 1_000_000, 5_000_000),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Use small or medium.")
        };
    }

    private static class AccountingSqlServerVolumeGenerator
    {
        public static async Task GenerateAsync(string connectionString, PerformanceSeed seed,
            AccountingLoadProfile profile, CancellationToken cancellationToken)
        {
            const int journalBatchSize = 2_500;
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            for (var offset = 0; offset < profile.JournalCount; offset += journalBatchSize)
            {
                var count = Math.Min(journalBatchSize, profile.JournalCount - offset);
                var entries = CreateEntries(seed, offset, count);
                var lines = CreateLines(seed, profile, offset, count);
                await BulkCopyAsync(connection, "ledger_entries", entries, cancellationToken);
                await BulkCopyAsync(connection, "ledger_entry_lines", lines, cancellationToken);
            }
        }

        private static DataTable CreateEntries(PerformanceSeed seed, int offset, int count)
        {
            var table = Table(
                ("id", typeof(Guid)), ("company_id", typeof(Guid)), ("fiscal_period_id", typeof(Guid)),
                ("entry_number", typeof(string)), ("entry_at", typeof(DateTime)), ("status", typeof(string)),
                ("source_type", typeof(string)), ("source_id", typeof(string)), ("posted_at", typeof(DateTime)),
                ("description", typeof(string)), ("created_at", typeof(DateTime)), ("updated_at", typeof(DateTime)),
                ("document_date", typeof(DateTime)), ("posting_date", typeof(DateTime)),
                ("base_currency", typeof(string)), ("posting_type", typeof(string)),
                ("source_version", typeof(string)), ("idempotency_key", typeof(string)));
            for (var local = 0; local < count; local++)
            {
                var index = offset + local;
                var occurred = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddMinutes(index % 525_600);
                table.Rows.Add(DeterministicGuid(20, index), seed.CompanyId, seed.PeriodId,
                    $"CAP-{index + 1:D8}", occurred, LedgerEntryStatuses.Posted,
                    "capacity_fixture", $"source-{index + 1:D8}", occurred,
                    "Deterministic production-shaped capacity journal", occurred, occurred,
                    occurred.Date, occurred.Date, "USD", LedgerPostingTypeValues.SourceDocument,
                    "1", $"capacity:{index + 1:D8}");
            }
            return table;
        }

        private static DataTable CreateLines(PerformanceSeed seed, AccountingLoadProfile profile,
            int offset, int journalCount)
        {
            var table = Table(
                ("id", typeof(Guid)), ("company_id", typeof(Guid)), ("ledger_entry_id", typeof(Guid)),
                ("finance_account_id", typeof(Guid)), ("debit_amount", typeof(decimal)),
                ("credit_amount", typeof(decimal)), ("currency", typeof(string)),
                ("description", typeof(string)), ("created_at", typeof(DateTime)));
            var linesPerJournal = profile.LineCount / profile.JournalCount;
            for (var local = 0; local < journalCount; local++)
            {
                var journalIndex = offset + local;
                var journalId = DeterministicGuid(20, journalIndex);
                var occurred = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddMinutes(journalIndex % 525_600);
                var amount = 100m + journalIndex % 100;
                for (var line = 0; line < linesPerJournal; line++)
                {
                    var accountIndex = (journalIndex + (line * Math.Max(1, profile.AccountCount / linesPerJournal))) % profile.AccountCount;
                    var (debit, credit) = BalancedAmounts(linesPerJournal, line, amount);
                    table.Rows.Add(DeterministicGuid(30 + line, journalIndex), seed.CompanyId, journalId,
                        seed.AccountIds[accountIndex], debit, credit, "USD", "Capacity fixture line", occurred);
                }
            }
            return table;
        }

        private static (decimal Debit, decimal Credit) BalancedAmounts(int linesPerJournal, int line, decimal amount) =>
            linesPerJournal switch
            {
                4 when line is 0 or 1 => (amount, 0m),
                4 => (0m, amount),
                5 when line == 0 => (amount, 0m),
                5 when line is 1 or 2 => (amount / 2m, 0m),
                5 => (0m, amount),
                _ => throw new InvalidOperationException($"The {linesPerJournal}-line profile has no balanced shape.")
            };

        private static DataTable Table(params (string Name, Type Type)[] columns)
        {
            var table = new DataTable();
            foreach (var column in columns) table.Columns.Add(column.Name, column.Type);
            return table;
        }

        private static async Task BulkCopyAsync(SqlConnection connection, string tableName, DataTable table,
            CancellationToken cancellationToken)
        {
            using var copy = new SqlBulkCopy(connection,
                SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock, null)
            {
                DestinationTableName = tableName,
                BatchSize = table.Rows.Count,
                BulkCopyTimeout = 300
            };
            foreach (DataColumn column in table.Columns)
                copy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await copy.WriteToServerAsync(table, cancellationToken);
        }
    }
}
