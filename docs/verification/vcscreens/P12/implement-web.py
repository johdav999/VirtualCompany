from pathlib import Path
p=Path('src/VirtualCompany.Web/Services/ApprovalApiClient.cs');s=p.read_text(encoding='utf-8-sig')
s=s.replace('private readonly HttpClient _httpClient;', 'private readonly ICompanyApiTransport _transport;').replace('_httpClient = httpClient;', '_transport = new CompanyApiTransport(httpClient);')
a=s.index('        if (_useOfflineMode)',s.index('public Task<IReadOnlyList'));b=s.index('        var uri',a);s=s[:a]+'        EnsureOnline(companyId);\n\n'+s[b:]
s=s.replace('return GetAsync<IReadOnlyList<ApprovalRequestViewModel>>(uri, cancellationToken);','return GetAsync<IReadOnlyList<ApprovalRequestViewModel>>(companyId, uri, cancellationToken);')
a=s.index('        if (_useOfflineMode)',s.index('public Task<ApprovalRequestViewModel> GetAsync'));b=s.index('        return GetAsync',a);s=s[:a]+'        EnsureOnline(companyId);\n\n'+s[b:]
s=s.replace('GetAsync<ApprovalRequestViewModel>($"api/', 'GetAsync<ApprovalRequestViewModel>(companyId, $"api/')
a=s.index('        if (_useOfflineMode)',s.index('public Task<ApprovalDecisionResultViewModel> DecideAsync'));b=s.index('        return SendAsync',a);s=s[:a]+'        EnsureOnline(companyId);\n\n'+s[b:]
s=s.replace('            HttpMethod.Post,','            companyId, HttpMethod.Post,',1)
s=s.replace('GetAsync<T>(string uri,','GetAsync<T>(Guid companyId, string uri,').replace('SendAsync<T>(HttpMethod method,','SendAsync<T>(Guid companyId, HttpMethod method,')
s=s.replace('_httpClient.GetAsync(uri, cancellationToken)','_transport.SendAsync(companyId, HttpMethod.Get, uri, null, cancellationToken)')
a=s.index('            using var request = new HttpRequestMessage');b=s.index('            if (response.IsSuccessStatusCode)',a)
s=s[:a]+'''            using var response = await _transport.SendAsync(companyId, method, uri, JsonContent.Create(payload), cancellationToken);
'''+s[b:]
s=s.replace('_httpClient.BaseAddress','_transport.BaseAddress')
s=s.replace('        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);', '''        ApiProblemResponse? problem = null;
        try { problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken); }
        catch (JsonException) { }''')
a=s.index('    internal static IReadOnlyList<ApprovalRequestViewModel> OfflineApprovals');s=s[:a]+'''    private void EnsureOnline(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company context is required.", nameof(companyId));
        if (_useOfflineMode) throw new OnboardingApiException("Approval review is unavailable offline. Reconnect and retry.");
    }

'''+s[a:]
s=s.replace('    public DateTime CreatedAt { get; set; }','    public ApprovalReviewViewModel? Review { get; set; }\n    public DateTime CreatedAt { get; set; }',1)
s=s.replace('public sealed class ApprovalDecisionRequest\n{','public sealed class ApprovalDecisionRequest\n{\n    public Guid? ClientRequestId { get; set; }\n    public string? ReviewToken { get; set; }')
s+='''
public sealed class ApprovalReviewViewModel
{
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
'''
p.write_text(s,encoding='utf-8')
# Keep the parent list, selection and all Work return navigation; share the actual review.
p=Path('src/VirtualCompany.Web/Components/Work/WorkApprovalsPanel.razor');s=p.read_text(encoding='utf-8-sig')
a=s.index('            <span class="vc-kicker">@selected.DisplayStatus');b=s.index('\n        }\n    </aside>',a)
s=s[:a]+'''            <DecisionReview CompanyId="CompanyId" Approval="selected" OnDecision="ReviewDecidedAsync" />'''+s[b:]
a=s.index('    private async Task DecideAsync');b=s.index('    private string ItemClass',a)
s=s[:a]+'''    private async Task ReviewDecidedAsync(ApprovalRequestViewModel result)
    {
        selected = ApprovalPresentationFormatter.Format(result);
        approvals = ApprovalPresentationFormatter.Format(await ApprovalApiClient.ListAsync(CompanyId, "pending"));
        decisionRecorded = true;
    }

'''+s[b:]
s=s.replace('    private bool isDeciding;\n','').replace('    private string? decisionComment;\n','').replace(' decisionComment = null;','')
p.write_text(s,encoding='utf-8')
p=Path('src/VirtualCompany.Web/Pages/Approvals.razor');s=p.read_text(encoding='utf-8-sig')
a=s.index('                <ApprovalDetail Approval="selectedApproval"');b=s.index(' />',a)+3
s=s[:a]+'''                @if (selectedApproval is not null)
                {
                    <DecisionReview CompanyId="CompanyId.Value" Approval="selectedApproval" OnDecision="ReviewDecidedAsync" />
                }'''+s[b:]
s=s.replace('@code {','''@code {
    private async Task ReviewDecidedAsync(ApprovalRequestViewModel result)
    {
        selectedApproval = ApprovalPresentationFormatter.Format(result);
        approvals = ApprovalPresentationFormatter.Format(await ApprovalApiClient.ListAsync(CompanyId!.Value, StatusOrDefault));
    }
''',1)
p.write_text(s,encoding='utf-8')
