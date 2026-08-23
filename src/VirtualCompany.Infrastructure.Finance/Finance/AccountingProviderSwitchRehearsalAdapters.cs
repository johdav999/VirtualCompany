using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class InternalLedgerProviderSwitchRehearsalAdapter : IAccountingProviderSwitchRehearsalAdapter
{
    public bool CanHandle(string targetKind, string? targetProviderKey) =>
        targetKind == AccountingProviderEndpointKinds.Internal;

    public Task<AccountingProviderSwitchRehearsalTargetResult> PreviewAsync(
        AccountingProviderSwitchRehearsalTargetRequest request, CancellationToken cancellationToken)
    {
        var outcomes = new Dictionary<Guid, string>();
        foreach (var record in request.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var _ = JsonDocument.Parse(record.NormalizedDataJson);
                outcomes[record.Id] = "accepted";
            }
            catch (JsonException)
            {
                outcomes[record.Id] = "rejected_invalid_normalized_data";
            }
        }
        return Task.FromResult(new AccountingProviderSwitchRehearsalTargetResult(true, false,
            "local_target_simulation",
            "Virtual Company validation ran without committing ledger records. This rehearsal is not an authoritative posting.",
            outcomes));
    }
}

public sealed class FortnoxProviderSwitchRehearsalAdapter : IAccountingProviderSwitchRehearsalAdapter
{
    public bool CanHandle(string targetKind, string? targetProviderKey) =>
        targetKind == AccountingProviderEndpointKinds.External &&
        string.Equals(targetProviderKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase);

    public Task<AccountingProviderSwitchRehearsalTargetResult> PreviewAsync(
        AccountingProviderSwitchRehearsalTargetRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AccountingProviderSwitchRehearsalTargetResult(false, false, "unsupported",
            "Fortnox does not provide a non-authoritative migration preview API. Local target simulation was used and Fortnox acceptance is not yet proven.",
            new Dictionary<Guid, string>()));
}

public sealed class UnavailableProviderSwitchRehearsalAdapter : IAccountingProviderSwitchRehearsalAdapter
{
    public bool CanHandle(string targetKind, string? targetProviderKey) => true;
    public Task<AccountingProviderSwitchRehearsalTargetResult> PreviewAsync(
        AccountingProviderSwitchRehearsalTargetRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AccountingProviderSwitchRehearsalTargetResult(false, false, "unsupported",
            "The target provider has no registered preview adapter. Local target simulation was used and provider acceptance is not proven.",
            new Dictionary<Guid, string>()));
}
