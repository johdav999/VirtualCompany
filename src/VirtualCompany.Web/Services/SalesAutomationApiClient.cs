using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesAutomationApiClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _offlineMode;

    public SalesAutomationApiClient(HttpClient httpClient, bool offlineMode)
    {
        _httpClient = httpClient;
        _offlineMode = offlineMode;
    }

    public async Task<OutboundAutomationPolicyViewModel> GetPolicyAsync(CancellationToken cancellationToken = default)
    {
        if (_offlineMode)
        {
            return new OutboundAutomationPolicyViewModel();
        }

        return await _httpClient.GetFromJsonAsync<OutboundAutomationPolicyViewModel>("api/automation/outbound-policy", cancellationToken)
            ?? new OutboundAutomationPolicyViewModel();
    }

    public async Task<OutboundAutomationPolicyViewModel> UpdatePolicyAsync(UpdateOutboundAutomationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("api/automation/outbound-policy", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OutboundAutomationPolicyViewModel>(cancellationToken: cancellationToken)
            ?? new OutboundAutomationPolicyViewModel();
    }

    public async Task<IReadOnlyList<OutboundReviewQueueItemViewModel>> ListReviewQueueAsync(CancellationToken cancellationToken = default)
    {
        if (_offlineMode)
        {
            return [];
        }

        return await _httpClient.GetFromJsonAsync<IReadOnlyList<OutboundReviewQueueItemViewModel>>("api/review-queue/outbound", cancellationToken)
            ?? [];
    }

    public async Task<OutboundReviewQueueDetailViewModel?> GetReviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_offlineMode)
        {
            return null;
        }

        return await _httpClient.GetFromJsonAsync<OutboundReviewQueueDetailViewModel>($"api/review-queue/outbound/{id:D}", cancellationToken);
    }

    public Task<OutboundReviewQueueDetailViewModel?> ApproveAsync(Guid id, string? comment, CancellationToken cancellationToken = default) =>
        DecideAsync(id, "approve", new OutboundReviewDecisionRequest(comment), cancellationToken);

    public Task<OutboundReviewQueueDetailViewModel?> RejectAsync(Guid id, string? comment, CancellationToken cancellationToken = default) =>
        DecideAsync(id, "reject", new OutboundReviewDecisionRequest(comment), cancellationToken);

    public async Task<OutboundReviewQueueDetailViewModel?> EditAndApproveAsync(Guid id, OutboundEditAndApproveRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/review-queue/outbound/{id:D}/edit-and-approve", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OutboundReviewQueueDetailViewModel>(cancellationToken: cancellationToken);
    }

    private async Task<OutboundReviewQueueDetailViewModel?> DecideAsync(Guid id, string action, OutboundReviewDecisionRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/review-queue/outbound/{id:D}/{action}", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OutboundReviewQueueDetailViewModel>(cancellationToken: cancellationToken);
    }
}

public sealed class OutboundAutomationPolicyViewModel
{
    public Guid Id { get; set; }
    public bool OutboundEnabled { get; set; }
    public int MaxEmailsPerDay { get; set; } = 25;
    public bool RequireApprovalFirstContact { get; set; } = true;
    public bool RequireApprovalPricingDiscussion { get; set; } = true;
    public bool RequireApprovalFollowUps { get; set; } = true;
    public bool RequireApprovalReEngagement { get; set; } = true;
    public int WebsiteLeadDeduplicationWindowMinutes { get; set; } = 10080;
    public string WebsiteLeadFormKey { get; set; } = string.Empty;
    public Guid? WebsiteLeadFollowUpSequenceId { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed record UpdateOutboundAutomationPolicyRequest(
    bool OutboundEnabled,
    int MaxEmailsPerDay,
    bool RequireApprovalFirstContact,
    bool RequireApprovalPricingDiscussion,
    bool RequireApprovalFollowUps,
    bool RequireApprovalReEngagement,
    int WebsiteLeadDeduplicationWindowMinutes,
    Guid? WebsiteLeadFollowUpSequenceId);

public sealed record OutboundReviewQueueItemViewModel(Guid Id, Guid SequenceExecutionStepId, Guid CampaignId, Guid ContactId, string ContactName, string ContactEmail, string Category, string Status, string Reason, DateTime RequestedUtc);

public sealed record OutboundReviewQueueDetailViewModel(
    Guid Id, Guid SequenceExecutionStepId, Guid CampaignId, Guid ContactId, string ContactName, string ContactEmail,
    string Category, string Status, string ReasonCode, string Reason, string Subject, string Body, string? EditedSubject,
    string? EditedBody, Guid? DecidedByUserId, DateTime? DecidedUtc, string? DecisionComment, DateTime RequestedUtc);

public sealed record OutboundReviewDecisionRequest(string? Comment);

public sealed record OutboundEditAndApproveRequest(string Subject, string Body, string? Comment);