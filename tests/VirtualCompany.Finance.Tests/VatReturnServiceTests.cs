using System.Security.Claims;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Finance.Tests;

public sealed class VatReturnServiceTests
{
    [Fact]
    public async Task Swedish_return_calculates_golden_boxes_reconciles_and_replays_idempotently()
    {
        await using var fixture = await Fixture.CreateAsync();
        var filing = await fixture.CreateFilingPeriodAsync();
        await fixture.AddSalesAsync("A1", 100m, 25m);
        await fixture.AddPurchaseAsync("B1", 100m, 25m);

        var command = new CalculateVatReturnCommand(fixture.CompanyId, filing.Id, null,
            "vat-return:2026-08:v1", fixture.UserId);
        var first = await fixture.Service.CalculateAsync(command, CancellationToken.None);
        var replay = await fixture.Service.CalculateAsync(command, CancellationToken.None);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(VatReturnStatusValues.Calculated, first.Status);
        Assert.Empty(first.Issues);
        Assert.Equal(100m, first.Boxes.Single(x => x.BoxCode == "05").ExactAmount);
        Assert.Equal(25m, first.Boxes.Single(x => x.BoxCode == "10").ExactAmount);
        Assert.Equal(25m, first.Boxes.Single(x => x.BoxCode == "48").ExactAmount);
        Assert.Equal(0m, first.Boxes.Single(x => x.BoxCode == "49").ExactAmount);
        Assert.Equal(2, first.IncludedSourceCount);
        Assert.Equal(3, first.Contributions.Count);
        Assert.Contains(VatReturnAllowedActions.RequestApproval, first.AllowedActions);
        Assert.Equal(4, await fixture.Context.VatReturnBoxResults.CountAsync());
        Assert.Single(await fixture.Context.VatReturns.ToListAsync());
    }

    [Fact]
    public async Task Posted_source_after_calculation_makes_return_stale_and_removes_approval_action()
    {
        await using var fixture = await Fixture.CreateAsync();
        var filing = await fixture.CreateFilingPeriodAsync();
        await fixture.AddSalesAsync("A1", 100m, 25m);
        var calculated = await fixture.Service.CalculateAsync(new CalculateVatReturnCommand(
            fixture.CompanyId, filing.Id, null, "vat-return:stale", fixture.UserId), CancellationToken.None);

        await fixture.AddSalesAsync("A2", 200m, 50m);
        var current = await fixture.Service.GetAsync(new GetVatReturnQuery(fixture.CompanyId, calculated.Id),
            CancellationToken.None);

        Assert.True(current.IsStale);
        Assert.Equal(VatReturnStatusValues.NeedsReview, current.Status);
        Assert.DoesNotContain(VatReturnAllowedActions.RequestApproval, current.AllowedActions);
        Assert.DoesNotContain(VatReturnAllowedActions.Finalize, current.AllowedActions);
        await Assert.ThrowsAsync<VatReturnOperationException>(() => fixture.Service.RequestApprovalAsync(
            new RequestVatReturnApprovalCommand(fixture.CompanyId, current.Id, current.InputHash!, fixture.UserId),
            CancellationToken.None));
    }

    [Fact]
    public async Task Approved_return_finalizes_to_a_checksummed_recoverable_human_filing_package()
    {
        await using var fixture = await Fixture.CreateAsync();
        var filing = await fixture.CreateFilingPeriodAsync();
        await fixture.AddSalesAsync("A1", 100m, 25m);
        var calculated = await fixture.Service.CalculateAsync(new CalculateVatReturnCommand(
            fixture.CompanyId, filing.Id, null, "vat-return:finalize", fixture.UserId), CancellationToken.None);
        var requested = await fixture.Service.RequestApprovalAsync(new RequestVatReturnApprovalCommand(
            fixture.CompanyId, calculated.Id, calculated.InputHash!, fixture.UserId), CancellationToken.None);
        var approval = await fixture.Context.ApprovalRequests.Include(x => x.Steps)
            .SingleAsync(x => x.Id == requested.ApprovalRequestId);
        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, Guid.NewGuid(), "Reviewed VAT evidence.");
        await fixture.Context.SaveChangesAsync();

        var finalized = await fixture.Service.FinalizeAsync(new FinalizeVatReturnCommand(
            fixture.CompanyId, calculated.Id, calculated.InputHash!, fixture.UserId), CancellationToken.None);
        var package = await fixture.Service.DownloadPackageAsync(new GetVatReturnPackageQuery(
            fixture.CompanyId, calculated.Id), CancellationToken.None);

        Assert.Equal(VatReturnStatusValues.Locked, finalized.Status);
        Assert.True(finalized.CanDownloadPackage);
        Assert.Equal(finalized.PackageChecksum, package.Checksum);
        Assert.Equal(package.Checksum, Convert.ToHexString(SHA256.HashData(package.Content)).ToLowerInvariant());
        using var json = JsonDocument.Parse(package.Content);
        Assert.Equal("not_configured", json.RootElement.GetProperty("submissionCapability").GetString());
        Assert.Equal("25", json.RootElement.GetProperty("boxes").EnumerateArray()
            .Single(x => x.GetProperty("boxCode").GetString() == "10").GetProperty("filingAmount").GetRawText());

        var correction = await fixture.Service.CreateCorrectionAsync(new CreateVatReturnCorrectionCommand(
            fixture.CompanyId, finalized.Id, "A later journal changes the filing evidence.",
            "voucher:correction-1", "vat-return:correction:1", fixture.UserId), CancellationToken.None);
        var original = await fixture.Service.GetAsync(new GetVatReturnQuery(fixture.CompanyId, finalized.Id),
            CancellationToken.None);
        Assert.Equal(finalized.Id, correction.CorrectionOfVatReturnId);
        Assert.Equal(VatReturnStatusValues.Corrected, original.Status);
        Assert.Equal(finalized.PackageChecksum, original.PackageChecksum);
    }

    [Fact]
    public async Task Reporting_locked_fiscal_period_is_a_blocking_source_referenced_issue()
    {
        await using var fixture = await Fixture.CreateAsync();
        var filing = await fixture.CreateFilingPeriodAsync();
        await fixture.AddSalesAsync("A1", 100m, 25m);
        await fixture.LockFiscalPeriodAsync();

        var calculated = await fixture.Service.CalculateAsync(new CalculateVatReturnCommand(
            fixture.CompanyId, filing.Id, null, "vat-return:locked-period", fixture.UserId),
            CancellationToken.None);

        Assert.Equal(VatReturnStatusValues.NeedsReview, calculated.Status);
        var issue = Assert.Single(calculated.Issues, x => x.Code == VatReturnIssueCodes.FiscalPeriodLocked);
        Assert.Equal($"fiscal-period:{filing.FiscalPeriodId:D}", issue.SourceReference);
        Assert.DoesNotContain(VatReturnAllowedActions.RequestApproval, calculated.AllowedActions);
    }

    [Fact]
    public async Task Credit_note_sign_and_whole_krona_rounding_are_deterministic()
    {
        await using var fixture = await Fixture.CreateAsync();
        var filing = await fixture.CreateFilingPeriodAsync();
        await fixture.AddSalesAsync("A1", 10.40m, 2.60m);
        await fixture.AddSalesCreditAsync("C1", 4m, 1m);

        var calculated = await fixture.Service.CalculateAsync(new CalculateVatReturnCommand(
            fixture.CompanyId, filing.Id, null, "vat-return:sign-rounding", fixture.UserId),
            CancellationToken.None);

        var basis = calculated.Boxes.Single(x => x.BoxCode == "05");
        var output = calculated.Boxes.Single(x => x.BoxCode == "10");
        Assert.Equal(6.40m, basis.ExactAmount);
        Assert.Equal(6, basis.FilingAmount);
        Assert.Equal(1.60m, output.ExactAmount);
        Assert.Equal(2, output.FilingAmount);
        Assert.Empty(calculated.Issues);
    }

    [Fact]
    public async Task Unsupported_currency_is_blocking_and_retains_the_voucher_reference()
    {
        await using var fixture = await Fixture.CreateAsync();
        var filing = await fixture.CreateFilingPeriodAsync();
        await fixture.AddForeignCurrencyFactsAsync("FX1", 100m, 25m);

        var calculated = await fixture.Service.CalculateAsync(new CalculateVatReturnCommand(
            fixture.CompanyId, filing.Id, null, "vat-return:currency", fixture.UserId),
            CancellationToken.None);

        Assert.Equal(VatReturnStatusValues.NeedsReview, calculated.Status);
        var issue = Assert.Single(calculated.Issues, x => x.Code == VatReturnIssueCodes.CurrencyMismatch);
        Assert.Equal("voucher:FX1", issue.SourceReference);
        var exception = await Assert.ThrowsAsync<VatReturnOperationException>(() =>
            fixture.Service.RequestApprovalAsync(new RequestVatReturnApprovalCommand(
                fixture.CompanyId, calculated.Id, calculated.InputHash!, fixture.UserId), CancellationToken.None));
        Assert.Equal("vat_return_blocking_issues", exception.Code);
    }

    [Fact]
    public void Finalized_original_is_immutable_when_a_linked_correction_is_constructed()
    {
        var companyId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var actor = Guid.NewGuid();
        var now = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var original = new VatReturn(Guid.NewGuid(), companyId, periodId, 1, "original", null, null, null, now);
        original.ReplaceCalculation(now, new string('a', 64), new string('b', 64), 1, 0, 25m, 0m, 25m, 25, false);
        original.AttachApproval(Guid.NewGuid(), now); original.MarkApproved(now);
        original.Finalize(actor, now, $"{companyId:N}/vat/original.json", new string('c', 64),
            "vat-original.json", "application/json", 100);
        var originalChecksum = original.PackageChecksum;

        var correction = new VatReturn(Guid.NewGuid(), companyId, periodId, 2, "correction",
            original.Id, "A posted correction changed the period.", "evidence:journal:2", now.AddDays(1));

        Assert.Equal(VatReturnStatuses.Locked, original.Status);
        Assert.Equal(originalChecksum, original.PackageChecksum);
        Assert.Equal(original.Id, correction.CorrectionOfVatReturnId);
        Assert.Equal(VatReturnStatuses.Draft, correction.Status);
        Assert.Throws<InvalidOperationException>(() => original.ReplaceCalculation(now.AddDays(1),
            new string('d', 64), new string('e', 64), 1, 0, 0m, 0m, 0m, 0, false));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DateTime _now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        private readonly FinanceAccount _revenue;
        private readonly FinanceAccount _expense;
        private readonly FinanceAccount _receivable;
        private readonly FinanceAccount _payable;
        private readonly FinanceAccount _outputVat;
        private readonly FinanceAccount _inputVat;
        private readonly FiscalPeriod _fiscal;

        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context, VatReturnService service,
            Guid companyId, Guid userId, FinanceAccount revenue, FinanceAccount expense,
            FinanceAccount receivable, FinanceAccount payable, FinanceAccount outputVat,
            FinanceAccount inputVat, FiscalPeriod fiscal)
        {
            _connection = connection; Context = context; Service = service; CompanyId = companyId; UserId = userId;
            _revenue = revenue; _expense = expense; _receivable = receivable; _payable = payable;
            _outputVat = outputVat; _inputVat = inputVat; _fiscal = fiscal;
        }

        public VirtualCompanyDbContext Context { get; }
        public VatReturnService Service { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid(); var userId = Guid.NewGuid();
            var contextAccessor = new CompanyContext(companyId, userId);
            var context = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection).Options, contextAccessor);
            await context.Database.EnsureCreatedAsync();
            var now = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
            context.Companies.Add(new Company(companyId, "VAT return company"));
            var fiscal = new FiscalPeriod(Guid.NewGuid(), companyId, "August 2026",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
            context.FiscalPeriods.Add(fiscal);
            FinanceAccount Account(string code, string name, string type, string accountClass,
                string normal, string? role = null) => new(Guid.NewGuid(), companyId, code, name, type,
                "SEK", 0m, now, accountClass: accountClass, normalBalance: normal,
                effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true,
                controlAccountRole: role, restrictManualPosting: role is not null);
            var revenue = Account("3001", "Sales", "revenue", FinanceAccountClassValues.Income, FinanceNormalBalanceValues.Credit);
            var expense = Account("4001", "Purchases", "expense", FinanceAccountClassValues.Expense, FinanceNormalBalanceValues.Debit);
            var receivable = Account("1510", "Receivable", "asset", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit, AccountingAccountRoleKeys.AccountsReceivable);
            var payable = Account("2440", "Payable", "liability", FinanceAccountClassValues.Liability, FinanceNormalBalanceValues.Credit, AccountingAccountRoleKeys.AccountsPayable);
            var output = Account("2611", "Output VAT", "liability", FinanceAccountClassValues.Liability, FinanceNormalBalanceValues.Credit, AccountingAccountRoleKeys.TaxOutput25);
            var input = Account("2641", "Input VAT", "asset", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit, AccountingAccountRoleKeys.TaxInput);
            context.FinanceAccounts.AddRange(revenue, expense, receivable, payable, output, input);
            var pack = new SwedishCandidateAccountingPolicyPack();
            context.AccountingConfigurations.Add(new AccountingConfiguration(Guid.NewGuid(), companyId,
                "SEK", 1, 1, pack.Definition.PackKey, pack.Definition.Version, new DateOnly(2026, 1, 1),
                2, AccountingRoundingModeValues.MidpointToEven, userId, now));
            await context.SaveChangesAsync();
            var service = new VatReturnService(context, new MembershipResolver(companyId, userId),
                new CurrentUser(userId), new AccountingPolicyPackResolver([pack]),
                new ApprovalService(context), new MemoryStorage(), new AuditEventWriter(context),
                new FixedClock(now));
            return new(connection, context, service, companyId, userId, revenue, expense, receivable,
                payable, output, input, fiscal);
        }

        public Task<VatFilingPeriodDto> CreateFilingPeriodAsync() => Service.CreateFilingPeriodAsync(
            new CreateVatFilingPeriodCommand(CompanyId, "2026-08", new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31), "SEK", _fiscal.Id, UserId), CancellationToken.None);

        public Task AddSalesAsync(string voucher, decimal basis, decimal tax) => AddEntryAsync(voucher,
            "customer_invoice", basis + tax,
            [(_receivable, basis + tax, 0m, null), (_revenue, 0m, basis, SalesFacts(basis, tax)),
                (_outputVat, 0m, tax, SalesFacts(basis, tax))]);

        public Task AddSalesCreditAsync(string voucher, decimal basis, decimal tax) => AddEntryAsync(voucher,
            "customer_credit_note", basis + tax,
            [(_receivable, 0m, basis + tax, null),
                (_revenue, basis, 0m, SalesFacts(basis, tax, "customer_credit_note")),
                (_outputVat, tax, 0m, SalesFacts(basis, tax, "customer_credit_note"))]);

        public Task AddForeignCurrencyFactsAsync(string voucher, decimal basis, decimal tax) => AddEntryAsync(voucher,
            "customer_invoice", basis + tax,
            [(_receivable, basis + tax, 0m, null),
                (_revenue, 0m, basis, SalesFacts(basis, tax, currency: "EUR")),
                (_outputVat, 0m, tax, SalesFacts(basis, tax, currency: "EUR"))]);

        public Task AddPurchaseAsync(string voucher, decimal basis, decimal tax) => AddEntryAsync(voucher,
            "supplier_invoice", basis + tax,
            [(_expense, basis, 0m, PurchaseFacts(basis, tax)), (_inputVat, tax, 0m, PurchaseFacts(basis, tax)),
                (_payable, 0m, basis + tax, null)]);

        public async Task LockFiscalPeriodAsync()
        {
            _fiscal.Close(_now);
            _fiscal.LockReporting(UserId, _now);
            await Context.SaveChangesAsync();
        }

        private async Task AddEntryAsync(string voucher, string sourceType, decimal gross,
            IReadOnlyList<(FinanceAccount Account, decimal Debit, decimal Credit, string? Facts)> lines)
        {
            var entry = new LedgerEntry(Guid.NewGuid(), CompanyId, _fiscal.Id, voucher, _now,
                LedgerEntryStatuses.Posted, sourceType: sourceType, sourceId: Guid.NewGuid().ToString("D"),
                postingDate: new DateOnly(2026, 8, 24), baseCurrency: "SEK", sourceVersion: "1",
                policyPackKey: AccountingPolicyPackDefaults.SwedishCandidatePackKey,
                policyPackVersion: AccountingPolicyPackDefaults.SwedishCandidateVersion);
            foreach (var line in lines) entry.Lines.Add(new LedgerEntryLine(Guid.NewGuid(), CompanyId,
                entry.Id, line.Account.Id, line.Debit, line.Credit, "SEK", description: voucher,
                createdUtc: _now, taxFactsJson: line.Facts));
            Context.LedgerEntries.Add(entry);
            await Context.SaveChangesAsync();
        }

        private static string SalesFacts(decimal basis, decimal tax,
            string documentType = "customer_invoice", string currency = "SEK") => $$"""{"schemaVersion":"2.0","policyPackKey":"{{AccountingPolicyPackDefaults.SwedishCandidatePackKey}}","policyPackVersion":"{{AccountingPolicyPackDefaults.SwedishCandidateVersion}}","taxRuleKey":"se_domestic_sales_25","taxRuleVersion":"2026.1","direction":"sales","documentType":"{{documentType}}","documentCurrency":"{{currency}}","taxableBasis":"{{basis.ToString(CultureInfo.InvariantCulture)}}","taxAmount":"{{tax.ToString(CultureInfo.InvariantCulture)}}","vatBoxes":"05,10"}""";
        private static string PurchaseFacts(decimal basis, decimal tax) => $$"""{"schemaVersion":"2.0","policyPackKey":"{{AccountingPolicyPackDefaults.SwedishCandidatePackKey}}","policyPackVersion":"{{AccountingPolicyPackDefaults.SwedishCandidateVersion}}","taxRuleKey":"se_domestic_purchase_25_full_recovery","taxRuleVersion":"2026.1","direction":"purchase","documentType":"supplier_invoice","documentCurrency":"SEK","taxableBasis":"{{basis.ToString(CultureInfo.InvariantCulture)}}","taxAmount":"{{tax.ToString(CultureInfo.InvariantCulture)}}","recoverableTaxAmount":"{{tax.ToString(CultureInfo.InvariantCulture)}}","vatBoxes":"48"}""";

        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class CompanyContext(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId; public Guid? UserId => userId; public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? company) => CompanyId = company;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? context) => CompanyId = context?.CompanyId;
    }

    private sealed class MembershipResolver(Guid companyId, Guid userId) : ICompanyMembershipContextResolver
    {
        private readonly ResolvedCompanyMembershipContext _membership = new(Guid.NewGuid(), companyId, userId,
            "VAT return company", CompanyMembershipRole.Owner, CompanyMembershipStatus.Active);
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult<ResolvedCompanyMembershipContext?>(_membership);
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid requestedCompanyId, CancellationToken cancellationToken) => Task.FromResult<ResolvedCompanyMembershipContext?>(requestedCompanyId == _membership.CompanyId ? _membership : null);
    }

    private sealed class CurrentUser(Guid userId) : ICurrentUserAccessor
    {
        public ClaimsPrincipal Principal { get; } = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));
        public bool IsAuthenticated => true; public Guid? UserId => userId;
        public AuthenticatedUserIdentity Current => new(true, userId, null);
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    private sealed class MemoryStorage : ICompanyDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _items = new();
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream(_items[storageKey], false));
        public async Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken)
        { using var memory = new MemoryStream(); await request.Content.CopyToAsync(memory, cancellationToken); _items[request.StorageKey] = memory.ToArray(); return new(request.StorageKey, null); }
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) { _items.Remove(storageKey); return Task.CompletedTask; }
    }

    private sealed class ApprovalService(VirtualCompanyDbContext db) : IApprovalRequestService
    {
        public Task<ApprovalRequestDto> CreateAsync(Guid companyId, CreateApprovalRequestCommand command, CancellationToken cancellationToken)
        {
            var entity = ApprovalRequest.CreateForTarget(Guid.NewGuid(), companyId,
                ApprovalTargetEntityTypeValues.Parse(command.TargetEntityType), command.TargetEntityId,
                command.RequestedByActorType, command.RequestedByActorId, command.ApprovalType,
                command.ThresholdContext ?? new Dictionary<string, JsonNode?>(), command.RequiredRole,
                command.RequiredUserId, []);
            db.ApprovalRequests.Add(entity);
            return Task.FromResult(new ApprovalRequestDto(entity.Id, companyId, entity.TargetEntityType,
                entity.TargetEntityId, entity.RequestedByActorType, entity.RequestedByActorId,
                entity.ApprovalType, entity.RequiredRole, entity.RequiredUserId, entity.Status.ToStorageValue(),
                entity.ThresholdContext, [], null, null, null, "VAT return approval", "VAT return evidence",
                [], null, entity.CreatedUtc));
        }
        public Task<IReadOnlyList<ApprovalRequestDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApprovalRequestDto> GetAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApprovalDecisionResultDto> DecideAsync(Guid companyId, ApprovalDecisionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
