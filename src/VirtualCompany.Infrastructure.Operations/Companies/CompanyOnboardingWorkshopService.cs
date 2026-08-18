using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOnboardingWorkshopService(
    ICompanyOnboardingService onboarding,
    IGuidedWorkSessionService guidedWork,
    VirtualCompanyDbContext db,
    ICompanyExecutionScopeFactory executionScopes) : ICompanyOnboardingWorkshopService
{
    public const string FacilitatorTemplateId = "company-setup-advisor";

    public async Task<CompanyOnboardingWorkshopBootstrapDto> StartOrResumeAsync(
        CreateCompanyWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var progress = request.CompanyId.HasValue
            ? await onboarding.SaveProgressAsync(new SaveCompanyOnboardingProgressRequest(
                request.CompanyId,
                request.Name,
                request.Industry,
                request.BusinessType,
                request.Branding,
                request.Settings,
                request.Timezone,
                request.Currency,
                request.Language,
                request.ComplianceRegion,
                request.CurrentStep,
                request.SelectedTemplateId), cancellationToken)
            : await onboarding.CreateWorkspaceAsync(request, cancellationToken);
        var companyId = progress.CompanyId ?? throw new InvalidOperationException("The onboarding workspace was not created.");

        var agent = await db.Agents.IgnoreQueryFilters().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.TemplateId == FacilitatorTemplateId,
            cancellationToken);
        if (agent is null)
        {
            agent = new Agent(Guid.NewGuid(), companyId, FacilitatorTemplateId, "Eva", "Company Setup Advisor", "Operations", null,
                AgentSeniority.Senior, AgentStatus.Restricted, AgentAutonomyLevel.Guided,
                personality: new Dictionary<string,JsonNode?> { ["style"] = "Patient, structured, and explicit about assumptions." },
                objectives: new Dictionary<string,JsonNode?> { ["company_setup"] = "Help the owner create a reviewed company foundation." },
                tools: new Dictionary<string,JsonNode?> { ["guided_draft"] = true, ["workshop_documents"] = true, ["public_research"] = true },
                scopes: new Dictionary<string,JsonNode?> { ["knowledge"] = "read", ["operations"] = "guided" },
                roleBrief: "Facilitate company onboarding only. Do not send messages, publish, purchase, approve, execute finance actions, change autonomy, or write integrations.");
            db.Agents.Add(agent);
            await db.SaveChangesAsync(cancellationToken);
        }

        using var companyScope = executionScopes.BeginScope(companyId);
        var session = await guidedWork.StartAsync(companyId,
            new StartGuidedWorkSessionCommand(GuidedArtifactTypes.CompanyOnboarding, agent.Id), cancellationToken);
        var route = $"/agents/{agent.Id}/workshops/{GuidedArtifactTypes.CompanyOnboarding}?companyId={companyId}&sessionId={session.Id}";
        return new(companyId, agent.Id, session.Id, route, session.Messages.Count > 0);
    }
}
