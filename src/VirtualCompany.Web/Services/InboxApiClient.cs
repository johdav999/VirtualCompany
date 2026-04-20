using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class InboxApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public InboxApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<InboxViewModel> GetInboxAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(OfflineInbox(companyId));
        }

        return GetAsync<InboxViewModel>($"api/companies/{companyId}/inbox", cancellationToken);
    }

    public Task<ApprovalRequestViewModel> GetPendingApprovalDetailAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(ApprovalApiClient.OfflineApprovals(companyId).Single(x => x.Id == approvalId));
        }

        return GetAsync<ApprovalRequestViewModel>($"api/companies/{companyId}/approvals/inbox/{approvalId}", cancellationToken);
    }

    public Task<ApprovalDecisionResultViewModel> DecidePendingApprovalAsync(
        Guid companyId,
        Guid approvalId,
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            var approval = ApprovalApiClient.OfflineApprovals(companyId).Single(x => x.Id == approvalId);
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
            $"api/companies/{companyId}/approvals/inbox/{approvalId}/decisions",
            request,
            cancellationToken);
    }

    public Task<NotificationListItemViewModel> SetStatusAsync(Guid companyId, Guid notificationId, string status, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            var notification = OfflineInbox(companyId).Notifications.Single(x => x.Id == notificationId);
            notification.Status = status;
            if (string.Equals(status, "read", StringComparison.OrdinalIgnoreCase))
            {
                notification.ReadAt ??= DateTime.UtcNow;
            }
            else if (string.Equals(status, "actioned", StringComparison.OrdinalIgnoreCase))
            {
                notification.ReadAt ??= DateTime.UtcNow;
                notification.ActionedAt ??= DateTime.UtcNow;
            }

            return Task.FromResult(notification);
        }

        return SendAsync<NotificationListItemViewModel>(
            HttpMethod.Patch,
            $"api/companies/{companyId}/inbox/notifications/{notificationId}/status",
            new SetNotificationStatusRequest { Status = status },
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
            using var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(payload) };
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

    private static InboxViewModel OfflineInbox(Guid companyId) =>
        new()
        {
            Notifications =
            [
                new() { Id = Guid.Parse("f2216d20-d3e4-4e73-9a88-36e8f3e90c95"), CompanyId = companyId, RecipientUserId = Guid.NewGuid(), Type = "approval_requested", Priority = "high", Title = "Threshold approval requested", Body = "Review task 9bc83a53771648cd8150f9b4b4926e39.", RelatedEntityType = "approval_request", RelatedEntityId = Guid.Parse("d0131d57-1895-4b75-9624-9b8b04d8a116"), Status = "unread", CreatedAt = DateTime.UtcNow }
            ],
            PendingApprovals =
            [
                new() { Id = Guid.Parse("d0131d57-1895-4b75-9624-9b8b04d8a116"), ApprovalType = "threshold", TargetEntityType = "task", TargetEntityId = Guid.Parse("9bc83a53-7716-48cd-8150-f9b4b4926e39"), Status = "pending", RationaleSummary = "This action exceeded a configured approval threshold.", RequestedByActorType = "agent", RequestedByActorId = Guid.Parse("6ed95431-1b05-4419-8342-245d87d516e8"), RequiredRole = "owner", AffectedDataSummary = "Task: Vendor payment run for April", ThresholdSummary = "Threshold: amount 25000 (configured 10000)", CurrentStep = new ApprovalStepViewModel { Id = Guid.Parse("7c797ca4-4a74-49f5-a669-9dc4173f2aa6"), SequenceNo = 1, ApproverType = "role", ApproverRef = "owner", Status = "pending" }, CreatedAt = DateTime.UtcNow }
            ],
            UnreadCount = 1,
            PendingApprovalCount = 1
        };

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class InboxViewModel
{
    public List<NotificationListItemViewModel> Notifications { get; set; } = [];
    public List<ApprovalInboxItemViewModel> PendingApprovals { get; set; } = [];
    public int UnreadCount { get; set; }
    public int PendingApprovalCount { get; set; }
}

public sealed class NotificationListItemViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RecipientUserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string RelatedEntityType { get; set; } = string.Empty;
    public Guid? RelatedEntityId { get; set; }
    public string? ActionUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? ActionedAt { get; set; }
    public Guid? ActionedByUserId { get; set; }
}

public sealed class ApprovalInboxItemViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public string TargetEntityType { get; set; } = string.Empty;
    public Guid TargetEntityId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RationaleSummary { get; set; } = string.Empty;
    public string AffectedDataSummary { get; set; } = string.Empty;
    public string RequestedByActorType { get; set; } = string.Empty;
    public Guid RequestedByActorId { get; set; }
    public string? RequiredRole { get; set; }
    public Guid? RequiredUserId { get; set; }
    public string? ThresholdSummary { get; set; }
    public ApprovalStepViewModel? CurrentStep { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SetNotificationStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
