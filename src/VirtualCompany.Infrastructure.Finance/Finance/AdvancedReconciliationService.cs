using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class AdvancedReconciliationService : IAdvancedReconciliationReadService, IAdvancedReconciliationCommandService
{
    private const int MaxLimit = 500;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly CompanyBankTransactionService _bankTransactions;
    private readonly FinancePaymentAllocationService _paymentAllocations;
    private readonly IAccountingPostingService _postingService;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ICompanyContextAccessor? _companyContext;
    private readonly TimeProvider _timeProvider;

    public AdvancedReconciliationService(
        VirtualCompanyDbContext dbContext,
        CompanyBankTransactionService bankTransactions,
        FinancePaymentAllocationService paymentAllocations,
        IAccountingPostingService postingService,
        IAuditEventWriter auditWriter,
        ICompanyContextAccessor? companyContext = null,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _bankTransactions = bankTransactions;
        _paymentAllocations = paymentAllocations;
        _postingService = postingService;
        _auditWriter = auditWriter;
        _companyContext = companyContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AdvancedReconciliationWorkspaceDto> ListAsync(ListAdvancedReconciliationGroupsQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var limit = query.Limit <= 0 ? 100 : Math.Min(query.Limit, MaxLimit);
        var rows = _dbContext.AdvancedReconciliationGroups.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = AdvancedReconciliationGroupStatuses.Normalize(query.Status);
            if (!AdvancedReconciliationGroupStatuses.IsSupported(status)) throw Validation(nameof(query.Status), "Unsupported reconciliation queue status.");
            rows = rows.Where(x => x.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(x => x.Reference.Contains(search) || x.Counterparty.Contains(search));
        }
        if (query.MaximumConfidence.HasValue)
        {
            var maximum = Score(query.MaximumConfidence.Value, nameof(query.MaximumConfidence));
            rows = rows.Where(x => x.ConfidenceScore <= maximum);
        }

        var groups = await rows.Include(x => x.Nodes).Include(x => x.Edges)
            .OrderBy(x => x.Status == AdvancedReconciliationGroupStatuses.Proposed ? 0 : 1)
            .ThenBy(x => (double)x.ConfidenceScore).ThenByDescending(x => x.UpdatedUtc).Take(limit).ToListAsync(cancellationToken);
        var currentRule = await CurrentRuleAsync(query.CompanyId, cancellationToken);
        var staleGroupIds = await FindStaleGroupIdsAsync(groups, currentRule?.Version, cancellationToken);
        var summaries = groups.Select(x => MapSummary(x, currentRule?.Version) with
        {
            IsStale = staleGroupIds.Contains(x.Id)
        }).ToArray();
        var metrics = new AdvancedReconciliationQualityMetricsDto(
            summaries.Count(x => x.Status == AdvancedReconciliationGroupStatuses.Proposed),
            currentRule is null ? 0 : summaries.Count(x => x.ConfidenceScore < currentRule.LowConfidenceThreshold),
            summaries.Count(x => x.Status == AdvancedReconciliationGroupStatuses.Conflict),
            summaries.Count(x => x.IsStale),
            summaries.Length == 0 ? 0m : decimal.Round(summaries.Average(x => x.ConfidenceScore), 4, MidpointRounding.AwayFromZero),
            summaries.Where(x => x.Status == AdvancedReconciliationGroupStatuses.Accepted).Sum(x => x.ExpectedBankTotal));
        return new(summaries, metrics, currentRule is null ? null : MapRule(currentRule));
    }

    public async Task<AdvancedReconciliationGroupDetailDto?> GetAsync(GetAdvancedReconciliationGroupQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        if (query.GroupId == Guid.Empty) throw new ArgumentException("Group id is required.", nameof(query));
        var group = await LoadGroupAsync(query.CompanyId, query.GroupId, false, cancellationToken);
        return group is null ? null : await MapDetailAsync(group, cancellationToken);
    }

    public async Task<IReadOnlyList<AdvancedReconciliationRuleDto>> ListRulesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        return await _dbContext.AdvancedReconciliationRules.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.Version).Select(x => MapRule(x)).ToListAsync(cancellationToken);
    }

    public Task<AdvancedReconciliationRuleDto> CreateRuleVersionAsync(CreateAdvancedReconciliationRuleCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureTenant(command.CompanyId); await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
            ValidatePattern(command.ReferenceNormalizationPattern, nameof(command.ReferenceNormalizationPattern));
            ValidatePattern(command.CounterpartyNormalizationPattern, nameof(command.CounterpartyNormalizationPattern));
            ValidatePattern(command.ProviderPattern, nameof(command.ProviderPattern));
            var now = UtcNow();
            var current = await _dbContext.AdvancedReconciliationRules.IgnoreQueryFilters()
                .Where(x => x.CompanyId == command.CompanyId && x.SupersededUtc == null)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            var nextVersion = (await _dbContext.AdvancedReconciliationRules.IgnoreQueryFilters()
                .Where(x => x.CompanyId == command.CompanyId).MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
            current?.Supersede(now);
            var rule = new AdvancedReconciliationRule(Guid.NewGuid(), command.CompanyId, nextVersion, command.Name,
                command.ReferenceNormalizationPattern, command.CounterpartyNormalizationPattern, command.ProviderPattern,
                command.AmountTolerance, command.TimingWindowDays, command.RecommendationThreshold,
                command.LowConfidenceThreshold, command.MaterialityThreshold, command.ActorUserId, now);
            _dbContext.AdvancedReconciliationRules.Add(rule);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MapRule(rule);
        }, cancellationToken);

    public Task<AdvancedReconciliationGroupDetailDto> CreateGroupAsync(CreateAdvancedReconciliationGroupCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureTenant(command.CompanyId); await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
            if (command.Nodes is not { Count: > 0 }) throw Validation(nameof(command.Nodes), "At least one reconciliation node is required.");
            if (command.Edges is null) throw Validation(nameof(command.Edges), "Reconciliation edges are required.");
            if (command.Nodes.Select(x => x.NodeId).Any(x => x == Guid.Empty) || command.Nodes.GroupBy(x => x.NodeId).Any(x => x.Count() > 1))
                throw Validation(nameof(command.Nodes), "Every node must have a unique identity.");
            if (command.Edges.Select(x => x.EdgeId).Any(x => x == Guid.Empty) || command.Edges.GroupBy(x => x.EdgeId).Any(x => x.Count() > 1))
                throw Validation(nameof(command.Edges), "Every edge must have a unique identity.");

            var rule = await ResolveRuleAsync(command.CompanyId, command.RuleVersion, cancellationToken)
                ?? await CreateDefaultRuleAsync(command.CompanyId, command.ActorUserId, cancellationToken);
            if (command.CorrectionOfGroupId.HasValue)
            {
                var correctedExists = await _dbContext.AdvancedReconciliationGroups.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.CompanyId == command.CompanyId && x.Id == command.CorrectionOfGroupId &&
                        (x.Status == AdvancedReconciliationGroupStatuses.Accepted || x.Status == AdvancedReconciliationGroupStatuses.Reversed), cancellationToken);
                if (!correctedExists) throw Validation(nameof(command.CorrectionOfGroupId), "A correction must link to an accepted or reversed reconciliation group.");
            }

            var hydrated = await HydrateNodesAsync(command, cancellationToken);
            var graphNodes = hydrated.Nodes.Select(ToGraphNode).ToArray();
            var graphEdges = command.Edges.Select(x => new AdvancedReconciliationGraphEdge(x.EdgeId, x.SourceNodeId, x.TargetNodeId, x.EdgeType, x.Amount)).ToArray();
            var evaluation = AdvancedReconciliationGraphPolicy.Evaluate(graphNodes, graphEdges, rule.AmountTolerance);
            if (!evaluation.IsBalanced) throw Validation(nameof(command.Nodes), $"{AdvancedReconciliationReasonCodes.UnbalancedGroup}: {string.Join(" ", evaluation.Errors)}");

            var contributions = EvaluateReasons(rule, hydrated, evaluation);
            var confidence = Score(contributions.Sum(x => x.Contribution), "ConfidenceScore");
            if (confidence < rule.RecommendationThreshold)
                throw Validation(nameof(command.Nodes), "The deterministic evidence does not meet the configured recommendation threshold.");
            var requiresApproval = evaluation.ExpectedBankTotal >= rule.MaterialityThreshold || confidence < rule.LowConfidenceThreshold;
            var now = UtcNow();
            var group = new AdvancedReconciliationGroup(Guid.NewGuid(), command.CompanyId, rule.Id, rule.Version,
                command.CorrectionOfGroupId, command.Reference, command.Counterparty, command.Currency,
                evaluation.ExpectedBankTotal, confidence, requiresApproval, command.ActorUserId, now);
            _dbContext.AdvancedReconciliationGroups.Add(group);
            foreach (var node in hydrated.Nodes)
                _dbContext.AdvancedReconciliationNodes.Add(new AdvancedReconciliationNode(node.Id, command.CompanyId, group.Id,
                    node.NodeType, node.RecordId, node.Label, node.Reference, node.Currency, node.Amount, node.Direction,
                    node.AdjustmentKind, node.DebitAmount, node.CreditAmount, node.ExpectedRecordVersion, node.Sequence));
            foreach (var edge in command.Edges)
                _dbContext.AdvancedReconciliationEdges.Add(new AdvancedReconciliationEdge(edge.EdgeId, command.CompanyId,
                    group.Id, edge.SourceNodeId, edge.TargetNodeId, edge.EdgeType, edge.Amount));
            foreach (var reason in contributions)
                _dbContext.AdvancedReconciliationReasonContributions.Add(new AdvancedReconciliationReasonContribution(
                    Guid.NewGuid(), command.CompanyId, group.Id, reason.FeatureKey, reason.Contribution, reason.Explanation, reason.Evidence));
            _dbContext.AdvancedReconciliationEvents.Add(new AdvancedReconciliationEvent(Guid.NewGuid(), command.CompanyId,
                group.Id, "proposed", command.ActorUserId, "{}", Snapshot(group, evaluation), now));
            await _dbContext.SaveChangesAsync(cancellationToken);
            return (await GetAsync(new(command.CompanyId, group.Id), cancellationToken))!;
        }, cancellationToken);

    public Task<AdvancedReconciliationGroupDetailDto> AcceptAsync(AcceptAdvancedReconciliationGroupCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureTenant(command.CompanyId); await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
            var group = await LoadGroupAsync(command.CompanyId, command.GroupId, true, cancellationToken)
                ?? throw new KeyNotFoundException("The reconciliation group was not found.");
            if (group.Version != command.ExpectedVersion) throw Validation(nameof(command.ExpectedVersion), AdvancedReconciliationReasonCodes.GroupVersionConflict);
            var currentRule = await CurrentRuleAsync(command.CompanyId, cancellationToken);
            if (currentRule is null || currentRule.Version != command.ExpectedRuleVersion || group.RuleVersion != command.ExpectedRuleVersion)
                throw Validation(nameof(command.ExpectedRuleVersion), AdvancedReconciliationReasonCodes.RuleVersionConflict);
            await EnsureRecordVersionsAsync(group, cancellationToken);
            var evaluation = Evaluate(group, currentRule.AmountTolerance);
            if (!evaluation.IsBalanced) throw Validation(nameof(command.GroupId), AdvancedReconciliationReasonCodes.UnbalancedGroup);
            if (string.IsNullOrWhiteSpace(command.DecisionReason)) throw Validation(nameof(command.DecisionReason), "An authorized review reason is required.");

            var before = Snapshot(group, evaluation);
            var nodes = group.Nodes.ToDictionary(x => x.Id);
            foreach (var edge in group.Edges.Where(x => x.EdgeType == AdvancedReconciliationEdgeTypes.PaymentDocument).OrderBy(x => x.Id))
            {
                var payment = nodes[edge.SourceNodeId]; var document = nodes[edge.TargetNodeId];
                await _paymentAllocations.CreateWithinAmbientTransactionAsync(new CreateFinancePaymentAllocationCommand(command.CompanyId,
                    new CreateFinancePaymentAllocationDto(payment.RecordId!.Value,
                        document.NodeType == AdvancedReconciliationNodeTypes.Invoice ? document.RecordId : null,
                        document.NodeType == AdvancedReconciliationNodeTypes.Bill ? document.RecordId : null,
                        edge.Amount, group.Currency, $"advanced-reconciliation:{group.Id:N}:edge:{edge.Id:N}")), cancellationToken);
            }

            var bankNodes = group.Nodes.Where(x => x.NodeType == AdvancedReconciliationNodeTypes.BankTransaction).OrderBy(x => x.Sequence).ToArray();
            foreach (var bank in bankNodes)
            {
                var payments = group.Edges.Where(x => x.SourceNodeId == bank.Id && x.EdgeType == AdvancedReconciliationEdgeTypes.BankPayment)
                    .Select(x => new BankTransactionPaymentMatchDto(nodes[x.TargetNodeId].RecordId!.Value, x.Amount)).ToArray();
                var adjustments = group.Edges.Where(x => x.SourceNodeId == bank.Id && x.EdgeType == AdvancedReconciliationEdgeTypes.BankAdjustment)
                    .Select(x => nodes[x.TargetNodeId]).Select(x => new BankReconciliationAdjustmentDto(
                        x.AdjustmentKind ?? AccountingAccountRoleKeys.Suspense, x.DebitAmount, x.CreditAmount, x.Label)).ToArray();
                await _bankTransactions.ReconcileWithinAmbientTransactionAsync(new ReconcileBankTransactionCommand(command.CompanyId,
                    bank.RecordId!.Value, payments, command.ActorUserId, long.Parse(bank.ExpectedRecordVersion!, CultureInfo.InvariantCulture),
                    BankReconciliationHandlingModes.Payment, command.DecisionReason, Adjustments: adjustments,
                    IdempotencyKey: $"advanced-reconciliation:{command.CompanyId:N}:{group.Id:N}:{bank.Id:N}:{group.Version}",
                    CorrelationId: command.CorrelationId), cancellationToken);
            }

            var ledgerIds = await _dbContext.BankTransactionCashLedgerLinks.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && bankNodes.Select(node => node.RecordId!.Value).Contains(x.BankTransactionId))
                .Select(x => x.LedgerEntryId).Distinct().ToListAsync(cancellationToken);
            var now = UtcNow(); group.Accept(command.ExpectedVersion, command.ActorUserId, command.DecisionReason, now);
            var evidence = JsonSerializer.Serialize(new { ledgerEntryIds = ledgerIds, nodeIds = group.Nodes.Select(x => x.Id), edgeIds = group.Edges.Select(x => x.Id) });
            _dbContext.AdvancedReconciliationResults.Add(new AdvancedReconciliationResult(Guid.NewGuid(), command.CompanyId,
                group.Id, null, AdvancedReconciliationResultOutcomes.Accepted, group.Version, group.RuleVersion,
                evaluation.ExpectedBankTotal, evaluation.AllocatedAmount, evaluation.FeeAmount, evaluation.RoundingAmount,
                evaluation.ResidualAmount, evidence, command.ActorUserId, now));
            _dbContext.AdvancedReconciliationEvents.Add(new AdvancedReconciliationEvent(Guid.NewGuid(), command.CompanyId,
                group.Id, "accepted", command.ActorUserId, before, Snapshot(group, evaluation), now));
            await WriteAuditAsync(group, command.ActorUserId, "accepted", command.DecisionReason, command.CorrelationId, now, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return (await GetAsync(new(command.CompanyId, group.Id), cancellationToken))!;
        }, cancellationToken);

    public Task<AdvancedReconciliationGroupDetailDto> RejectAsync(RejectAdvancedReconciliationGroupCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureTenant(command.CompanyId); await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
            var group = await LoadGroupAsync(command.CompanyId, command.GroupId, true, cancellationToken)
                ?? throw new KeyNotFoundException("The reconciliation group was not found.");
            var evaluation = Evaluate(group, 0.01m); var before = Snapshot(group, evaluation); var now = UtcNow();
            group.Reject(command.ExpectedVersion, command.ActorUserId, command.DecisionReason, now);
            _dbContext.AdvancedReconciliationEvents.Add(new AdvancedReconciliationEvent(Guid.NewGuid(), command.CompanyId,
                group.Id, "rejected", command.ActorUserId, before, Snapshot(group, evaluation), now));
            await WriteAuditAsync(group, command.ActorUserId, "rejected", command.DecisionReason, command.CorrelationId, now, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return (await GetAsync(new(command.CompanyId, group.Id), cancellationToken))!;
        }, cancellationToken);

    public Task<AdvancedReconciliationGroupDetailDto> ReverseAsync(ReverseAdvancedReconciliationGroupCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureTenant(command.CompanyId); await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
            var group = await LoadGroupAsync(command.CompanyId, command.GroupId, true, cancellationToken)
                ?? throw new KeyNotFoundException("The reconciliation group was not found.");
            if (group.Version != command.ExpectedVersion) throw Validation(nameof(command.ExpectedVersion), AdvancedReconciliationReasonCodes.GroupVersionConflict);
            var acceptedResult = group.Results.Where(x => x.Outcome == AdvancedReconciliationResultOutcomes.Accepted)
                .OrderByDescending(x => x.CreatedUtc).FirstOrDefault() ?? throw Validation(nameof(command.GroupId), "The group has no accepted result to reverse.");
            var ledgerIds = ReadLedgerIds(acceptedResult.EvidenceJson);
            if (ledgerIds.Count == 0) throw Validation(nameof(command.GroupId), "The accepted result has no governed journal to reverse.");
            var reversalLedgerIds = new List<Guid>();
            foreach (var ledgerId in ledgerIds)
            {
                var reversed = await _postingService.ReverseAsync(new ReverseAccountingEntryCommand(command.CompanyId,
                    ledgerId, command.FiscalPeriodId, "CR", command.PostingDate, command.Reason,
                    $"advanced-reconciliation:{group.Version}:reversal",
                    $"advanced-reconciliation:{group.Id:N}:reverse:{ledgerId:N}:{group.Version}", command.ActorUserId,
                    CorrelationId: command.CorrelationId), cancellationToken);
                reversalLedgerIds.Add(reversed.Journal.Id);
            }
            var currentRule = await ResolveRuleAsync(command.CompanyId, group.RuleVersion, cancellationToken)
                ?? throw Validation(nameof(command.GroupId), "The historical rule version is unavailable.");
            var evaluation = Evaluate(group, currentRule.AmountTolerance); var before = Snapshot(group, evaluation); var now = UtcNow();
            group.Reverse(command.ExpectedVersion, command.ActorUserId, command.Reason, now);
            _dbContext.AdvancedReconciliationResults.Add(new AdvancedReconciliationResult(Guid.NewGuid(), command.CompanyId,
                group.Id, acceptedResult.Id, AdvancedReconciliationResultOutcomes.Reversal, group.Version, group.RuleVersion,
                evaluation.ExpectedBankTotal, evaluation.AllocatedAmount, evaluation.FeeAmount, evaluation.RoundingAmount,
                evaluation.ResidualAmount, JsonSerializer.Serialize(new { ledgerEntryIds = reversalLedgerIds, reverses = acceptedResult.Id }), command.ActorUserId, now));
            _dbContext.AdvancedReconciliationEvents.Add(new AdvancedReconciliationEvent(Guid.NewGuid(), command.CompanyId,
                group.Id, "reversed", command.ActorUserId, before, Snapshot(group, evaluation), now));
            await WriteAuditAsync(group, command.ActorUserId, "reversed", command.Reason, command.CorrelationId, now, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return (await GetAsync(new(command.CompanyId, group.Id), cancellationToken))!;
        }, cancellationToken);

    private async Task<HydratedGraph> HydrateNodesAsync(CreateAdvancedReconciliationGroupCommand command, CancellationToken cancellationToken)
    {
        var bankIds = Ids(command.Nodes, AdvancedReconciliationNodeTypes.BankTransaction);
        var paymentIds = Ids(command.Nodes, AdvancedReconciliationNodeTypes.Payment);
        var invoiceIds = Ids(command.Nodes, AdvancedReconciliationNodeTypes.Invoice);
        var billIds = Ids(command.Nodes, AdvancedReconciliationNodeTypes.Bill);
        var banks = await _dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == command.CompanyId && bankIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var payments = await _dbContext.Payments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == command.CompanyId && paymentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var invoices = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == command.CompanyId && invoiceIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var bills = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == command.CompanyId && billIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (banks.Count != bankIds.Length || payments.Count != paymentIds.Length || invoices.Count != invoiceIds.Length || bills.Count != billIds.Length)
            throw new KeyNotFoundException("One or more reconciliation records were not found in the active company.");

        var nodes = new List<HydratedNode>(command.Nodes.Count); var dates = new List<DateTime>(); var providers = new List<string>();
        foreach (var input in command.Nodes.OrderBy(x => x.Sequence))
        {
            var type = AdvancedReconciliationNodeTypes.Normalize(input.NodeType);
            if (!AdvancedReconciliationNodeTypes.IsSupported(type)) throw Validation(nameof(command.Nodes), $"Unsupported node type '{input.NodeType}'.");
            HydratedNode node;
            if (type == AdvancedReconciliationNodeTypes.BankTransaction)
            {
                var value = banks[input.RecordId!.Value]; dates.Add(value.BookingDate); providers.Add(value.ImportSource ?? "manual");
                node = new(input.NodeId, type, value.Id, value.Counterparty, value.ReferenceText, value.Currency,
                    value.AbsoluteAmount, value.Amount >= 0m ? AdvancedReconciliationDirections.Incoming : AdvancedReconciliationDirections.Outgoing,
                    null, 0m, 0m, value.SourceVersion.ToString(CultureInfo.InvariantCulture), input.Sequence);
            }
            else if (type == AdvancedReconciliationNodeTypes.Payment)
            {
                var value = payments[input.RecordId!.Value]; dates.Add(value.PaymentDate);
                node = new(input.NodeId, type, value.Id, "Payment", value.CounterpartyReference, value.Currency,
                    value.Amount, null, null, 0m, 0m, Ticks(value.UpdatedUtc), input.Sequence);
            }
            else if (type == AdvancedReconciliationNodeTypes.Invoice)
            {
                var value = invoices[input.RecordId!.Value]; dates.Add(value.DueUtc);
                node = new(input.NodeId, type, value.Id, $"Invoice {value.InvoiceNumber}", value.InvoiceNumber, value.Currency,
                    value.Amount, null, null, 0m, 0m, Ticks(value.UpdatedUtc), input.Sequence);
            }
            else if (type == AdvancedReconciliationNodeTypes.Bill)
            {
                var value = bills[input.RecordId!.Value]; dates.Add(value.DueUtc);
                node = new(input.NodeId, type, value.Id, $"Bill {value.BillNumber}", value.BillNumber, value.Currency,
                    value.Amount, null, null, 0m, 0m, Ticks(value.UpdatedUtc), input.Sequence);
            }
            else
            {
                var kind = type == AdvancedReconciliationNodeTypes.Residual ? AccountingAccountRoleKeys.Suspense : input.AdjustmentKind;
                if (string.IsNullOrWhiteSpace(kind) || !AccountingAccountRoleKeys.BankAdjustmentRoles.Contains(kind))
                    throw Validation(nameof(command.Nodes), "Adjustment and residual nodes require a supported accounting role.");
                if ((input.DebitAmount > 0m) == (input.CreditAmount > 0m))
                    throw Validation(nameof(command.Nodes), "Every adjustment or residual node requires exactly one positive debit or credit.");
                node = new(input.NodeId, type, null, input.Label ?? FriendlyKind(kind), input.Reference ?? command.Reference,
                    command.Currency, Math.Max(input.DebitAmount, input.CreditAmount), null, kind,
                    input.DebitAmount, input.CreditAmount, null, input.Sequence);
            }
            if (!string.Equals(node.Currency, command.Currency, StringComparison.OrdinalIgnoreCase))
                throw Validation(nameof(command.Currency), "Every reconciliation record must use the group currency.");
            nodes.Add(node);
        }
        return new(nodes, dates, providers);
    }

    private static IReadOnlyList<Reason> EvaluateReasons(AdvancedReconciliationRule rule, HydratedGraph graph, AdvancedReconciliationGraphEvaluation evaluation)
    {
        var references = graph.Nodes.Where(x => x.NodeType is AdvancedReconciliationNodeTypes.BankTransaction or AdvancedReconciliationNodeTypes.Payment or AdvancedReconciliationNodeTypes.Invoice or AdvancedReconciliationNodeTypes.Bill).Select(x => Normalize(x.Reference, rule.ReferenceNormalizationPattern)).Where(x => x.Length > 2).ToArray();
        var referenceMatch = references.SelectMany((value, index) => references.Skip(index + 1).Select(other => value.Contains(other, StringComparison.Ordinal) || other.Contains(value, StringComparison.Ordinal))).Any(x => x);
        var counterparties = graph.Nodes.Where(x => x.NodeType == AdvancedReconciliationNodeTypes.BankTransaction).Select(x => Normalize(x.Label, rule.CounterpartyNormalizationPattern)).Distinct(StringComparer.Ordinal).ToArray();
        var counterpartyMatch = counterparties.Length == 1;
        var timingMatch = graph.Dates.Count < 2 || (graph.Dates.Max() - graph.Dates.Min()).TotalDays <= rule.TimingWindowDays;
        var providerMatch = graph.Providers.Any(x => IsMatch(x, rule.ProviderPattern));
        return
        [
            new("normalized_reference", referenceMatch ? .25m : 0m, "Normalized references align across source records.", referenceMatch ? "A shared normalized reference was found." : "No shared normalized reference was found."),
            new("counterparty", counterpartyMatch ? .20m : 0m, "Counterparty evidence is consistent.", counterpartyMatch ? counterparties.SingleOrDefault() ?? "One counterparty" : "Multiple counterparties"),
            new("amount", evaluation.IsBalanced ? .30m : 0m, "Amounts net to the expected control total.", $"Expected {evaluation.ExpectedBankTotal:0.00}; allocated {evaluation.AllocatedAmount:0.00}."),
            new("timing", timingMatch ? .15m : 0m, "Dates fall within the configured timing window.", $"Configured window: {rule.TimingWindowDays} days."),
            new("provider_pattern", providerMatch ? .10m : 0m, "Provider/source pattern is recognized.", string.Join(", ", graph.Providers.Distinct(StringComparer.OrdinalIgnoreCase)))
        ];
    }

    private async Task EnsureRecordVersionsAsync(AdvancedReconciliationGroup group, CancellationToken cancellationToken)
    {
        foreach (var node in group.Nodes.Where(x => AdvancedReconciliationNodeTypes.IsRecordBacked(x.NodeType)))
        {
            string? actual = node.NodeType switch
            {
                AdvancedReconciliationNodeTypes.BankTransaction => (await _dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == group.CompanyId && x.Id == node.RecordId, cancellationToken))?.SourceVersion.ToString(CultureInfo.InvariantCulture),
                AdvancedReconciliationNodeTypes.Payment => Ticks((await _dbContext.Payments.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == group.CompanyId && x.Id == node.RecordId, cancellationToken))?.UpdatedUtc),
                AdvancedReconciliationNodeTypes.Invoice => Ticks((await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == group.CompanyId && x.Id == node.RecordId, cancellationToken))?.UpdatedUtc),
                AdvancedReconciliationNodeTypes.Bill => Ticks((await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == group.CompanyId && x.Id == node.RecordId, cancellationToken))?.UpdatedUtc),
                _ => null
            };
            if (actual is null || !string.Equals(actual, node.ExpectedRecordVersion, StringComparison.Ordinal))
                throw Validation(nameof(group.Id), $"{AdvancedReconciliationReasonCodes.RecordVersionConflict}: {node.Label}");
        }
    }

    private async Task<HashSet<Guid>> FindStaleGroupIdsAsync(IReadOnlyList<AdvancedReconciliationGroup> groups,
        int? currentRuleVersion, CancellationToken cancellationToken)
    {
        var stale = groups.Where(x => x.Status == AdvancedReconciliationGroupStatuses.Proposed &&
            currentRuleVersion != x.RuleVersion).Select(x => x.Id).ToHashSet();
        var proposed = groups.Where(x => x.Status == AdvancedReconciliationGroupStatuses.Proposed && !stale.Contains(x.Id)).ToArray();
        var nodes = proposed.SelectMany(x => x.Nodes).Where(x => AdvancedReconciliationNodeTypes.IsRecordBacked(x.NodeType)).ToArray();
        if (nodes.Length == 0) return stale;

        var bankIds = nodes.Where(x => x.NodeType == AdvancedReconciliationNodeTypes.BankTransaction).Select(x => x.RecordId!.Value).Distinct().ToArray();
        var paymentIds = nodes.Where(x => x.NodeType == AdvancedReconciliationNodeTypes.Payment).Select(x => x.RecordId!.Value).Distinct().ToArray();
        var invoiceIds = nodes.Where(x => x.NodeType == AdvancedReconciliationNodeTypes.Invoice).Select(x => x.RecordId!.Value).Distinct().ToArray();
        var billIds = nodes.Where(x => x.NodeType == AdvancedReconciliationNodeTypes.Bill).Select(x => x.RecordId!.Value).Distinct().ToArray();
        List<RecordVersionRow> bankRows = bankIds.Length == 0 ? [] : await _dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => proposed[0].CompanyId == x.CompanyId && bankIds.Contains(x.Id))
            .Select(x => new RecordVersionRow(x.Id, x.SourceVersion, null)).ToListAsync(cancellationToken);
        List<RecordVersionRow> paymentRows = paymentIds.Length == 0 ? [] : await _dbContext.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => proposed[0].CompanyId == x.CompanyId && paymentIds.Contains(x.Id))
            .Select(x => new RecordVersionRow(x.Id, null, x.UpdatedUtc)).ToListAsync(cancellationToken);
        List<RecordVersionRow> invoiceRows = invoiceIds.Length == 0 ? [] : await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(x => proposed[0].CompanyId == x.CompanyId && invoiceIds.Contains(x.Id))
            .Select(x => new RecordVersionRow(x.Id, null, x.UpdatedUtc)).ToListAsync(cancellationToken);
        List<RecordVersionRow> billRows = billIds.Length == 0 ? [] : await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
            .Where(x => proposed[0].CompanyId == x.CompanyId && billIds.Contains(x.Id))
            .Select(x => new RecordVersionRow(x.Id, null, x.UpdatedUtc)).ToListAsync(cancellationToken);
        var bankVersions = bankRows.ToDictionary(x => x.Id, x => x.SourceVersion!.Value.ToString(CultureInfo.InvariantCulture));
        var paymentVersions = paymentRows.ToDictionary(x => x.Id, x => Ticks(x.UpdatedUtc)!);
        var invoiceVersions = invoiceRows.ToDictionary(x => x.Id, x => Ticks(x.UpdatedUtc)!);
        var billVersions = billRows.ToDictionary(x => x.Id, x => Ticks(x.UpdatedUtc)!);

        foreach (var group in proposed)
        {
            foreach (var node in group.Nodes.Where(x => AdvancedReconciliationNodeTypes.IsRecordBacked(x.NodeType)))
            {
                var versions = node.NodeType switch
                {
                    AdvancedReconciliationNodeTypes.BankTransaction => bankVersions,
                    AdvancedReconciliationNodeTypes.Payment => paymentVersions,
                    AdvancedReconciliationNodeTypes.Invoice => invoiceVersions,
                    AdvancedReconciliationNodeTypes.Bill => billVersions,
                    _ => null
                };
                if (versions is null || !versions.TryGetValue(node.RecordId!.Value, out var actual) ||
                    !string.Equals(actual, node.ExpectedRecordVersion, StringComparison.Ordinal))
                {
                    stale.Add(group.Id);
                    break;
                }
            }
        }
        return stale;
    }

    private async Task<bool> IsStaleAsync(AdvancedReconciliationGroup group, int? currentRuleVersion, CancellationToken cancellationToken)
    {
        if (group.Status != AdvancedReconciliationGroupStatuses.Proposed) return false;
        if (currentRuleVersion != group.RuleVersion) return true;
        try { await EnsureRecordVersionsAsync(group, cancellationToken); return false; }
        catch (FinanceValidationException) { return true; }
    }

    private async Task<AdvancedReconciliationGroupDetailDto> MapDetailAsync(AdvancedReconciliationGroup group, CancellationToken cancellationToken)
    {
        var currentRule = await CurrentRuleAsync(group.CompanyId, cancellationToken);
        var historicalRule = await ResolveRuleAsync(group.CompanyId, group.RuleVersion, cancellationToken);
        var evaluation = Evaluate(group, historicalRule?.AmountTolerance ?? .01m);
        var stale = await IsStaleAsync(group, currentRule?.Version, cancellationToken);
        var summary = MapSummary(group, currentRule?.Version) with { IsStale = stale };
        var variance = evaluation.IsBalanced ? 0m : decimal.Round(group.ExpectedBankTotal - evaluation.AllocatedAmount, 2, MidpointRounding.AwayFromZero);
        var blocking = stale ? "The rule or one of the source records changed after this suggestion was created." : evaluation.IsBalanced ? null : string.Join(" ", evaluation.Errors);
        return new(summary, evaluation.AllocatedAmount, evaluation.FeeAmount, evaluation.RoundingAmount,
            evaluation.ResidualAmount, variance, evaluation.IsBalanced, blocking,
            group.Nodes.OrderBy(x => x.Sequence).Select(MapNode).ToArray(), group.Edges.OrderBy(x => x.Id).Select(MapEdge).ToArray(),
            group.ReasonContributions.OrderByDescending(x => x.Contribution).Select(x => new AdvancedReconciliationReasonContributionDto(x.FeatureKey, x.Contribution, x.Explanation, x.Evidence)).ToArray(),
            group.Results.OrderBy(x => x.CreatedUtc).Select(MapResult).ToArray(),
            group.Events.OrderByDescending(x => x.CreatedUtc).Select(x => new AdvancedReconciliationEventDto(x.Id, x.Action, x.ActorUserId, x.BeforeJson, x.AfterJson, x.CreatedUtc)).ToArray());
    }

    private async Task<AdvancedReconciliationGroup?> LoadGroupAsync(Guid companyId, Guid groupId, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<AdvancedReconciliationGroup> query = _dbContext.AdvancedReconciliationGroups.IgnoreQueryFilters();
        if (!tracking) query = query.AsNoTracking();
        return await query.Include(x => x.Nodes).Include(x => x.Edges).Include(x => x.ReasonContributions)
            .Include(x => x.Results).Include(x => x.Events).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == groupId, cancellationToken);
    }

    private async Task<AdvancedReconciliationRule?> CurrentRuleAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.AdvancedReconciliationRules.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.SupersededUtc == null).OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
    private async Task<AdvancedReconciliationRule?> ResolveRuleAsync(Guid companyId, int? version, CancellationToken cancellationToken) =>
        version.HasValue
            ? await _dbContext.AdvancedReconciliationRules.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Version == version.Value, cancellationToken)
            : await CurrentRuleAsync(companyId, cancellationToken);
    private async Task<AdvancedReconciliationRule> CreateDefaultRuleAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var rule = new AdvancedReconciliationRule(Guid.NewGuid(), companyId, 1, "Standard deterministic settlement matching",
            @"[\s\-_/]+", @"[\s\-_/.,]+", ".*", .01m, 7, .30m, .75m, 10000m, actorUserId, now);
        _dbContext.AdvancedReconciliationRules.Add(rule); await _dbContext.SaveChangesAsync(cancellationToken); return rule;
    }

    private async Task EnsureActorAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        if (!await _dbContext.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == actorUserId && x.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw new UnauthorizedAccessException("The reconciliation actor must be an active company member.");
    }
    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId));
        if (_companyContext?.CompanyId is Guid active && active != companyId) throw new UnauthorizedAccessException("Advanced reconciliation is scoped to the active company.");
    }
    private async Task WriteAuditAsync(AdvancedReconciliationGroup group, Guid actor, string action, string reason, string? correlationId, DateTime now, CancellationToken cancellationToken) =>
        await _auditWriter.WriteAsync(new AuditEventWriteRequest(group.CompanyId, AuditActorTypes.User, actor,
            AuditEventActions.AccountingBankReconciliationReviewed, "advanced_reconciliation_group", group.Id.ToString("N"),
            AuditEventOutcomes.Succeeded, $"Advanced reconciliation group was {action}: {reason}", ["bank_transaction", "payment", "native_ledger"],
            new Dictionary<string, string?> { ["action"] = action, ["groupVersion"] = group.Version.ToString(CultureInfo.InvariantCulture), ["ruleVersion"] = group.RuleVersion.ToString(CultureInfo.InvariantCulture) }, correlationId, now), cancellationToken);

    private static AdvancedReconciliationGraphEvaluation Evaluate(AdvancedReconciliationGroup group, decimal tolerance) =>
        AdvancedReconciliationGraphPolicy.Evaluate(group.Nodes.Select(ToGraphNode).ToArray(), group.Edges.Select(x => new AdvancedReconciliationGraphEdge(x.Id, x.SourceNodeId, x.TargetNodeId, x.EdgeType, x.Amount)).ToArray(), tolerance);
    private static AdvancedReconciliationGraphNode ToGraphNode(AdvancedReconciliationNode node) => new(node.Id, node.NodeType, node.Amount, node.Currency, node.Direction, node.AdjustmentKind, node.DebitAmount, node.CreditAmount);
    private static AdvancedReconciliationGraphNode ToGraphNode(HydratedNode node) => new(node.Id, node.NodeType, node.Amount, node.Currency, node.Direction, node.AdjustmentKind, node.DebitAmount, node.CreditAmount);
    private static AdvancedReconciliationGroupSummaryDto MapSummary(AdvancedReconciliationGroup group, int? currentRuleVersion)
    {
        var evaluation = Evaluate(group, .01m);
        return new(group.Id, group.Reference, group.Counterparty, group.Currency, group.ExpectedBankTotal, group.ConfidenceScore,
            group.Status, evaluation.Cardinality, group.Nodes.Count(x => x.NodeType == AdvancedReconciliationNodeTypes.BankTransaction),
            group.Nodes.Count(x => x.NodeType == AdvancedReconciliationNodeTypes.Payment),
            group.Nodes.Count(x => x.NodeType is AdvancedReconciliationNodeTypes.Invoice or AdvancedReconciliationNodeTypes.Bill),
            group.RuleVersion, group.Version, group.RequiresApproval,
            group.Status == AdvancedReconciliationGroupStatuses.Proposed && currentRuleVersion.HasValue && currentRuleVersion != group.RuleVersion,
            group.UpdatedUtc);
    }
    private static AdvancedReconciliationRuleDto MapRule(AdvancedReconciliationRule x) => new(x.Id, x.Version, x.Name,
        x.ReferenceNormalizationPattern, x.CounterpartyNormalizationPattern, x.ProviderPattern, x.AmountTolerance,
        x.TimingWindowDays, x.RecommendationThreshold, x.LowConfidenceThreshold, x.MaterialityThreshold, x.CreatedUtc, x.SupersededUtc);
    private static AdvancedReconciliationNodeDto MapNode(AdvancedReconciliationNode x) => new(x.Id, x.NodeType, x.RecordId, x.Label, x.Reference, x.Currency, x.Amount, x.Direction, x.AdjustmentKind, x.DebitAmount, x.CreditAmount, x.ExpectedRecordVersion, x.Sequence);
    private static AdvancedReconciliationEdgeDto MapEdge(AdvancedReconciliationEdge x) => new(x.Id, x.SourceNodeId, x.TargetNodeId, x.EdgeType, x.Amount);
    private static AdvancedReconciliationResultDto MapResult(AdvancedReconciliationResult x) => new(x.Id, x.ParentResultId,
        x.Outcome, x.GroupVersion, x.RuleVersion, x.ExpectedBankTotal, x.AllocatedAmount, x.FeeAmount, x.RoundingAmount,
        x.ResidualAmount, ReadLedgerIds(x.EvidenceJson), x.CreatedByUserId, x.CreatedUtc);
    private static IReadOnlyList<Guid> ReadLedgerIds(string evidenceJson)
    {
        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            return document.RootElement.TryGetProperty("ledgerEntryIds", out var values) && values.ValueKind == JsonValueKind.Array
                ? values.EnumerateArray().Select(x => x.GetGuid()).ToArray() : [];
        }
        catch (JsonException) { return []; }
    }
    private static string Snapshot(AdvancedReconciliationGroup group, AdvancedReconciliationGraphEvaluation evaluation) => JsonSerializer.Serialize(new
    { group.Id, group.Status, group.Version, group.RuleVersion, evaluation.ExpectedBankTotal, evaluation.AllocatedAmount, evaluation.FeeAmount, evaluation.RoundingAmount, evaluation.ResidualAmount, evaluation.Cardinality });
    private static Guid[] Ids(IReadOnlyList<AdvancedReconciliationNodeInputDto> nodes, string type) => nodes.Where(x => AdvancedReconciliationNodeTypes.Normalize(x.NodeType) == type).Select(x => x.RecordId ?? Guid.Empty).Distinct().ToArray();
    private static string Normalize(string value, string pattern) => Regex.Replace(value ?? string.Empty, pattern, string.Empty, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout).ToUpperInvariant();
    private static bool IsMatch(string value, string pattern) => Regex.IsMatch(value ?? string.Empty, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);
    private static void ValidatePattern(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500) throw Validation(name, "A bounded normalization pattern is required.");
        try { _ = new Regex(value, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout); }
        catch (ArgumentException ex) { throw Validation(name, $"The pattern is invalid: {ex.Message}"); }
    }
    private static string FriendlyKind(string value) => value.Replace('_', ' ');
    private static string? Ticks(DateTime? value) => value?.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
    private static decimal Score(decimal value, string name) => value is < 0m or > 1m ? throw Validation(name, "Confidence must be between 0 and 1.") : decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static FinanceValidationException Validation(string field, string message) => new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] }, message);

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null) return await action();
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => { await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken); var result = await action(); await tx.CommitAsync(cancellationToken); return result; });
    }

    private sealed record HydratedNode(Guid Id, string NodeType, Guid? RecordId, string Label, string Reference,
        string Currency, decimal Amount, string? Direction, string? AdjustmentKind, decimal DebitAmount,
        decimal CreditAmount, string? ExpectedRecordVersion, int Sequence);
    private sealed record HydratedGraph(IReadOnlyList<HydratedNode> Nodes, IReadOnlyList<DateTime> Dates, IReadOnlyList<string> Providers);
    private sealed record Reason(string FeatureKey, decimal Contribution, string Explanation, string Evidence);
    private sealed record RecordVersionRow(Guid Id, long? SourceVersion, DateTime? UpdatedUtc);
}
