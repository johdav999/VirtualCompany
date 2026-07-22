namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public async Task<IReadOnlyList<FinanceClosePeriodOptionViewModel>> ListClosePeriodsAsync(Guid companyId,
        Guid agentId, CancellationToken cancellationToken = default) =>
        await SendCompanyScopedGetAsync<IReadOnlyList<FinanceClosePeriodOptionViewModel>>(companyId,
            $"api/companies/{companyId:D}/finance/agents/{agentId:D}/analysis/close-periods", false, cancellationToken) ?? [];

    public Task<RoleAgentAnalysisViewModel> AnalyzeForAgentAsync(Guid companyId, Guid agentId,
        RoleAgentAnalysisRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<RoleAgentAnalysisRequestViewModel, RoleAgentAnalysisViewModel>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/finance/agents/{agentId:D}/analysis", request, cancellationToken);

    public Task<FinanceCashScenarioAnalysisViewModel> AnalyzeCashScenariosAsync(Guid companyId, Guid agentId,
        FinanceCashScenarioAnalysisRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceCashScenarioAnalysisRequestViewModel, FinanceCashScenarioAnalysisViewModel>(companyId,
            HttpMethod.Post, $"api/companies/{companyId:D}/finance/agents/{agentId:D}/analysis/cash-scenarios", request, cancellationToken);

    public Task<FinancePaymentRunAnalysisViewModel> AnalyzePaymentRunAsync(Guid companyId, Guid agentId,
        FinancePaymentRunAnalysisRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinancePaymentRunAnalysisRequestViewModel, FinancePaymentRunAnalysisViewModel>(companyId,
            HttpMethod.Post, $"api/companies/{companyId:D}/finance/agents/{agentId:D}/analysis/payment-runs", request, cancellationToken);

    public Task<CommitFinancePaymentRunResultViewModel> CommitPaymentRunAsync(Guid companyId, Guid agentId,
        CommitFinancePaymentRunRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<CommitFinancePaymentRunRequestViewModel, CommitFinancePaymentRunResultViewModel>(companyId,
            HttpMethod.Post, $"api/companies/{companyId:D}/finance/agents/{agentId:D}/analysis/payment-runs/commit", request, cancellationToken);

    public Task<FinanceCollectionsPlanViewModel> AnalyzeCollectionsAsync(Guid companyId, Guid agentId,
        FinanceCollectionsPlanRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceCollectionsPlanRequestViewModel, FinanceCollectionsPlanViewModel>(companyId,
            HttpMethod.Post, $"api/companies/{companyId:D}/finance/agents/{agentId:D}/analysis/collections-plans", request, cancellationToken);

    public Task<FinanceAccountingTreatmentViewModel> AnalyzeAccountingTreatmentAsync(Guid companyId, Guid agentId,
        FinanceAccountingTreatmentRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceAccountingTreatmentRequestViewModel, FinanceAccountingTreatmentViewModel>(companyId,
            HttpMethod.Post, $"api/companies/{companyId:D}/finance/agents/{agentId:D}/analysis/accounting-treatment", request, cancellationToken);

    public Task<FinanceCloseAnalysisViewModel> AnalyzeCloseAsync(Guid companyId, Guid agentId,
        FinanceCloseAnalysisRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceCloseAnalysisRequestViewModel, FinanceCloseAnalysisViewModel>(companyId,
            HttpMethod.Post, $"api/companies/{companyId:D}/finance/agents/{agentId:D}/analysis/close-analysis", request, cancellationToken);
}
