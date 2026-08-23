using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchTargetTransferTests
{
    private readonly FortnoxAccountingProviderSwitchTargetPreparationAdapter _adapter = new();

    [Theory]
    [InlineData(AccountingProviderSwitchStagingDatasets.Accounts, AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting, "accounts")]
    [InlineData(AccountingProviderSwitchStagingDatasets.Counterparties, AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting, "customers")]
    [InlineData(AccountingProviderSwitchStagingDatasets.Dimensions, AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting, "projects")]
    [InlineData(AccountingProviderSwitchStagingDatasets.OpeningBalanceCandidates, AccountingProviderSwitchTargetOperationModes.FinalAuthoritative, "vouchers")]
    [InlineData(AccountingProviderSwitchStagingDatasets.Journals, AccountingProviderSwitchTargetOperationModes.FinalAuthoritative, "vouchers")]
    [InlineData(AccountingProviderSwitchStagingDatasets.OpenItems, AccountingProviderSwitchTargetOperationModes.FinalAuthoritative, "invoices")]
    [InlineData(AccountingProviderSwitchStagingDatasets.Payments, AccountingProviderSwitchTargetOperationModes.FinalAuthoritative, "invoicepayments")]
    public void Fortnox_adapter_maps_supported_neutral_datasets_with_expected_execution_boundary(
        string dataset, string expectedMode, string expectedPath)
    {
        var first = _adapter.Map(Request(dataset));
        var second = _adapter.Map(Request(dataset));

        Assert.True(first.IsSupported);
        Assert.Equal(expectedMode, first.OperationMode);
        Assert.Equal(expectedPath, first.ProviderCommand!.Path);
        Assert.Equal(first.ProviderCommand.PayloadHash, second.ProviderCommand!.PayloadHash);
        Assert.Equal(first.ProviderCommand.SanitizedPayloadJson, second.ProviderCommand.SanitizedPayloadJson);
        Assert.DoesNotContain("fortnoxRaw", first.ProviderCommand.SanitizedPayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provider_to_provider_mapping_uses_only_the_normalized_record()
    {
        var operation = _adapter.Map(Request(AccountingProviderSwitchStagingDatasets.Counterparties,
            """{"counterpartyType":"supplier","number":"S-42","name":"Neutral Supplier"}"""));

        Assert.Equal("suppliers", operation.ProviderCommand!.Path);
        Assert.Contains("Neutral Supplier", operation.ProviderCommand.SanitizedPayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceProvider", operation.ProviderCommand.SanitizedPayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Attachments_stop_with_an_actionable_capability_result()
    {
        var operation = _adapter.Map(Request(AccountingProviderSwitchStagingDatasets.Documents));

        Assert.False(operation.IsSupported);
        Assert.Null(operation.ProviderCommand);
        Assert.Contains("Inbox upload contract", operation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_and_item_domain_state_keep_authoritative_operations_held()
    {
        var now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var batch = new AccountingProviderSwitchTargetTransferBatch(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 2, Hash('a'), "fortnox", Hash('b'), Guid.NewGuid(),
            "batch-key", "correlation", now);
        batch.Status = AccountingProviderSwitchTargetTransferBatchStatuses.Building;
        var item = new AccountingProviderSwitchTargetTransferItem(Guid.NewGuid(), batch.CompanyId, batch.SwitchId,
            batch.Id, Guid.NewGuid(), AccountingProviderSwitchStagingDatasets.OpeningBalanceCandidates,
            "opening-2026", "v1", Hash('c'), Hash('d'), 3,
            AccountingProviderSwitchTargetOperationModes.FinalAuthoritative, "create_opening_balance_voucher",
            Hash('e'), Hash('f'), "Validated opening balance voucher.", now);

        item.HoldForCutover("Held until final activation.", now);
        batch.CompleteBuild(1, 0, 0, 1, now);

        Assert.Equal(AccountingProviderSwitchTargetTransferItemStatuses.HeldForCutover, item.Status);
        Assert.Null(item.WriteRequestId);
        Assert.Equal(AccountingProviderSwitchTargetTransferBatchStatuses.ReadyForCutover, batch.Status);
    }

    [Fact]
    public void Ambiguous_provider_outcome_requires_reconciliation_and_cannot_be_blindly_retried()
    {
        var now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var item = new AccountingProviderSwitchTargetTransferItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), AccountingProviderSwitchStagingDatasets.Accounts, "1930", "v1",
            Hash('a'), Hash('b'), null, AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting,
            "create_account", Hash('c'), Hash('d'), "Create account 1930.", now);

        item.Fail("unknown_outcome", "The provider outcome is unknown.", ambiguous: true, now);

        Assert.True(item.ReconciliationNeeded);
        Assert.Equal(AccountingProviderSwitchTargetTransferItemStatuses.ReconciliationRequired, item.Status);
        item.Reconcile(true, "1930", "Provider confirmed the account exists.", now.AddMinutes(1));
        Assert.False(item.ReconciliationNeeded);
        Assert.Equal(AccountingProviderSwitchTargetTransferItemStatuses.Succeeded, item.Status);
    }

    private static AccountingProviderSwitchTargetMappingRequest Request(string dataset, string? json = null)
    {
        json ??= dataset switch
        {
            AccountingProviderSwitchStagingDatasets.Accounts => """{"number":"1930","description":"Bank account","active":true}""",
            AccountingProviderSwitchStagingDatasets.Counterparties => """{"counterpartyType":"customer","number":"C-42","name":"Neutral Customer"}""",
            AccountingProviderSwitchStagingDatasets.Dimensions => """{"dimensionType":"project","code":"P-1","name":"Migration"}""",
            AccountingProviderSwitchStagingDatasets.OpeningBalanceCandidates or AccountingProviderSwitchStagingDatasets.Journals =>
                """{"postingDate":"2026-09-01","voucherSeries":"A","lines":[]}""",
            AccountingProviderSwitchStagingDatasets.OpenItems => """{"documentType":"customer_invoice","customerNumber":"C-42","documentNumber":"100","invoiceDate":"2026-08-01"}""",
            AccountingProviderSwitchStagingDatasets.Payments => """{"invoiceNumber":"100","paymentDate":"2026-08-15","amount":100}""",
            _ => "{}"
        };
        var record = new AccountingProviderSwitchTargetRecord(Guid.NewGuid(), dataset, "source-42", "v1",
            Hash('a'), Hash('b'), json, "{}", 100m, "SEK", AccountingProviderSwitchDispositions.Ready, 1);
        return new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 1, Hash('c'), "fortnox", record,
            Hash('d'), "adapter-test");
    }

    private static string Hash(char value) => new(value, 64);
}
