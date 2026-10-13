using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Agents;

public sealed class AgentRoleAnalysisState
{
    public bool IsRunningRoleAnalysis { get; set; }
    public string RoleAnalysisType { get; set; } = string.Empty;
    public string RoleAnalysisObjective { get; set; } = string.Empty;
    public RoleAgentAnalysisViewModel? RoleAnalysisResult { get; set; }
    public FinanceCashScenarioAnalysisRequestViewModel FinanceCashRequest { get; set; } = new();
    public FinanceCashScenarioAnalysisViewModel? FinanceCashAnalysis { get; set; }
    public FinancePaymentRunAnalysisRequestViewModel FinancePaymentRunRequest { get; set; } = new();
    public FinancePaymentRunAnalysisViewModel? FinancePaymentRun { get; set; }
    public FinanceCollectionsPlanViewModel? FinanceCollectionsPlan { get; set; }
    public IReadOnlyList<FinanceBillResponse> FinanceBills { get; set; } = [];
    public IReadOnlyList<FinanceClosePeriodOptionViewModel> FinanceClosePeriods { get; set; } = [];
    public Guid SelectedAccountingBillId { get; set; }
    public Guid SelectedClosePeriodId { get; set; }
    public FinanceAccountingTreatmentViewModel? FinanceAccountingTreatment { get; set; }
    public FinanceCloseAnalysisRequestViewModel FinanceCloseRequest { get; set; } = new();
    public FinanceCloseAnalysisViewModel? FinanceCloseAnalysis { get; set; }
    public HashSet<Guid> SelectedPaymentBillIds { get; set; } = [];
    public bool PaymentRunReviewed { get; set; }
    public bool IsCommittingPaymentRun { get; set; }
    public CommitFinancePaymentRunResultViewModel? PaymentRunCommitResult { get; set; }
    public SalesNextBestActionViewModel? SalesNextActions { get; set; }
    public SalesForecastScenarioResultViewModel? SalesForecast { get; set; }
    public SalesCampaignOptimizationResultViewModel? SalesCampaigns { get; set; }
    public SupportQueueAnalysisResultViewModel? SupportQueue { get; set; }
    public SupportRiskAssessmentResultViewModel? SupportRisk { get; set; }
    public SupportRecurringIssueResultViewModel? SupportRecurringIssues { get; set; }
    public SupportKnowledgeCoverageResultViewModel? SupportKnowledgeCoverage { get; set; }
    public string? ErrorMessage { get; private set; }
    public async Task RunAsync(Guid companyId, Guid agentId, AgentOperatingProfileViewModel selectedAgentProfile, FinanceApiClient FinanceApiClient, SalesApiClient SalesApiClient, SupportApiClient SupportApiClient)
    {
        if (string.IsNullOrWhiteSpace(RoleAnalysisType)) return;
        IsRunningRoleAnalysis = true;
        ErrorMessage = null;
        var request = new RoleAgentAnalysisRequestViewModel
        {
            AnalysisType = RoleAnalysisType,
            Objective = string.IsNullOrWhiteSpace(RoleAnalysisObjective) ? null : RoleAnalysisObjective.Trim(),
            HorizonDays = 30
        };
        try
        {
            FinanceCashAnalysis = null;
            FinancePaymentRun = null;
            FinanceCollectionsPlan = null;
            FinanceAccountingTreatment = null;
            FinanceCloseAnalysis = null;
            SalesNextActions = null;
            SalesForecast = null;
            SalesCampaigns = null;
            SupportQueue = null;
            SupportRisk = null;
            SupportRecurringIssues = null;
            SupportKnowledgeCoverage = null;
            PaymentRunCommitResult = null;
            SelectedPaymentBillIds.Clear();
            PaymentRunReviewed = false;
            if (selectedAgentProfile.Department.Equals("finance", StringComparison.OrdinalIgnoreCase) && RoleAnalysisType == "cash_liquidity")
            {
                FinanceCashRequest.Objective = request.Objective;
                FinanceCashAnalysis = await FinanceApiClient.AnalyzeCashScenariosAsync(companyId, agentId, FinanceCashRequest);
                RoleAnalysisResult = FinanceCashAnalysis.Advice;
            }
            else if (selectedAgentProfile.Department.Equals("finance", StringComparison.OrdinalIgnoreCase) && RoleAnalysisType == "payables")
            {
                FinancePaymentRunRequest.Objective = request.Objective;
                FinancePaymentRunRequest.CutoffUtc = DateTime.SpecifyKind(FinancePaymentRunRequest.CutoffUtc.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime();
                FinancePaymentRun = await FinanceApiClient.AnalyzePaymentRunAsync(companyId, agentId, FinancePaymentRunRequest);
                RoleAnalysisResult = FinancePaymentRun.Advice;
                foreach (var item in FinancePaymentRun.Items.Where(x => x.Group == "pay")) SelectedPaymentBillIds.Add(item.BillId);
            }
            else if (selectedAgentProfile.Department.Equals("finance", StringComparison.OrdinalIgnoreCase) && RoleAnalysisType == "receivables")
            {
                FinanceCollectionsPlan = await FinanceApiClient.AnalyzeCollectionsAsync(companyId, agentId,
                    new FinanceCollectionsPlanRequestViewModel { HorizonDays = 90, Objective = request.Objective });
                RoleAnalysisResult = FinanceCollectionsPlan.Advice;
            }
            else if (selectedAgentProfile.Department.Equals("finance", StringComparison.OrdinalIgnoreCase) && RoleAnalysisType == "accounting_treatment")
            {
                if (SelectedAccountingBillId == Guid.Empty)
                {
                    ErrorMessage = "Choose a supplier bill before running accounting treatment analysis.";
                    return;
                }
                FinanceAccountingTreatment = await FinanceApiClient.AnalyzeAccountingTreatmentAsync(companyId, agentId,
                    new FinanceAccountingTreatmentRequestViewModel { BillId = SelectedAccountingBillId, Objective = request.Objective });
                RoleAnalysisResult = FinanceAccountingTreatment.Advice;
            }
            else if (selectedAgentProfile.Department.Equals("finance", StringComparison.OrdinalIgnoreCase) && RoleAnalysisType == "close_analysis")
            {
                if (SelectedClosePeriodId == Guid.Empty)
                {
                    ErrorMessage = "Choose a fiscal period before running close readiness analysis.";
                    return;
                }
                FinanceCloseRequest.FiscalPeriodId = SelectedClosePeriodId;
                FinanceCloseRequest.Objective = request.Objective;
                FinanceCloseAnalysis = await FinanceApiClient.AnalyzeCloseAsync(companyId, agentId, FinanceCloseRequest);
                RoleAnalysisResult = FinanceCloseAnalysis.Advice;
            }
            else if (selectedAgentProfile.Department.Equals("sales", StringComparison.OrdinalIgnoreCase) && RoleAnalysisType == "next_best_action")
            {
                SalesNextActions = await SalesApiClient.AnalyzeNextActionsAsync(companyId, agentId,
                    new SalesNextBestActionRequestViewModel { Objective = request.Objective });
                RoleAnalysisResult = SalesNextActions.Advice;
            }
            else if (selectedAgentProfile.Department.Equals("sales", StringComparison.OrdinalIgnoreCase) && RoleAnalysisType == "forecast_analysis")
            {
                SalesForecast = await SalesApiClient.AnalyzeForecastScenariosAsync(companyId, agentId,
                    new SalesForecastScenarioRequestViewModel { HorizonDays = 90, Objective = request.Objective });
                RoleAnalysisResult = SalesForecast.Advice;
            }
            else if (selectedAgentProfile.Department.Equals("sales", StringComparison.OrdinalIgnoreCase) && RoleAnalysisType == "campaign_optimization")
            {
                SalesCampaigns = await SalesApiClient.AnalyzeCampaignsAsync(companyId, agentId,
                    new SalesCampaignOptimizationRequestViewModel { Objective = request.Objective });
                RoleAnalysisResult = SalesCampaigns.Advice;
            }
            else if (IsSupportDepartment(selectedAgentProfile.Department) && RoleAnalysisType == "triage_analysis")
            {
                SupportQueue = await SupportApiClient.AnalyzeQueueAsync(companyId, agentId,
                    new SupportQueueAnalysisRequestViewModel { Objective = request.Objective });
                RoleAnalysisResult = SupportQueue.Advice;
            }
            else if (IsSupportDepartment(selectedAgentProfile.Department) && RoleAnalysisType == "risk_escalation")
            {
                SupportRisk = await SupportApiClient.AnalyzeRiskAsync(companyId, agentId,
                    new SupportRiskAssessmentRequestViewModel { Objective = request.Objective });
                RoleAnalysisResult = SupportRisk.Advice;
            }
            else if (IsSupportDepartment(selectedAgentProfile.Department) && RoleAnalysisType == "root_cause_analysis")
            {
                SupportRecurringIssues = await SupportApiClient.AnalyzeRecurringIssuesAsync(companyId, agentId,
                    new SupportRecurringIssueRequestViewModel { Objective = request.Objective });
                RoleAnalysisResult = SupportRecurringIssues.Advice;
            }
            else if (IsSupportDepartment(selectedAgentProfile.Department) && RoleAnalysisType == "knowledge_coverage")
            {
                SupportKnowledgeCoverage = await SupportApiClient.AnalyzeKnowledgeCoverageAsync(companyId, agentId,
                    new SupportKnowledgeCoverageRequestViewModel { Objective = request.Objective });
                RoleAnalysisResult = SupportKnowledgeCoverage.Advice;
            }
            else
            {
                RoleAnalysisResult = selectedAgentProfile.Department.ToLowerInvariant() switch
                {
                    "finance" => await FinanceApiClient.AnalyzeForAgentAsync(companyId, agentId, request),
                    "sales" => await SalesApiClient.AnalyzeForAgentAsync(companyId, agentId, request),
                    "support" or "customer support" => await SupportApiClient.AnalyzeForAgentAsync(companyId, agentId, request),
                    _ => null
                };
            }
        }
        catch (FinanceApiException ex) { ErrorMessage = ex.Message; }
        catch (SalesApiException ex) { ErrorMessage = ex.Message; }
        catch (SupportApiException ex) { ErrorMessage = ex.Message; }
        finally { IsRunningRoleAnalysis = false; }
    }

    public async Task CommitPaymentRunAsync(Guid companyId, Guid agentId, FinanceApiClient FinanceApiClient)
    {
        if (FinancePaymentRun is null) return;
        IsCommittingPaymentRun = true;
        ErrorMessage = null;
        try
        {
            PaymentRunCommitResult = await FinanceApiClient.CommitPaymentRunAsync(companyId, agentId,
                new CommitFinancePaymentRunRequestViewModel
                {
                    RecommendationRunId = FinancePaymentRun.Advice.RunId,
                    SnapshotToken = FinancePaymentRun.SnapshotToken,
                    AnalysisRequest = FinancePaymentRunRequest,
                    SelectedBillIds = SelectedPaymentBillIds.ToList(),
                    Reviewed = PaymentRunReviewed
                });
        }
        catch (FinanceApiException ex) { ErrorMessage = ex.Message; }
        finally { IsCommittingPaymentRun = false; }
    }

    public void TogglePaymentBill(Guid billId, bool selected)
    {
        if (selected) SelectedPaymentBillIds.Add(billId); else SelectedPaymentBillIds.Remove(billId);
        PaymentRunReviewed = false;
    }

    public void ResetFinanceAnalysis()
    {
        FinanceCashAnalysis = null;
        FinancePaymentRun = null;
        FinanceCollectionsPlan = null;
        FinanceAccountingTreatment = null;
        FinanceCloseAnalysis = null;
        SelectedPaymentBillIds.Clear();
        PaymentRunReviewed = false;
        PaymentRunCommitResult = null;
    }

    private static bool IsSupportDepartment(string value) => value.Equals("support", StringComparison.OrdinalIgnoreCase) || value.Equals("customer support", StringComparison.OrdinalIgnoreCase);
    public static IReadOnlyList<RoleAnalysisOption> OptionsFor(string? department) => (department ?? string.Empty).ToLowerInvariant() switch
    {
        "finance" =>
        [
            new("cash_liquidity", "Cash and liquidity"), new("payables", "Payables"),
            new("receivables", "Receivables"), new("accounting_treatment", "Accounting treatment"),
            new("close_analysis", "Close readiness"), new("operating_cadence", "Finance priorities")
        ],
        "sales" =>
        [
            new("lead_intelligence", "Lead intelligence"), new("next_best_action", "Next best actions"),
            new("deal_risk", "Deal risk"), new("forecast_analysis", "Forecast"),
            new("campaign_optimization", "Campaigns"), new("proposal_advice", "Proposal advice"),
            new("operating_cadence", "Sales priorities")
        ],
        "support" or "customer support" =>
        [
            new("triage_analysis", "Queue triage"), new("grounded_reply", "Grounded reply"),
            new("risk_escalation", "Risk and escalation"), new("root_cause_analysis", "Recurring issues"),
            new("knowledge_coverage", "Knowledge coverage"), new("operating_cadence", "Support priorities")
        ],
        _ => []
    };

}
public sealed record RoleAnalysisOption(string Value, string Label);
