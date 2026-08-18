using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed class GuidedWorkApiClient(ICompanyApiTransport transport, bool offline)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public Task<GuidedWorkSessionViewModel> StartAsync(Guid companyId, Guid agentId, string artifactType, Guid? targetArtifactId = null, CancellationToken ct = default) =>
        SendAsync<GuidedWorkSessionViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId}/guided-work-sessions", new { artifactType, agentId, targetArtifactId }, ct);
    public Task<List<GuidedArtifactOptionViewModel>> OptionsAsync(Guid companyId,Guid agentId,CancellationToken ct=default)=>
        SendAsync<List<GuidedArtifactOptionViewModel>>(companyId,HttpMethod.Get,$"api/companies/{companyId}/guided-work-sessions/options?agentId={agentId}",null,ct);
    public Task<GuidedWorkSessionViewModel> GetAsync(Guid companyId, Guid sessionId, CancellationToken ct = default) =>
        SendAsync<GuidedWorkSessionViewModel>(companyId, HttpMethod.Get, $"api/companies/{companyId}/guided-work-sessions/{sessionId}", null, ct);
    public Task<GuidedWorkSessionListViewModel> ListAsync(Guid companyId, string? status = null, string? artifactType = null, CancellationToken ct = default) =>
        SendAsync<GuidedWorkSessionListViewModel>(companyId, HttpMethod.Get, $"api/companies/{companyId}/guided-work-sessions?status={Uri.EscapeDataString(status ?? string.Empty)}&artifactType={Uri.EscapeDataString(artifactType ?? string.Empty)}", null, ct);
    public Task<GuidedWorkTurnResultViewModel> TurnAsync(Guid companyId, Guid sessionId, string body, int expectedVersion, CancellationToken ct = default,
        Guid? clientRequestId=null, string modality="text", string? providerEventId=null, bool interrupted=false, int? durationMs=null, string? transportVersion=null) =>
        SendAsync<GuidedWorkTurnResultViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId}/guided-work-sessions/{sessionId}/turns",
            new { body, clientRequestId = clientRequestId??Guid.NewGuid(), expectedVersion, modality, providerEventId, interrupted, durationMs, transportVersion }, ct);
    public Task<GuidedWorkSessionViewModel> CorrectAsync(Guid companyId, Guid sessionId, string path, JsonNode? value, string status, int expectedVersion, Guid? clientRequestId = null, CancellationToken ct = default) =>
        SendAsync<GuidedWorkSessionViewModel>(companyId, HttpMethod.Put, $"api/companies/{companyId}/guided-work-sessions/{sessionId}/fields/{Uri.EscapeDataString(path)}", new { value, status, clientRequestId = clientRequestId ?? Guid.NewGuid(), expectedVersion }, ct);
    public Task<GuidedWorkSessionViewModel> ChangeStatusesAsync(Guid companyId, Guid sessionId, IReadOnlyList<string> paths, string? fromStatus, string status, string explanation, int expectedVersion, Guid? clientRequestId = null, CancellationToken ct = default) =>
        SendAsync<GuidedWorkSessionViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId}/guided-work-sessions/{sessionId}/fields/status", new { paths, fromStatus, status, explanation, clientRequestId = clientRequestId ?? Guid.NewGuid(), expectedVersion }, ct);
    public Task<GuidedWorkReviewViewModel> ReviewAsync(Guid companyId, Guid sessionId, int expectedVersion, Guid? clientRequestId = null, CancellationToken ct = default) =>
        SendAsync<GuidedWorkReviewViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId}/guided-work-sessions/{sessionId}/review", new { clientRequestId = clientRequestId ?? Guid.NewGuid(), expectedVersion }, ct);
    public Task<GuidedWorkCommitViewModel> CommitAsync(Guid companyId, Guid sessionId, string reviewToken, int expectedVersion, CancellationToken ct = default) =>
        SendAsync<GuidedWorkCommitViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId}/guided-work-sessions/{sessionId}/commit", new { reviewToken, clientRequestId = Guid.NewGuid(), expectedVersion }, ct);
    public Task<GuidedWorkSessionViewModel> CancelAsync(Guid companyId, Guid sessionId, int expectedVersion, Guid? clientRequestId = null, CancellationToken ct = default) =>
        SendAsync<GuidedWorkSessionViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId}/guided-work-sessions/{sessionId}/cancel", new { clientRequestId = clientRequestId ?? Guid.NewGuid(), expectedVersion }, ct);
    public Task<List<GuidedWorkshopDocumentViewModel>> DocumentsAsync(Guid companyId,Guid sessionId,CancellationToken ct=default)=>
        SendAsync<List<GuidedWorkshopDocumentViewModel>>(companyId,HttpMethod.Get,$"api/companies/{companyId}/guided-work-sessions/{sessionId}/documents",null,ct);
    public async Task<GuidedWorkshopDocumentViewModel> UploadDocumentAsync(Guid companyId,Guid sessionId,Stream stream,string fileName,string? contentType,CancellationToken ct=default)
    {
        if(offline)throw new OnboardingApiException("Guided work requires the backend API.");
        using var content=new MultipartFormDataContent();using var file=new StreamContent(stream);file.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType)?"application/octet-stream":contentType);content.Add(file,"file",fileName);content.Add(new StringContent(Path.GetFileNameWithoutExtension(fileName)),"title");
        using var response=await transport.SendAsync(companyId,HttpMethod.Post,$"api/companies/{companyId}/guided-work-sessions/{sessionId}/documents",content,ct);
        if(response.IsSuccessStatusCode)return await response.Content.ReadFromJsonAsync<GuidedWorkshopDocumentViewModel>(JsonOptions,ct)??throw new OnboardingApiException("The server returned an empty response.");
        throw await ReadErrorAsync(response,ct);
    }

    public Task<GuidedVoiceTransportResponse> StartVoiceCallAsync(Guid companyId, Guid sessionId, string offerSdp, CancellationToken ct = default) =>
        SendVoiceAsync(companyId, HttpMethod.Post, $"api/companies/{companyId}/guided-work-sessions/{sessionId}/voice/calls",
            new StringContent(offerSdp, Encoding.UTF8, "application/sdp"), ct);

    public Task<GuidedVoiceTransportResponse> EndVoiceCallAsync(Guid companyId, Guid sessionId, Guid bindingId, CancellationToken ct = default) =>
        SendVoiceAsync(companyId, HttpMethod.Delete, $"api/companies/{companyId}/guided-work-sessions/{sessionId}/voice/calls/{bindingId}", null, ct);
    public Task<GuidedWorkMessageViewModel> RecordVoiceAgentMessageAsync(Guid companyId,Guid sessionId,string providerResponseId,string body,CancellationToken ct=default)=>
        SendAsync<GuidedWorkMessageViewModel>(companyId,HttpMethod.Post,$"api/companies/{companyId}/guided-work-sessions/{sessionId}/voice/messages",new{providerResponseId,body},ct);

    private async Task<GuidedVoiceTransportResponse> SendVoiceAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken ct)
    {
        if (offline) throw new OnboardingApiException("Guided work requires the backend API.");
        using var response = await transport.SendAsync(companyId, method, uri, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return new GuidedVoiceTransportResponse(
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString(),
            body,
            Header(response, "X-Guided-Voice-Binding"),
            Header(response, "X-Guided-Voice-Expires"),
            Header(response, "Retry-After"));
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private async Task<T> SendAsync<T>(Guid companyId, HttpMethod method, string uri, object? body, CancellationToken ct)
    {
        if (offline) throw new OnboardingApiException("Guided work requires the backend API.");
        using var response = await transport.SendAsync(companyId, method, uri, body is null ? null : JsonContent.Create(body), ct);
        if (response.IsSuccessStatusCode) return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct) ?? throw new OnboardingApiException("The server returned an empty response.");
        throw await ReadErrorAsync(response,ct);
    }
    private static async Task<OnboardingApiException> ReadErrorAsync(HttpResponseMessage response,CancellationToken ct)
    {
        GuidedApiProblem? problem = null;
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(raw)) problem = JsonSerializer.Deserialize<GuidedApiProblem>(raw, JsonOptions);
        }
        catch (JsonException) { }
        var message = problem?.Detail ?? problem?.Title ??
            "The workshop service is unavailable. Your existing work is preserved; try again or continue in chat.";
        var validationDetails = problem?.Errors?.SelectMany(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (validationDetails.Length > 0) message = $"{message}: {string.Join(" ", validationDetails)}";
        return new OnboardingApiException(message, problem?.Errors, (int)response.StatusCode);
    }
    private sealed class GuidedApiProblem { public string? Title { get; set; } public string? Detail { get; set; } public Dictionary<string, string[]>? Errors { get; set; } }
}

public sealed record GuidedVoiceTransportResponse(int StatusCode, string? ContentType, string Body, string? BindingId, string? ExpiresAt, string? RetryAfter = null);
public sealed class GuidedWorkshopDocumentViewModel { public Guid DocumentId{get;set;} public string Title{get;set;}="";public string OriginalFileName{get;set;}="";public long FileSizeBytes{get;set;}public string Status{get;set;}="processing";public string StatusLabel{get;set;}="Processing";public bool IsReady{get;set;}public string? FailureMessage{get;set;}public DateTime UpdatedAt{get;set;} }

public sealed class GuidedWorkSessionViewModel
{
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public Guid ConversationId { get; set; } public Guid AgentId { get; set; }
    public string AgentDisplayName { get; set; } = ""; public string AgentRoleName { get; set; } = "";
    public string ArtifactType { get; set; } = ""; public string ArtifactLabel { get; set; } = ""; public string SchemaVersion { get; set; } = "";
    public GuidedArtifactCapabilitiesViewModel Capabilities { get; set; } = new();
    public Guid? TargetArtifactId { get; set; } public string? TargetArtifactVersion { get; set; } public string Status { get; set; } = "";
    public int Sequence { get; set; } public int Version { get; set; } public int RequiredFieldCount { get; set; } public int ReadyFieldCount { get; set; }
    public string SafeSummary { get; set; } = ""; public string? NextQuestion { get; set; }
    public List<GuidedDraftFieldViewModel> Fields { get; set; } = []; public List<GuidedWorkMessageViewModel> Messages { get; set; } = [];
    public DateTime CreatedAt { get; set; } public DateTime UpdatedAt { get; set; } public DateTime? CompletedAt { get; set; } public DateTime? CancelledAt { get; set; }
}
public sealed class GuidedDraftFieldViewModel
{
    public Guid Id { get; set; } public string Path { get; set; } = ""; public string Label { get; set; } = ""; public string Description { get; set; } = "";
    public string ValueType { get; set; } = ""; public bool IsRequired { get; set; } public JsonNode? Value { get; set; } public string Status { get; set; } = "missing";
    public string SourceType { get; set; } = ""; public Guid? SourceMessageId { get; set; } public Dictionary<string, JsonNode?> SourceMetadata { get; set; } = [];
    public string? Explanation { get; set; } public List<string> AllowedValues { get; set; } = []; public int Version { get; set; } public DateTime UpdatedAt { get; set; }
}
public sealed class GuidedWorkMessageViewModel { public Guid Id { get; set; } public string SenderType { get; set; } = ""; public Guid? SenderId { get; set; } public string Body { get; set; } = ""; public DateTime CreatedAt { get; set; } }
public sealed class GuidedFieldChangeViewModel { public string Path { get; set; } = ""; public string Label { get; set; } = ""; public JsonNode? PreviousValue { get; set; } public JsonNode? Value { get; set; } public string Status { get; set; } = ""; public string Explanation { get; set; } = ""; }
public sealed class GuidedWorkTurnResultViewModel { public GuidedWorkSessionViewModel Session { get; set; } = new(); public GuidedWorkMessageViewModel UserMessage { get; set; } = new(); public GuidedWorkMessageViewModel AgentMessage { get; set; } = new(); public List<GuidedFieldChangeViewModel> Changes { get; set; } = []; }
public sealed class GuidedWorkReviewViewModel { public GuidedWorkSessionViewModel Session { get; set; } = new(); public string ReviewToken { get; set; } = ""; public DateTime ExpiresAt { get; set; } public List<string> MissingFields { get; set; } = []; public List<string> Conflicts { get; set; } = []; public List<GuidedFieldChangeViewModel> ProposedChanges { get; set; } = []; public List<GuidedReviewInsightViewModel> Insights { get; set; } = []; }
public sealed class GuidedReviewInsightViewModel { public string Label { get; set; } = ""; public string Value { get; set; } = ""; public string Meaning { get; set; } = ""; }
public sealed class GuidedWorkCommitViewModel { public GuidedWorkSessionViewModel Session { get; set; } = new(); public string ArtifactType { get; set; } = ""; public Guid? ArtifactId { get; set; } public string? ArtifactVersion { get; set; } public string Summary { get; set; } = ""; }
public sealed class GuidedWorkSessionListViewModel { public List<GuidedWorkSessionViewModel> Items { get; set; } = []; public int TotalCount { get; set; } public int Skip { get; set; } public int Take { get; set; } }
public sealed class GuidedArtifactOptionViewModel { public string ArtifactType{get;set;}="";public string DisplayName{get;set;}="";public string SchemaVersion{get;set;}="";public bool RequiresTargetArtifact{get;set;} }
public sealed class GuidedArtifactCapabilitiesViewModel { public bool SupportsDocumentAttachments{get;set;} public List<string> AllowedDocumentExtensions{get;set;}=[]; public List<string> DocumentDataScopes{get;set;}=[]; public bool SupportsVoiceDocumentSearch{get;set;} public bool SupportsExternalResearch{get;set;} }
