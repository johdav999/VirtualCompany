using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Companies;

internal static class FinanceApprovalContinuationBinding
{
    public const string SchemaVersion = "finance-approval-binding-v1";
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);

    public static async Task<JsonObject> CreateAsync(
        VirtualCompanyDbContext dbContext,
        ICompanyToolRegistry toolRegistry,
        ToolExecutionAttempt attempt,
        Guid approvalRequestId,
        Guid initiatingUserId,
        Guid? delegationAuthorityId,
        ToolExecutionDecisionDto decision,
        AgentEffectiveAuthorityDto authority,
        DateTime issuedUtc,
        CancellationToken cancellationToken,
        FinanceAutonomyApprovalContextDto? autonomyContext = null)
    {
        if (!toolRegistry.TryGetTool(attempt.ToolName, out var registration) ||
            registration.FinanceRiskClassification is null)
        {
            throw new InvalidOperationException("Finance approval binding requires a registered Finance risk classification.");
        }

        var classification = registration.FinanceRiskClassification;
        var payloadHash = Hash(CanonicalizeDictionary(attempt.RequestPayload));
        var targetSnapshot = await BuildTargetSnapshotAsync(dbContext, attempt, cancellationToken);
        var integrationStateHash = await BuildIntegrationStateHashAsync(
            dbContext, attempt.CompanyId, classification, cancellationToken);
        var thresholdEvaluationHash = Hash(Canonicalize(
            decision.ThresholdEvaluations is null
                ? new JsonArray()
                : JsonSerializer.SerializeToNode(decision.ThresholdEvaluations)));
        var businessIdempotencyKey = ComputeBusinessIdempotencyKey(attempt);
        var expiryUtc = issuedUtc.Add(DefaultLifetime);

        var binding = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["companyId"] = attempt.CompanyId,
            ["approvalRequestId"] = approvalRequestId,
            ["executionId"] = attempt.Id,
            ["initiatingUserId"] = initiatingUserId,
            ["delegationAuthorityId"] = delegationAuthorityId,
            ["agentId"] = attempt.AgentId,
            ["toolName"] = attempt.ToolName,
            ["toolVersion"] = attempt.ToolVersion,
            ["actionType"] = attempt.ActionType.ToStorageValue(),
            ["scope"] = attempt.Scope,
            ["normalizedPayloadHash"] = payloadHash,
            ["targetSnapshot"] = targetSnapshot,
            ["targetSnapshotHash"] = Hash(Canonicalize(targetSnapshot)),
            ["effectiveAuthorityVersion"] = authority.AuthorityVersion,
            ["effectiveAuthorityHash"] = authority.AuthorityHash,
            ["riskPolicyVersion"] = classification.PolicyVersion,
            ["riskTier"] = classification.RiskTier,
            ["requiredActorPermission"] = classification.RequiredActorPermission,
            ["approvalBehavior"] = classification.DefaultApprovalBehavior,
            ["financeApprovalPolicyVersion"] = ReadDecisionMetadata(decision, "financeApprovalPolicyVersion"),
            ["thresholdEvaluationHash"] = thresholdEvaluationHash,
            ["integrationStateHash"] = integrationStateHash,
            ["segregationRequired"] = classification.RequiresSegregation,
            ["sensitiveAction"] = registration.SensitiveAction,
            ["externalSideEffectClass"] = classification.ExternalSideEffectClassification,
            ["businessIdempotencyKey"] = businessIdempotencyKey,
            ["continuationKey"] = ComputeContinuationKey(attempt, payloadHash, classification.PolicyVersion),
            ["issuedUtc"] = issuedUtc,
            ["expiresUtc"] = expiryUtc
        };
        if (autonomyContext is not null)
            binding["financeAutonomy"] = JsonSerializer.SerializeToNode(autonomyContext);
        return binding;
    }

    public static async Task<JsonArray> BuildTargetSnapshotAsync(
        VirtualCompanyDbContext dbContext,
        ToolExecutionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.ToolName.Equals("categorize_transaction", StringComparison.OrdinalIgnoreCase))
        {
            var id = ReadGuid(attempt.RequestPayload, "transactionId");
            var target = id.HasValue
                ? await dbContext.FinanceTransactions.IgnoreQueryFilters().AsNoTracking()
                    .Where(item => item.CompanyId == attempt.CompanyId && item.Id == id.Value)
                    .Select(item => new
                    {
                        item.Id, item.AccountId, item.CounterpartyId, item.InvoiceId, item.BillId,
                        item.TransactionUtc, item.TransactionType, item.Amount, item.Currency,
                        item.Description, item.ExternalReference, item.CreatedUtc
                    })
                    .SingleOrDefaultAsync(cancellationToken)
                : null;
            return new JsonArray(CreateTarget("finance_transaction", id, target));
        }

        if (attempt.ToolName.Equals("approve_invoice", StringComparison.OrdinalIgnoreCase))
        {
            var id = ReadGuid(attempt.RequestPayload, "invoiceId");
            var target = id.HasValue
                ? await dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
                    .Where(item => item.CompanyId == attempt.CompanyId && item.Id == id.Value)
                    .Select(item => new
                    {
                        item.Id, item.CounterpartyId, item.InvoiceNumber, item.Amount, item.PaidAmount,
                        item.Currency, item.Status, item.SettlementStatus, item.PostingStatus,
                        item.ProcessingStatus, item.UpdatedUtc
                    })
                    .SingleOrDefaultAsync(cancellationToken)
                : null;
            return new JsonArray(CreateTarget("finance_invoice", id, target));
        }

        if (attempt.ToolName.Equals("post_paid_supplier_bill_expense", StringComparison.OrdinalIgnoreCase))
        {
            var id = ReadGuid(attempt.RequestPayload, "billId");
            var target = id.HasValue
                ? await dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
                    .Where(item => item.CompanyId == attempt.CompanyId && item.Id == id.Value)
                    .Select(item => new
                    {
                        item.Id, item.CounterpartyId, item.BillNumber, item.Amount, item.PaidAmount,
                        item.Currency, item.Status, item.SettlementStatus, item.PostingStatus,
                        item.ProcessingStatus, item.UpdatedUtc
                    })
                    .SingleOrDefaultAsync(cancellationToken)
                : null;
            return new JsonArray(CreateTarget("finance_bill", id, target));
        }

        if (AccountingProviderSwitchAgentToolIds.ExecuteTools.Contains(attempt.ToolName, StringComparer.OrdinalIgnoreCase))
        {
            var id = ReadGuid(attempt.RequestPayload, "switchId");
            var target = id.HasValue
                ? await dbContext.AccountingProviderSwitches.IgnoreQueryFilters().AsNoTracking()
                    .Where(item => item.CompanyId == attempt.CompanyId && item.Id == id.Value)
                    .Select(item => new
                    {
                        item.Id, item.Version, item.Status, item.SourceKind, item.SourceProviderKey,
                        item.TargetKind, item.TargetProviderKey, item.UpdatedUtc
                    })
                    .SingleOrDefaultAsync(cancellationToken)
                : null;
            return new JsonArray(CreateTarget("accounting_provider_switch", id, target));
        }

        return [];
    }

    public static async Task<string> BuildIntegrationStateHashAsync(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        FinanceToolRiskClassification classification,
        CancellationToken cancellationToken)
    {
        if (string.Equals(classification.ExternalSideEffectClassification,
                FinanceToolExternalSideEffects.InternalStateChange, StringComparison.Ordinal))
        {
            return Hash("internal_state_only");
        }

        var providerConfigurations = await dbContext.FinanceIntegrationProviderConfigurations.AsNoTracking()
            .OrderBy(item => item.ProviderKey)
            .Select(item => new
            {
                item.ProviderKey, item.Enabled, item.ScopesJson, item.ValidationStatus,
                item.LastValidatedUtc, item.Version, item.UpdatedUtc
            })
            .ToArrayAsync(cancellationToken);
        var connections = await dbContext.FortnoxConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id, item.Status, item.GrantedScopes, item.AccessTokenExpiresUtc,
                item.RefreshTokenExpiresUtc, item.LastValidatedUtc, item.UpdatedUtc
            })
            .ToArrayAsync(cancellationToken);

        return Hash(Canonicalize(JsonSerializer.SerializeToNode(new
        {
            providerConfigurations,
            connections
        })));
    }

    public static string ComputePayloadHash(IReadOnlyDictionary<string, JsonNode?> payload) =>
        Hash(CanonicalizeDictionary(payload));

    public static string ComputeBusinessIdempotencyKey(ToolExecutionAttempt attempt) =>
        ReadString(attempt.RequestPayload, "idempotencyKey") ??
        $"finance-agent:{attempt.CompanyId:N}:{attempt.Id:N}";

    public static string ComputeContinuationKey(
        ToolExecutionAttempt attempt,
        string payloadHash,
        string policyVersion) =>
        Hash($"{attempt.CompanyId:N}|{attempt.Id:N}|{payloadHash}|{policyVersion}");

    public static string ComputeThresholdEvaluationHash(ToolExecutionDecisionDto decision) =>
        Hash(Canonicalize(decision.ThresholdEvaluations is null
            ? new JsonArray()
            : JsonSerializer.SerializeToNode(decision.ThresholdEvaluations)));

    public static string ComputeTargetSnapshotHash(JsonArray snapshot) => Hash(Canonicalize(snapshot));

    public static async Task<FinanceToolRiskEvaluationContext> BuildRiskContextAsync(
        VirtualCompanyDbContext dbContext,
        ToolExecutionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (!attempt.ToolName.Equals("categorize_transaction", StringComparison.OrdinalIgnoreCase))
        {
            return new FinanceToolRiskEvaluationContext(
                null, 1, null, null, true, "approval_continuation_registry");
        }

        var transactionId = ReadGuid(attempt.RequestPayload, "transactionId");
        var requestedCategory = ReadString(attempt.RequestPayload, "category");
        if (!transactionId.HasValue)
        {
            return new FinanceToolRiskEvaluationContext(
                null, 0, null, requestedCategory, false, "approval_continuation_target_missing");
        }

        var transaction = await dbContext.FinanceTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CompanyId == attempt.CompanyId && item.Id == transactionId.Value)
            .Select(item => new { item.Amount, item.TransactionType })
            .SingleOrDefaultAsync(cancellationToken);
        return transaction is null
            ? new FinanceToolRiskEvaluationContext(
                null, 0, null, requestedCategory, false, "approval_continuation_target_not_found")
            : new FinanceToolRiskEvaluationContext(
                Math.Abs(transaction.Amount), 1, transaction.TransactionType,
                requestedCategory is null ? null : FinanceTransactionCategories.Normalize(requestedCategory),
                true, "approval_continuation_finance_transactions");
    }

    private static JsonObject CreateTarget(string entityType, Guid? id, object? snapshot)
    {
        var snapshotNode = snapshot is null ? null : JsonSerializer.SerializeToNode(snapshot);
        return new JsonObject
        {
            ["entityType"] = entityType,
            ["entityId"] = id,
            ["exists"] = snapshot is not null,
            ["version"] = snapshotNode is null ? "missing" : Hash(Canonicalize(snapshotNode))
        };
    }

    private static string? ReadDecisionMetadata(ToolExecutionDecisionDto decision, string key) =>
        decision.Metadata.TryGetValue(key, out var node) ? NodeString(node) : null;

    internal static string? ReadBindingString(JsonObject binding, string key) =>
        binding.TryGetPropertyValue(key, out var node) ? NodeString(node) : null;

    internal static Guid? ReadBindingGuid(JsonObject binding, string key) =>
        binding.TryGetPropertyValue(key, out var node) && node is JsonValue value &&
        (value.TryGetValue<Guid>(out var guid) ||
         value.TryGetValue<string>(out var text) && Guid.TryParse(text, out guid)) && guid != Guid.Empty
            ? guid
            : null;

    internal static DateTime? ReadBindingUtc(JsonObject binding, string key)
    {
        if (!binding.TryGetPropertyValue(key, out var node) || node is not JsonValue value) return null;
        if (value.TryGetValue<DateTime>(out var dateTime)) return dateTime.ToUniversalTime();
        return value.TryGetValue<string>(out var text) &&
               DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out dateTime)
            ? dateTime.ToUniversalTime()
            : null;
    }

    internal static bool? ReadBindingBoolean(JsonObject binding, string key) =>
        binding.TryGetPropertyValue(key, out var node) && node is JsonValue value &&
        value.TryGetValue<bool>(out var result)
            ? result
            : null;

    private static Guid? ReadGuid(IReadOnlyDictionary<string, JsonNode?> source, string key)
    {
        if (!source.TryGetValue(key, out var node) || node is not JsonValue value) return null;
        if (value.TryGetValue<Guid>(out var guid) && guid != Guid.Empty) return guid;
        return value.TryGetValue<string>(out var text) && Guid.TryParse(text, out guid) && guid != Guid.Empty
            ? guid
            : null;
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonNode?> source, string key) =>
        source.TryGetValue(key, out var node) ? NodeString(node) : null;

    private static string? NodeString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static string CanonicalizeDictionary(IReadOnlyDictionary<string, JsonNode?> source)
    {
        var root = new JsonObject();
        foreach (var item in source.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            root[item.Key] = item.Value?.DeepClone();
        }
        return Canonicalize(root);
    }

    private static string Canonicalize(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject value => "{" + string.Join(",", value.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => JsonSerializer.Serialize(item.Key) + ":" + Canonicalize(item.Value))) + "}",
        JsonArray value => "[" + string.Join(",", value.Select(Canonicalize)) + "]",
        _ => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
