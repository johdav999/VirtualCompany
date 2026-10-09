namespace VirtualCompany.Application.Tasks;

public sealed record DecisionWorkSourceRef(string Kind, Guid VersionId, string ItemKey);
public sealed record DecisionWorkSource(Guid CompanyId, DecisionWorkSourceRef Reference, int Version,
    string Fingerprint, string Title, Guid? SuggestedOwnerId, DateTime SavedUtc, string Timezone,
    string SourcePath, string EvidenceJson, bool IsLatest, string RevisionNotice);
public sealed record DecisionWorkPerson(Guid Id, string Name);
public sealed record DecisionWorkInput(DecisionWorkSourceRef Source, string Objective, Guid OwnerUserId,
    DateTime DueUtc, string AcceptanceOutcome, IReadOnlyList<Guid> ProposedCollaborators, string ProposedConstraints);
public sealed record DecisionWorkPreview(Guid CompanyId, DecisionWorkInput Input, DecisionWorkSource Source,
    string Owner, IReadOnlyList<DecisionWorkPerson> Collaborators, string RequiredReview, string Fingerprint);
public sealed record ConfirmDecisionWork(Guid RequestId, DecisionWorkInput Input, string ExpectedFingerprint);
public sealed record DecisionWorkDocument(Guid CompanyId, Guid TaskId, DecisionWorkPreview Created,
    DateTime CreatedUtc, string Status, Guid? ApprovalId, string WorkPath, string OriginPath, string RevisionNotice);
public sealed record DecisionWorkContext(Guid CompanyId, DecisionWorkSource Source,
    IReadOnlyList<DecisionWorkPerson> Owners, IReadOnlyList<DecisionWorkDocument> Work);
public interface IDecisionWorkService
{
    Task<DecisionWorkContext> ContextAsync(Guid company, DecisionWorkSourceRef source, CancellationToken ct);
    Task<DecisionWorkPreview> PreviewAsync(Guid company, DecisionWorkInput input, CancellationToken ct);
    Task<DecisionWorkDocument> CreateAsync(Guid company, ConfirmDecisionWork command, CancellationToken ct);
    Task<DecisionWorkDocument> OpenAsync(Guid company, Guid task, CancellationToken ct);
    Task<DecisionWorkDocument> SubmitReviewAsync(Guid company, Guid task, CancellationToken ct);
}
