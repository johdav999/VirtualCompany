using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchStagingTests
{
    private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public void Disposition_rules_require_traceable_mapping_duplicate_and_approval_evidence()
    {
        var record = CreateRecord();

        Assert.Throws<InvalidOperationException>(() => record.ResolveDisposition(
            AccountingProviderSwitchDispositions.Mapped, "Map account.", null, null, null, null, null, false, Now));
        Assert.Throws<InvalidOperationException>(() => record.ResolveDisposition(
            AccountingProviderSwitchDispositions.Duplicate, "Duplicate record.", null, null, null, null, null, false, Now));
        Assert.Throws<InvalidOperationException>(() => record.ResolveDisposition(
            AccountingProviderSwitchDispositions.ExcludedWithApproval, "Approved exclusion.", Guid.NewGuid(), 1,
            Guid.NewGuid(), Hash, null, false, Now));

        var decisionId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        record.ResolveDisposition(AccountingProviderSwitchDispositions.ExcludedWithApproval,
            "Approved exclusion.", decisionId, 1, approvalId, Hash, null, true, Now);

        Assert.Equal(AccountingProviderSwitchDispositions.ExcludedWithApproval, record.Disposition);
        Assert.Equal(decisionId, record.MappingDecisionId);
        Assert.Equal(approvalId, record.ApprovalRequestId);
    }

    [Fact]
    public void Stable_identity_includes_source_version_and_same_record_key_does_not()
    {
        var first = CreateRecord("v1");
        var replay = CreateRecord("v1");
        var changed = CreateRecord("v2");

        Assert.Equal(first.StableIdentityHash, replay.StableIdentityHash);
        Assert.NotEqual(first.StableIdentityHash, changed.StableIdentityHash);
        Assert.Equal(first.SourceRecordKeyHash, changed.SourceRecordKeyHash);
    }

    [Fact]
    public void Automatic_mapping_acceptance_is_limited_to_exact_non_material_decisions()
    {
        var companyId = Guid.NewGuid();
        var switchId = Guid.NewGuid();
        var setId = Guid.NewGuid();
        var material = new AccountingProviderSwitchMappingDecision(Guid.NewGuid(), companyId, switchId,
            setId, 1, AccountingProviderSwitchMappingTypes.Account, "3000", "3000", "exact_identifier",
            1m, "{}", true, 1, 100m, Hash, Guid.NewGuid(), Now);
        var ambiguous = new AccountingProviderSwitchMappingDecision(Guid.NewGuid(), companyId, switchId,
            setId, 1, AccountingProviderSwitchMappingTypes.TaxCode, "VAT", "standard", "known_tax_semantics",
            .9m, "{}", false, 1, 100m, Hash, Guid.NewGuid(), Now);
        var exact = new AccountingProviderSwitchMappingDecision(Guid.NewGuid(), companyId, switchId,
            setId, 1, AccountingProviderSwitchMappingTypes.Currency, "SEK", "SEK", "known_currency_identifier",
            1m, "{}", false, 1, 100m, Hash, Guid.NewGuid(), Now);

        Assert.Throws<InvalidOperationException>(() => material.ApproveAutomatically(Now));
        Assert.Throws<InvalidOperationException>(() => ambiguous.ApproveAutomatically(Now));
        exact.ApproveAutomatically(Now);
        Assert.Equal(AccountingProviderSwitchMappingStatuses.Approved, exact.Status);
    }

    private static AccountingProviderSwitchStagedRecord CreateRecord(string version = "v1") => new(
        Guid.NewGuid(), Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.NewGuid(),
        new AccountingProviderEndpoint(AccountingProviderEndpointKinds.External, "fortnox"),
        AccountingProviderSwitchStagingDatasets.Accounts, "3000", version, Now, Hash, Hash,
        "{\"code\":\"3000\"}", "{}", 100m, "SEK", AccountingProviderSwitchDispositions.Ready, Now);
}
