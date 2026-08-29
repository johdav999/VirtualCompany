using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Finance;

public sealed record AdvancedReconciliationGraphNode(
    Guid Id,
    string NodeType,
    decimal Amount,
    string Currency,
    string? Direction = null,
    string? AdjustmentKind = null,
    decimal DebitAmount = 0m,
    decimal CreditAmount = 0m);

public sealed record AdvancedReconciliationGraphEdge(
    Guid Id,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string EdgeType,
    decimal Amount);

public sealed record AdvancedReconciliationGraphEvaluation(
    bool IsBalanced,
    decimal ExpectedBankTotal,
    decimal AllocatedAmount,
    decimal FeeAmount,
    decimal RoundingAmount,
    decimal ResidualAmount,
    string Cardinality,
    IReadOnlyList<string> Errors);

public static class AdvancedReconciliationGraphPolicy
{
    public static AdvancedReconciliationGraphEvaluation Evaluate(
        IReadOnlyCollection<AdvancedReconciliationGraphNode> nodes,
        IReadOnlyCollection<AdvancedReconciliationGraphEdge> edges,
        decimal tolerance = 0.01m)
    {
        nodes ??= [];
        edges ??= [];
        var errors = new List<string>();
        var normalizedTolerance = Math.Max(0m, Round(tolerance));
        var nodeById = new Dictionary<Guid, AdvancedReconciliationGraphNode>();

        foreach (var node in nodes)
        {
            if (node.Id == Guid.Empty || !nodeById.TryAdd(node.Id, node))
                errors.Add("Every reconciliation node must have a unique non-empty identity.");
            if (!AdvancedReconciliationNodeTypes.IsSupported(node.NodeType))
                errors.Add($"Node '{node.Id}' has an unsupported type.");
            if (node.Amount < 0m || node.DebitAmount < 0m || node.CreditAmount < 0m)
                errors.Add($"Node '{node.Id}' contains a negative amount.");
            if (string.IsNullOrWhiteSpace(node.Currency) || node.Currency.Trim().Length != 3)
                errors.Add($"Node '{node.Id}' must use a three-letter currency.");
            if (node.DebitAmount > 0m && node.CreditAmount > 0m)
                errors.Add($"Node '{node.Id}' cannot contain both a debit and a credit.");
        }

        var currency = nodes.Select(x => x.Currency.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        if (currency.Length > 1)
            errors.Add("All nodes in a reconciliation group must use the same currency.");

        var edgeIds = new HashSet<Guid>();
        foreach (var edge in edges)
        {
            if (edge.Id == Guid.Empty || !edgeIds.Add(edge.Id))
                errors.Add("Every reconciliation edge must have a unique non-empty identity.");
            if (!AdvancedReconciliationEdgeTypes.IsSupported(edge.EdgeType))
                errors.Add($"Edge '{edge.Id}' has an unsupported type.");
            if (!nodeById.ContainsKey(edge.SourceNodeId) || !nodeById.ContainsKey(edge.TargetNodeId))
                errors.Add($"Edge '{edge.Id}' references a node outside the group.");
            if (edge.Amount <= 0m && AdvancedReconciliationEdgeTypes.Normalize(edge.EdgeType) != AdvancedReconciliationEdgeTypes.BankAdjustment)
                errors.Add($"Edge '{edge.Id}' must have a positive amount.");
        }

        var bankNodes = nodes.Where(x => AdvancedReconciliationNodeTypes.Normalize(x.NodeType) == AdvancedReconciliationNodeTypes.BankTransaction).ToArray();
        var paymentNodes = nodes.Where(x => AdvancedReconciliationNodeTypes.Normalize(x.NodeType) == AdvancedReconciliationNodeTypes.Payment).ToArray();
        var documentNodes = nodes.Where(x => AdvancedReconciliationNodeTypes.Normalize(x.NodeType) is AdvancedReconciliationNodeTypes.Invoice or AdvancedReconciliationNodeTypes.Bill).ToArray();
        if (bankNodes.Length == 0)
            errors.Add("A reconciliation group must contain at least one bank transaction.");

        foreach (var edge in edges.Where(x => nodeById.ContainsKey(x.SourceNodeId) && nodeById.ContainsKey(x.TargetNodeId)))
        {
            var sourceType = AdvancedReconciliationNodeTypes.Normalize(nodeById[edge.SourceNodeId].NodeType);
            var targetType = AdvancedReconciliationNodeTypes.Normalize(nodeById[edge.TargetNodeId].NodeType);
            var edgeType = AdvancedReconciliationEdgeTypes.Normalize(edge.EdgeType);
            var isValid = edgeType switch
            {
                AdvancedReconciliationEdgeTypes.BankPayment => sourceType == AdvancedReconciliationNodeTypes.BankTransaction && targetType == AdvancedReconciliationNodeTypes.Payment,
                AdvancedReconciliationEdgeTypes.PaymentDocument => sourceType == AdvancedReconciliationNodeTypes.Payment && targetType is AdvancedReconciliationNodeTypes.Invoice or AdvancedReconciliationNodeTypes.Bill,
                AdvancedReconciliationEdgeTypes.BankAdjustment => sourceType == AdvancedReconciliationNodeTypes.BankTransaction && targetType is AdvancedReconciliationNodeTypes.Adjustment or AdvancedReconciliationNodeTypes.Residual,
                _ => false
            };
            if (!isValid)
                errors.Add($"Edge '{edge.Id}' does not connect compatible reconciliation nodes.");
        }

        foreach (var bank in bankNodes)
        {
            if (!AdvancedReconciliationDirections.IsSupported(bank.Direction))
            {
                errors.Add($"Bank node '{bank.Id}' must specify incoming or outgoing direction.");
                continue;
            }

            var allocated = edges
                .Where(x => x.SourceNodeId == bank.Id && AdvancedReconciliationEdgeTypes.Normalize(x.EdgeType) == AdvancedReconciliationEdgeTypes.BankPayment)
                .Sum(x => Round(x.Amount));
            var adjustments = edges
                .Where(x => x.SourceNodeId == bank.Id && AdvancedReconciliationEdgeTypes.Normalize(x.EdgeType) == AdvancedReconciliationEdgeTypes.BankAdjustment)
                .Select(x => nodeById.GetValueOrDefault(x.TargetNodeId))
                .Where(x => x is not null)
                .Cast<AdvancedReconciliationGraphNode>()
                .ToArray();
            var debits = adjustments.Sum(x => Round(x.DebitAmount));
            var credits = adjustments.Sum(x => Round(x.CreditAmount));
            var debitControl = AdvancedReconciliationDirections.Normalize(bank.Direction) == AdvancedReconciliationDirections.Incoming
                ? Round(bank.Amount) + debits
                : allocated + debits;
            var creditControl = AdvancedReconciliationDirections.Normalize(bank.Direction) == AdvancedReconciliationDirections.Incoming
                ? allocated + credits
                : Round(bank.Amount) + credits;
            if (Math.Abs(Round(debitControl - creditControl)) > normalizedTolerance)
                errors.Add($"Bank node '{bank.Id}' does not balance to its payment and adjustment evidence.");
        }

        var expected = Round(bankNodes.Sum(x => x.Amount));
        var allocatedTotal = Round(edges.Where(x => AdvancedReconciliationEdgeTypes.Normalize(x.EdgeType) == AdvancedReconciliationEdgeTypes.BankPayment).Sum(x => x.Amount));
        var adjustmentNodes = nodes.Where(x => AdvancedReconciliationNodeTypes.Normalize(x.NodeType) is AdvancedReconciliationNodeTypes.Adjustment or AdvancedReconciliationNodeTypes.Residual).ToArray();
        var fee = Round(adjustmentNodes.Where(x => string.Equals(x.AdjustmentKind, "bank_fee", StringComparison.OrdinalIgnoreCase)).Sum(LineAmount));
        var rounding = Round(adjustmentNodes.Where(x => string.Equals(x.AdjustmentKind, "rounding_difference", StringComparison.OrdinalIgnoreCase)).Sum(LineAmount));
        var residual = Round(adjustmentNodes.Where(x => AdvancedReconciliationNodeTypes.Normalize(x.NodeType) == AdvancedReconciliationNodeTypes.Residual || string.Equals(x.AdjustmentKind, "suspense", StringComparison.OrdinalIgnoreCase)).Sum(LineAmount));

        var cardinality = bankNodes.Length switch
        {
            1 when documentNodes.Length > 1 => "one_to_many",
            > 1 when documentNodes.Length == 1 => "many_to_one",
            > 1 when documentNodes.Length > 1 => "many_to_many",
            _ when paymentNodes.Length > 1 => "split",
            _ => "partial_or_exact"
        };

        return new(errors.Count == 0, expected, allocatedTotal, fee, rounding, residual, cardinality, errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static decimal LineAmount(AdvancedReconciliationGraphNode node) => Round(Math.Max(node.DebitAmount, node.CreditAmount));
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

