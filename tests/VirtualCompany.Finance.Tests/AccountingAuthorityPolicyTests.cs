using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingAuthorityPolicyTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Authority_is_enforced_per_period_for_native_provider_export_and_import_operations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        dbContext.Companies.Add(new Company(companyId, "Authority test company"));
        dbContext.AccountingAuthorityPeriods.AddRange(
            new AccountingAuthorityPeriod(Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31), AccountingAuthorityValues.InternalLedger, null, actorId,
                "Internal ledger launch.", NowUtc),
            new AccountingAuthorityPeriod(Guid.NewGuid(), companyId, new DateOnly(2026, 2, 1),
                null, AccountingAuthorityValues.ExternalProvider, FinanceIntegrationProviderKeys.Fortnox, actorId,
                "External authority after cutover.", NowUtc));
        await dbContext.SaveChangesAsync();
        var policy = new AccountingAuthorityPolicy(dbContext);

        var internalNative = await Evaluate(policy, companyId, new DateOnly(2026, 1, 31),
            AccountingAuthorityOperationValues.NativeAuthoritativePosting);
        var internalProviderWrite = await Evaluate(policy, companyId, new DateOnly(2026, 1, 31),
            AccountingAuthorityOperationValues.ProviderAuthoritativeWrite, FinanceIntegrationProviderKeys.Fortnox);
        var internalExport = await Evaluate(policy, companyId, new DateOnly(2026, 1, 31),
            AccountingAuthorityOperationValues.DownstreamExport, FinanceIntegrationProviderKeys.Fortnox);
        var externalNative = await Evaluate(policy, companyId, new DateOnly(2026, 2, 1),
            AccountingAuthorityOperationValues.NativeAuthoritativePosting);
        var externalProviderWrite = await Evaluate(policy, companyId, new DateOnly(2026, 2, 1),
            AccountingAuthorityOperationValues.ProviderAuthoritativeWrite, FinanceIntegrationProviderKeys.Fortnox);
        var externalExport = await Evaluate(policy, companyId, new DateOnly(2026, 2, 1),
            AccountingAuthorityOperationValues.DownstreamExport, FinanceIntegrationProviderKeys.Fortnox);
        var externalImport = await Evaluate(policy, companyId, new DateOnly(2026, 2, 1),
            AccountingAuthorityOperationValues.ImportProjection, FinanceIntegrationProviderKeys.Fortnox);

        Assert.True(internalNative.IsAllowed);
        Assert.False(internalProviderWrite.IsAllowed);
        Assert.Equal(AccountingAuthorityReasonCodes.ProviderPostingBlocked, internalProviderWrite.ReasonCode);
        Assert.True(internalExport.IsAllowed);
        Assert.False(externalNative.IsAllowed);
        Assert.Equal(AccountingAuthorityReasonCodes.NativePostingBlocked, externalNative.ReasonCode);
        Assert.True(externalProviderWrite.IsAllowed);
        Assert.False(externalExport.IsAllowed);
        Assert.True(externalImport.IsAllowed);
    }

    [Fact]
    public async Task Migration_state_allows_reconciliation_but_blocks_normal_posting_and_export()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        dbContext.Companies.Add(new Company(companyId, "Migration test company"));
        dbContext.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(
            Guid.NewGuid(), companyId, new DateOnly(2026, 3, 1), null,
            AccountingAuthorityValues.Migration, FinanceIntegrationProviderKeys.Fortnox, actorId,
            "Cut over at the March boundary.", NowUtc, AccountingAuthorityValues.ExternalProvider));
        await dbContext.SaveChangesAsync();
        var policy = new AccountingAuthorityPolicy(dbContext);

        var native = await Evaluate(policy, companyId, new DateOnly(2026, 3, 2),
            AccountingAuthorityOperationValues.NativeAuthoritativePosting);
        var export = await Evaluate(policy, companyId, new DateOnly(2026, 3, 2),
            AccountingAuthorityOperationValues.DownstreamExport, FinanceIntegrationProviderKeys.Fortnox);
        var reconciliation = await Evaluate(policy, companyId, new DateOnly(2026, 3, 2),
            AccountingAuthorityOperationValues.MigrationReconciliation, FinanceIntegrationProviderKeys.Fortnox);

        Assert.False(native.IsAllowed);
        Assert.False(export.IsAllowed);
        Assert.True(reconciliation.IsAllowed);
    }

    [Fact]
    public async Task Internal_authority_routes_sales_document_requests_to_native_finance_review_without_a_provider()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        dbContext.Companies.Add(new Company(companyId, "Native finance company"));
        dbContext.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(
            Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), null,
            AccountingAuthorityValues.InternalLedger, null, actorId,
            "Native accounting is the default.", NowUtc));
        await dbContext.SaveChangesAsync();
        var service = new FinanceAccountingActionService(
            dbContext,
            writeCommands: null!,
            providerRegistry: null!,
            fortnoxExecutor: null!,
            adapters: []);

        var result = await service.RequestDocumentAsync(new RequestFinanceDocumentActionCommand(
            companyId, "sales_deal", Guid.NewGuid().ToString("D"), "v1", "invoice",
            new DateOnly(2026, 8, 20), "Example customer", "Won sales deal", 1250m, "SEK", null,
            Guid.NewGuid(), actorId, "sales-test"), CancellationToken.None);

        Assert.Equal(AccountingAuthorityValues.InternalLedger, result.Authority);
        Assert.Equal("virtual_company", result.DestinationKey);
        Assert.Equal(FinanceAccountingActionStatuses.FinanceReviewRequired, result.Status);
        Assert.Null(result.ApprovalId);
        Assert.Empty(dbContext.FinanceIntegrationWriteCommands);
    }

    [Fact]
    public async Task Internal_authority_routes_support_customer_credit_to_native_finance_review_without_a_provider()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        dbContext.Companies.Add(new Company(companyId, "Native support finance company"));
        dbContext.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(
            Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), null,
            AccountingAuthorityValues.InternalLedger, null, actorId,
            "Native accounting is authoritative.", NowUtc));
        await dbContext.SaveChangesAsync();
        var service = new FinanceAccountingActionService(
            dbContext,
            writeCommands: null!,
            providerRegistry: null!,
            fortnoxExecutor: null!,
            adapters: []);

        var result = await service.RequestCustomerDocumentExportAsync(
            new RequestFinanceCustomerDocumentExportCommand(
                companyId,
                Guid.NewGuid(),
                new DateOnly(2026, 8, 20),
                Guid.NewGuid(),
                actorId,
                "Support agent",
                "support-refund-test"),
            CancellationToken.None);

        Assert.Equal(AccountingAuthorityValues.InternalLedger, result.Authority);
        Assert.Equal("virtual_company", result.DestinationKey);
        Assert.Equal(FinanceAccountingActionStatuses.FinanceReviewRequired, result.Status);
        Assert.Null(result.ApprovalId);
        Assert.Empty(dbContext.FinanceIntegrationWriteCommands);
    }

    [Fact]
    public void Cutover_cannot_complete_until_balances_mappings_and_conflicts_are_reconciled()
    {
        var actorId = Guid.NewGuid();
        var period = new AccountingAuthorityPeriod(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 4, 1), null,
            AccountingAuthorityValues.Migration, FinanceIntegrationProviderKeys.Fortnox, actorId,
            "Move authority at a fiscal-period boundary.", NowUtc, AccountingAuthorityValues.ExternalProvider);

        Assert.Throws<InvalidOperationException>(() => period.CompleteCutover(actorId, NowUtc.AddMinutes(1)));
        period.RecordCutoverValidation(true, true, true, 2, "Two source conflicts remain.", actorId, NowUtc.AddMinutes(2));
        Assert.False(period.IsCutoverReady);
        Assert.Throws<InvalidOperationException>(() => period.CompleteCutover(actorId, NowUtc.AddMinutes(3)));

        period.RecordCutoverValidation(true, true, true, 0, "Opening balances, trial balance, and source mappings reconcile.", actorId, NowUtc.AddMinutes(4));
        period.CompleteCutover(actorId, NowUtc.AddMinutes(5));

        Assert.Equal(AccountingAuthorityValues.ExternalProvider, period.Authority);
        Assert.Null(period.TargetAuthority);
        Assert.NotNull(period.CompletedUtc);
    }

    [Fact]
    public void Provider_failures_are_classified_for_safe_reconciliation_and_operator_action()
    {
        AssertFailure(new TaskCanceledException(), false,
            AccountingProviderExportFailureCategories.UnknownOutcome, ambiguous: true);
        AssertFailure(new InvalidOperationException("local persistence failed"), true,
            AccountingProviderExportFailureCategories.ProviderSuccessLocalFailure, ambiguous: true);
        AssertFailure(new FortnoxApiException("Reconnect.", HttpStatusCode.Unauthorized, "authorization", requiresReconnect: true), false,
            AccountingProviderExportFailureCategories.StaleCredentials, ambiguous: false);
        AssertFailure(new FortnoxApiException("Scope missing.", HttpStatusCode.Forbidden, "scope"), false,
            AccountingProviderExportFailureCategories.MissingScope, ambiguous: false);
        AssertFailure(new FortnoxApiException("Slow down.", HttpStatusCode.TooManyRequests, "rate_limit", isTransient: true), false,
            AccountingProviderExportFailureCategories.RateLimited, ambiguous: false);
        AssertFailure(new FortnoxApiException("Invalid voucher.", HttpStatusCode.BadRequest, "validation"), false,
            AccountingProviderExportFailureCategories.Validation, ambiguous: false);
    }

    [Fact]
    public void Fortnox_mapping_is_adapter_owned_and_deterministic_for_a_neutral_committed_voucher()
    {
        var adapter = new FortnoxAccountingProviderExportAdapter();
        var envelope = new AccountingProviderExportEnvelope(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "A-42", new DateOnly(2026, 8, 20),
            "Customer invoice", "customer_invoice", "invoice-42", "v3", "SEK",
            [
                new AccountingProviderExportLine("1510", "Accounts receivable", 1250m, 0m, "SEK", "Invoice 42"),
                new AccountingProviderExportLine("3000", "Revenue", 0m, 1250m, "SEK", "Invoice 42")
            ]);

        var first = adapter.Map(envelope);
        var second = adapter.Map(envelope);

        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, first.ProviderKey);
        Assert.Equal("vouchers", first.Path);
        Assert.Equal(first.PayloadHash, second.PayloadHash);
        Assert.Contains("VoucherRows", first.SanitizedPayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_model_has_company_scoped_stable_identity_and_write_request_uniqueness()
    {
        using var dbContext = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite("Data Source=:memory:").Options);
        var entity = dbContext.Model.FindEntityType(typeof(AccountingProviderExport))!;
        var indexes = entity.GetIndexes().ToArray();

        Assert.Contains(indexes, index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(AccountingProviderExport.CompanyId), nameof(AccountingProviderExport.StableIdentity)]));
        Assert.Contains(indexes, index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(AccountingProviderExport.CompanyId), nameof(AccountingProviderExport.WriteRequestId)]));
    }

    [Fact]
    public void Provider_write_record_preserves_the_approved_authority_period_context()
    {
        var authorityPeriodId = Guid.NewGuid();
        var accountingDate = new DateOnly(2026, 8, 20);
        var record = new FinanceIntegrationWriteCommandRecord(
            Guid.NewGuid(), Guid.NewGuid(), null, null, FinanceIntegrationWriteCommandTypes.InvoiceExport,
            "POST", "invoices", "Example customer", "Create invoice", "hash", "{}", "correlation", NowUtc,
            accountingDate, AccountingAuthorityOperationValues.ProviderAuthoritativeWrite, authorityPeriodId);

        Assert.Equal(accountingDate, record.AccountingDate);
        Assert.Equal(AccountingAuthorityOperationValues.ProviderAuthoritativeWrite, record.AuthorityOperation);
        Assert.Equal(authorityPeriodId, record.AuthorityPeriodId);
    }

    private static Task<AccountingAuthorityPolicyDecision> Evaluate(
        AccountingAuthorityPolicy policy,
        Guid companyId,
        DateOnly date,
        string operation,
        string? providerKey = null) =>
        policy.EvaluateAsync(new(companyId, date, operation, providerKey), CancellationToken.None);

    private static void AssertFailure(Exception exception, bool providerAccepted, string category, bool ambiguous)
    {
        var failure = AccountingProviderExportService.Classify(exception, providerAccepted);
        Assert.Equal(category, failure.Category);
        Assert.Equal(ambiguous, failure.Ambiguous);
        Assert.False(string.IsNullOrWhiteSpace(failure.Summary));
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
}
