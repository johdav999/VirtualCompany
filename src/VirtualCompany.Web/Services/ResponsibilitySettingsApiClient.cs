using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public interface IResponsibilitySettingsApiClient
{
    Task<ResponsibilitySettingsViewModel?> GetAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<ResponsibilityPresetPreviewViewModel> PreviewAsync(Guid companyId, ResponsibilityPresetRequestViewModel request, CancellationToken cancellationToken = default);
    Task<ResponsibilityPresetApplyResultViewModel> ApplyAsync(Guid companyId, ResponsibilityPresetRequestViewModel request, CancellationToken cancellationToken = default);
    Task<ResponsibilityAssignmentViewModel> UpsertAsync(Guid companyId, UpsertResponsibilityAssignmentRequest request, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid companyId, Guid assignmentId, long? expectedVersion, string? reason, CancellationToken cancellationToken = default);
}

public sealed class ResponsibilitySettingsApiClient(
    ICompanyApiTransport transport,
    bool useOfflineMode,
    IApiProblemMessageResolver problemResolver) : IResponsibilitySettingsApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ResponsibilitySettingsViewModel?> GetAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendAsync<ResponsibilitySettingsViewModel>(companyId, HttpMethod.Get,
            $"api/companies/{companyId:D}/responsibilities", null, allowForbidden: true, cancellationToken);

    public Task<ResponsibilityPresetPreviewViewModel> PreviewAsync(Guid companyId,
        ResponsibilityPresetRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<ResponsibilityPresetPreviewViewModel>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/responsibilities/presets/preview", request, cancellationToken);

    public Task<ResponsibilityPresetApplyResultViewModel> ApplyAsync(Guid companyId,
        ResponsibilityPresetRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<ResponsibilityPresetApplyResultViewModel>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/responsibilities/presets/apply", request, cancellationToken);

    public Task<ResponsibilityAssignmentViewModel> UpsertAsync(Guid companyId,
        UpsertResponsibilityAssignmentRequest request, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<ResponsibilityAssignmentViewModel>(companyId, HttpMethod.Put,
            $"api/companies/{companyId:D}/responsibilities/assignments", request, cancellationToken);

    public async Task RemoveAsync(Guid companyId, Guid assignmentId, long? expectedVersion, string? reason,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable(companyId);
        var query = new List<string>();
        if (expectedVersion.HasValue) query.Add($"expectedVersion={expectedVersion.Value}");
        if (!string.IsNullOrWhiteSpace(reason)) query.Add($"reason={Uri.EscapeDataString(reason.Trim())}");
        var uri = $"api/companies/{companyId:D}/responsibilities/assignments/{assignmentId:D}" +
                  (query.Count == 0 ? string.Empty : $"?{string.Join('&', query)}");
        using var response = await transport.SendAsync(companyId, HttpMethod.Delete, uri, null, cancellationToken);
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
    }

    private async Task<T> SendRequiredAsync<T>(Guid companyId, HttpMethod method, string uri, object? payload,
        CancellationToken cancellationToken) where T : class =>
        await SendAsync<T>(companyId, method, uri, payload, false, cancellationToken)
        ?? throw new ResponsibilitySettingsApiException("The server returned an empty responsibility response.");

    private async Task<T?> SendAsync<T>(Guid companyId, HttpMethod method, string uri, object? payload,
        bool allowForbidden, CancellationToken cancellationToken) where T : class
    {
        EnsureAvailable(companyId);
        try
        {
            using var content = payload is null ? null : JsonContent.Create(payload, options: JsonOptions);
            using var response = await transport.SendAsync(companyId, method, uri, content, cancellationToken);
            if (allowForbidden && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) return null;
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (HttpRequestException)
        {
            var endpoint = transport.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
            throw new ResponsibilitySettingsApiException(
                $"The web app could not reach the backend API at {endpoint}. Start the API project or update the API base URL.");
        }
    }

    private void EnsureAvailable(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        if (useOfflineMode) throw new ResponsibilitySettingsApiException("Responsibility settings require the backend API.");
    }

    private async Task<ResponsibilitySettingsApiException> CreateExceptionAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ApiProblemResponse? problem = null;
        try { problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(JsonOptions, cancellationToken); }
        catch (JsonException) { }
        var message = problemResolver.Resolve(problem, response.StatusCode == HttpStatusCode.Conflict
            ? "The assignment changed. Refresh and try again."
            : "Responsibility settings could not be updated.");
        return new ResponsibilitySettingsApiException(message, problem?.Errors, response.StatusCode);
    }
}

public sealed class ResponsibilitySettingsApiException : Exception
{
    public ResponsibilitySettingsApiException(string message,
        IReadOnlyDictionary<string, string[]>? errors = null, HttpStatusCode? statusCode = null) : base(message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
        StatusCode = statusCode;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public HttpStatusCode? StatusCode { get; }
}

public sealed class ResponsibilitySettingsViewModel
{
    public Guid CompanyId { get; set; }
    public string CompanySize { get; set; } = "micro";
    public List<ResponsibilityAssignmentViewModel> Assignments { get; set; } = [];
    public List<ResponsibilityPresetMetadataViewModel> AvailablePresets { get; set; } = [];
    public bool CanManage { get; set; }
    public List<ResponsibilityMemberViewModel> Members { get; set; } = [];
    public List<ResponsibilityAgentViewModel> Agents { get; set; } = [];
}

public sealed class ResponsibilityAssignmentViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ResponsibilityArea { get; set; } = string.Empty;
    public string AssignmentKind { get; set; } = string.Empty;
    public ResponsibilityMemberViewModel AssignedMember { get; set; } = new();
    public ResponsibilityAgentViewModel? PrimaryAgent { get; set; }
    public string AuthorityLevel { get; set; } = "level_1";
    public Guid? ApprovalPolicyId { get; set; }
    public ResponsibilityMemberViewModel? EscalationMember { get; set; }
    public long Version { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class ResponsibilityMemberViewModel
{
    public Guid MembershipId { get; set; }
    public Guid? UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class ResponsibilityAgentViewModel
{
    public Guid AgentId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> CompatibleAreas { get; set; } = [];
}

public sealed class ResponsibilityPresetMetadataViewModel
{
    public string CompanySize { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> ResponsibilityAreas { get; set; } = [];
    public bool SupportsManagerSelections { get; set; }
    public bool AddsExecutiveOversight { get; set; }
}

public sealed class ResponsibilityPresetRequestViewModel
{
    public string CompanySize { get; set; } = "micro";
    public Guid OwnerMembershipId { get; set; }
    public Dictionary<string, Guid>? ManagerMembershipIds { get; set; }
    public string Mode { get; set; } = "fill_missing";
    public string? Reason { get; set; }
}

public sealed class ResponsibilityPresetPreviewViewModel
{
    public Guid CompanyId { get; set; }
    public string CompanySize { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public List<ResponsibilityPresetChangeViewModel> Changes { get; set; } = [];
}

public sealed class ResponsibilityPresetChangeViewModel
{
    public string ResponsibilityArea { get; set; } = string.Empty;
    public string AssignmentKind { get; set; } = string.Empty;
    public string ChangeKind { get; set; } = string.Empty;
    public Guid? PreviousMembershipId { get; set; }
    public Guid AssignedMembershipId { get; set; }
    public Guid? PreviousAgentId { get; set; }
    public Guid? PrimaryAgentId { get; set; }
}

public sealed class ResponsibilityPresetApplyResultViewModel
{
    public ResponsibilityPresetPreviewViewModel Preview { get; set; } = new();
    public List<ResponsibilityAssignmentViewModel> Assignments { get; set; } = [];
}

public sealed class UpsertResponsibilityAssignmentRequest
{
    public Guid? AssignmentId { get; set; }
    public string ResponsibilityArea { get; set; } = string.Empty;
    public string AssignmentKind { get; set; } = "primary";
    public Guid AssignedMembershipId { get; set; }
    public Guid? PrimaryAgentId { get; set; }
    public string AuthorityLevel { get; set; } = "level_1";
    public Guid? ApprovalPolicyId { get; set; }
    public Guid? EscalationMembershipId { get; set; }
    public long? ExpectedVersion { get; set; }
    public string? Reason { get; set; }
}
