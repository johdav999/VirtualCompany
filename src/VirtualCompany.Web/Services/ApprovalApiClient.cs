using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed class ApprovalApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ICompanyApiTransport _transport;
    private readonly bool _useOfflineMode;

    public ApprovalApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _transport = new CompanyApiTransport(httpClient);
        _useOfflineMode = useOfflineMode;
    }

    public Task<IReadOnlyList<ApprovalRequestViewModel>> ListAsync(
        Guid companyId,
        string? status = "pending",
        CancellationToken cancellationToken = default)
    {
        EnsureOnline(companyId);

        var uri = string.IsNullOrWhiteSpace(status)
            ? $"api/companies/{companyId}/approvals"
            : $"api/companies/{companyId}/approvals?status={Uri.EscapeDataString(status)}";
        return GetAsync<IReadOnlyList<ApprovalRequestViewModel>>(companyId, uri, cancellationToken);
    }

    public Task<ApprovalRequestViewModel> GetAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken = default)
    {
        EnsureOnline(companyId);

        return GetAsync<ApprovalRequestViewModel>(companyId, $"api/companies/{companyId}/approvals/{approvalId}", cancellationToken);
    }

    public Task<ApprovalDecisionResultViewModel> DecideAsync(
        Guid companyId,
        Guid approvalId,
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnline(companyId);

        return SendAsync<ApprovalDecisionResultViewModel>(
            companyId, HttpMethod.Post,
            $"api/companies/{companyId}/approvals/{approvalId}/decisions",
            request,
            cancellationToken);
    }

    private async Task<T> GetAsync<T>(Guid companyId, string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _transport.SendAsync(companyId, HttpMethod.Get, uri, null, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                    ?? throw new OnboardingApiException("The server returned an empty response.");
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<T> SendAsync<T>(Guid companyId, HttpMethod method, string uri, object payload, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _transport.SendAsync(companyId, method, uri, JsonContent.Create(payload), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                    ?? throw new OnboardingApiException("The server returned an empty response.");
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<OnboardingApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ApiProblemResponse? problem = null;
        try { problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken); }
        catch (JsonException) { }
        return problem?.Errors is { Count: > 0 }
            ? new OnboardingApiException(problem.Detail ?? problem.Title ?? "The request failed.", problem.Errors)
            : new OnboardingApiException(problem?.Detail ?? problem?.Title ?? $"The request failed with status code {(int)response.StatusCode}.");
    }

    private OnboardingApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _transport.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new OnboardingApiException($"The web app could not reach the backend API at {baseAddress}. Start the API project or update the web app API base URL.");
    }

    private void EnsureOnline(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company context is required.", nameof(companyId));
        if (_useOfflineMode) throw new OnboardingApiException("Approval review is unavailable offline. Reconnect and retry.");
    }

    internal static IReadOnlyList<ApprovalRequestViewModel> OfflineApprovals(Guid companyId) =>
    [
        new()
        {
            Id = Guid.Parse("d0131d57-1895-4b75-9624-9b8b04d8a116"),
            CompanyId = companyId,
            TargetEntityType = "task",
            TargetEntityId = Guid.Parse("9bc83a53-7716-48cd-8150-f9b4b4926e39"),
            ApprovalType = "threshold",
            Status = "pending",
            RequestedByActorType = "agent",
            RequestedByActorId = Guid.Parse("6ed95431-1b05-4419-8342-245d87d516e8"),
            RequiredRole = "owner",
            RequiredUserId = null,
            RationaleSummary = "This action exceeded a configured approval threshold.",
            Steps =
            [
                new ApprovalStepViewModel { Id = Guid.Parse("7c797ca4-4a74-49f5-a669-9dc4173f2aa6"), SequenceNo = 1, ApproverType = "role", ApproverRef = "owner", Status = "pending" }
            ],
            AffectedDataSummary = "Task: Vendor payment run for April",
            CurrentStep = new ApprovalStepViewModel { Id = Guid.Parse("7c797ca4-4a74-49f5-a669-9dc4173f2aa6"), SequenceNo = 1, ApproverType = "role", ApproverRef = "owner", Status = "pending" },
            AffectedEntities = [new ApprovalAffectedEntityViewModel { EntityType = "task", EntityId = Guid.Parse("9bc83a53-7716-48cd-8150-f9b4b4926e39"), Label = "Vendor payment run for April" }],
            ThresholdSummary = "Threshold: amount 25000 (configured 10000)",
            CreatedAt = DateTime.UtcNow
        }
    ];

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class ApprovalRequestViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TargetEntityType { get; set; } = string.Empty;
    public Guid TargetEntityId { get; set; }
    public string RequestedByActorType { get; set; } = string.Empty;
    public Guid RequestedByActorId { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public string? RequiredRole { get; set; }
    public Guid? RequiredUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, JsonNode?> ThresholdContext { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ApprovalStepViewModel> Steps { get; set; } = [];
    public ApprovalStepViewModel? CurrentStep { get; set; }
    public string? DecisionSummary { get; set; }
    public string? RejectionComment { get; set; }
    public string RationaleSummary { get; set; } = string.Empty;
    public string AffectedDataSummary { get; set; } = string.Empty;
    public List<ApprovalAffectedEntityViewModel> AffectedEntities { get; set; } = [];
    public string? ThresholdSummary { get; set; }
    public ApprovalReviewViewModel? Review { get; set; }
    public DateTime CreatedAt { get; set; }
    public string DisplayType { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
    public string DisplayStatus { get; set; } = string.Empty;
    public string DisplayReference { get; set; } = string.Empty;
    public string? DisplayAmount { get; set; }
    public string DisplayReason { get; set; } = string.Empty;
    public string DisplayDecisionSummary { get; set; } = string.Empty;
    public string DisplayTrigger { get; set; } = string.Empty;
    public string? DisplayScenario { get; set; }
    public string? DisplayCounterparty { get; set; }
    public string? DisplayPaymentActivity { get; set; }
    public string? DisplayStatusNote { get; set; }
}

public sealed class ApprovalAffectedEntityViewModel
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class ApprovalStepViewModel
{
    public string? ReviewerName { get; set; }
    public Guid Id { get; set; }
    public int SequenceNo { get; set; }
    public string ApproverType { get; set; } = string.Empty;
    public string ApproverRef { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Comment { get; set; }
}

public sealed class ApprovalDecisionRequest
{
    public Guid? ClientRequestId { get; set; }
    public string? ReviewToken { get; set; }
    public Guid ApprovalId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public Guid? StepId { get; set; }
    public string? Comment { get; set; }
}

public sealed class ApprovalDecisionResultViewModel
{
    public ApprovalRequestViewModel Approval { get; set; } = new();
    public ApprovalStepViewModel? DecidedStep { get; set; }
    public ApprovalStepViewModel? NextStep { get; set; }
    public bool IsFinalized { get; set; }
}

public sealed class ApprovalReviewViewModel
{
    public string? ExecutionStatus { get; set; }
    public string Token { get; set; } = "";
    public bool CanDecide { get; set; }
    public bool ProposalChanged { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Reviewer { get; set; } = "";
    public string VersionEvidence { get; set; } = "";
    public List<ApprovalComparisonViewModel> Comparison { get; set; } = [];
    public List<ApprovalEvidenceViewModel> Evidence { get; set; } = [];
}
public sealed class ApprovalComparisonViewModel
{
    public string Field { get; set; } = "";
    public string? Before { get; set; }
    public string? Proposed { get; set; }
}
public sealed class ApprovalEvidenceViewModel
{
    public string Label { get; set; } = "";
    public string Href { get; set; } = "";
}
