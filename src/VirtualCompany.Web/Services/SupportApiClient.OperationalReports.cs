namespace VirtualCompany.Web.Services;

public sealed partial class SupportApiClient
{
    public async Task<SupportOperationalReport> GetOperationalReportAsync(Guid companyId, SupportOperationalReportQuery query,
        bool assignedToMe = false, CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>();
        Add(parameters, "view", query.View); Add(parameters, "status", query.Status);
        Add(parameters, "priority", query.Priority); Add(parameters, "category", query.Category);
        Add(parameters, "search", query.Search); Add(parameters, "assignedUserId", query.AssignedUserId?.ToString("D"));
        Add(parameters, "assignedAgentId", query.AssignedAgentId?.ToString("D"));
        Add(parameters, "unassigned", query.Unassigned.ToString()); Add(parameters, "assignedToMe", assignedToMe.ToString());
        Add(parameters, "contactId", query.ContactId?.ToString("D")); Add(parameters, "customerCompanyId", query.CustomerCompanyId?.ToString("D"));
        Add(parameters, "ageBucket", query.AgeBucket); Add(parameters, "failedReply", query.FailedReply.ToString());
        return await GetAsync<SupportOperationalReport>(companyId, "api/support/reports?" + string.Join('&', parameters), false, cancellationToken)
            ?? throw new SupportApiException("The support report returned no data.");
    }

    public Task<SupportKnowledgeContext?> GetCaseKnowledgeAsync(Guid companyId, Guid caseId, CancellationToken cancellationToken = default) =>
        GetAsync<SupportKnowledgeContext>(companyId, $"api/support/cases/{caseId:D}/knowledge", true, cancellationToken);
}
