namespace VirtualCompany.Application.Tasks;
public interface IDecisionWorkService
{
    Task<DecisionWorkContext> ContextAsync(Guid company, DecisionWorkSourceRef source, CancellationToken ct);
    Task<DecisionWorkPreview> PreviewAsync(Guid company, DecisionWorkInput input, CancellationToken ct);
    Task<DecisionWorkDocument> CreateAsync(Guid company, ConfirmDecisionWork command, CancellationToken ct);
    Task<DecisionWorkDocument> OpenAsync(Guid company, Guid task, CancellationToken ct);
    Task<DecisionWorkDocument> SubmitReviewAsync(Guid company, Guid task, CancellationToken ct);
}
