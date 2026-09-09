using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesMeetingChangeProposalApiClient(ICompanyApiTransport transport, bool offline, IApiProblemMessageResolver? resolver = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<IReadOnlyList<SalesMeetingChangeProposalViewModel>> ListAsync(Guid companyId, Guid sessionId, CancellationToken ct = default) => await SendAsync<List<SalesMeetingChangeProposalViewModel>>(companyId, HttpMethod.Get, Base(sessionId), null, ct) ?? [];
    public Task<IReadOnlyList<SalesMeetingChangeProposalViewModel>?> GenerateAsync(Guid companyId, Guid sessionId, GenerateSalesMeetingChangeProposalsViewModel request, CancellationToken ct = default) => SendAsync<IReadOnlyList<SalesMeetingChangeProposalViewModel>>(companyId, HttpMethod.Post, Base(sessionId) + "/generate", JsonContent.Create(request), ct);
    public Task<SalesMeetingChangeProposalViewModel?> EditAsync(Guid companyId, Guid sessionId, Guid id, EditSalesMeetingChangeProposalViewModel request, CancellationToken ct = default) => SendAsync<SalesMeetingChangeProposalViewModel>(companyId, HttpMethod.Put, Base(sessionId) + $"/{id:D}", JsonContent.Create(request), ct);
    public Task<SalesMeetingChangeProposalViewModel?> ApproveAsync(Guid companyId, Guid sessionId, Guid id, long version, CancellationToken ct = default) => ReviewAsync(companyId, sessionId, id, "approve", version, null, ct);
    public Task<SalesMeetingChangeProposalViewModel?> RejectAsync(Guid companyId, Guid sessionId, Guid id, long version, string? reason, CancellationToken ct = default) => ReviewAsync(companyId, sessionId, id, "reject", version, reason, ct);
    public Task<BulkApproveSalesMeetingChangeProposalsResultViewModel?> ApproveAllSafeAsync(Guid companyId, Guid sessionId, IReadOnlyList<Guid> ids, CancellationToken ct = default) => SendAsync<BulkApproveSalesMeetingChangeProposalsResultViewModel>(companyId, HttpMethod.Post, Base(sessionId) + "/approve-all-safe", JsonContent.Create(new { proposalIds = ids }), ct);
    public Task<SalesMeetingChangeProposalViewModel?> ExecuteAsync(Guid companyId, Guid sessionId, Guid id, long version, CancellationToken ct = default) => SendAsync<SalesMeetingChangeProposalViewModel>(companyId, HttpMethod.Post, Base(sessionId) + $"/{id:D}/execute", JsonContent.Create(new { expectedVersion = version }), ct);
    private Task<SalesMeetingChangeProposalViewModel?> ReviewAsync(Guid companyId, Guid sessionId, Guid id, string action, long version, string? reason, CancellationToken ct) => SendAsync<SalesMeetingChangeProposalViewModel>(companyId, HttpMethod.Post, Base(sessionId) + $"/{id:D}/{action}", JsonContent.Create(new { expectedVersion = version, reason }), ct);
    private async Task<T?> SendAsync<T>(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken ct)
    {
        if (offline) throw new SalesMeetingChangeProposalApiException("Sales meeting review needs the backend API.");
        try { using var response = await transport.SendAsync(companyId, method, uri, content, ct); if (response.StatusCode == HttpStatusCode.NotFound) return default; if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, ct); return await response.Content.ReadFromJsonAsync<T>(Json, ct); }
        catch (HttpRequestException) { throw new SalesMeetingChangeProposalApiException("The sales meeting review service could not be reached."); }
    }
    private async Task<SalesMeetingChangeProposalApiException> ErrorAsync(HttpResponseMessage response, CancellationToken ct) { var p = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, ct); var fallback = p?.Errors?.SelectMany(x => x.Value).FirstOrDefault() ?? p?.Detail ?? p?.Title ?? "The sales meeting review request failed."; return new(resolver?.Resolve(p, fallback) ?? fallback, response.StatusCode, p?.Errors); }
    private static string Base(Guid sessionId) => $"api/sales/meeting-sessions/{sessionId:D}/change-proposals";
}

public sealed class SalesMeetingChangeProposalApiException(string message, HttpStatusCode? status = null, IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message) { public HttpStatusCode? StatusCode { get; } = status; public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors; }
public sealed record SalesMeetingNextMeetingValueViewModel(Guid CalendarConnectionId, DateTime StartsUtc, DateTime EndsUtc, string TimeZoneId, string Title, string Description, string? Location, bool CreateOnlineMeeting = true, string? Conferencing=null);
public sealed record SalesMeetingProposedValueViewModel(string Kind, string? StringValue = null, decimal? DecimalValue = null, Guid? GuidValue = null, DateTime? DateTimeValue = null, SalesMeetingNextMeetingValueViewModel? NextMeetingValue = null);
public sealed record GenerateSalesMeetingChangeProposalItemViewModel(string TargetType, Guid TargetId, string Action, string Field, SalesMeetingProposedValueViewModel ProposedValue, Guid EvidenceArtifactId, IReadOnlyList<string> SourceIds, decimal Confidence, string Rationale);
public sealed record GenerateSalesMeetingChangeProposalsViewModel(IReadOnlyList<GenerateSalesMeetingChangeProposalItemViewModel> Proposals);
public sealed record EditSalesMeetingChangeProposalViewModel(long ExpectedVersion, SalesMeetingProposedValueViewModel ProposedValue, IReadOnlyList<string> SourceIds, decimal Confidence, string Rationale);
public sealed record SalesMeetingChangePolicyViewModel(bool IsAllowed, string ReasonCode, string Explanation, bool RequiresApproval, bool IsSafeForBulkApproval, string RiskClass, string PolicyVersion);
public sealed record SalesMeetingChangeProposalViewModel(Guid Id, Guid SessionId, Guid EvidenceArtifactId, string TargetType, Guid TargetId, string TargetLabel, string Action, string Field, string FieldLabel, SalesMeetingProposedValueViewModel ProposedValue, string BeforeDisplayValue, string AfterDisplayValue, string TargetVersion, decimal Confidence, string Rationale, IReadOnlyList<string> SourceIds, string EvidenceVersionHash, SalesMeetingChangePolicyViewModel Policy, string Status, Guid? ApprovalRequestId, string? ApprovalBindingHash, Guid? ReviewedByUserId, DateTime? ReviewedUtc, DateTime? ApprovedUtc, DateTime? RejectedUtc, int ExecutionAttemptCount, string IdempotencyKey, string? ExecutedBeforeValue, string? ExecutedAfterValue, string? ProviderReference, string? LastErrorCode, string? LastErrorSummary, DateTime? ExecutedUtc, DateTime CreatedUtc, DateTime UpdatedUtc, long Version);
public sealed record BulkApproveSalesMeetingChangeProposalsResultViewModel(IReadOnlyList<SalesMeetingChangeProposalViewModel> Approved, IReadOnlyList<Guid> SkippedProposalIds);
