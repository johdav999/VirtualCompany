using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class Release2RenewalIntegrationTests
{
    [Fact]
    public async Task Renewal_native_sources_revision_one_decision_and_controlled_outbox_delivery_reconcile()
    {
        using var f=new Release2NativeDraftFactory();var s=await Release2PolicyIntegrationTests.Setup(f,"sales.proposal_drafting","deal");using var c=BusinessWorkEvidenceIntegrationTests.Client(f);
        // The reusable business fixture creates six unrelated manual reviews.
        // Close those through the owning decision API so Today's bounded five-item
        // shortlist demonstrates this renewal rather than random GUID tie ordering.
        var baselineApprovals=(await c.GetFromJsonAsync<List<ApprovalRequestDto>>($"/api/companies/{s.Company}/approvals"))!;
        foreach(var baseline in baselineApprovals)
        {
            var baselineRoute=$"/api/companies/{s.Company}/approvals/{baseline.Id}";
            var review=(await c.GetFromJsonAsync<ApprovalRequestDto>(baselineRoute))!;
            (await c.PostAsJsonAsync(baselineRoute+"/decisions",new ApprovalDecisionCommand(review.Id,"reject",review.CurrentStep!.Id,"Close unrelated synthetic fixture review before the bounded renewal demonstration.",Guid.NewGuid(),review.Review!.Token))).EnsureSuccessStatusCode();
        }
        Guid researchAgent=Guid.Empty,prospect=Guid.Empty,financeAgent=Guid.Empty,supportAgent=Guid.Empty,caseId=Guid.Empty,invoice=Guid.Empty;
        var knowledge=Guid.NewGuid();var chunkId=Guid.NewGuid();var researchGoal=Guid.NewGuid();
        const string terms="Renewal terms remain subject to the customer's retained agreement and human review. Open service commitments must be confirmed before delivery.";
        await f.SeedAsync(async db=>
        {
            researchAgent=await db.TaskTypePolicies.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company).Select(x=>x.AgentId).FirstAsync();prospect=await db.ProspectAccounts.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company).Select(x=>x.Id).SingleAsync();
            financeAgent=await db.Agents.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company&&x.Department=="Finance").Select(x=>x.Id).FirstAsync();supportAgent=await db.Agents.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company&&x.Department=="Support").Select(x=>x.Id).FirstAsync();caseId=await db.SupportCases.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company).Select(x=>x.Id).SingleAsync();
            var bill=await db.FinanceInvoices.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==s.Company);invoice=bill.Id;db.Entry(bill).Property(x=>x.DueUtc).CurrentValue=DateTime.UtcNow.AddDays(-1);
            var goal=new CompanyGoal(researchGoal,s.Company,"Renewal account research","Review the account before drafting.",CompanyGoalPriority.Normal,DateTime.UtcNow.AddDays(-1),DateTime.UtcNow.AddDays(2),ownerAgentId:researchAgent);goal.Activate();db.Add(goal);
            var doc=new CompanyKnowledgeDocument(knowledge,s.Company,"Approved renewal review guide",CompanyKnowledgeDocumentType.Reference,"p18/renewal.md",null,"renewal.md","text/markdown",".md",terms.Length,accessScope:new CompanyKnowledgeDocumentAccessScope(s.Company,CompanyKnowledgeDocumentAccessScope.CompanyVisibility));doc.MarkScanClean();doc.MarkProcessing();doc.MarkProcessed();doc.MarkIndexed(terms,1,1,"deterministic","fixture","v1",256,"p18-renewal");db.Add(doc);
            db.Add(new CompanyKnowledgeChunk(chunkId,s.Company,knowledge,1,0,terms,"["+string.Join(',',Enumerable.Repeat("0.0625",256))+"]","deterministic","fixture","v1",256,sourceReference:"renewal.md:1"));
        });
        var research=await Release2PolicyIntegrationTests.Queue(c,s.Company,researchAgent,"sales.account_research",prospect,1,researchGoal);await Release2PolicyIntegrationTests.Dispatch(f,s.Company);
        await f.SeedAsync(async db=>Assert.Equal(WorkTaskStatus.Completed,(await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==research)).Status));
        await Release2PolicyIntegrationTests.Apply(c,s.Company,await Release2PolicyIntegrationTests.Preview(c,s.Company,new(s.Agent,"sales.proposal_drafting","automatic",2,DateTime.UtcNow.AddDays(1),"Internal proposal; delivery separately reviewed")));
        var proposalTask=await Release2PolicyIntegrationTests.Queue(c,s.Company,s.Agent,"sales.proposal_drafting",s.Record,1);await Release2PolicyIntegrationTests.Dispatch(f,s.Company);
        FinanceCollectionsPlanResult finance=null!;SupportReplyDraftDto support=null!;SalesProposalAdviceResult revision=null!;
        await f.ExecuteScopeAsync(async scope=>
        {
            await SetActor(scope,s.Company,s.Owner);
            finance=await scope.ServiceProvider.GetRequiredService<IFinanceAgentDecisionService>().AnalyzeCollectionsAsync(s.Company,financeAgent,s.Owner,new(Objective:"Review overdue obligations before renewal; no collection delivery."),default);
            Assert.Contains(finance.Items,x=>x.InvoiceId==invoice&&x.OutstandingAmount==1000&&x.Currency=="SEK");
            support=(await scope.ServiceProvider.GetRequiredService<ISupportReplyDraftService>().GenerateDraftAsync(s.Company,s.Owner,caseId,new(ForceReview:true),default))!;
            revision=await scope.ServiceProvider.GetRequiredService<ISalesAgentDecisionService>().AdviseProposalAsync(s.Company,s.Agent,s.Owner,new(s.Record,Objective:$"Revise the internal proposal using Finance source {finance.Items.Single().SourceId} and retained Support draft {support.Id}. Preserve unconfirmed terms."),default);
            Assert.True(revision.RequiresReview);Assert.NotEmpty(revision.Unknowns);
        });
        Guid rootId=Guid.Empty,financeArtifact=Guid.Empty,supportArtifact=Guid.Empty,revisedArtifact=Guid.Empty;
        // The controlled runner retains outputs from the actual owners as immutable collaboration receipts.
        // It exercises the evidence/approval contract; it does not claim an autonomous LLM conversation.
        await f.SeedAsync(async db=>
        {
            var root=new WorkTask(Guid.NewGuid(),s.Company,"sales_renewal","Renewal: retained proposal and source review","Review sources before controlled customer delivery.",WorkTaskPriority.High,s.Agent,null,"user",s.Owner,new Dictionary<string,JsonNode?>{["dealId"]=JsonValue.Create(s.Record)});root.UpdateStatus(WorkTaskStatus.AwaitingApproval);db.Add(root);rootId=root.Id;
            var parent=new WorkTask(Guid.NewGuid(),s.Company,"manager_worker_collaboration","Renewal collaboration","Retained native owner contributions",WorkTaskPriority.Normal,s.Agent,root.Id,"user",s.Owner);db.Add(parent);
            var financeTask=new WorkTask(Guid.NewGuid(),s.Company,"finance_review","Renewal obligations review","Native Finance read/advice; no money movement",WorkTaskPriority.Normal,financeAgent,parent.Id,"user",s.Owner);financeTask.UpdateStatus(WorkTaskStatus.Completed,new Dictionary<string,JsonNode?>{["collections"]=JsonSerializer.SerializeToNode(finance)});db.Add(financeTask);
            var supportTask=new WorkTask(Guid.NewGuid(),s.Company,"support_review","Renewal service commitments","Native Support draft; human review required",WorkTaskPriority.Normal,supportAgent,parent.Id,"user",s.Owner);supportTask.UpdateStatus(WorkTaskStatus.Completed,new Dictionary<string,JsonNode?>{["draft"]=JsonSerializer.SerializeToNode(support)});db.Add(supportTask);
            var proposal=await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==proposalTask);proposal.LinkToParent(parent.Id);
            var plan=Guid.NewGuid();
            CollaborationContribution Receipt(WorkTask task,Guid agent,int sequence,int version,string output)=>new(s.Company,parent.Id,task.Id,plan,agent,sequence,version,OperatingCollaborationRole.Contributor,OperatingCollaborationPattern.SequentialHandoff,task.Title,"completed",output,"Retained actual owning-service result; controlled AI analysis response.");
            var fin=Receipt(financeTask,financeAgent,1,1,JsonSerializer.Serialize(finance));var sup=Receipt(supportTask,supportAgent,2,1,JsonSerializer.Serialize(support));var initial=Receipt(proposal,s.Agent,3,1,JsonSerializer.Serialize(proposal.OutputPayload));var revised=Receipt(proposal,s.Agent,3,2,JsonSerializer.Serialize(revision));financeArtifact=fin.Id;supportArtifact=sup.Id;revisedArtifact=revised.Id;db.AddRange(fin,sup,initial,revised,new CollaborationArtifactHandoff(s.Company,fin.Id,revised.Id,true),new CollaborationArtifactHandoff(s.Company,sup.Id,revised.Id,true));
        });
        using var created=await HttpClientJsonExtensions.PostAsJsonAsync(c,$"/api/companies/{s.Company}/approvals",new CreateApprovalRequestCommand("task",rootId,"user",s.Owner,"renewal_terms",new Dictionary<string,JsonNode?>{["reason"]=JsonValue.Create("Review native Finance and Support inputs and revised proposal; customer delivery remains separate.")},RequiredUserId:s.Owner));created.EnsureSuccessStatusCode();var approval=(await created.Content.ReadFromJsonAsync<ApprovalRequestDto>())!;
        var today=(await c.GetFromJsonAsync<TodayWorkspaceDto>($"/api/companies/{s.Company}/workspace/today?lens=company&refresh=true"))!;Assert.Contains(today.Decisions,x=>x.RelatedApprovalId==approval.Id);
        var work=(await c.GetFromJsonAsync<AgentWorkItemDto>($"/api/companies/{s.Company}/agent-work/task/{rootId}"))!;Assert.Contains(work.RelatedRecords,x=>x.Route.Contains(approval.Id.ToString()));
        var collaboration=(await c.GetFromJsonAsync<CollaborationEvidenceDto>($"/api/companies/{s.Company}/agent-work/task/{rootId}/collaboration"))!;Assert.Contains(collaboration.RelatedRecords,x=>x.Route.Contains(approval.Id.ToString()));var artifact=collaboration.Artifacts.Single(x=>x.Id==revisedArtifact);Assert.Contains(financeArtifact,artifact.InputIds);Assert.Contains(supportArtifact,artifact.InputIds);Assert.Equal(2,artifact.Version);
        var record=(await c.GetFromJsonAsync<BusinessWorkEvidenceDto>($"/api/companies/{s.Company}/agent-work/business/deal/{s.Record}"))!;Assert.Contains(record.Decisions!,x=>x.ApprovalId==approval.Id);
        var route=$"/api/companies/{s.Company}/approvals/{approval.Id}";approval=(await c.GetFromJsonAsync<ApprovalRequestDto>(route))!;var command=new ApprovalDecisionCommand(approval.Id,"approve",approval.CurrentStep!.Id,"Sources reviewed; retain unconfirmed terms.",Guid.NewGuid(),approval.Review!.Token);(await c.PostAsJsonAsync(route+"/decisions",command)).EnsureSuccessStatusCode();(await c.PostAsJsonAsync(route+"/decisions",command)).EnsureSuccessStatusCode();Assert.Empty(f.Delivery.Calls);
        await f.ExecuteScopeAsync(async scope=>
        {
            await SetActor(scope,s.Company,s.Owner);var service=scope.ServiceProvider.GetRequiredService<ISupportReplyDraftService>();
            // This exact source is processed/indexed/company-visible. The owning safety policy rechecks it.
            var db=scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();var draft=await db.SupportReplyDrafts.SingleAsync(x=>x.Id==support.Id);db.Entry(draft).Property(x=>x.SourceReferencesJson).CurrentValue=new JsonArray(new JsonObject{["type"]="knowledge_chunk",["trusted"]=true,["documentId"]=knowledge.ToString(),["entityId"]=chunkId.ToString()}).ToJsonString();await db.SaveChangesAsync();
            await service.EditDraftAsync(s.Company,s.Owner,support.Id,new(terms,"Clear"),default);await service.ApproveDraftAsync(s.Company,s.Owner,support.Id,new("Approved controlled renewal review reply"),default);await service.SendDraftAsync(s.Company,s.Owner,support.Id,new(ToEmail:"renewal-controlled@example.invalid",Subject:"Controlled renewal review",OriginalMessageId:"p18-original"),default);
        });
        Assert.Empty(f.Delivery.Calls);await f.ExecuteScopeAsync(async scope=>await scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>().DispatchPendingAsync(default));await f.ExecuteScopeAsync(async scope=>await scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>().DispatchPendingAsync(default));Assert.Single(f.Delivery.Calls);
        await f.SeedAsync(async db=>{Assert.Equal(ApprovalRequestStatus.Approved,(await db.ApprovalRequests.IgnoreQueryFilters().SingleAsync(x=>x.Id==approval.Id)).Status);Assert.Equal(WorkTaskStatus.InProgress,(await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==rootId)).Status);Assert.NotNull((await db.SupportReplyDrafts.IgnoreQueryFilters().SingleAsync(x=>x.Id==support.Id)).SentUtc);Assert.Single(await db.SupportMessages.IgnoreQueryFilters().Where(x=>x.ReplyDraftId==support.Id&&x.ProviderMessageId=="p18-confirmed-reply").ToListAsync());Assert.Single(await db.AuditEvents.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company&&x.Action=="approval.review.approve"&&x.TargetId==approval.Id.ToString("N")).ToListAsync());});
        // This owning command records a human-created draft, not an attributed agent send.
        // Reconcile the actual send without inventing agent attribution in the report.
        await f.SeedAsync(async db=>Assert.Null((await db.SupportReplyDrafts.IgnoreQueryFilters().SingleAsync(x=>x.Id==support.Id)).CreatedByAgentId));
        var result=(await c.GetFromJsonAsync<AgentSupervisionReport>($"/api/companies/{s.Company}/agent-supervision?view=outcomes"))!;Assert.Equal(0,result.Measures.Single(x=>x.Code=="delivery_recorded").Count);
        if(Environment.GetEnvironmentVariable("VCSCREENS_P18_EVIDENCE_DIR") is {Length:>0} evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory,"renewal-source-reconciliation.json"),JsonSerializer.Serialize(new
            {
                observedUtc=DateTime.UtcNow,environment="GUID-isolated SQLite + authenticated TestServer; actual queue/owners/outbox; controlled AI and recording mail adapter",
                company=s.Company,root=rootId,deal=s.Record,researchTask=research,proposalTask,financeInvoice=invoice,financeArtifact,supportArtifact,revisedArtifact,
                approval=approval.Id,entryPoints=new[]{"Today","Work","collaboration","business deal"},sameDecision=true,proposalVersion=artifact.Version,exactInputIds=artifact.InputIds,
                supportDraft=support.Id,providerCalls=f.Delivery.Calls.Count,providerMessage="p18-confirmed-reply",controlledRecipient="renewal-controlled@example.invalid",
                reportSnapshot=result.SnapshotHash,owningSentRecords=1,attributedAgentSendCount=result.Measures.Single(x=>x.Code=="delivery_recorded").Count,
                limits="Test adapter confirmation and persisted SentUtc, not live mailbox delivery or customer receipt. Native human-created Support drafts have no recorded agent attribution and are excluded from agent-send counts. Controlled runner records real owner outputs as immutable contributions; autonomous LLM coordination/writing quality unverified."
            },new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    private static async Task SetActor(IServiceScope scope,Guid company,Guid owner)
    {var db=scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();var member=await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company&&x.UserId==owner);scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyContext(new(member.Id,company,owner,"Controlled renewal",member.Role,member.Status));}
}
