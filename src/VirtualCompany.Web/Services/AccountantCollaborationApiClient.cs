using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public sealed class AccountantCollaborationApiClient(HttpClient httpClient, ICompanyApiTransport companyTransport)
{
    public async Task<AccountantPortfolioViewModel> GetPortfolioAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<AccountantPortfolioViewModel>("api/accountant/portfolio", ct)
        ?? new AccountantPortfolioViewModel();

    public async Task<List<AccountantEngagementViewModel>> GetEngagementsAsync(Guid companyId, CancellationToken ct = default)
    {
        using var response = await companyTransport.SendAsync(companyId, HttpMethod.Get,
            $"api/companies/{companyId}/accountant-collaboration/engagements", null, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<AccountantEngagementViewModel>>(cancellationToken: ct) ?? [];
    }

    public async Task<AccountantEngagementViewModel> AddReviewItemAsync(Guid companyId, Guid engagementId,
        bool finding, string severity, string content, string targetType, Guid? targetId, CancellationToken ct = default) =>
        await SendAsync(companyId, $"engagements/{engagementId}/review-items",
            new { IsFinding = finding, Severity = severity, Content = content, TargetType = targetType, TargetId = targetId }, ct);

    public async Task<AccountantEngagementViewModel> RequestEvidenceAsync(Guid companyId, Guid engagementId,
        string text, string targetType, Guid? targetId, Guid? assignedTo, DateTime dueUtc, CancellationToken ct = default) =>
        await SendAsync(companyId, $"engagements/{engagementId}/evidence-requests",
            new { RequestText = text, TargetType = targetType, TargetId = targetId, AssignedToUserId = assignedTo, DueUtc = dueUtc }, ct);

    public async Task<AccountantEngagementViewModel> SignOffAsync(Guid companyId, Guid engagementId,
        string conclusion, long expectedVersion, CancellationToken ct = default) =>
        await SendAsync(companyId, $"engagements/{engagementId}/sign-off", new { Conclusion = conclusion, ExpectedVersion = expectedVersion }, ct);

    private async Task<AccountantEngagementViewModel> SendAsync(Guid companyId, string path, object body, CancellationToken ct)
    {
        using var content = JsonContent.Create(body);
        using var response = await companyTransport.SendAsync(companyId, HttpMethod.Post,
            $"api/companies/{companyId}/accountant-collaboration/{path}", content, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<AccountantEngagementViewModel>(cancellationToken: ct)
            ?? throw new AccountantCollaborationApiException("The collaboration response was empty.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var problem = await response.Content.ReadFromJsonAsync<AccountantProblemViewModel>(cancellationToken: ct);
        throw new AccountantCollaborationApiException(problem?.Detail ?? "The accountant collaboration request failed.");
    }
}

public sealed class AccountantCollaborationApiException(string message) : Exception(message);
public sealed class AccountantProblemViewModel { public string? Detail { get; set; } }
public sealed class AccountantPortfolioViewModel
{
    public int ActiveCompanyCount { get; set; }
    public int ClosingSoonCount { get; set; }
    public int HighRiskCount { get; set; }
    public int OpenEvidenceRequestCount { get; set; }
    public List<AccountantPortfolioCompanyViewModel> Companies { get; set; } = [];
}
public sealed class AccountantPortfolioCompanyViewModel
{
    public Guid CompanyId { get; set; } public string CompanyName { get; set; } = ""; public Guid GrantId { get; set; }
    public string GrantStatus { get; set; } = ""; public DateTime EffectiveFromUtc { get; set; } public DateTime? EffectiveUntilUtc { get; set; }
    public DateTime? LastAccessUtc { get; set; } public int OpenEngagements { get; set; } public DateTime? NextDueUtc { get; set; }
    public string CloseStatus { get; set; } = ""; public int VatOrComplianceIssues { get; set; } public int UnreconciledItems { get; set; }
    public int FailedIntegrations { get; set; } public int PendingApprovals { get; set; } public int OpenEvidenceRequests { get; set; }
    public int OverdueEvidenceRequests { get; set; }
    public int RiskCount => VatOrComplianceIssues + FailedIntegrations + OverdueEvidenceRequests;
}
public sealed class AccountantEngagementViewModel
{
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public string CompanyName { get; set; } = "";
    public Guid? FiscalPeriodId { get; set; } public string? FiscalPeriodName { get; set; }
    public string Title { get; set; } = ""; public string EngagementType { get; set; } = ""; public string Status { get; set; } = "";
    public DateTime DueUtc { get; set; } public long Version { get; set; }
    public List<AccountantReviewItemViewModel> ReviewItems { get; set; } = [];
    public List<AccountantEvidenceRequestViewModel> EvidenceRequests { get; set; } = [];
    public List<AccountantSignOffViewModel> SignOffs { get; set; } = [];
    public List<AccountantHistoryViewModel> History { get; set; } = [];
}
public sealed class AccountantReviewItemViewModel
{
    public Guid Id { get; set; } public bool IsFinding { get; set; } public string Severity { get; set; } = "";
    public string Content { get; set; } = ""; public string TargetType { get; set; } = ""; public string Status { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
public sealed class AccountantEvidenceRequestViewModel
{
    public Guid Id { get; set; } public string RequestText { get; set; } = ""; public string TargetType { get; set; } = "";
    public DateTime DueUtc { get; set; } public string Status { get; set; } = ""; public List<AccountantEvidenceResponseViewModel> Responses { get; set; } = [];
}
public sealed class AccountantEvidenceResponseViewModel { public string ResponseText { get; set; } = ""; public Guid? DocumentId { get; set; } public bool DocumentAccessible { get; set; } public DateTime CreatedUtc { get; set; } }
public sealed class AccountantSignOffViewModel { public string Conclusion { get; set; } = ""; public DateTime SignedUtc { get; set; } }
public sealed class AccountantHistoryViewModel { public string Action { get; set; } = ""; public string SafeSummary { get; set; } = ""; public DateTime OccurredUtc { get; set; } }
