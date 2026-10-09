namespace VirtualCompany.Application.Orchestration;
public interface IAnnualPlanningService
{
    Task<AnnualPlanningOptions> OptionsAsync(Guid company,int year,CancellationToken ct);
    Task<AnnualPlanPreview> PreviewAsync(Guid company,AnnualPlanInput input,CancellationToken ct);
    Task<AnnualPlanDocument> SaveAsync(Guid company,SaveAnnualPlan command,CancellationToken ct);
    Task<AnnualPlanDocument> OpenAsync(Guid company,Guid id,CancellationToken ct);
    Task<IReadOnlyList<AnnualPlanSummary>> HistoryAsync(Guid company,int year,CancellationToken ct);
    Task<AnnualPlanDocument> ReviewAsync(Guid company,Guid id,ReviewAnnualPlan command,CancellationToken ct);
    Task<bool> CanReadApprovalAsync(Guid company,Guid id,CancellationToken ct);
    Task<string> ApprovalMaterialAsync(Guid company,Guid id,CancellationToken ct);
    Task ApplyDecisionAsync(Guid company,Guid id,Guid approval,string status,CancellationToken ct);
}

