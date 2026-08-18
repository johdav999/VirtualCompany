using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyOnboardingWorkshopServiceTests
{
    [Fact]
    public async Task Repeated_start_reuses_eva_and_the_active_guided_session()
    {
        var companyId=Guid.NewGuid();
        await using var db=new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var onboarding=new OnboardingStub(companyId);var guided=new GuidedStub(companyId);
        var service=new CompanyOnboardingWorkshopService(onboarding,guided,db,new ExecutionScopeStub());
        var request=new CreateCompanyWorkspaceRequest("Example","","",null,null,null,null,null,null,1,null);

        var first=await service.StartOrResumeAsync(request,CancellationToken.None);
        var second=await service.StartOrResumeAsync(request,CancellationToken.None);

        var eva=Assert.Single(await db.Agents.IgnoreQueryFilters().ToListAsync());
        Assert.Equal("Eva",eva.DisplayName);Assert.Equal(AgentStatus.Restricted,eva.Status);Assert.Equal(AgentAutonomyLevel.Guided,eva.AutonomyLevel);
        Assert.Equal(first.AgentId,second.AgentId);Assert.Equal(first.SessionId,second.SessionId);
        Assert.All(guided.Commands,x=>Assert.Equal(GuidedArtifactTypes.CompanyOnboarding,x.ArtifactType));
    }

    [Fact]
    public async Task Existing_company_is_saved_before_its_workshop_is_resumed()
    {
        var companyId=Guid.NewGuid();
        await using var db=new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var onboarding=new OnboardingStub(companyId);var guided=new GuidedStub(companyId);
        var service=new CompanyOnboardingWorkshopService(onboarding,guided,db,new ExecutionScopeStub());
        var request=new CreateCompanyWorkspaceRequest("Updated name","Services","Consultancy",null,null,"Europe/Stockholm","SEK","en-GB","EU",3,null,companyId);

        var result=await service.StartOrResumeAsync(request,CancellationToken.None);

        Assert.Equal(companyId,result.CompanyId);
        Assert.NotNull(onboarding.SavedRequest);
        Assert.Equal(companyId,onboarding.SavedRequest!.CompanyId);
        Assert.Equal("Updated name",onboarding.SavedRequest.Name);
        Assert.Equal(0,onboarding.CreateCalls);
    }

    private sealed class OnboardingStub(Guid companyId):ICompanyOnboardingService
    {
        public int CreateCalls { get; private set; }
        public SaveCompanyOnboardingProgressRequest? SavedRequest { get; private set; }
        public Task<CompanyOnboardingProgressDto> CreateWorkspaceAsync(CreateCompanyWorkspaceRequest request,CancellationToken ct)
        {
            CreateCalls++;
            return Task.FromResult(Progress(request.Name,request.Industry,request.BusinessType,request.Timezone,request.Currency,request.Language,request.ComplianceRegion,request.SelectedTemplateId));
        }
        public Task<CreateCompanyResultDto> CreateCompanyAsync(CreateCompanyCommand command,CancellationToken ct)=>throw new NotSupportedException();
        public Task<OnboardingTemplateRecommendationDto?> GetRecommendedDefaultsAsync(GetOnboardingTemplateRecommendationRequest request,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<OnboardingTemplateDto>> GetTemplatesAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<CompanyOnboardingProgressDto?> GetProgressAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<CompanyOnboardingProgressDto?> GetProgressAsync(Guid requestedCompanyId,CancellationToken ct)=>throw new NotSupportedException();
        public Task<CompanyOnboardingProgressDto> SaveProgressAsync(SaveCompanyOnboardingProgressRequest request,CancellationToken ct)
        {
            SavedRequest=request;
            return Task.FromResult(Progress(request.Name,request.Industry,request.BusinessType,request.Timezone,request.Currency,request.Language,request.ComplianceRegion,request.SelectedTemplateId));
        }
        public Task<CompanyOnboardingProgressDto> AbandonOnboardingAsync(AbandonCompanyOnboardingRequest request,CancellationToken ct)=>throw new NotSupportedException();
        public Task<CompleteCompanyOnboardingResultDto> CompleteOnboardingAsync(CompleteCompanyOnboardingRequest request,CancellationToken ct)=>throw new NotSupportedException();
        private CompanyOnboardingProgressDto Progress(string name,string industry,string businessType,string? timezone,string? currency,string? language,string? complianceRegion,string? selectedTemplateId) =>
            new(companyId,name,industry,businessType,timezone??"",currency??"",language??"",complianceRegion??"",1,selectedTemplateId,"in_progress",false,true,DateTime.UtcNow,null,null,null,[],null!,null!);
    }
    private sealed class ExecutionScopeStub:ICompanyExecutionScopeFactory
    {
        public IDisposable BeginScope(Guid companyId)=>new Scope();
        private sealed class Scope:IDisposable { public void Dispose(){} }
    }
    private sealed class GuidedStub(Guid companyId):IGuidedWorkSessionService
    {
        private readonly Guid _sessionId=Guid.NewGuid();public List<StartGuidedWorkSessionCommand> Commands{get;}=[];
        public Task<GuidedWorkSessionDto> StartAsync(Guid company,StartGuidedWorkSessionCommand command,CancellationToken ct){Commands.Add(command);return Task.FromResult(new GuidedWorkSessionDto(_sessionId,companyId,Guid.NewGuid(),command.AgentId,"Eva","Company Setup Advisor",command.ArtifactType,"Company onboarding","1.0",companyId,null,"active",0,1,1,0,"Started",null,new(),[],[],DateTime.UtcNow,DateTime.UtcNow,null,null));}
        public Task<IReadOnlyList<GuidedArtifactOptionDto>> ListArtifactOptionsAsync(Guid c,Guid a,CancellationToken ct)=>throw new NotSupportedException();
        public Task<GuidedWorkSessionListDto> ListAsync(Guid c,ListGuidedWorkSessionsQuery q,CancellationToken ct)=>throw new NotSupportedException();
        public Task<GuidedWorkSessionDto> GetAsync(Guid c,Guid s,CancellationToken ct)=>throw new NotSupportedException();
        public Task<GuidedWorkTurnResultDto> AddTurnAsync(Guid c,Guid s,AddGuidedWorkTurnCommand q,CancellationToken ct)=>throw new NotSupportedException();
        public Task<GuidedWorkMessageDto> RecordVoiceAgentMessageAsync(Guid c,Guid s,RecordGuidedVoiceAgentMessageCommand q,CancellationToken ct)=>throw new NotSupportedException();
        public Task<GuidedWorkSessionDto> CorrectFieldAsync(Guid c,Guid s,string p,CorrectGuidedDraftFieldCommand q,CancellationToken ct)=>throw new NotSupportedException();
        public Task<GuidedWorkSessionDto> ChangeFieldStatusesAsync(Guid c,Guid s,ChangeGuidedDraftFieldStatusesCommand q,CancellationToken ct)=>throw new NotSupportedException();
        public Task<GuidedWorkReviewDto> PrepareReviewAsync(Guid c,Guid s,PrepareGuidedWorkReviewCommand q,CancellationToken ct)=>throw new NotSupportedException();
        public Task<GuidedWorkCommitResultDto> ConfirmCommitAsync(Guid c,Guid s,ConfirmGuidedWorkCommitCommand q,CancellationToken ct)=>throw new NotSupportedException();
        public Task<GuidedWorkSessionDto> CancelAsync(Guid c,Guid s,CancelGuidedWorkSessionCommand q,CancellationToken ct)=>throw new NotSupportedException();
    }
}
