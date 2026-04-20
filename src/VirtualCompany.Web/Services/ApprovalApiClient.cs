using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed class ApprovalApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public ApprovalApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<IReadOnlyList<ApprovalRequestViewModel>> ListAsync(
        Guid companyId,
        string? status = "pending",
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<ApprovalRequestViewModel>>(OfflineApprovals(companyId));
        }

        var uri = string.IsNullOrWhiteSpace(status)
            ? $"api/companies/{companyId}/approvals"
            : $"api/companies/{companyId}/approvals?status={Uri.EscapeDataString(status)}";
        return GetAsync<IReadOnlyList<ApprovalRequestViewModel>>(uri, cancellationToken);
    }

    public Task<ApprovalRequestViewModel> GetAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(OfflineApprovals(companyId).Single(x => x.Id == approvalId));
        }

        return GetAsync<ApprovalRequestViewModel>($"api/companies/{companyId}/approvals/{approvalId}", cancellationToken);
    }

    public Task<ApprovalDecisionResultViewModel> DecideAsync(
        Guid companyId,
        Guid approvalId,
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            var approval = OfflineApprovals(companyId).Single(x => x.Id == approvalId);
            approval.Status = request.Decision.StartsWith("reject", StringComparison.OrdinalIgnoreCase) ? "rejected" : "approved";
            var currentStep = approval.CurrentStep;
            if (currentStep is not null)
            {
                currentStep.Status = approval.Status;
                currentStep.DecidedAt = DateTime.UtcNow;
                currentStep.Comment = request.Comment?.Trim();
                approval.RejectionComment = approval.Status == "rejected" ? currentStep.Comment : null;
            }

            return Task.FromResult(new ApprovalDecisionResultViewModel { Approval = approval, DecidedStep = currentStep, IsFinalized = true });
        }

        return SendAsync<ApprovalDecisionResultViewModel>(
            HttpMethod.Post,
            $"api/companies/{companyId}/approvals/{approvalId}/decisions",
            request,
            cancellationToken);
    }

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
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

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, object payload, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri)
            {
                Content = JsonContent.Create(payload)
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken);
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
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        return problem?.Errors is { Count: > 0 }
            ? new OnboardingApiException(problem.Detail ?? problem.Title ?? "The request failed.", problem.Errors)
            : new OnboardingApiException(problem?.Detail ?? problem?.Title ?? $"The request failed with status code {(int)response.StatusCode}.");
    }

    private OnboardingApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new OnboardingApiException($"The web app could not reach the backend API at {baseAddress}. Start the API project or update the web app API base URL.");
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
