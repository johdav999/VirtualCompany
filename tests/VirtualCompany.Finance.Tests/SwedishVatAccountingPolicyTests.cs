using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class SwedishVatAccountingPolicyTests
{
    [Fact]
    public void Every_checked_in_decision_fixture_executes_against_the_runtime_policy()
    {
        using var document = LoadGoldenDocument();
        var policy = new AccountingTaxDecisionPolicy();
        var pack = new SwedishCandidateAccountingPolicyPack();

        foreach (var fixture in document.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var input = fixture.GetProperty("input");
            var expected = fixture.GetProperty("expectedDecision");
            var evidence = input.TryGetProperty("evidenceClassifications", out var evidenceValues)
                ? evidenceValues.EnumerateArray().Select(value => new AccountingTaxEvidenceInput(value.GetString()!)).ToArray()
                : [];
            var decision = policy.Decide(pack, new AccountingTaxDecisionInput(
                input.GetProperty("ruleKey").GetString()!,
                DateOnly.Parse(input.GetProperty("accountingDate").GetString()!),
                input.GetProperty("direction").GetString()!,
                input.GetProperty("documentType").GetString()!,
                input.GetProperty("lineClassification").GetString()!,
                input.GetProperty("lineAmount").GetDecimal(), 2, AccountingRoundingModeValues.MidpointToEven,
                CompanyVatRegistrationStatus: input.GetProperty("companyVatRegistrationStatus").GetString()!,
                CounterpartyJurisdiction: input.TryGetProperty("counterpartyJurisdiction", out var jurisdiction) ? jurisdiction.GetString()! : "SE",
                CounterpartyVatStatus: input.TryGetProperty("counterpartyVatStatus", out var vatStatus) ? vatStatus.GetString()! : "unknown",
                EvidenceClassifications: evidence.Select(value => value.Classification).ToHashSet(StringComparer.OrdinalIgnoreCase),
                CompanyCountryCode: "SE", AccountingCurrency: "SEK",
                BookkeepingMethod: StatutoryBookkeepingMethodValues.Accrual, DocumentCurrency: "SEK", Evidence: evidence));

            var expectedAllowed = expected.GetProperty("isAllowed").GetBoolean();
            Assert.True(decision.IsAllowed == expectedAllowed,
                $"Fixture '{fixture.GetProperty("fixtureId").GetString()}' returned '{decision.ReasonCode}'.");
            if (expected.TryGetProperty("reasonCode", out var reasonCode))
                Assert.Equal(reasonCode.GetString(), decision.ReasonCode);
            Assert.Equal(expected.GetProperty("taxableBasis").GetDecimal(), decision.TaxableBasis);
            Assert.Equal(expected.GetProperty("taxAmount").GetDecimal(), decision.TaxAmount);
            Assert.Equal(expected.GetProperty("grossAmount").GetDecimal(), decision.GrossAmount);
        }
    }

    [Fact]
    public async Task Checked_in_sales_fixture_previews_balanced_bas_journal_with_vat_boxes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var golden = LoadFixture("domestic-sale-25-exclusive");

        var preview = await fixture.CustomerPolicy.PreviewAsync(new(fixture.CompanyId, fixture.InvoiceId,
            new(fixture.PeriodId, "G", null,
            [new("Domestic consulting", golden.InputAmount, SwedishCandidateAccountingPolicyPack.DomesticSales25RuleKey,
                "standard_goods_or_services", "SE", "unknown",
                [new("operator_classified_domestic_standard_25", "invoice-evidence")])]), fixture.ActorId), default);

        Assert.True(preview.IsReady, string.Join(" | ", preview.Issues.Select(issue => issue.Explanation)));
        Assert.Equal(golden.TaxableBasis, preview.NetAmount);
        Assert.Equal(golden.TaxAmount, preview.TaxAmount);
        Assert.Equal(golden.GrossAmount, preview.GrossAmount);
        Assert.Equal(preview.JournalLines.Sum(line => line.DebitAmount), preview.JournalLines.Sum(line => line.CreditAmount));
        Assert.Contains(preview.JournalLines, line => line.AccountCode == "1510" && line.DebitAmount == 125m);
        Assert.Contains(preview.JournalLines, line => line.AccountCode == "3001" && line.CreditAmount == 100m);
        Assert.Contains(preview.JournalLines, line => line.AccountRole == AccountingAccountRoleKeys.TaxOutput25 &&
            line.AccountCode == "2611" && line.CreditAmount == 25m &&
            line.VatBoxMappings!.SequenceEqual(["05", "10"]));
    }

    [Fact]
    public async Task Checked_in_purchase_fixture_previews_balanced_bas_journal_with_box_48()
    {
        await using var fixture = await Fixture.CreateAsync();
        var golden = LoadFixture("domestic-purchase-25-inclusive-full-recovery");

        var preview = await fixture.SupplierPolicy.PreviewAsync(new(fixture.CompanyId, fixture.BillId,
            new(fixture.PeriodId, "G", null,
            [new("Domestic goods", golden.InputAmount, fixture.ExpenseAccountId,
                SwedishCandidateAccountingPolicyPack.DomesticPurchase25RuleKey, "expense", "SE", "unknown",
                [new("operator_classified_domestic_standard_25", "supplier-invoice"),
                 new("business_use_full_recovery", "business-use-attestation")])]), fixture.ActorId), default);

        Assert.True(preview.IsReady, string.Join(" | ", preview.Issues.Select(issue => issue.Explanation)));
        Assert.Equal(golden.TaxableBasis, preview.NetAmount);
        Assert.Equal(golden.TaxAmount, preview.RecoverableTaxAmount);
        Assert.Equal(preview.JournalLines.Sum(line => line.DebitAmount), preview.JournalLines.Sum(line => line.CreditAmount));
        Assert.Contains(preview.JournalLines, line => line.AccountCode == "4000" && line.DebitAmount == 100m);
        Assert.Contains(preview.JournalLines, line => line.AccountRole == AccountingAccountRoleKeys.TaxInput &&
            line.AccountCode == "2641" && line.DebitAmount == 25m &&
            line.VatBoxMappings!.SequenceEqual(["48"]));
        Assert.Contains(preview.JournalLines, line => line.AccountCode == "2440" && line.CreditAmount == 125m);
    }

    [Fact]
    public async Task Swedish_preview_requires_real_evidence_and_does_not_infer_it_from_the_rule_key()
    {
        await using var fixture = await Fixture.CreateAsync();

        var preview = await fixture.CustomerPolicy.PreviewAsync(new(fixture.CompanyId, fixture.InvoiceId,
            new(fixture.PeriodId, "G", null,
            [new("Unsupported evidence", 100m, SwedishCandidateAccountingPolicyPack.DomesticSales25RuleKey,
                "standard_goods_or_services", "SE")]), fixture.ActorId), default);

        Assert.False(preview.IsReady);
        Assert.Contains(preview.Issues, issue => issue.Explanation.Contains("evidence is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cash_method_company_is_blocked_even_when_the_domestic_rule_key_is_valid()
    {
        await using var fixture = await Fixture.CreateAsync(StatutoryBookkeepingMethodValues.Cash);

        var preview = await fixture.CustomerPolicy.PreviewAsync(new(fixture.CompanyId, fixture.InvoiceId,
            new(fixture.PeriodId, "G", null,
            [new("Domestic consulting", 100m, SwedishCandidateAccountingPolicyPack.DomesticSales25RuleKey,
                "standard_goods_or_services", "SE", "unknown",
                [new("operator_classified_domestic_standard_25", "invoice-evidence")])]), fixture.ActorId), default);

        Assert.False(preview.IsReady);
        Assert.Contains(preview.Issues, issue => issue.Explanation.Contains("accrual", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Submission_retains_versioned_tax_facts_unchanged_after_configuration_pack_change()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = fixture.CustomerInput();

        await fixture.CustomerService.SubmitAsync(new(fixture.CompanyId, fixture.InvoiceId, input, null,
            "swedish-tax-facts", fixture.ActorId, "swedish-tax-facts-test"), default);
        var storedLine = await fixture.Context.CustomerInvoiceAccountingLines.SingleAsync();
        var originalFacts = storedLine.TaxFactsJson;
        using (var facts = JsonDocument.Parse(originalFacts))
        {
            Assert.Equal("2.0", facts.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("sweden-domestic-vat-launch-2026.1", facts.RootElement.GetProperty("specificationKey").GetString());
            Assert.Equal("1.1.0", facts.RootElement.GetProperty("policyPackVersion").GetString());
            Assert.Equal("2026.1", facts.RootElement.GetProperty("taxRuleVersion").GetString());
            Assert.Equal("operator_classified_domestic_standard_25", facts.RootElement.GetProperty("evidenceClassification").GetString());
            Assert.Equal("midpoint_to_even", facts.RootElement.GetProperty("roundingMode").GetString());
        }

        var configuration = await fixture.Context.AccountingConfigurations.SingleAsync();
        configuration.ApplyPolicyPack(configuration.PolicyPackKey, "1.2.0", new DateOnly(2027, 1, 1),
            fixture.ActorId, new DateTime(2026, 8, 25, 8, 0, 0, DateTimeKind.Utc));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        Assert.Equal(originalFacts, (await fixture.Context.CustomerInvoiceAccountingLines.SingleAsync()).TaxFactsJson);
    }

    [Fact]
    public async Task Blocked_submission_writes_safe_durable_tax_audit_event()
    {
        await using var fixture = await Fixture.CreateAsync();
        var missingEvidence = fixture.CustomerInput() with
        {
            Lines = [fixture.CustomerInput().Lines.Single() with { TaxEvidence = [] }]
        };

        await Assert.ThrowsAsync<CustomerInvoiceAccountingException>(() => fixture.CustomerService.SubmitAsync(
            new(fixture.CompanyId, fixture.InvoiceId, missingEvidence, null, "blocked-tax",
                fixture.ActorId, "blocked-tax-test"), default));

        var audit = await fixture.Context.AuditEvents.SingleAsync(item =>
            item.Action == AuditEventActions.AccountingTaxDecisionBlocked);
        Assert.Equal(AuditEventOutcomes.Blocked, audit.Outcome);
        Assert.Equal(fixture.InvoiceId.ToString("N"), audit.TargetId);
        Assert.DoesNotContain("operator_classified", audit.RationaleSummary, StringComparison.OrdinalIgnoreCase);
    }

    private static GoldenFixture LoadFixture(string fixtureId)
    {
        using var document = LoadGoldenDocument();
        var fixture = document.RootElement.GetProperty("fixtures").EnumerateArray()
            .Single(item => item.GetProperty("fixtureId").GetString() == fixtureId);
        var input = fixture.GetProperty("input");
        var expected = fixture.GetProperty("expectedDecision");
        return new(input.GetProperty("lineAmount").GetDecimal(), expected.GetProperty("taxableBasis").GetDecimal(),
            expected.GetProperty("taxAmount").GetDecimal(), expected.GetProperty("grossAmount").GetDecimal());
    }

    private static JsonDocument LoadGoldenDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "financial-app-r1-prompts.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName,
            "docs", "finance", "swedish-domestic-vat-launch-2026.1", "golden-fixtures.json")));
    }

    private sealed record GoldenFixture(decimal InputAmount, decimal TaxableBasis, decimal TaxAmount, decimal GrossAmount);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context, Guid companyId, Guid actorId,
            Guid invoiceId, Guid billId, Guid periodId, Guid expenseAccountId,
            CustomerInvoiceAccountingPolicy customerPolicy, SupplierBillAccountingPolicy supplierPolicy,
            IAccountingPolicyPackResolver resolver)
        {
            _connection = connection;
            Context = context;
            CompanyId = companyId;
            ActorId = actorId;
            InvoiceId = invoiceId;
            BillId = billId;
            PeriodId = periodId;
            ExpenseAccountId = expenseAccountId;
            CustomerPolicy = customerPolicy;
            SupplierPolicy = supplierPolicy;
            Resolver = resolver;
        }

        public VirtualCompanyDbContext Context { get; }
        public Guid CompanyId { get; }
        public Guid ActorId { get; }
        public Guid InvoiceId { get; }
        public Guid BillId { get; }
        public Guid PeriodId { get; }
        public Guid ExpenseAccountId { get; }
        public CustomerInvoiceAccountingPolicy CustomerPolicy { get; }
        public SupplierBillAccountingPolicy SupplierPolicy { get; }
        public IAccountingPolicyPackResolver Resolver { get; }
        public CustomerInvoiceAccountingService CustomerService => new(Context, CustomerPolicy, null!, null!,
            new AuditEventWriter(Context), new FixedTimeProvider(), Resolver);

        public CustomerInvoiceAccountingInput CustomerInput() => new(PeriodId, "G", null,
        [new("Domestic consulting", 100m, SwedishCandidateAccountingPolicyPack.DomesticSales25RuleKey,
            "standard_goods_or_services", "SE", "unknown",
            [new("operator_classified_domestic_standard_25", "invoice-evidence")])]);

        public static async Task<Fixture> CreateAsync(string bookkeepingMethod = StatutoryBookkeepingMethodValues.Accrual)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var counterpartyId = Guid.NewGuid();
            var invoiceId = Guid.NewGuid();
            var billId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            var configurationId = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            var now = new DateTime(2026, 8, 24, 8, 0, 0, DateTimeKind.Utc);
            var context = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options,
                new Accessor(companyId, actorId));
            await context.Database.EnsureCreatedAsync();
            var pack = new SwedishCandidateAccountingPolicyPack();
            var configuration = new AccountingConfiguration(configurationId, companyId, "SEK", 1, 1,
                pack.Definition.PackKey, pack.Definition.Version, new DateOnly(2026, 1, 1), 2,
                AccountingRoundingModeValues.MidpointToEven, actorId, now);
            configuration.SetSetupState(AccountingSetupStateValues.Ready, actorId, now);

            var controlRoles = pack.Definition.AccountRoles.Where(role => role.IsControlAccount)
                .Select(role => role.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var accounts = pack.Definition.ChartTemplates.Single().Accounts.ToDictionary(account => account.Code,
                account => Account(companyId, account.Code, account.Label, account.AccountClass,
                    account.NormalBalance, now, controlRoles.Contains(account.DefaultRoleKey ?? string.Empty)
                        ? account.DefaultRoleKey : null));
            context.Companies.Add(new Company(companyId, "Swedish VAT fixture"));
            context.CompanyStatutoryProfiles.Add(new CompanyStatutoryProfile(Guid.NewGuid(), companyId,
                Profile(bookkeepingMethod, now), actorId, now));
            context.FinanceCounterparties.Add(new FinanceCounterparty(counterpartyId, companyId,
                "Domestic counterparty", "customer", createdUtc: now));
            context.FinanceAccounts.AddRange(accounts.Values);
            context.AccountingConfigurations.Add(configuration);
            foreach (var account in accounts.Values.Where(account => !string.IsNullOrWhiteSpace(account.ControlAccountRole)))
                context.AccountingConfigurationAccountRoles.Add(new(Guid.NewGuid(), companyId, configurationId,
                    account.ControlAccountRole!, account.Id, now));
            foreach (var mapping in new[] { ("revenue", "3001"), ("operating_expense", "4000") })
                context.AccountingConfigurationAccountRoles.Add(new(Guid.NewGuid(), companyId, configurationId,
                    mapping.Item1, accounts[mapping.Item2].Id, now));
            context.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "August 2026",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            context.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), companyId, "G", "General", "G", true, now));
            context.CompanyKnowledgeDocuments.Add(new CompanyKnowledgeDocument(documentId, companyId, "Accounting evidence",
                CompanyKnowledgeDocumentType.Reference, "finance/swedish-vat-evidence.pdf", null,
                "swedish-vat-evidence.pdf", "application/pdf", ".pdf", 512,
                new Dictionary<string, JsonNode?> { ["checksum_sha256"] = JsonValue.Create(new string('c', 64)) },
                new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility)));
            context.FinanceInvoices.Add(new FinanceInvoice(invoiceId, companyId, counterpartyId, "INV-SE-1", now,
                now.AddDays(30), 125m, "SEK", "approved", documentId, createdUtc: now, updatedUtc: now));
            context.FinanceBills.Add(new FinanceBill(billId, companyId, counterpartyId, "BILL-SE-1", now,
                now.AddDays(30), 125m, "SEK", "approved", documentId, createdUtc: now, updatedUtc: now));
            await context.SaveChangesAsync();

            var resolver = new AccountingPolicyPackResolver([pack]);
            return new Fixture(connection, context, companyId, actorId, invoiceId, billId, periodId,
                accounts["4000"].Id, new CustomerInvoiceAccountingPolicy(context, resolver),
                new SupplierBillAccountingPolicy(context, resolver), resolver);
        }

        private static FinanceAccount Account(Guid companyId, string code, string name, string accountClass,
            string normalBalance, DateTime now, string? role) => new(Guid.NewGuid(), companyId, code, name,
            accountClass, "SEK", 0m, now, accountClass: accountClass, normalBalance: normalBalance,
            effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true, controlAccountRole: role);

        private static CompanyStatutoryProfileValues Profile(string bookkeepingMethod, DateTime now) => new(
            "Example Legal AB", "556016-0680", "SE556016068001", StatutoryVatRegistrationStatusValues.Registered,
            "Examplegatan 1", null, "111 22", "Stockholm", "SE", null, null, null, null, null,
            "SE", "SEK", StatutoryFiscalYearBasisValues.CalendarYear, bookkeepingMethod,
            new DateOnly(2000, 1, 1), new DateOnly(2000, 1, 1), null, true,
            StatutoryVerificationStatusValues.Unverified, StatutoryProfileSourceKindValues.UserEntry,
            "test-attestation", now, null, null);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class Accessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
    }
}
