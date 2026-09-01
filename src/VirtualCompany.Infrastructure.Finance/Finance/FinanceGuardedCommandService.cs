using System.Diagnostics.Metrics;
using VirtualCompany.Application.Finance;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceGuardedCommandService(
    IFinanceReadService reads,
    IFinanceCommandService commands) : IFinanceGuardedCommandService
{
    public async Task<GuardedCategorizationBatchResultDto> CategorizeTransactionsAsync(
        CategorizeTransactionsGuardedCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CompanyId == Guid.Empty || command.ActorUserId == Guid.Empty || command.AgentId == Guid.Empty)
            throw new ArgumentException("Company, actor, and agent context are required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Trim().Length is < 8 or > 200)
            throw new ArgumentException("A bounded business idempotency key is required.", nameof(command));
        if (command.Items.Count is < 1 or > FinanceGuardedCommandContract.MaximumCategorizationBatchSize)
            throw new ArgumentOutOfRangeException(nameof(command),
                $"Categorization batches must contain between 1 and {FinanceGuardedCommandContract.MaximumCategorizationBatchSize} items.");

        var decisions = new List<GuardedCategorizationItemDecisionDto>(command.Items.Count);
        var seen = new HashSet<Guid>();
        for (var index = 0; index < command.Items.Count; index++)
        {
            var item = command.Items[index];
            if (item.TransactionId == Guid.Empty)
            {
                decisions.Add(Rejected(index, item, null, "transaction_id_required", "A transaction id is required."));
                continue;
            }
            if (!seen.Add(item.TransactionId))
            {
                decisions.Add(Rejected(index, item, null, "duplicate_batch_target",
                    "The transaction is already represented by an earlier item in this request."));
                continue;
            }

            FinanceTransactionDetailDto? current;
            try
            {
                current = await reads.GetTransactionDetailAsync(
                    new(command.CompanyId, item.TransactionId, FinanceDataSources.Operational), cancellationToken);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            {
                decisions.Add(Rejected(index, item, null, "transaction_read_blocked",
                    "The current transaction state could not be verified for this company."));
                continue;
            }

            if (current is null)
            {
                decisions.Add(Rejected(index, item, null, "transaction_not_found",
                    "The transaction is not available for this company."));
                continue;
            }
            if (!current.Permissions.CanChangeTransactionCategory)
            {
                decisions.Add(Rejected(index, item, current, "transaction_category_change_not_allowed",
                    "The owning Finance policy does not allow this transaction category to change."));
                continue;
            }

            var expected = FinanceTransactionCategories.Normalize(item.ExpectedCategory);
            var requested = FinanceTransactionCategories.Normalize(item.Category);
            if (!FinanceTransactionCategories.IsSupported(expected) || !FinanceTransactionCategories.IsSupported(requested))
            {
                decisions.Add(Rejected(index, item, current, "transaction_category_unsupported",
                    "A supported expected and requested category is required."));
                continue;
            }
            if (!string.Equals(current.Category, expected, StringComparison.OrdinalIgnoreCase))
            {
                decisions.Add(Rejected(index, item, current, "transaction_state_stale",
                    "The transaction category changed after this request was prepared."));
                continue;
            }
            if (string.Equals(current.Category, requested, StringComparison.OrdinalIgnoreCase))
            {
                decisions.Add(new(index, item.TransactionId, expected, requested, current.Category,
                    Math.Abs(current.Amount), current.Currency, "unchanged", "requested_state_already_applied",
                    "The transaction already has the requested category; no mutation was repeated.", false, null));
                continue;
            }

            try
            {
                var after = await commands.UpdateTransactionCategoryAsync(
                    new(command.CompanyId, item.TransactionId, requested), cancellationToken);
                decisions.Add(new(index, item.TransactionId, expected, requested, after.TransactionType,
                    Math.Abs(after.Amount), after.Currency, "applied", "category_updated",
                    "The owning Finance command applied the requested category.", true, after));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException)
            {
                decisions.Add(Rejected(index, item, current, "authoritative_command_rejected",
                    "The owning Finance command rejected this item; it was not silently skipped."));
            }
        }

        var mutated = decisions.Count(item => item.Mutated);
        var rejected = decisions.Count(item => item.Outcome == "rejected");
        var eligible = decisions.Count - rejected;
        var exposure = decisions.Where(item => item.Outcome != "rejected").Sum(item => item.AbsoluteAmount ?? 0m);
        FinanceGuardedCommandTelemetry.Record(command.Items.Count, mutated, rejected);
        return new(FinanceGuardedCommandContract.Version, command.IdempotencyKey.Trim(), command.Items.Count,
            eligible, mutated, rejected, exposure, decisions, mutated > 0 && rejected > 0,
            rejected == 0
                ? $"All {command.Items.Count} categorization items received an authoritative decision."
                : $"{mutated} item(s) changed and {rejected} item(s) were rejected with explicit reasons.");
    }

    private static GuardedCategorizationItemDecisionDto Rejected(
        int index,
        GuardedTransactionCategorizationItem item,
        FinanceTransactionDetailDto? current,
        string reasonCode,
        string explanation) =>
        new(index, item.TransactionId, item.ExpectedCategory, item.Category, current?.Category,
            current is null ? null : Math.Abs(current.Amount), current?.Currency, "rejected", reasonCode,
            explanation, false, null);
}

internal static class FinanceGuardedCommandTelemetry
{
    internal const string MeterName = "VirtualCompany.Finance.GuardedCommands";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("finance.guarded_commands.requests");
    private static readonly Counter<long> Items = Meter.CreateCounter<long>("finance.guarded_commands.items");

    public static void Record(int requested, int mutated, int rejected)
    {
        Requests.Add(1);
        Items.Add(requested, new KeyValuePair<string, object?>("outcome", rejected > 0 ? "partial_or_rejected" : "accepted"));
        Items.Add(mutated, new KeyValuePair<string, object?>("outcome", "mutated"));
    }
}
