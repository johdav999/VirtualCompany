using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class AgentStaffOverviewApiClient(
    ICompanyApiTransport transport,
    bool useOfflineMode,
    IApiProblemMessageResolver problemResolver)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<AgentStaffOverviewViewModel?> GetAsync(
        Guid companyId,
        int? year = null,
        int? month = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new OnboardingApiException("A company is required to load the agent staff overview.");
        }

        if (year.HasValue != month.HasValue)
        {
            throw new ArgumentException("Year and month must be supplied together.");
        }

        if (useOfflineMode)
        {
            throw new OnboardingApiException("The agent staff overview requires a connection to the backend API.");
        }

        try
        {
            var periodQuery = year.HasValue
                ? $"?year={year.Value}&month={month!.Value}"
                : string.Empty;
            using var response = await transport.SendAsync(
                companyId,
                HttpMethod.Get,
                $"api/companies/{companyId:D}/executive-cockpit/agent-staff{periodQuery}",
                null,
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<AgentStaffOverviewViewModel>(SerializerOptions, cancellationToken)
                    ?? throw new OnboardingApiException("The server returned an empty agent staff overview.");
            }

            var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
            throw new OnboardingApiException(problemResolver.Resolve(problem, "The agent staff overview could not be loaded."));
        }
        catch (HttpRequestException)
        {
            var baseAddress = transport.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
            throw new OnboardingApiException($"The web app could not reach the backend API at {baseAddress}. Start the API project or update the web app API base URL.");
        }
    }
}

public sealed class AgentStaffOverviewViewModel
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public AgentStaffOverviewPeriodViewModel Period { get; set; } = new();
    public AgentStaffFinancialSummaryViewModel Finance { get; set; } = new();
    public AgentStaffSalesSummaryViewModel Sales { get; set; } = new();
    public AgentStaffSupportSummaryViewModel Support { get; set; } = new();
    public AgentStaffStageCountsViewModel StageCounts { get; set; } = new();
    public List<AgentStaffRowViewModel> Agents { get; set; } = [];
    public List<AgentStaffAttentionItemViewModel> AttentionItems { get; set; } = [];
}

public sealed class AgentStaffOverviewPeriodViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class AgentStaffFinancialSummaryViewModel
{
    public bool CanView { get; set; }
    public bool IsInitialized { get; set; }
    public bool HasData { get; set; }
    public decimal? Revenue { get; set; }
    public decimal? Costs { get; set; }
    public decimal? Result { get; set; }
    public string? Currency { get; set; }
    public decimal? RevenueChangePercentage { get; set; }
    public decimal? CostsChangePercentage { get; set; }
    public decimal? ResultChangePercentage { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
}

public sealed class AgentStaffSalesSummaryViewModel
{
    public bool HasData { get; set; }
    public decimal PipelineValue { get; set; }
    public decimal ForecastRevenue { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int DealsNeedingAttention { get; set; }
    public string Route { get; set; } = string.Empty;
}

public sealed class AgentStaffSupportSummaryViewModel
{
    public int CasesAtSlaRisk { get; set; }
    public int BreachedCases { get; set; }
    public int OpenCases { get; set; }
    public string Route { get; set; } = string.Empty;
}

public sealed class AgentStaffStageCountsViewModel
{
    public int Planned { get; set; }
    public int InProgress { get; set; }
    public int AwaitingHumanApproval { get; set; }
    public int Completed { get; set; }
}

public sealed class AgentStaffRowViewModel
{
    public Guid AgentId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string ProfileRoute { get; set; } = string.Empty;
    public List<AgentStaffTaskViewModel> Planned { get; set; } = [];
    public List<AgentStaffTaskViewModel> InProgress { get; set; } = [];
    public List<AgentStaffTaskViewModel> AwaitingHumanApproval { get; set; } = [];
    public List<AgentStaffTaskViewModel> Completed { get; set; } = [];
    public AgentStaffStageCountsViewModel StageCounts { get; set; } = new();
}

public sealed class AgentStaffTaskViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DueUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Route { get; set; } = string.Empty;
    public Guid? ApprovalId { get; set; }
    public string? ApprovalRoute { get; set; }
}

public sealed class AgentStaffAttentionItemViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? ActionLabel { get; set; }
    public string? Route { get; set; }
}
