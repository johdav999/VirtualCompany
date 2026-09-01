using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceAdvancedAccountingAgentService(
    IBankStatementImportCenterService statementImports,
    IAdvancedReconciliationReadService reconciliation,
    IBankTransactionReadService bankReconciliation,
    IFinancePaymentReadService paymentReads,
    IFinanceReadService financeReads,
    IPaymentBatchService paymentBatches,
    IPaymentBatchExecutionService paymentExecutions,
    IExchangeRateService exchangeRates,
    ICurrencyRevaluationService revaluation,
    IAccountingDimensionService dimensions,
    IAccountingScheduleService schedules,
    IFixedAssetService fixedAssets) : IFinanceAdvancedAccountingAgentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InternalToolExecutionResponse> ExecuteAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var toolName = request.ToolName.Trim().ToLowerInvariant();
        if (!FinanceAdvancedAccountingAgentToolIds.Contains(toolName))
            return Reject("unsupported_advanced_accounting_tool", "This advanced-accounting tool is not supported.");

        var expectedAction = FinanceAdvancedAccountingAgentToolIds.ActionFor(toolName);
        if (request.ActionKind != expectedAction)
            return Reject("advanced_accounting_action_mismatch",
                $"{toolName} requires the {expectedAction.ToString().ToLowerInvariant()} action class.");

        try
        {
            return toolName switch
            {
                FinanceAdvancedAccountingAgentToolIds.ReadStatementImports => await ReadStatementImportsAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.ReadReconciliation => await ReadReconciliationAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.ReadSubledgerSettlement => await ReadSubledgerAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.ReadPaymentBatches => await ReadPaymentBatchesAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.ReadExchangeRates => await ReadExchangeRatesAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.ReadRevaluation => await ReadRevaluationAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.ReadDimensions => await ReadDimensionsAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.ReadSchedules => await ReadSchedulesAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.ReadFixedAssets => await ReadFixedAssetsAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary => ReadInventoryBoundary(),
                FinanceAdvancedAccountingAgentToolIds.RecommendReconciliationReview => await RecommendReconciliationAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.RecommendRateEvidenceRemediation => await RecommendRateEvidenceAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.RecommendScheduleAssetReview => await RecommendScheduleAssetAsync(request, cancellationToken),
                FinanceAdvancedAccountingAgentToolIds.PrioritizeSubledgerExceptions => await RecommendSubledgerAsync(request, cancellationToken),
                _ => throw new InvalidOperationException("Unreachable advanced-accounting tool route.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (KeyNotFoundException) { return Reject("advanced_accounting_object_not_found", "The requested object was not found in this company or is not accessible to this caller."); }
        catch (UnauthorizedAccessException) { return Reject("advanced_accounting_object_access_denied", "The requested object is not accessible to this caller."); }
        catch (ArgumentException ex) { return Reject("advanced_accounting_request_invalid", Safe(ex.Message)); }
        catch (BankStatementImportOperationException ex) { return Reject(ex.ReasonCode, Safe(ex.SafeMessage)); }
        catch (PaymentBatchException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
        catch (ExchangeRateOperationException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
        catch (CurrencyRevaluationException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
        catch (AccountingDimensionException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
        catch (AccountingScheduleException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
        catch (FixedAssetException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
    }

    private async Task<InternalToolExecutionResponse> ReadStatementImportsAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var take = Take(request);
        if (TryGuid(request, "jobId", out var jobId))
        {
            var job = await statementImports.GetJobAsync(request.CompanyId, jobId, ct)
                ?? throw new KeyNotFoundException();
            var bounded = Bound(job, take);
            return Success("statementImports", bounded, StatementSources(bounded),
                ["review_import", "open_statement_import_workspace"], job.Rows.Count > bounded.Rows.Count);
        }

        var skip = Skip(request);
        var workspace = await statementImports.GetWorkspaceAsync(request.CompanyId, ct);
        var jobs = workspace.Jobs.Skip(skip).Take(take).Select(job => Bound(job, take)).ToArray();
        var boundedWorkspace = workspace with
        {
            Accounts = workspace.Accounts.Take(take).ToArray(),
            CsvProfiles = workspace.CsvProfiles.Take(take).ToArray(),
            Jobs = jobs
        };
        return Success("statementImports", boundedWorkspace, jobs.SelectMany(StatementSources),
            ["review_import", "open_statement_import_workspace"],
            skip + jobs.Length < workspace.Jobs.Count || workspace.Accounts.Count > take || workspace.CsvProfiles.Count > take,
            new(skip, take, workspace.Jobs.Count));
    }

    private async Task<InternalToolExecutionResponse> ReadReconciliationAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var take = Take(request);
        if (TryGuid(request, "bankTransactionId", out var bankTransactionId))
        {
            var detail = await bankReconciliation.GetReconciliationDetailAsync(
                new(request.CompanyId, bankTransactionId), ct) ?? throw new KeyNotFoundException();
            return Success("reconciliation", new { Kind = "bank", Detail = detail },
                BankReconciliationSources(detail), ["review_candidate_evidence", "open_reconciliation_workspace"]);
        }
        if (string.Equals(Text(request, "reconciliationKind", 20), "bank", StringComparison.OrdinalIgnoreCase))
        {
            var bankWorkspace = await bankReconciliation.ListReconciliationAsync(new(request.CompanyId,
                Text(request, "status", 40), Text(request, "search", 160), null, null, take), ct);
            return Success("reconciliation", new { Kind = "bank", Workspace = bankWorkspace },
                bankWorkspace.Items.Select(x => Source("bank_transaction", x.BankTransactionId)),
                ["review_candidate_evidence", "open_reconciliation_workspace"], bankWorkspace.Items.Count == take);
        }
        if (TryGuid(request, "groupId", out var groupId))
        {
            var group = await reconciliation.GetAsync(new(request.CompanyId, groupId), ct)
                ?? throw new KeyNotFoundException();
            var bounded = Bound(group, take);
            return Success("reconciliation", bounded, ReconciliationSources(bounded),
                ["review_candidate_evidence", "open_reconciliation_workspace"], IsTruncated(group, bounded),
                freshness: group.Summary.IsStale ? "authoritative_stale" : "authoritative_live");
        }

        var workspace = await reconciliation.ListAsync(new(request.CompanyId, Text(request, "status", 40),
            Text(request, "search", 160), Decimal(request, "maximumConfidence"), take), ct);
        return Success("reconciliation", workspace, workspace.Groups.Select(x => Source("reconciliation_group", x.Id)),
            ["review_candidate_evidence", "open_reconciliation_workspace"], workspace.Groups.Count == take);
    }

    private async Task<InternalToolExecutionResponse> ReadSubledgerAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var take = Take(request);
        if (TryGuid(request, "allocationId", out var allocationId))
        {
            var trace = await paymentReads.GetAllocationTraceAsync(new(request.CompanyId, allocationId), ct)
                ?? throw new KeyNotFoundException();
            var settlements = trace.Settlement is null
                ? Array.Empty<FinancePaymentAllocationDto>()
                : new[] { trace.Settlement };
            return Success("subledgerSettlement", trace, AllocationSources(settlements),
                ["review_settlement", "open_receivables_or_payables"]);
        }

        var selectors = new[]
        {
            (Name: "paymentId", Value: OptionalGuid(request, "paymentId")),
            (Name: "invoiceId", Value: OptionalGuid(request, "invoiceId")),
            (Name: "billId", Value: OptionalGuid(request, "billId"))
        }.Where(x => x.Value.HasValue).ToArray();
        if (selectors.Length != 1)
            throw new ArgumentException("Exactly one of allocationId, paymentId, invoiceId, or billId is required.");

        IReadOnlyList<FinancePaymentAllocationDto> allocations = selectors[0].Name switch
        {
            "paymentId" => await paymentReads.GetAllocationsByPaymentAsync(new(request.CompanyId, selectors[0].Value!.Value), ct),
            "invoiceId" => await paymentReads.GetAllocationsByInvoiceAsync(new(request.CompanyId, selectors[0].Value!.Value), ct),
            _ => await paymentReads.GetAllocationsByBillAsync(new(request.CompanyId, selectors[0].Value!.Value), ct)
        };
        var bounded = allocations.Take(take).ToArray();
        return Success("subledgerSettlement", bounded, AllocationSources(bounded),
            ["review_settlement", "open_receivables_or_payables"], allocations.Count > bounded.Length,
            new(0, take, allocations.Count));
    }

    private async Task<InternalToolExecutionResponse> ReadPaymentBatchesAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (TryGuid(request, "executionId", out var executionId))
        {
            var execution = await paymentExecutions.GetAsync(new(request.CompanyId, executionId), ct)
                ?? throw new KeyNotFoundException();
            return Success("paymentBatches", new { Execution = Bound(execution, Take(request)) },
                PaymentExecutionSources(execution), ["review_execution", "open_payment_batches"]);
        }
        if (TryGuid(request, "batchId", out var batchId))
        {
            var unboundedBatch = await paymentBatches.GetAsync(new(request.CompanyId, batchId), ct)
                ?? throw new KeyNotFoundException();
            var batch = Bound(unboundedBatch, Take(request));
            var execution = await paymentExecutions.GetForBatchAsync(new(request.CompanyId, batchId), ct);
            return Success("paymentBatches", new { Batch = batch, Execution = execution is null ? null : Bound(execution, Take(request)) },
                PaymentBatchSources(batch).Concat(execution is null ? [] : PaymentExecutionSources(execution)),
                ["review_batch", "open_payment_batches"],
                unboundedBatch.Obligations.Count > batch.Obligations.Count || unboundedBatch.Instructions.Count > batch.Instructions.Count ||
                (unboundedBatch.Validation?.Issues.Count ?? 0) > (batch.Validation?.Issues.Count ?? 0));
        }
        var take = Take(request);
        var batches = await paymentBatches.ListAsync(new(request.CompanyId, Text(request, "status", 40), take), ct);
        var bills = await financeReads.GetBillsAsync(new(request.CompanyId, Limit: take), ct);
        var proposals = bills.Where(x => x.PaymentProposal is not null).Select(x => x.PaymentProposal!).Take(take).ToArray();
        return Success("paymentBatches", new { Batches = batches, PaymentProposals = proposals },
            batches.Items.Select(x => Source("payment_batch", x.Id))
                .Concat(proposals.Select(x => Source("supplier_payment_proposal", x.Id)))
                .Concat(proposals.Select(x => Source("bill", x.BillId))),
            ["review_batch", "review_payment_proposal", "open_payment_batches"],
            batches.Items.Count == take || proposals.Length == take || bills.Count == take);
    }

    private async Task<InternalToolExecutionResponse> ReadExchangeRatesAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (TryGuid(request, "observationId", out var observationId))
        {
            var observation = await exchangeRates.GetObservationAsync(request.CompanyId, observationId, ct);
            return Success("exchangeRates", observation,
                [Source("exchange_rate_observation", observation.Id), Source("exchange_rate_set", observation.RateSetId),
                    Source("exchange_rate_evidence", observation.EvidenceChecksum)],
                ["review_rate_evidence", "open_exchange_rates"],
                freshness: observation.ApprovalStatus == "approved" ? "authoritative_approved" : "review_required");
        }

        var skip = Skip(request);
        var take = Take(request);
        var currencies = await exchangeRates.GetCurrenciesAsync(request.CompanyId, ct);
        var sources = await exchangeRates.GetSourcesAsync(request.CompanyId, ct);
        var sets = await exchangeRates.GetSetsAsync(request.CompanyId, skip, take, ct);
        var readiness = await exchangeRates.GetReadinessAsync(request.CompanyId, ct);
        var value = new
        {
            Currencies = currencies.Take(take).ToArray(),
            Sources = sources.Take(take).ToArray(),
            Sets = sets,
            SetPage = new { Skip = skip, Take = take, MayHaveMore = sets.Count == take },
            Readiness = readiness with { Sources = readiness.Sources.Take(take).ToArray() },
            NoRateInvented = true
        };
        var sourceIds = sources.Select(x => Source("exchange_rate_source", x.Id))
            .Concat(sets.Select(x => Source("exchange_rate_set", x.Id)))
            .Concat(sets.Select(x => Source("exchange_rate_evidence", x.ContentHash)));
        return Success("exchangeRates", value, sourceIds, ["review_rate_evidence", "open_exchange_rates"],
            sets.Count == take || currencies.Count > take || sources.Count > take,
            page: null,
            freshness: readiness.Status == "ready" ? "authoritative_live" : "review_required");
    }

    private async Task<InternalToolExecutionResponse> ReadRevaluationAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var take = Take(request);
        if (TryGuid(request, "runId", out var runId))
        {
            var run = Bound(await revaluation.GetAsync(new(request.CompanyId, runId), ct), take);
            return Success("revaluation", run, RevaluationSources(run),
                ["review_rate_bindings", "open_currency_revaluation"], freshness: RevaluationFreshness(run));
        }
        var skip = Skip(request);
        var raw = await revaluation.ListAsync(new(request.CompanyId, OptionalGuid(request, "fiscalPeriodId"), skip, take), ct);
        var result = raw with { Items = raw.Items.Select(x => Bound(x, take)).ToArray() };
        return Success("revaluation", result, result.Items.SelectMany(RevaluationSources),
            ["review_rate_bindings", "open_currency_revaluation"], result.Skip + result.Items.Count < result.TotalCount ||
            raw.Items.Any(x => x.Population.Count > take || x.RateBindings.Count > take || x.ProposalLines.Count > take ||
                               x.Reviews.Count > take || x.Reconciliations.Count > take),
            new(result.Skip, result.Take, result.TotalCount));
    }

    private async Task<InternalToolExecutionResponse> ReadDimensionsAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var take = Take(request);
        var workspace = await dimensions.GetWorkspaceAsync(request.CompanyId, ct);
        var value = new
        {
            DimensionTypes = workspace.DimensionTypes.Take(take).Select(x => x with { Members = x.Members.Take(take).ToArray() }).ToArray(),
            AccountPolicies = workspace.AccountPolicies.Take(take).ToArray(),
            CombinationRules = workspace.CombinationRules.Take(take).ToArray(),
            ExternalMappings = workspace.ExternalMappings.Take(take).ToArray(),
            MappingConflicts = workspace.MappingConflicts.Take(take).ToArray(),
            AllocationTemplates = workspace.AllocationTemplates.Take(take).Select(x => x with { Lines = x.Lines.Take(take).ToArray() }).ToArray(),
            workspace.ActiveDimensionCount,
            workspace.ActiveMemberCount,
            workspace.RequiredAccountRuleCount,
            workspace.OpenMappingConflictCount
        };
        var sources = workspace.DimensionTypes.Select(x => Source("dimension_type", x.Id))
            .Concat(workspace.DimensionTypes.SelectMany(x => x.Members).Select(x => Source("dimension_member", x.Id)))
            .Concat(workspace.AccountPolicies.Select(x => Source("dimension_account_policy", x.Id)))
            .Concat(workspace.MappingConflicts.Select(x => Source("dimension_mapping_conflict", x.Id)))
            .Concat(workspace.AllocationTemplates.Select(x => Source("allocation_template", x.Id)));
        return Success("dimensions", value, sources, ["review_dimension_usage", "open_dimensions"],
            workspace.DimensionTypes.Count > take || workspace.DimensionTypes.Any(x => x.Members.Count > take) ||
            workspace.AccountPolicies.Count > take || workspace.CombinationRules.Count > take ||
            workspace.ExternalMappings.Count > take || workspace.MappingConflicts.Count > take ||
            workspace.AllocationTemplates.Count > take);
    }

    private async Task<InternalToolExecutionResponse> ReadSchedulesAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var take = Take(request);
        if (TryGuid(request, "scheduleId", out var scheduleId))
        {
            var schedule = await schedules.GetAsync(new(request.CompanyId, scheduleId), ct);
            var bounded = Bound(schedule, take);
            AccountingSchedulePreviewDto? preview = null;
            if (Boolean(request, "includePreview"))
                preview = await schedules.PreviewAsync(new(request.CompanyId, scheduleId, schedule.Version, request.AgentId), ct);
            return Success("schedules", new { Schedule = bounded, Preview = preview, CalculationSource = "owning_schedule_service" },
                ScheduleSources(bounded), schedule.AllowedActions, IsTruncated(schedule, bounded));
        }
        var skip = Skip(request);
        var raw = await schedules.ListAsync(new(request.CompanyId, Text(request, "status", 40), skip, take), ct);
        var result = raw with { Items = raw.Items.Select(x => Bound(x, take)).ToArray() };
        return Success("schedules", result, result.Items.SelectMany(ScheduleSources),
            ["review_schedule", "open_accounting_schedules"], result.Skip + result.Items.Count < result.TotalCount ||
            raw.Items.Zip(result.Items).Any(pair => IsTruncated(pair.First, pair.Second)),
            new(result.Skip, result.Take, result.TotalCount));
    }

    private async Task<InternalToolExecutionResponse> ReadFixedAssetsAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var take = Take(request);
        FixedAssetDto? asset = null;
        FixedAssetListDto? list = null;
        if (TryGuid(request, "assetId", out var assetId))
            asset = Bound(await fixedAssets.GetAsync(new(request.CompanyId, assetId), ct), take);
        else
        {
            var raw = await fixedAssets.ListAsync(new(request.CompanyId, Text(request, "status", 40),
                OptionalGuid(request, "assetClassId"), Text(request, "search", 160), Skip(request), take), ct);
            list = raw with { Items = raw.Items.Select(x => Bound(x, take)).ToArray() };
        }

        FixedAssetDepreciationPreviewDto? preview = null;
        var from = OptionalDate(request, "periodStart");
        var to = OptionalDate(request, "periodEnd");
        if (from.HasValue != to.HasValue)
            throw new ArgumentException("periodStart and periodEnd must be supplied together.");
        if (from.HasValue)
        {
            RequireRange(from.Value, to!.Value);
            preview = await fixedAssets.PreviewDepreciationAsync(new(request.CompanyId, from.Value, to.Value), ct);
            preview = preview with { Items = preview.Items.Take(take).ToArray() };
        }

        var sources = asset is not null ? FixedAssetSources(asset) : list!.Items.SelectMany(FixedAssetSources);
        if (preview is not null) sources = sources.Concat(preview.Items.Select(x => Source("fixed_asset", x.AssetId)));
        var truncated = asset is not null
            ? asset.Components.Count == take || asset.Events.Count == take || (preview?.Items.Count == take)
            : list!.Skip + list.Items.Count < list.TotalCount ||
              list.Items.Any(x => x.Components.Count == take || x.Events.Count == take) || preview?.Items.Count == take;
        return Success("fixedAssets", new { Asset = asset, List = list, DepreciationPreview = preview, CalculationSource = "owning_fixed_asset_service" },
            sources, ["review_asset", "open_fixed_assets"], truncated,
            list is null ? null : new(list.Skip, list.Take, list.TotalCount));
    }

    private static InternalToolExecutionResponse ReadInventoryBoundary() =>
        Success("inventoryBoundary", new
        {
            Supported = false,
            QuantityAccountingSupported = false,
            ValuationSupported = false,
            CogsAccountingSupported = false,
            ReasonCode = "inventory_accounting_unsupported",
            Explanation = FinanceAdvancedAccountingAgentContract.InventoryBoundary,
            SafeAlternative = "Use commerce records only as non-accounting operational context and obtain an authoritative inventory subledger before accounting conclusions."
        }, [], ["state_unsupported_boundary", "request_authoritative_inventory_subledger"]);

    private async Task<InternalToolExecutionResponse> RecommendReconciliationAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var groupId = RequiredGuid(request, "groupId");
        var group = await reconciliation.GetAsync(new(request.CompanyId, groupId), ct) ?? throw new KeyNotFoundException();
        var reasons = group.ReasonContributions.Take(Take(request)).ToArray();
        return Recommendation("reconciliationRecommendation", new
        {
            group.Summary.Id,
            group.Summary.Version,
            group.Summary.RuleVersion,
            group.Summary.ConfidenceScore,
            group.Summary.RequiresApproval,
            group.Summary.IsStale,
            group.IsBalanced,
            group.Variance,
            group.BlockingReason,
            ReasonContributions = reasons,
            ReviewRequired = group.Summary.IsStale || group.Summary.RequiresApproval || !group.IsBalanced,
            MatchApplied = false,
            AllowedActions = new[] { "review_candidate_evidence", "open_reconciliation_workspace" }
        }, ReconciliationSources(group), group.ReasonContributions.Count > reasons.Length);
    }

    private async Task<InternalToolExecutionResponse> RecommendRateEvidenceAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var take = Take(request);
        var readiness = await exchangeRates.GetReadinessAsync(request.CompanyId, ct);
        var sets = await exchangeRates.GetSetsAsync(request.CompanyId, 0, take, ct);
        var runs = await revaluation.ListAsync(new(request.CompanyId, OptionalGuid(request, "fiscalPeriodId"), 0, take), ct);
        var reviewSets = sets.Where(x => x.Status != "approved").ToArray();
        var reviewRuns = runs.Items.Where(x => x.ReviewCount > 0 || x.RateBindings.Any(binding => string.IsNullOrWhiteSpace(binding.EvidenceChecksum))).ToArray();
        return Recommendation("rateEvidenceRecommendation", new
        {
            readiness.Status,
            readiness.Issues,
            ReviewRateSets = reviewSets,
            ReviewRevaluationRuns = reviewRuns,
            ReviewRequired = readiness.Status != "ready" || reviewSets.Length > 0 || reviewRuns.Length > 0,
            InventedRate = (decimal?)null,
            InventedAmount = (decimal?)null,
            AllowedActions = new[] { "review_rate_evidence", "open_exchange_rates", "open_currency_revaluation" }
        }, sets.Select(x => Source("exchange_rate_set", x.Id)).Concat(runs.Items.SelectMany(RevaluationSources)),
            sets.Count == take || runs.Items.Count == take);
    }

    private async Task<InternalToolExecutionResponse> RecommendScheduleAssetAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var scheduleId = OptionalGuid(request, "scheduleId");
        var assetId = OptionalGuid(request, "assetId");
        if (scheduleId.HasValue == assetId.HasValue)
            throw new ArgumentException("Exactly one of scheduleId or assetId is required.");
        if (scheduleId.HasValue)
        {
            var schedule = await schedules.GetAsync(new(request.CompanyId, scheduleId.Value), ct);
            return Recommendation("scheduleAssetRecommendation", new
            {
                ObjectType = "accounting_schedule",
                schedule.Id,
                schedule.Version,
                schedule.Status,
                schedule.Reconciliation,
                OpenExceptions = schedule.Occurrences.SelectMany(x => x.Exceptions).Where(x => x.ResolvedUtc is null).Take(Take(request)).ToArray(),
                schedule.AllowedActions,
                CalculationSource = "owning_schedule_service",
                StateChanged = false
            }, ScheduleSources(schedule));
        }
        var asset = await fixedAssets.GetAsync(new(request.CompanyId, assetId!.Value), ct);
        return Recommendation("scheduleAssetRecommendation", new
        {
            ObjectType = "fixed_asset",
            asset.Id,
            asset.Version,
            asset.Status,
            asset.NetBookValue,
            RecentEvents = asset.Events.OrderByDescending(x => x.CreatedUtc).Take(Take(request)).ToArray(),
            TaxTreatmentDetermined = false,
            CalculationSource = "owning_fixed_asset_service",
            StateChanged = false
        }, FixedAssetSources(asset));
    }

    private async Task<InternalToolExecutionResponse> RecommendSubledgerAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var read = await ReadSubledgerAsync(request, ct);
        if (!read.Success) return read;
        var node = read.Data["subledgerSettlement"];
        return Recommendation("subledgerExceptionRecommendation", new
        {
            SettlementEvidence = node,
            PriorityRules = new[] { "reversed_or_failed", "review_required", "unsettled", "settled" },
            AllocationApplied = false,
            PaymentReleased = false,
            AllowedActions = new[] { "review_settlement", "open_receivables_or_payables" }
        }, MetadataSourceIds(read.Metadata));
    }

    private static BankStatementImportJobDto Bound(BankStatementImportJobDto value, int take) => value with
    {
        Issues = value.Issues.Take(take).ToArray(),
        Rows = value.Rows.Take(take).ToArray()
    };

    private static AdvancedReconciliationGroupDetailDto Bound(AdvancedReconciliationGroupDetailDto value, int take) => value with
    {
        Nodes = value.Nodes.Take(take).ToArray(), Edges = value.Edges.Take(take).ToArray(),
        ReasonContributions = value.ReasonContributions.Take(take).ToArray(), Results = value.Results.Take(take).ToArray(),
        History = value.History.Take(take).ToArray()
    };

    private static CurrencyRevaluationRunDto Bound(CurrencyRevaluationRunDto value, int take) => value with
    {
        Population = value.Population.Take(take).ToArray(), RateBindings = value.RateBindings.Take(take).ToArray(),
        ProposalLines = value.ProposalLines.Take(take).ToArray(), Reviews = value.Reviews.Take(take).ToArray(),
        Reconciliations = value.Reconciliations.Take(take).ToArray()
    };

    private static PaymentBatchExecutionDto Bound(PaymentBatchExecutionDto value, int take) => value with
    {
        AuthorizationUri = null,
        Attempts = value.Attempts.Take(take).ToArray(),
        Acknowledgements = value.Acknowledgements.Take(take).ToArray(),
        Instructions = value.Instructions.Take(take).ToArray(),
        Remittances = value.Remittances.Take(take).ToArray()
    };

    private static PaymentBatchDetailDto Bound(PaymentBatchDetailDto value, int take) => value with
    {
        Obligations = value.Obligations.Take(take).ToArray(),
        Instructions = value.Instructions.Take(take).ToArray(),
        Validation = value.Validation is null ? null : value.Validation with { Issues = value.Validation.Issues.Take(take).ToArray() }
    };

    private static AccountingScheduleDto Bound(AccountingScheduleDto value, int take) => value with
    {
        CurrentVersion = value.CurrentVersion is null ? null : value.CurrentVersion with
        {
            Lines = value.CurrentVersion.Lines.Take(take).ToArray(), Evidence = value.CurrentVersion.Evidence.Take(take).ToArray()
        },
        Occurrences = value.Occurrences.Take(take).Select(x => x with { Exceptions = x.Exceptions.Take(take).ToArray() }).ToArray()
    };

    private static FixedAssetDto Bound(FixedAssetDto value, int take) => value with
    {
        Components = value.Components.Take(take).ToArray(), Events = value.Events.Take(take).ToArray(),
        DimensionFacts = value.DimensionFacts.Take(take).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
    };

    private static bool IsTruncated(AdvancedReconciliationGroupDetailDto original, AdvancedReconciliationGroupDetailDto bounded) =>
        original.Nodes.Count > bounded.Nodes.Count || original.Edges.Count > bounded.Edges.Count ||
        original.ReasonContributions.Count > bounded.ReasonContributions.Count || original.Results.Count > bounded.Results.Count ||
        original.History.Count > bounded.History.Count;

    private static bool IsTruncated(AccountingScheduleDto original, AccountingScheduleDto bounded) =>
        original.Occurrences.Count > bounded.Occurrences.Count ||
        (original.CurrentVersion?.Lines.Count ?? 0) > (bounded.CurrentVersion?.Lines.Count ?? 0) ||
        (original.CurrentVersion?.Evidence.Count ?? 0) > (bounded.CurrentVersion?.Evidence.Count ?? 0);

    private static IEnumerable<string> StatementSources(BankStatementImportJobDto job) =>
        new[] { Source("statement_import", job.Id), Source("bank_account", job.BankAccountId), Source("statement_checksum", job.Checksum) }
            .Concat(job.Rows.Select(x => Source("statement_row", x.Id)))
            .Concat(job.Rows.Where(x => x.ImportedBankTransactionId.HasValue).Select(x => Source("bank_transaction", x.ImportedBankTransactionId!.Value)));

    private static IEnumerable<string> ReconciliationSources(AdvancedReconciliationGroupDetailDto group) =>
        new[] { Source("reconciliation_group", group.Summary.Id), Source("reconciliation_rule_version", group.Summary.RuleVersion.ToString()) }
            .Concat(group.Nodes.Select(x => Source("reconciliation_node", x.Id)))
            .Concat(group.Nodes.Where(x => x.RecordId.HasValue).Select(x => Source(x.NodeType, x.RecordId!.Value)))
            .Concat(group.Results.Select(x => Source("reconciliation_result", x.Id)));

    private static IEnumerable<string> BankReconciliationSources(BankReconciliationDetailDto detail) =>
        new[] { Source("bank_transaction", detail.Transaction.Id), Source("bank_transaction_version", detail.SourceVersion.ToString()) }
            .Concat(detail.CandidatePayments.Select(x => Source("payment", x.PaymentId)))
            .Concat(detail.Journals.Select(x => Source("ledger_entry", x.LedgerEntryId)))
            .Concat(detail.FollowUp is null ? [] : [Source("bank_reconciliation_follow_up", detail.FollowUp.Id)]);

    private static IEnumerable<string> AllocationSources(IEnumerable<FinancePaymentAllocationDto> allocations) =>
        allocations.Select(x => Source("payment_allocation", x.Id))
            .Concat(allocations.Select(x => Source("payment", x.PaymentId)))
            .Concat(allocations.Where(x => x.InvoiceId.HasValue).Select(x => Source("invoice", x.InvoiceId!.Value)))
            .Concat(allocations.Where(x => x.BillId.HasValue).Select(x => Source("bill", x.BillId!.Value)))
            .Concat(allocations.Where(x => x.SettlementExchangeRateConversionId.HasValue).Select(x => Source("exchange_rate_conversion", x.SettlementExchangeRateConversionId!.Value)))
            .Concat(allocations.Where(x => x.SettlementLedgerEntryId.HasValue).Select(x => Source("ledger_entry", x.SettlementLedgerEntryId!.Value)));

    private static IEnumerable<string> PaymentBatchSources(PaymentBatchDetailDto batch) =>
        new[] { Source("payment_batch", batch.Summary.Id), Source("payment_batch_version", batch.Summary.Version.ToString()) }
            .Concat(batch.Obligations.Select(x => Source(x.ObligationType, x.SourceId)))
            .Concat(batch.Instructions.Select(x => Source("payment_instruction", x.Id)))
            .Concat(batch.Validation?.Issues.Select(x => Source("payment_validation_issue", x.Id)) ?? []);

    private static IEnumerable<string> PaymentExecutionSources(PaymentBatchExecutionDto execution) =>
        new[] { Source("payment_execution", execution.Id), Source("payment_batch", execution.BatchId), Source("payment_execution_request_hash", execution.RequestHash) }
            .Concat(execution.Attempts.Select(x => Source("payment_execution_attempt", x.Id)))
            .Concat(execution.Acknowledgements.Select(x => Source("payment_acknowledgement", x.Id)))
            .Concat(execution.Instructions.Select(x => Source("payment_execution_instruction", x.Id)))
            .Concat(execution.Settlement is null ? [] : [Source("payment_settlement", execution.Settlement.Id)])
            .Concat(execution.Settlement?.LedgerEntryIds.Select(x => Source("ledger_entry", x)) ?? []);

    private static IEnumerable<string> RevaluationSources(CurrencyRevaluationRunDto run) =>
        new[] { Source("currency_revaluation_run", run.Id), Source("fiscal_period", run.FiscalPeriodId) }
            .Concat(run.Population.Select(x => Source("currency_revaluation_population", x.Id)))
            .Concat(run.RateBindings.Select(x => Source("exchange_rate_conversion", x.ExchangeRateConversionId)))
            .Concat(run.RateBindings.Select(x => Source("exchange_rate_evidence", x.EvidenceChecksum)))
            .Concat(run.Reconciliations.Select(x => Source("currency_revaluation_reconciliation", x.Id)));

    private static IEnumerable<string> ScheduleSources(AccountingScheduleDto schedule) =>
        new[] { Source("accounting_schedule", schedule.Id), Source("accounting_schedule_version", schedule.Version.ToString()) }
            .Concat(schedule.CurrentVersion is null ? [] : [Source("accounting_schedule_definition", schedule.CurrentVersion.Id), Source("schedule_payload_hash", schedule.CurrentVersion.PayloadHash)])
            .Concat(schedule.CurrentVersion?.Evidence.Select(x => Source("document", x.DocumentId)) ?? [])
            .Concat(schedule.Occurrences.Select(x => Source("accounting_schedule_occurrence", x.Id)))
            .Concat(schedule.Occurrences.Where(x => x.LedgerEntryId.HasValue).Select(x => Source("ledger_entry", x.LedgerEntryId!.Value)));

    private static IEnumerable<string> FixedAssetSources(FixedAssetDto asset) =>
        new[] { Source("fixed_asset", asset.Id), Source("fixed_asset_class", asset.AssetClassId), Source("fixed_asset_source", asset.SourceType + ":" + asset.SourceId + ":" + asset.SourceVersion) }
            .Concat(asset.SourceDocumentId.HasValue ? [Source("document", asset.SourceDocumentId.Value)] : [])
            .Concat(asset.Components.Select(x => Source("fixed_asset_component", x.Id)))
            .Concat(asset.Events.Select(x => Source("fixed_asset_event", x.Id)))
            .Concat(asset.Events.Where(x => x.LedgerEntryId.HasValue).Select(x => Source("ledger_entry", x.LedgerEntryId!.Value)));

    private static string RevaluationFreshness(CurrencyRevaluationRunDto run) =>
        run.ReviewCount > 0 || run.RateBindings.Any(x => string.IsNullOrWhiteSpace(x.EvidenceChecksum))
            ? "review_required"
            : "authoritative_evidence";

    private static InternalToolExecutionResponse Success<T>(string property, T value, IEnumerable<string> sources,
        IEnumerable<string> allowedActions, bool truncated = false, PageMetadata? page = null,
        string freshness = "authoritative_live")
    {
        var metadata = Metadata(sources, allowedActions, truncated, freshness);
        if (page is not null)
        {
            metadata["skip"] = JsonValue.Create(page.Skip);
            metadata["take"] = JsonValue.Create(page.Take);
            metadata["totalCount"] = JsonValue.Create(page.TotalCount);
        }
        return InternalToolExecutionResponse.Succeeded("Authoritative advanced-accounting read completed.",
            new() { [property] = JsonSerializer.SerializeToNode(value, JsonOptions) }, metadata);
    }

    private static InternalToolExecutionResponse Recommendation<T>(string property, T value,
        IEnumerable<string> sources, bool truncated = false) =>
        InternalToolExecutionResponse.Succeeded("Evidence-backed review recommendation prepared; authoritative state was not changed.",
            new() { [property] = JsonSerializer.SerializeToNode(value, JsonOptions) },
            Metadata(sources, ["review_evidence", "open_owning_workspace"], truncated, "review_required"));

    private static InternalToolExecutionResponse Reject(string code, string message) =>
        InternalToolExecutionResponse.Failed("blocked", code, message, null,
            Metadata([], ["correct_request", "open_owning_workspace"]));

    private static Dictionary<string, JsonNode?> Metadata(IEnumerable<string> sources, IEnumerable<string> allowedActions,
        bool truncated = false, string freshness = "authoritative_live")
    {
        var distinct = sources.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["contractVersion"] = JsonValue.Create(FinanceAdvancedAccountingAgentContract.Version),
            ["generatedUtc"] = JsonValue.Create(DateTime.UtcNow),
            ["freshness"] = JsonValue.Create(freshness),
            ["truncated"] = JsonValue.Create(truncated),
            ["sourceIdCount"] = JsonValue.Create(distinct.Length),
            ["sourceIdsTruncated"] = JsonValue.Create(distinct.Length > FinanceAdvancedAccountingAgentContract.MaximumSourceIds),
            ["sourceIds"] = new JsonArray(distinct.Take(FinanceAdvancedAccountingAgentContract.MaximumSourceIds).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["allowedActions"] = new JsonArray(allowedActions.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["authorityNotice"] = JsonValue.Create(FinanceAdvancedAccountingAgentContract.AuthorityNotice)
        };
    }

    private static IEnumerable<string> MetadataSourceIds(IReadOnlyDictionary<string, JsonNode?> metadata) =>
        metadata.TryGetValue("sourceIds", out var node) && node is JsonArray values
            ? values.Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!)
            : [];

    private static int Take(InternalToolExecutionRequest request) => Integer(request, "take",
        FinanceAdvancedAccountingAgentContract.MaximumPageSize, 1, FinanceAdvancedAccountingAgentContract.MaximumPageSize);
    private static int Skip(InternalToolExecutionRequest request) => Integer(request, "skip", 0, 0, 100_000);
    private static int Integer(InternalToolExecutionRequest request, string key, int fallback, int min, int max)
    {
        if (!request.Payload.TryGetValue(key, out var node) || node is null) return fallback;
        var value = node.GetValue<int>();
        if (value < min || value > max) throw new ArgumentException($"{key} must be between {min} and {max}.");
        return value;
    }
    private static decimal? Decimal(InternalToolExecutionRequest request, string key) =>
        request.Payload.TryGetValue(key, out var node) && node is not null ? node.GetValue<decimal>() : null;
    private static bool Boolean(InternalToolExecutionRequest request, string key) =>
        request.Payload.TryGetValue(key, out var node) && node is not null && node.GetValue<bool>();
    private static string? Text(InternalToolExecutionRequest request, string key, int maxLength)
    {
        if (!request.Payload.TryGetValue(key, out var node) || node is null) return null;
        var value = node.GetValue<string>().Trim();
        if (value.Length == 0) return null;
        if (value.Length > maxLength) throw new ArgumentException($"{key} must be at most {maxLength} characters.");
        return value;
    }
    private static Guid RequiredGuid(InternalToolExecutionRequest request, string key) =>
        OptionalGuid(request, key) ?? throw new ArgumentException($"{key} is required.");
    private static Guid? OptionalGuid(InternalToolExecutionRequest request, string key) =>
        TryGuid(request, key, out var value) ? value : null;
    private static bool TryGuid(InternalToolExecutionRequest request, string key, out Guid value)
    {
        value = Guid.Empty;
        if (!request.Payload.TryGetValue(key, out var node) || node is null) return false;
        if (!Guid.TryParse(node.GetValue<string>(), out value) || value == Guid.Empty)
            throw new ArgumentException($"{key} must be a non-empty UUID.");
        return true;
    }
    private static DateOnly? OptionalDate(InternalToolExecutionRequest request, string key)
    {
        if (!request.Payload.TryGetValue(key, out var node) || node is null) return null;
        if (!DateOnly.TryParse(node.GetValue<string>(), out var value)) throw new ArgumentException($"{key} must be an ISO date.");
        return value;
    }
    private static void RequireRange(DateOnly from, DateOnly to)
    {
        if (to < from) throw new ArgumentException("periodEnd must be on or after periodStart.");
        if (to.DayNumber - from.DayNumber + 1 > FinanceAdvancedAccountingAgentContract.MaximumCalculationRangeDays)
            throw new ArgumentException($"The calculation range cannot exceed {FinanceAdvancedAccountingAgentContract.MaximumCalculationRangeDays} days.");
    }
    private static string Source(string type, Guid id) => Source(type, id.ToString());
    private static string Source(string type, string identity) => type + ":" + identity;
    private static string Safe(string value) => value.Length <= 500 ? value : value[..500];
    private sealed record PageMetadata(int Skip, int Take, int TotalCount);
}
