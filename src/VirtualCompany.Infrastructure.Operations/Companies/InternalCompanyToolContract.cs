using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class InternalCompanyToolContract : IInternalCompanyToolContract
{
    private readonly ICompanyTaskQueryService _taskQueryService;
    private readonly ICompanyTaskCommandService _taskCommandService;
    private readonly IProactiveTaskCreationService _proactiveTaskCreationService;
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly ICompanyKnowledgeSearchService _knowledgeSearchService;
    private readonly IFinanceToolProvider _financeToolProvider;
    private readonly IFinanceTransactionAnomalyDetectionService _financeAnomalyDetectionService;
    private readonly IFinanceAgentAnalysisService _financeAgentAnalysisService;
    private readonly IFinanceLedgerAgentReadService _financeLedgerAgentReadService;
    private readonly IFinanceCloseComplianceAgentService _financeCloseComplianceAgentService;
    private readonly IFinanceAdvancedAccountingAgentService _financeAdvancedAccountingAgentService;
    private readonly IFinanceAccountingDraftAgentService _financeAccountingDraftAgentService;
    private readonly IFinanceOperationalProposalAgentService _financeOperationalProposalAgentService;
    private readonly IFinanceGuardedCommandService _financeGuardedCommandService;
    private readonly IAccountingProviderSwitchAgentService _accountingProviderSwitchAgentService;
    private readonly ILeadGenerationService _leadGenerationService;

    public InternalCompanyToolContract(
        ICompanyTaskQueryService taskQueryService,
        ICompanyTaskCommandService taskCommandService,
        IProactiveTaskCreationService proactiveTaskCreationService,
        IApprovalRequestService approvalRequestService,
        ICompanyKnowledgeSearchService knowledgeSearchService,
        IFinanceToolProvider financeToolProvider,
        IFinanceTransactionAnomalyDetectionService financeAnomalyDetectionService,
        IFinanceAgentAnalysisService financeAgentAnalysisService,
        IFinanceLedgerAgentReadService financeLedgerAgentReadService,
        IFinanceCloseComplianceAgentService financeCloseComplianceAgentService,
        IFinanceAdvancedAccountingAgentService financeAdvancedAccountingAgentService,
        IFinanceAccountingDraftAgentService financeAccountingDraftAgentService,
        IFinanceOperationalProposalAgentService financeOperationalProposalAgentService,
        IFinanceGuardedCommandService financeGuardedCommandService,
        IAccountingProviderSwitchAgentService accountingProviderSwitchAgentService,
        ILeadGenerationService leadGenerationService)
    {
        _taskQueryService = taskQueryService;
        _taskCommandService = taskCommandService;
        _proactiveTaskCreationService = proactiveTaskCreationService;
        _approvalRequestService = approvalRequestService;
        _knowledgeSearchService = knowledgeSearchService;
        _financeToolProvider = financeToolProvider;
        _financeAnomalyDetectionService = financeAnomalyDetectionService;
        _financeAgentAnalysisService = financeAgentAnalysisService;
        _financeLedgerAgentReadService = financeLedgerAgentReadService;
        _financeCloseComplianceAgentService = financeCloseComplianceAgentService;
        _financeAdvancedAccountingAgentService = financeAdvancedAccountingAgentService;
        _financeAccountingDraftAgentService = financeAccountingDraftAgentService;
        _financeOperationalProposalAgentService = financeOperationalProposalAgentService;
        _financeGuardedCommandService = financeGuardedCommandService;
        _accountingProviderSwitchAgentService = accountingProviderSwitchAgentService;
        _leadGenerationService = leadGenerationService;
    }

    public async Task<InternalToolExecutionResponse> ExecuteAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Failed("company_context_required", "Tool execution requires a company context.");
        }

        if (request.AgentId == Guid.Empty)
        {
            return Failed("agent_context_required", "Tool execution requires an agent context.");
        }

        if (request.ExecutionId == Guid.Empty)
        {
            return Failed("execution_context_required", "Tool execution requires an execution context.");
        }

        try
        {
            FinanceExecuteToolReadinessContract? readiness = null;
            if (request.ActionKind == ToolActionType.Execute &&
                FinanceExecuteToolReadinessCatalog.TryGet(request.ToolName, out readiness))
            {
                var blockers = FinanceExecuteToolReadinessCatalog.ValidateRequest(readiness, request.Payload);
                if (blockers.Count > 0)
                    return DecorateFinanceCommandResponse(request, readiness,
                        Failed("finance_command_not_ready",
                            "The Finance command is missing current readiness evidence or exceeds its bounded batch contract."), blockers);
            }

            var response = request.ToolName.Trim().ToLowerInvariant() switch
            {
                "tasks.get" => await ExecuteTaskGetAsync(request, cancellationToken),
                "tasks.list" => await ExecuteTaskListAsync(request, cancellationToken),
                "tasks.update_status" => await ExecuteTaskStatusUpdateAsync(request, cancellationToken),
                "approvals.create_request" => await ExecuteApprovalCreateRequestAsync(request, cancellationToken),
                "knowledge.search" => await ExecuteKnowledgeSearchAsync(request, cancellationToken),
                "get_cash_balance" => await ExecuteGetCashBalanceAsync(request, cancellationToken),
                "list_transactions" => await ExecuteListTransactionsAsync(request, cancellationToken),
                "resolve_finance_agent_query" => await ExecuteResolveFinanceAgentQueryAsync(request, cancellationToken),
                "list_uncategorized_transactions" => await ExecuteListUncategorizedTransactionsAsync(request, cancellationToken),
                "list_invoices_awaiting_approval" => await ExecuteListInvoicesAwaitingApprovalAsync(request, cancellationToken),
                "get_profit_and_loss_summary" => await ExecuteGetProfitAndLossSummaryAsync(request, cancellationToken),
                "recommend_transaction_category" => await ExecuteRecommendTransactionCategoryAsync(request, cancellationToken),
                "recommend_invoice_approval_decision" => await ExecuteRecommendInvoiceApprovalDecisionAsync(request, cancellationToken),
                "evaluate_transaction_anomaly" => await ExecuteEvaluateTransactionAnomalyAsync(request, cancellationToken),
                FinanceAgentAnalysisToolIds.Analyze => await ExecuteFinanceAnalysisAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.LookupAccounts => await _financeLedgerAgentReadService.ExecuteAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadFiscalPeriods => await _financeLedgerAgentReadService.ExecuteAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.SearchJournals => await _financeLedgerAgentReadService.ExecuteAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadGeneralLedger => await _financeLedgerAgentReadService.ExecuteAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadTrialBalance => await _financeLedgerAgentReadService.ExecuteAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadStatement => await _financeLedgerAgentReadService.ExecuteAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadReportDefinitions => await _financeLedgerAgentReadService.ExecuteAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadReportSnapshot => await _financeLedgerAgentReadService.ExecuteAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadSourceDrilldown => await _financeLedgerAgentReadService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadTemplates => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadInstance => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadReadiness => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadPeriodLockHistory => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadComplianceObligations => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadAuditPackages => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadAccountantAccessActivity => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadYearEnd => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.PrioritizeCloseBlockers => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ExplainCompliancePreparation => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ExplainAuditPackageCompleteness => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ExplainYearEndPrerequisites => await _financeCloseComplianceAgentService.ExecuteAsync(request, cancellationToken),
                _ when FinanceAdvancedAccountingAgentToolIds.Contains(request.ToolName) => await _financeAdvancedAccountingAgentService.ExecuteAsync(request, cancellationToken),
                _ when FinanceAccountingDraftAgentToolIds.Contains(request.ToolName) => await _financeAccountingDraftAgentService.ExecuteAsync(request, cancellationToken),
                _ when FinanceOperationalProposalAgentToolIds.Contains(request.ToolName) => await _financeOperationalProposalAgentService.ExecuteAsync(request, cancellationToken),
                "categorize_transaction" => await ExecuteCategorizeTransactionAsync(request, cancellationToken),
                FinanceGuardedCommandToolIds.CategorizeTransactions => await ExecuteCategorizeTransactionsAsync(request, cancellationToken),
                "approve_invoice" => await ExecuteApproveInvoiceAsync(request, cancellationToken),
                "post_paid_supplier_bill_expense" => await ExecutePostPaidSupplierBillExpenseAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadBriefing => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadStatus => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadCapabilities => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadInventory => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadGaps => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadMappings => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadRehearsal => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadReconciliation => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadApprovals => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadTransferProgress => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadMonitoring => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ReadAuditEvidence => await ExecuteMigrationReadAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.RecommendEffectivePeriod => await ExecuteMigrationRecommendationAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.RecommendStrategy => await ExecuteMigrationRecommendationAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.RecommendMapping => await ExecuteMigrationRecommendationAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.RecommendGapResolution => await ExecuteMigrationRecommendationAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.RecommendRequiredEvidence => await ExecuteMigrationRecommendationAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.RecommendCutoverPlan => await ExecuteMigrationRecommendationAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.RecommendFreezeWindow => await ExecuteMigrationRecommendationAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.RecommendMonitoringPeriod => await ExecuteMigrationRecommendationAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ExplainReadiness => await ExecuteMigrationRecommendationAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.StartAssessment => await ExecuteMigrationCommandAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.StartRehearsal => await ExecuteMigrationCommandAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.StartPreparation => await ExecuteMigrationCommandAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ApplyApprovedMapping => await ExecuteMigrationCommandAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.CreateFollowUpTask => await ExecuteMigrationCommandAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.RequestPlanApproval => await ExecuteMigrationCommandAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.StartApprovedFreeze => await ExecuteMigrationCommandAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.RequestActivationApproval => await ExecuteMigrationCommandAsync(request, cancellationToken),
                AccountingProviderSwitchAgentToolIds.ResumeRecovery => await ExecuteMigrationCommandAsync(request, cancellationToken),
                "sales.plan_prospecting_run" => await ExecutePlanProspectingRunAsync(request, cancellationToken),
                "sales.start_prospecting_run" => await ExecuteStartProspectingRunAsync(request, cancellationToken),
                "sales.list_prospects" => await ExecuteListProspectsAsync(request, cancellationToken),
                "sales.research_prospect" => await ExecuteResearchProspectAsync(request, cancellationToken),
                "sales.recommend_prospect_decision" => await ExecuteRecommendProspectDecisionAsync(request, cancellationToken),
                _ => Failed("unsupported_internal_tool", "The requested internal tool is not available.")
            };
            return readiness is null ? response : DecorateFinanceCommandResponse(request, readiness, response);
        }
        catch (TaskValidationException)
        {
            return Failed("task_validation_failed", "The task tool request was not valid.");
        }
        catch (ApprovalValidationException)
        {
            return Failed("approval_validation_failed", "The approval request was not valid.");
        }
        catch (CompanyKnowledgeSearchValidationException)
        {
            return Failed("knowledge_search_validation_failed", "The knowledge search request was not valid.");
        }
        catch (ArgumentException)
        {
            return Failed("finance_tool_validation_failed", "The finance tool request was not valid.");
        }
        catch (AccountingAuthorityException ex)
        {
            return Failed(ex.ReasonCode, SafeMigrationFailure(ex));
        }
        catch (KeyNotFoundException)
        {
            return Failed("tool_target_not_found", "The requested internal record was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failed("tool_access_denied", "The requested internal tool could not access the requested company record.");
        }
        catch (LeadGenerationValidationException ex)
        {
            return Failed("sales_tool_validation_failed", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Failed("finance_tool_not_available", ex.Message);
        }
    }

    private async Task<InternalToolExecutionResponse> ExecutePlanProspectingRunAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var failure)) return failure;
        var profileId = ReadGuid(request.Payload, "icpProfileId");
        if (profileId is null) return Failed("icp_profile_required", "An ideal customer profile is required.");
        var run = await _leadGenerationService.CreateRunAsync(request.CompanyId, request.AgentId, new CreateProspectingRunRequest(profileId.Value, ReadString(request.Payload,"name") ?? "Alex prospecting plan", Math.Clamp(ReadInt(request.Payload,"accountLimit") ?? 50,1,10000), Math.Clamp(ReadInt(request.Payload,"contactLimit") ?? 100,0,50000), ReadString(request.Payload,"sources") ?? "first_party", ReadString(request.Payload,"geography") ?? "", Math.Clamp(ReadInt(request.Payload,"freshnessDays") ?? 30,1,365), ReadDecimal(request.Payload,"estimatedCost") ?? 0, ReadString(request.Payload,"schedule")), ct);
        return InternalToolExecutionResponse.Succeeded("Prospecting run planned.", new Dictionary<string,JsonNode?> { ["prospectingRun"] = Serialize(run) }, Metadata(request,"lead_generation_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteStartProspectingRunAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var failure)) return failure;
        var id=ReadGuid(request.Payload,"runId"); if(id is null) return Failed("run_id_required","A prospecting run is required.");
        var run=await _leadGenerationService.StartRunAsync(request.CompanyId,request.AgentId,id.Value,ct);
        return InternalToolExecutionResponse.Succeeded(run.Status == "Planned" ? "Prospecting run is waiting for approval." : "Prospecting run started.",new Dictionary<string,JsonNode?>{{"prospectingRun",Serialize(run)}},Metadata(request,"lead_generation_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteListProspectsAsync(InternalToolExecutionRequest request,CancellationToken ct)
    {
        if(!EnsureAction(request,ToolActionType.Read,out var failure)) return failure;
        var result=await _leadGenerationService.ListAccountsAsync(request.CompanyId,new ProspectQuery(ReadString(request.Payload,"search"),ReadString(request.Payload,"status"),ReadString(request.Payload,"country"),ReadString(request.Payload,"source"),ReadInt(request.Payload,"page")??1,Math.Clamp(ReadInt(request.Payload,"pageSize")??50,1,100)),ct);
        return InternalToolExecutionResponse.Succeeded("Prospects retrieved.",new Dictionary<string,JsonNode?>{{"prospects",Serialize(result)}},Metadata(request,"lead_generation_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteResearchProspectAsync(InternalToolExecutionRequest request,CancellationToken ct)
    {
        if(!EnsureAction(request,ToolActionType.Recommend,out var failure)) return failure; var id=ReadGuid(request.Payload,"prospectId"); if(id is null)return Failed("prospect_id_required","A prospect is required.");
        var prospect=await _leadGenerationService.RefreshResearchAndScoreAsync(request.CompanyId,request.AgentId,id.Value,ct);
        return InternalToolExecutionResponse.Succeeded("Prospect research refreshed.",new Dictionary<string,JsonNode?>{{"prospect",Serialize(prospect)}},Metadata(request,"lead_generation_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteRecommendProspectDecisionAsync(InternalToolExecutionRequest request,CancellationToken ct)
    {
        if(!EnsureAction(request,ToolActionType.Recommend,out var failure)) return failure; var id=ReadGuid(request.Payload,"prospectId"); if(id is null)return Failed("prospect_id_required","A prospect is required."); var p=await _leadGenerationService.GetAccountAsync(request.CompanyId,id.Value,ct); if(p is null)return Failed("prospect_not_found","The prospect was not found.");
        var decision=p.FitOutcome=="disqualified"||p.OverallScore<40?"reject":p.OverallScore>=70?"accept":"review"; var explanation=decision switch{"accept"=>$"Strong fit with an overall score of {p.OverallScore:0}.","reject"=>$"Low fit or disqualifying criteria with an overall score of {p.OverallScore:0}.",_=>$"More evidence is needed before deciding; current score is {p.OverallScore:0}."};
        return InternalToolExecutionResponse.Succeeded("Prospect decision prepared.",new Dictionary<string,JsonNode?>{{"recommendation",Serialize(new{prospectId=p.Id,decision,explanation,p.OverallScore,p.DataConfidenceScore})}},Metadata(request,"lead_generation_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteTaskGetAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var taskId = ReadGuid(request.Payload, "taskId") ?? request.TaskId;
        if (!taskId.HasValue)
        {
            return Failed("task_id_required", "A task id is required to read task details.");
        }

        var task = await _taskQueryService.GetByIdAsync(
            request.CompanyId,
            new GetTaskByIdQuery(taskId.Value),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Task details were retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["task"] = Serialize(task),
                ["taskId"] = JsonValue.Create(task.Id),
                ["status"] = JsonValue.Create(task.Status)
            },
            Metadata(request, "task_query_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteTaskListAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var result = await _taskQueryService.ListAsync(
            request.CompanyId,
            new ListTasksQuery(
                ReadString(request.Payload, "status"),
                ReadGuid(request.Payload, "assignedAgentId"),
                ReadGuid(request.Payload, "parentTaskId"),
                ReadDateTime(request.Payload, "dueBefore"),
                ReadDateTime(request.Payload, "dueAfter"),
                ReadInt(request.Payload, "skip"),
                ReadInt(request.Payload, "take")),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Tasks were retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["tasks"] = Serialize(result.Items),
                ["totalCount"] = JsonValue.Create(result.TotalCount),
                ["skip"] = JsonValue.Create(result.Skip),
                ["take"] = JsonValue.Create(result.Take)
            },
            Metadata(request, "task_query_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteTaskStatusUpdateAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure))
        {
            return actionFailure;
        }

        var taskId = ReadGuid(request.Payload, "taskId") ?? request.TaskId;
        if (!taskId.HasValue)
        {
            return Failed("task_id_required", "A task id is required to update task status.");
        }

        var status = ReadString(request.Payload, "status");
        if (string.IsNullOrWhiteSpace(status))
        {
            return Failed("task_status_required", "A target task status is required.");
        }

        var result = await _taskCommandService.UpdateStatusAsync(
            request.CompanyId,
            taskId.Value,
            new UpdateTaskStatusCommand(
                status,
                ReadObject(request.Payload, "outputPayload"),
                ReadString(request.Payload, "rationaleSummary"),
                ReadDecimal(request.Payload, "confidenceScore")),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Task status was updated.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["taskId"] = JsonValue.Create(result.Id),
                ["companyId"] = JsonValue.Create(result.CompanyId),
                ["status"] = JsonValue.Create(result.Status),
                ["updatedAt"] = JsonValue.Create(result.UpdatedAt)
            },
            Metadata(request, "task_command_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteApprovalCreateRequestAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure))
        {
            return actionFailure;
        }

        var targetEntityType = ReadString(request.Payload, "targetEntityType") ?? "task";
        var targetEntityId = ReadGuid(request.Payload, "targetEntityId") ?? request.TaskId;
        if (!targetEntityId.HasValue)
        {
            return Failed("approval_target_required", "An approval target is required.");
        }

        var requiredUserId = ReadGuid(request.Payload, "requiredUserId");
        var requiredRole = ReadString(request.Payload, "requiredRole");
        var steps = ReadApprovalSteps(request.Payload);
        if (requiredUserId is null && string.IsNullOrWhiteSpace(requiredRole) && steps.Count == 0)
        {
            return Failed("approval_route_required", "An approval request requires at least one approver.");
        }

        var thresholdContext = ReadObject(request.Payload, "thresholdContext") ??
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["toolName"] = JsonValue.Create(request.ToolName),
                ["actionType"] = JsonValue.Create(request.ActionType),
                ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope),
                ["executionId"] = JsonValue.Create(request.ExecutionId),
                ["correlationId"] = string.IsNullOrWhiteSpace(request.CorrelationId) ? null : JsonValue.Create(request.CorrelationId)
            };

        var approval = await _approvalRequestService.CreateAsync(
            request.CompanyId,
            new CreateApprovalRequestCommand(
                targetEntityType,
                targetEntityId.Value,
                ReadString(request.Payload, "requestedByActorType") ?? "agent",
                ReadGuid(request.Payload, "requestedByActorId") ?? request.AgentId,
                ReadString(request.Payload, "approvalType") ?? "tool_execution",
                thresholdContext,
                requiredRole,
                requiredUserId,
                steps),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Approval request was created.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["approvalRequest"] = Serialize(approval),
                ["approvalRequestId"] = JsonValue.Create(approval.Id),
                ["approvalStatus"] = JsonValue.Create(approval.Status)
            },
            Metadata(request, "approval_request_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteKnowledgeSearchAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Context.ActionType is not ToolActionType.Read and not ToolActionType.Recommend)
        {
            return Failed("unsupported_action_type", "Knowledge search only supports read or recommend actions.");
        }

        var queryText = ReadString(request.Payload, "query") ?? ReadString(request.Payload, "queryText");
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Failed("knowledge_query_required", "A knowledge search query is required.");
        }

        var topN = Math.Clamp(ReadInt(request.Payload, "topN") ?? 5, 1, 20);
        var results = await _knowledgeSearchService.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(
                request.CompanyId,
                queryText,
                topN,
                new CompanyKnowledgeAccessContext(
                    request.CompanyId,
                    AgentId: request.AgentId)),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Knowledge search completed.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["results"] = Serialize(results),
                ["resultCount"] = JsonValue.Create(results.Count),
                ["query"] = JsonValue.Create(queryText)
            },
            Metadata(request, "knowledge_search_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteGetCashBalanceAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var balance = await _financeToolProvider.GetCashBalanceAsync(
            new GetFinanceCashBalanceQuery(request.CompanyId, ReadDateTime(request.Payload, "asOfUtc")),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Cash balance was retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["cashBalance"] = Serialize(balance),
                ["amount"] = JsonValue.Create(balance.Amount),
                ["currency"] = JsonValue.Create(balance.Currency),
                ["asOfUtc"] = JsonValue.Create(balance.AsOfUtc)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteResolveFinanceAgentQueryAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var queryText = ReadString(request.Payload, "queryText");
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Failed("finance_agent_query_required", "A supported finance agent query is required.");
        }

        var result = await _financeToolProvider.ResolveAgentQueryAsync(
            new GetFinanceAgentQueryQuery(request.CompanyId, queryText, ReadDateTime(request.Payload, "asOfUtc")),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            result.Summary,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["result"] = Serialize(result)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteListTransactionsAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var transactions = await _financeToolProvider.GetTransactionsAsync(
            new GetFinanceTransactionsQuery(
                request.CompanyId,
                ReadDateTime(request.Payload, "startUtc"),
                ReadDateTime(request.Payload, "endUtc"),
                ReadInt(request.Payload, "limit") ?? 100),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Finance transactions were retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transactions"] = Serialize(transactions),
                ["count"] = JsonValue.Create(transactions.Count)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteListUncategorizedTransactionsAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var transactions = await _financeToolProvider.GetTransactionsAsync(
            new GetFinanceTransactionsQuery(
                request.CompanyId,
                ReadDateTime(request.Payload, "startUtc"),
                ReadDateTime(request.Payload, "endUtc"),
                ReadInt(request.Payload, "limit") ?? 100),
            cancellationToken);

        var uncategorized = transactions
            .Where(transaction =>
                string.IsNullOrWhiteSpace(transaction.TransactionType) ||
                string.Equals(transaction.TransactionType, "uncategorized", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return InternalToolExecutionResponse.Succeeded(
            "Uncategorized finance transactions were retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transactions"] = Serialize(uncategorized),
                ["count"] = JsonValue.Create(uncategorized.Count)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteListInvoicesAwaitingApprovalAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var invoices = await _financeToolProvider.GetInvoicesAsync(
            new GetFinanceInvoicesQuery(
                request.CompanyId,
                ReadDateTime(request.Payload, "startUtc"),
                ReadDateTime(request.Payload, "endUtc"),
                ReadInt(request.Payload, "limit") ?? 100),
            cancellationToken);

        var awaitingApproval = invoices
            .Where(invoice =>
                string.Equals(invoice.Status, "awaiting_approval", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(invoice.Status, "pending_approval", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(invoice.Status, "pending", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return InternalToolExecutionResponse.Succeeded(
            "Invoices awaiting approval were retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoices"] = Serialize(awaitingApproval),
                ["count"] = JsonValue.Create(awaitingApproval.Count)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteGetProfitAndLossSummaryAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var summary = await _financeToolProvider.GetMonthlyProfitAndLossAsync(
            new GetFinanceMonthlyProfitAndLossQuery(
                request.CompanyId,
                ReadInt(request.Payload, "year") ?? DateTime.UtcNow.Year,
                ReadInt(request.Payload, "month") ?? DateTime.UtcNow.Month),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Profit and loss summary was retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["profitAndLossSummary"] = Serialize(summary),
                ["revenue"] = JsonValue.Create(summary.Revenue),
                ["expenses"] = JsonValue.Create(summary.Expenses),
                ["netResult"] = JsonValue.Create(summary.NetResult),
                ["currency"] = JsonValue.Create(summary.Currency)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteRecommendTransactionCategoryAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var actionFailure))
        {
            return actionFailure;
        }

        var transactionId = ReadGuid(request.Payload, "transactionId");
        if (!transactionId.HasValue)
        {
            return Failed("transaction_id_required", "A transaction id is required to recommend a category.");
        }

        var recommendation = await _financeToolProvider.RecommendTransactionCategoryAsync(request, cancellationToken);
        return InternalToolExecutionResponse.Succeeded(
            "Transaction category recommendation was prepared.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["recommendation"] = new JsonObject
                {
                    ["transactionId"] = JsonValue.Create(recommendation.TransactionId),
                    ["recommendedCategory"] = JsonValue.Create(recommendation.RecommendedCategory),
                    ["confidence"] = JsonValue.Create(recommendation.Confidence)
                }
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteRecommendInvoiceApprovalDecisionAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var actionFailure))
        {
            return actionFailure;
        }

        var invoiceId = ReadGuid(request.Payload, "invoiceId");
        if (!invoiceId.HasValue)
        {
            return Failed("invoice_id_required", "An invoice id is required to recommend an approval decision.");
        }

        var recommendation = await _financeToolProvider.RecommendInvoiceApprovalDecisionAsync(request, cancellationToken);
        return InternalToolExecutionResponse.Succeeded(
            "Invoice approval recommendation was prepared.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["recommendation"] = new JsonObject
                {
                    ["invoiceId"] = JsonValue.Create(recommendation.InvoiceId),
                    ["recommendedStatus"] = JsonValue.Create(recommendation.RecommendedStatus),
                    ["confidence"] = JsonValue.Create(recommendation.Confidence)
                }
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteEvaluateTransactionAnomalyAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var actionFailure))
        {
            return actionFailure;
        }

        var transactionId = ReadGuid(request.Payload, "transactionId");
        if (!transactionId.HasValue)
        {
            return Failed("transaction_id_required", "A transaction id is required to evaluate anomalies.");
        }

        var evaluation = await _financeAnomalyDetectionService.EvaluateAsync(
            new EvaluateFinanceTransactionAnomalyCommand(
                request.CompanyId,
                transactionId.Value,
                ReadGuid(request.Payload, "workflowInstanceId"),
                request.AgentId),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Transaction anomaly evaluation was completed.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["anomalyEvaluation"] = Serialize(evaluation),
                ["isAnomalous"] = JsonValue.Create(evaluation.IsAnomalous),
                ["anomalyCount"] = JsonValue.Create(evaluation.Anomalies.Count)
            },
            Metadata(request, "finance_anomaly_detection_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteCategorizeTransactionAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure))
        {
            return actionFailure;
        }

        var transactionId = ReadGuid(request.Payload, "transactionId");
        var category = ReadString(request.Payload, "category");
        if (!transactionId.HasValue || string.IsNullOrWhiteSpace(category))
        {
            return Failed("transaction_category_required", "A transaction id and category are required.");
        }

        var transaction = await _financeToolProvider.UpdateTransactionCategoryAsync(
            new UpdateFinanceTransactionCategoryCommand(request.CompanyId, transactionId.Value, category),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Transaction category was updated.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transaction"] = Serialize(transaction),
                ["transactionId"] = JsonValue.Create(transaction.Id),
                ["category"] = JsonValue.Create(transaction.TransactionType)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteApproveInvoiceAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure))
        {
            return actionFailure;
        }

        var invoiceId = ReadGuid(request.Payload, "invoiceId");
        var status = ReadString(request.Payload, "status") ?? "approved";
        if (!invoiceId.HasValue)
        {
            return Failed("invoice_id_required", "An invoice id is required to approve an invoice.");
        }

        var invoice = await _financeToolProvider.UpdateInvoiceApprovalStatusAsync(
            new UpdateFinanceInvoiceApprovalStatusCommand(request.CompanyId, invoiceId.Value, status),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Invoice approval status was updated.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoice"] = Serialize(invoice),
                ["invoiceId"] = JsonValue.Create(invoice.Id),
                ["status"] = JsonValue.Create(invoice.Status)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecutePostPaidSupplierBillExpenseAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure))
        {
            return actionFailure;
        }

        var billId = ReadGuid(request.Payload, "billId");
        if (!billId.HasValue)
        {
            return Failed("bill_id_required", "A supplier bill id is required to post an expense.");
        }

        var posting = await _financeToolProvider.PostPaidSupplierBillExpenseAsync(
            new PostPaidSupplierBillExpenseCommand(
                request.CompanyId,
                billId.Value,
                null,
                "Laura",
                ReadString(request.Payload, "providerKey")),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            posting.Summary,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["expensePosting"] = Serialize(posting),
                ["billId"] = JsonValue.Create(posting.BillId),
                ["draftActionId"] = JsonValue.Create(posting.DraftActionId),
                ["status"] = JsonValue.Create(posting.Status),
                ["posted"] = JsonValue.Create(posting.Posted)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteCategorizeTransactionsAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure)) return actionFailure;
        if (!request.ActorUserId.HasValue || request.ActorUserId == Guid.Empty)
            return Failed("finance_actor_required", "A current Finance actor is required.");
        if (!request.Payload.TryGetValue("items", out var node) || node is not JsonArray items)
            return Failed("categorization_items_required", "At least one categorization item is required.");

        var parsed = new List<GuardedTransactionCategorizationItem>(items.Count);
        foreach (var item in items.OfType<JsonObject>())
        {
            var values = item.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            parsed.Add(new(
                ReadGuid(values, "transactionId") ?? Guid.Empty,
                ReadString(values, "expectedCategory") ?? string.Empty,
                ReadString(values, "category") ?? string.Empty));
        }
        if (parsed.Count != items.Count)
            return Failed("categorization_items_invalid", "Every categorization item must be a structured object.");

        var result = await _financeGuardedCommandService.CategorizeTransactionsAsync(new(
            request.CompanyId, request.ActorUserId.Value, request.AgentId,
            ReadString(request.Payload, "idempotencyKey") ?? string.Empty, parsed,
            request.CorrelationId ?? request.ExecutionId.ToString("N")), cancellationToken);
        return InternalToolExecutionResponse.Succeeded(result.Summary,
            new Dictionary<string, JsonNode?> { ["categorizationBatch"] = Serialize(result) },
            Metadata(request, "finance_guarded_command_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteFinanceAnalysisAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var actionFailure))
        {
            return actionFailure;
        }

        var analysisType = ReadString(request.Payload, "analysisType");
        if (string.IsNullOrWhiteSpace(analysisType) || !FinanceAgentAnalysisTypes.All.Contains(analysisType))
        {
            return Failed("finance_analysis_type_unsupported", "A supported Finance analysis type is required.");
        }

        var result = await _financeAgentAnalysisService.AnalyzeAsync(
            request.CompanyId,
            request.AgentId,
            request.ActorUserId,
            new RoleAgentAnalysisRequest(
                analysisType,
                ReadGuid(request.Payload, "subjectId"),
                ReadInt(request.Payload, "horizonDays") ?? 30,
                ReadString(request.Payload, "objective"),
                ReadDateTime(request.Payload, "asOfUtc"),
                ReadString(request.Payload, "cadence") ?? "on_demand"),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            result.Summary,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["analysis"] = Serialize(result),
                ["analysisType"] = JsonValue.Create(analysisType),
                ["asOfUtc"] = JsonValue.Create(result.AsOfUtc)
            },
            Metadata(request, "finance_agent_analysis_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteMigrationReadAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var failure)) return failure;
        var switchId = ReadGuid(request.Payload, "switchId");
        if (!switchId.HasValue) return Failed("migration_switch_required", "Select an accounting migration to continue.");
        var limit = Math.Clamp(ReadInt(request.Payload, "limit") ?? 20, 1, 50);

        if (string.Equals(request.ToolName, AccountingProviderSwitchAgentToolIds.ReadBriefing, StringComparison.OrdinalIgnoreCase))
        {
            var briefing = await _accountingProviderSwitchAgentService.GetBriefingAsync(
                new(request.CompanyId, switchId.Value, limit), cancellationToken);
            return InternalToolExecutionResponse.Succeeded(
                "Laura prepared a current evidence-backed migration briefing.",
                new Dictionary<string, JsonNode?> { ["briefing"] = Serialize(briefing) },
                Metadata(request, "accounting_provider_switch_agent_service"));
        }

        var view = request.ToolName switch
        {
            AccountingProviderSwitchAgentToolIds.ReadStatus => AccountingProviderSwitchAgentEvidenceViews.Status,
            AccountingProviderSwitchAgentToolIds.ReadCapabilities => AccountingProviderSwitchAgentEvidenceViews.Capabilities,
            AccountingProviderSwitchAgentToolIds.ReadInventory => AccountingProviderSwitchAgentEvidenceViews.Inventory,
            AccountingProviderSwitchAgentToolIds.ReadGaps => AccountingProviderSwitchAgentEvidenceViews.Gaps,
            AccountingProviderSwitchAgentToolIds.ReadMappings => AccountingProviderSwitchAgentEvidenceViews.Mappings,
            AccountingProviderSwitchAgentToolIds.ReadRehearsal => AccountingProviderSwitchAgentEvidenceViews.Rehearsal,
            AccountingProviderSwitchAgentToolIds.ReadReconciliation => AccountingProviderSwitchAgentEvidenceViews.Reconciliation,
            AccountingProviderSwitchAgentToolIds.ReadApprovals => AccountingProviderSwitchAgentEvidenceViews.Approvals,
            AccountingProviderSwitchAgentToolIds.ReadTransferProgress => AccountingProviderSwitchAgentEvidenceViews.TransferProgress,
            AccountingProviderSwitchAgentToolIds.ReadMonitoring => AccountingProviderSwitchAgentEvidenceViews.Monitoring,
            AccountingProviderSwitchAgentToolIds.ReadAuditEvidence => AccountingProviderSwitchAgentEvidenceViews.Audit,
            _ => throw new ArgumentException("The migration evidence tool is not supported.")
        };
        var evidence = await _accountingProviderSwitchAgentService.GetEvidenceAsync(
            new(request.CompanyId, switchId.Value, view, limit), cancellationToken);
        return InternalToolExecutionResponse.Succeeded(
            "Laura retrieved current persisted migration evidence.",
            new Dictionary<string, JsonNode?> { ["evidence"] = Serialize(evidence) },
            Metadata(request, "accounting_provider_switch_agent_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteMigrationRecommendationAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var failure)) return failure;
        var recommendation = await _accountingProviderSwitchAgentService.RecommendAsync(
            new RecommendAccountingProviderSwitchActionQuery(
                request.CompanyId,
                ReadGuid(request.Payload, "switchId"),
                request.ToolName,
                ReadString(request.Payload, "sourceKind"),
                ReadString(request.Payload, "sourceProviderKey"),
                ReadString(request.Payload, "targetKind"),
                ReadString(request.Payload, "targetProviderKey"),
                ReadString(request.Payload, "requestedStrategy"),
                Math.Clamp(ReadInt(request.Payload, "limit") ?? 20, 1, 50)),
            cancellationToken);
        return InternalToolExecutionResponse.Succeeded(
            "Laura prepared a migration recommendation from current Finance evidence.",
            new Dictionary<string, JsonNode?> { ["recommendation"] = Serialize(recommendation) },
            Metadata(request, "accounting_provider_switch_agent_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteMigrationCommandAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var failure)) return failure;
        var switchId = ReadGuid(request.Payload, "switchId");
        var expectedSwitchVersion = ReadLong(request.Payload, "expectedSwitchVersion");
        var idempotencyKey = ReadString(request.Payload, "idempotencyKey");
        if (!switchId.HasValue || !expectedSwitchVersion.HasValue || expectedSwitchVersion <= 0 ||
            string.IsNullOrWhiteSpace(idempotencyKey) || !request.ActorUserId.HasValue || request.ActorUserId == Guid.Empty)
            return Failed("migration_context_stale_or_incomplete",
                "Read the current migration briefing and retry with its switch version and current approval context.");

        var context = new AccountingProviderSwitchAgentCommandContext(
            request.CompanyId, switchId.Value, expectedSwitchVersion.Value, request.ActorUserId.Value,
            request.AgentId, request.CorrelationId ?? request.ExecutionId.ToString("N"), idempotencyKey);
        AccountingProviderSwitchAgentCommandResultDto result;

        switch (request.ToolName)
        {
            case AccountingProviderSwitchAgentToolIds.StartAssessment:
                result = await _accountingProviderSwitchAgentService.StartAssessmentAsync(context, cancellationToken);
                break;
            case AccountingProviderSwitchAgentToolIds.StartRehearsal:
                result = await _accountingProviderSwitchAgentService.StartRehearsalAsync(context, cancellationToken);
                break;
            case AccountingProviderSwitchAgentToolIds.StartPreparation:
                var planId = ReadGuid(request.Payload, "planId") ?? throw new ArgumentException("PlanId is required.");
                result = await _accountingProviderSwitchAgentService.StartPreparationAsync(context, planId, cancellationToken);
                break;
            case AccountingProviderSwitchAgentToolIds.ApplyApprovedMapping:
                result = await _accountingProviderSwitchAgentService.ApplyApprovedMappingAsync(
                    context,
                    ReadGuid(request.Payload, "stagedRecordId") ?? throw new ArgumentException("StagedRecordId is required."),
                    ReadGuid(request.Payload, "mappingDecisionId") ?? throw new ArgumentException("MappingDecisionId is required."),
                    ReadLong(request.Payload, "expectedRecordVersion") ?? throw new ArgumentException("ExpectedRecordVersion is required."),
                    ReadString(request.Payload, "disposition") ?? throw new ArgumentException("Disposition is required."),
                    cancellationToken);
                break;
            case AccountingProviderSwitchAgentToolIds.RequestPlanApproval:
                result = await _accountingProviderSwitchAgentService.RequestPlanApprovalAsync(context,
                    ReadGuid(request.Payload, "planId") ?? throw new ArgumentException("PlanId is required."), cancellationToken);
                break;
            case AccountingProviderSwitchAgentToolIds.StartApprovedFreeze:
                result = await _accountingProviderSwitchAgentService.StartApprovedFreezeAsync(context,
                    ReadGuid(request.Payload, "cutoverExecutionId") ?? throw new ArgumentException("CutoverExecutionId is required."),
                    ReadLong(request.Payload, "expectedExecutionVersion") ?? throw new ArgumentException("ExpectedExecutionVersion is required."), cancellationToken);
                break;
            case AccountingProviderSwitchAgentToolIds.RequestActivationApproval:
                result = await _accountingProviderSwitchAgentService.RequestActivationApprovalAsync(context,
                    ReadGuid(request.Payload, "cutoverExecutionId") ?? throw new ArgumentException("CutoverExecutionId is required."),
                    ReadLong(request.Payload, "expectedExecutionVersion") ?? throw new ArgumentException("ExpectedExecutionVersion is required."), cancellationToken);
                break;
            case AccountingProviderSwitchAgentToolIds.ResumeRecovery:
                result = await _accountingProviderSwitchAgentService.ResumeRecoveryAsync(context,
                    ReadGuid(request.Payload, "cutoverExecutionId") ?? throw new ArgumentException("CutoverExecutionId is required."),
                    ReadLong(request.Payload, "expectedExecutionVersion") ?? throw new ArgumentException("ExpectedExecutionVersion is required."), cancellationToken);
                break;
            case AccountingProviderSwitchAgentToolIds.CreateFollowUpTask:
                var briefing = await _accountingProviderSwitchAgentService.GetBriefingAsync(
                    new(request.CompanyId, switchId.Value, 20), cancellationToken);
                if (briefing.SwitchVersion != expectedSwitchVersion.Value)
                    return Failed(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
                        "The migration changed. Read the current briefing before creating a follow-up task.");
                var task = await _proactiveTaskCreationService.CreateAsync(new CreateAgentInitiatedTaskCommand(
                    new ProactiveTaskTrigger(
                        request.CompanyId,
                        request.AgentId,
                        "accounting_provider_switch",
                        $"{switchId.Value:N}:{expectedSwitchVersion.Value}:{idempotencyKey}",
                        request.CorrelationId ?? request.ExecutionId.ToString("N"),
                        "A current accounting migration integrity item needs durable follow-up.",
                        new Dictionary<string, JsonNode?>
                        {
                            ["switchId"] = JsonValue.Create(switchId.Value),
                            ["switchVersion"] = JsonValue.Create(expectedSwitchVersion.Value),
                            ["financialIntegrityGap"] = JsonValue.Create(true),
                            ["dataSources"] = new JsonArray("accounting switch", "migration briefing")
                        },
                        TaskType: "finance.accounting_migration_follow_up",
                        TaskTitle: ReadString(request.Payload, "title") ?? "Review accounting migration evidence",
                        TaskDescription: ReadString(request.Payload, "description"),
                        TaskPriority: ReadString(request.Payload, "priority") ?? "high",
                        AssignedAgentId: request.AgentId)), cancellationToken);
                return InternalToolExecutionResponse.Succeeded(
                    task.Duplicate
                        ? "The existing durable finance follow-up task already covers this migration version."
                        : "A durable finance follow-up task was created from the current migration version.",
                    new Dictionary<string, JsonNode?> { ["task"] = Serialize(task) },
                    Metadata(request, "company_task_command_service"));
            default:
                return Failed("unsupported_migration_command", "This accounting migration command is not available.");
        }

        return InternalToolExecutionResponse.Succeeded(
            result.Summary,
            new Dictionary<string, JsonNode?> { ["commandResult"] = Serialize(result) },
            Metadata(request, "accounting_provider_switch_agent_service"));
    }

    private static string SafeMigrationFailure(AccountingAuthorityException exception)
    {
        if (exception.ReasonCode.Contains("not_found", StringComparison.OrdinalIgnoreCase))
            return "The accounting migration is unavailable for this company.";
        if (exception.IsConflict || exception.ReasonCode.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
            exception.ReasonCode.Contains("concurrency", StringComparison.OrdinalIgnoreCase))
            return "The accounting migration changed. Read the current briefing and retry from its allowed actions.";
        if (exception.ReasonCode.Contains("reconciliation_required", StringComparison.OrdinalIgnoreCase))
            return "The provider outcome must be reconciled before this action can continue; do not retry it blindly.";
        return "The accounting migration action could not continue. Read the current briefing and follow its allowed recovery action.";
    }

    private static bool EnsureAction(
        InternalToolExecutionRequest request,
        ToolActionType expectedAction,
        out InternalToolExecutionResponse failure)
    {
        if (request.Context.ActionType == expectedAction)
        {
            failure = null!;
            return true;
        }

        failure = Failed(
            "unsupported_action_type",
            $"The {request.ToolName} tool does not support the requested action type.");
        return false;
    }

    private static InternalToolExecutionResponse DecorateFinanceCommandResponse(
        InternalToolExecutionRequest request,
        FinanceExecuteToolReadinessContract readiness,
        InternalToolExecutionResponse response,
        IReadOnlyList<string>? readinessBlockers = null)
    {
        var data = response.Data.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(),
            StringComparer.OrdinalIgnoreCase);
        var metadata = response.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(),
            StringComparer.OrdinalIgnoreCase);
        var requested = new JsonObject(request.Payload.Select(pair =>
            KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())).ToArray());
        var actual = new JsonObject(response.Data.Select(pair =>
            KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())).ToArray());
        var blockers = new JsonArray((readinessBlockers ?? []).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        var itemDecisions = response.Data.TryGetValue("categorizationBatch", out var batch) &&
                            batch?["items"] is JsonArray batchItems
            ? batchItems.DeepClone()
            : new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["outcome"] = response.Success ? "applied_or_accepted" : "rejected",
                ["reasonCode"] = response.Success ? "authoritative_command_completed" : response.ErrorCode,
                ["mutated"] = response.Success
            });
        data["commandEffect"] = new JsonObject
        {
            ["contractVersion"] = readiness.ContractVersion,
            ["toolName"] = readiness.ToolName,
            ["requested"] = requested,
            ["actual"] = actual,
            ["afterState"] = response.Success ? actual.DeepClone() : null,
            ["itemDecisions"] = itemDecisions,
            ["readinessBlockers"] = blockers,
            ["externalEffectClassification"] = readiness.ExternalEffectClassification,
            ["retryBehavior"] = readiness.RetryBehavior,
            ["reconciliationBehavior"] = readiness.ReconciliationBehavior,
            ["rollbackOrRecoveryBehavior"] = readiness.RollbackOrRecoveryBehavior
        };
        metadata["financeExecuteReadiness"] = Serialize(readiness);
        metadata["requestedActualEffectRecorded"] = JsonValue.Create(true);
        return response with { Data = data, Metadata = metadata };
    }

    private static InternalToolExecutionResponse Failed(string errorCode, string userSafeSummary) =>
        InternalToolExecutionResponse.Failed("failed", errorCode, userSafeSummary);

    private static Dictionary<string, JsonNode?> Metadata(InternalToolExecutionRequest request, string contractName) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["contractName"] = JsonValue.Create(contractName),
            ["companyId"] = JsonValue.Create(request.CompanyId),
            ["agentId"] = JsonValue.Create(request.AgentId),
            ["executionId"] = JsonValue.Create(request.ExecutionId),
            ["toolName"] = JsonValue.Create(request.ToolName),
            ["actionType"] = JsonValue.Create(request.ActionType),
            ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope),
            ["toolVersion"] = string.IsNullOrWhiteSpace(request.ToolVersion) ? null : JsonValue.Create(request.ToolVersion),
            ["typedBoundary"] = JsonValue.Create(true)
        };

    private static JsonNode? Serialize<T>(T value) =>
        JsonSerializer.SerializeToNode(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string? ReadString(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        return null;
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<Guid>(out var guid) && guid != Guid.Empty)
            {
                return guid;
            }

            if (value.TryGetValue<string>(out var text) && Guid.TryParse(text, out guid) && guid != Guid.Empty)
            {
                return guid;
            }
        }

        return null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && int.TryParse(text, out number)
            ? number
            : null;
    }

    private static long? ReadLong(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && long.TryParse(text, out number)
            ? number
            : null;
    }

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && decimal.TryParse(text, out number)
            ? number
            : null;
    }

    private static DateTime? ReadDateTime(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<DateTime>(out var dateTime))
        {
            return dateTime;
        }

        return value.TryGetValue<string>(out var text) && DateTime.TryParse(text, out dateTime)
            ? dateTime
            : null;
    }

    private static Dictionary<string, JsonNode?>? ReadObject(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not JsonObject jsonObject)
        {
            return null;
        }

        return jsonObject.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CreateApprovalStepInput> ReadApprovalSteps(IReadOnlyDictionary<string, JsonNode?> payload)
    {
        if (!payload.TryGetValue("steps", out var node) || node is not JsonArray stepsArray)
        {
            return [];
        }

        var steps = new List<CreateApprovalStepInput>();
        foreach (var stepNode in stepsArray.OfType<JsonObject>())
        {
            var stepPayload = stepNode.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var sequenceNo = ReadInt(stepPayload, "sequenceNo") ?? 0;
            var approverType = ReadString(stepPayload, "approverType");
            var approverRef = ReadString(stepPayload, "approverRef");
            if (sequenceNo > 0 && !string.IsNullOrWhiteSpace(approverType) && !string.IsNullOrWhiteSpace(approverRef))
            {
                steps.Add(new CreateApprovalStepInput(sequenceNo, approverType, approverRef));
            }
        }

        return steps;
    }
}
