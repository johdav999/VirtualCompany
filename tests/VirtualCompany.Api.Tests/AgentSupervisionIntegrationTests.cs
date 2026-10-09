using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class AgentSupervisionIntegrationTests
{
    internal static string Route(Guid company,string view="work",string? metric=null)=>$"/api/companies/{company}/agent-supervision?from=2030-03-31&to=2030-03-31&view={view}"+(metric is null?"":$"&metric={metric}");
    [Fact]
    public async Task Company_outcomes_contributions_retries_and_old_work_have_distinct_units_and_exact_drilldowns()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);using var client=AgentSupervisionFixture.Client(factory);
        var outcomes=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"outcomes")))!;
        Assert.Equal(1,Measure(outcomes,"prepared_output").Count);Assert.Equal(1,Measure(outcomes,"prepared_output").Denominator);
        Assert.Equal(1,Measure(outcomes,"reviewed_outcome").Count);Assert.Equal(1,Measure(outcomes,"reviewed_outcome").Denominator);
        Assert.Equal(1,Measure(outcomes,"execution_failure").Count);Assert.Equal(1,Measure(outcomes,"execution_failure").Denominator);
        Assert.Equal(2,outcomes.Rows.Single(x=>x.Metric=="reviewed_outcome").Evidence.Count);
        Assert.Equal(f.OldTask,outcomes.Rows.Single(x=>x.Metric=="execution_failure").WorkId);Assert.Equal(2,outcomes.Rows.Single(x=>x.Metric=="execution_failure").Evidence.Count);
        Assert.All(outcomes.Rows.Where(x=>x.WorkId==f.Initiative),x=>Assert.Equal("initiative",x.WorkKind));
        var work=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company)))!;
        Assert.Equal(2,Measure(work,"contributions").Count);Assert.Single(work.Rows.Where(x=>x.Metric=="work_in_progress"));
        Assert.Equal(f.UnstartedInitiative,work.Rows.Single(x=>x.Metric=="work_in_progress").WorkId);
        Assert.All(work.Rows.Where(x=>x.Metric=="contributions"),x=>Assert.Equal(2,x.Evidence.Count));
        var policy=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"authority")))!;Assert.Equal(2,Measure(policy,"policy_exceptions").Count);Assert.Equal(2,Measure(policy,"policy_exceptions").Denominator);
        var corrections=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"bottlenecks","corrections")))!;
        Assert.Single(corrections.Rows);Assert.Equal(f.Initiative,corrections.Rows.Single().WorkId);Assert.Equal(2,corrections.Rows.Single().Evidence.Count);
        await factory.SeedAsync(async db=>{Assert.Equal(2,await db.ToolExecutionAttempts.IgnoreQueryFilters().CountAsync());Assert.Equal(3,await db.ApprovalRequests.IgnoreQueryFilters().CountAsync());});
    }
    [Fact]
    public async Task Approval_intervals_clip_DST_period_and_missing_terminal_time_is_unavailable()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);using var client=AgentSupervisionFixture.Client(factory);
        var report=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"bottlenecks")))!;
        Assert.Equal(23,(report.UntilUtc-report.FromUtc).TotalHours);Assert.Equal(AgentSupervisionFixture.Utc(30,23),report.FromUtc);
        var resolved=report.Rows.Single(x=>x.Metric=="approval_wait"&&x.ApprovalId==f.ResolvedApproval);Assert.Equal(7200,resolved.SecondsInPeriod);Assert.Equal(10800,resolved.TotalSeconds);Assert.False(resolved.Ongoing);
        var pending=report.Rows.Single(x=>x.Metric=="approval_wait"&&x.ApprovalId==f.PendingApproval);Assert.Equal(82800,pending.SecondsInPeriod);Assert.True(pending.Ongoing);
        var unknown=report.Rows.Single(x=>x.Metric=="approval_wait"&&x.ApprovalId==f.UnknownApproval);Assert.Null(unknown.TotalSeconds);Assert.Null(unknown.SecondsInPeriod);Assert.False(unknown.Ongoing);
        Assert.Equal(3,Measure(report,"approval_wait").Count);Assert.Equal(1,Measure(report,"approval_turnaround").Count);Assert.Equal(3,Measure(report,"approval_turnaround").Denominator);
        Assert.Equal(90000,Measure(report,"approval_wait").SecondsInPeriod);Assert.Equal(10800,Measure(report,"approval_turnaround").AverageTotalSeconds);Assert.Null(Measure(report,"blocked_duration").Count);
    }
    [Fact]
    public async Task Export_is_fresh_authorized_same_snapshot_and_formula_safe_with_exact_metric_scope()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);using var client=AgentSupervisionFixture.Client(factory);
        var route=Route(f.Company,"outcomes","execution_failure");var before=(await client.GetFromJsonAsync<AgentSupervisionReport>(route))!;
        await factory.SeedAsync(async db=>{var task=await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==f.OldTask);db.Entry(task).Property(x=>x.Title).CurrentValue="  =Changed retained title";});
        var exportRoute=route.Replace("agent-supervision?","agent-supervision/export?");using var response=await client.GetAsync(exportRoute);Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());Assert.True(response.Headers.CacheControl!.NoStore);
        var export=(await response.Content.ReadFromJsonAsync<SupervisionCsv>())!;Assert.Single(export.Report.Rows);Assert.Equal("execution_failure",export.Report.Rows.Single().Metric);Assert.NotEqual(before.SnapshotHash,export.Report.SnapshotHash);
        Assert.Contains(export.Report.SnapshotHash,export.Content);Assert.Contains("'  =Changed retained title",export.Content);Assert.Contains(f.OldTask.ToString(),export.Content);Assert.DoesNotContain("agent_contribution",export.Content);
        Assert.Equal(export.Report.Rows.Count(x=>x.Metric=="execution_failure"),Measure(export.Report,"execution_failure").Count);
    }
    [Theory]
    [InlineData("&view=unknown")][InlineData("&metric=business_outcome")][InlineData("&from=2030-04-04")][InlineData("&to=2029-03-31")][InlineData("&from=not-a-date")]
    public async Task Invalid_scopes_are_rejected(string suffix)
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);using var client=AgentSupervisionFixture.Client(factory);
        // Replace rather than repeat the relevant query key.
        var key=suffix.Split('=')[0][1..];var value=suffix[(suffix.IndexOf('=')+1)..];
        var route=$"/api/companies/{f.Company}/agent-supervision?from={(key=="from"?value:"2030-03-31")}&to={(key=="to"?value:"2030-03-31")}&view={(key=="view"?value:"work")}"+(key=="metric"?$"&metric={value}":"");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(route)).StatusCode);
    }
    [Fact]
    public async Task Responsibility_denial_foreign_agent_and_employee_never_leak_counts_or_export()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);
        await factory.SeedAsync(db=>{foreach(var (subject,role) in new[]{("p17-sales",CompanyMembershipRole.Manager),("p17-employee",CompanyMembershipRole.Employee)}){
            var user=Guid.NewGuid();var membership=Guid.NewGuid();db.AddRange(new User(user,subject+"@example.com",subject,"dev-header",subject),new CompanyMembership(membership,f.Company,user,role,CompanyMembershipStatus.Active));if(role==CompanyMembershipRole.Manager)db.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(),f.Company,ResponsibilityArea.Sales,ResponsibilityAssignmentKind.Primary,membership,null,AgentAutonomyLevel.Level1,null,null));}return Task.CompletedTask;});
        using var sales=AgentSupervisionFixture.Client(factory,"p17-sales");var visible=(await sales.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"outcomes")))!;Assert.Empty(visible.Rows);Assert.Empty(visible.Agents);Assert.Null(Measure(visible,"provider_confirmed").Count);
        Assert.Equal(HttpStatusCode.Forbidden,(await sales.GetAsync(Route(f.Company)+"&responsibility=finance")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await sales.GetAsync(Route(f.Company)+$"&agentId={f.Agent}")).StatusCode);
        using var employee=AgentSupervisionFixture.Client(factory,"p17-employee");Assert.Equal(HttpStatusCode.Forbidden,(await employee.GetAsync(Route(f.Company))).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await employee.GetAsync(Route(f.Company).Replace("agent-supervision?","agent-supervision/export?"))).StatusCode);
        using var owner=AgentSupervisionFixture.Client(factory);Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync(Route(Guid.NewGuid()))).StatusCode);
        var filtered=(await owner.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"outcomes")+$"&agentId={f.Agent}&responsibility=finance&taskType=finance_review"))!;Assert.Null(Measure(filtered,"provider_confirmed").Count);Assert.All(filtered.Rows,x=>Assert.Equal(f.Agent,x.AgentId));
    }
    private static SupervisionMeasure Measure(AgentSupervisionReport report,string metric)=>report.Measures.Single(x=>x.Code==metric);

    [Fact]
    public async Task Review_escalations_and_late_audit_receipts_on_older_approvals_are_retained_without_new_wait_intervals()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);
        await factory.SeedAsync(async db=>{
            var initiative=await db.OperatingInitiatives.IgnoreQueryFilters().SingleAsync(x=>x.Id==f.Initiative);
            foreach(var number in new[]{1,2}){
                var review=new OperatingReview(Guid.NewGuid(),f.Company,initiative.PlanId,1,f.Initiative,OperatingReviewOutcome.Escalate,"Accountable review required","Retained source","Recorded dependency","Human review","escalation-"+number,.8m);db.Add(review);db.Entry(review).Property(x=>x.CreatedUtc).CurrentValue=AgentSupervisionFixture.Utc(31,14+number);
            }
            var old=ApprovalRequest.CreateForTarget(Guid.NewGuid(),f.Company,ApprovalTargetEntityType.Task,f.Task,"user",f.Owner,"review",new Dictionary<string,System.Text.Json.Nodes.JsonNode?>(){["reason"]=System.Text.Json.Nodes.JsonValue.Create("Retained older decision")},null,f.Owner,[]);
            old.ApproveCurrentStep(old.CurrentActionableStep!.Id,f.Owner,"Retained older decision");db.Add(old);db.Entry(old).Property(x=>x.CreatedUtc).CurrentValue=AgentSupervisionFixture.Utc(28,10);db.Entry(old).Property(x=>x.DecidedUtc).CurrentValue=AgentSupervisionFixture.Utc(29,10);
            db.Add(new AuditEvent(Guid.NewGuid(),f.Company,"user",f.Owner,"approval.review.request_changes","approval_request",old.Id.ToString("D"),"changes_requested",occurredUtc:AgentSupervisionFixture.Utc(31,12)));
        });
        using var client=AgentSupervisionFixture.Client(factory);var authority=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"authority","escalations")))!;Assert.Single(authority.Rows);Assert.Equal(2,authority.Rows.Single().Evidence.Count);Assert.Equal(f.Initiative,authority.Rows.Single().WorkId);
        var bottlenecks=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"bottlenecks")))!;Assert.Equal(3,Measure(bottlenecks,"approval_wait").Count);Assert.Equal(1,Measure(bottlenecks,"corrections").Count);Assert.Contains(bottlenecks.Rows.Single(x=>x.Metric=="corrections").Evidence,x=>x.Source=="audit_event");
    }
    [Fact]
    public async Task Events_at_exclusive_end_are_excluded_and_autumn_day_has_25_hours()
    {
        var clock=new AgentSupervisionFixture.Clock();using var factory=new TestWebApplicationFactory(clock);var f=await AgentSupervisionFixture.Seed(factory);
        await factory.SeedAsync(async db=>{
            var initiative=await db.OperatingInitiatives.IgnoreQueryFilters().SingleAsync(x=>x.Id==f.Initiative);
            var review=new OperatingReview(Guid.NewGuid(),f.Company,initiative.PlanId,1,f.Initiative,OperatingReviewOutcome.CloseSuccessful,"Boundary review","Retained source","Boundary evidence","Close","exclusive-end",.8m);db.Add(review);db.Entry(review).Property(x=>x.CreatedUtc).CurrentValue=AgentSupervisionFixture.Utc(31,22);
            var attempt=new ToolExecutionAttempt(Guid.NewGuid(),f.Company,f.Agent,"get_cash_balance",ToolActionType.Read,"finance",taskId:f.OldTask,startedAtUtc:AgentSupervisionFixture.Utc(31,21));attempt.MarkFailed(null,null,AgentSupervisionFixture.Utc(31,22));db.Add(attempt);
        });
        using var client=AgentSupervisionFixture.Client(factory);var report=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"outcomes")))!;Assert.Equal(2,report.Rows.Single(x=>x.Metric=="reviewed_outcome").Evidence.Count);Assert.Equal(2,report.Rows.Single(x=>x.Metric=="execution_failure").Evidence.Count);
        clock.Now=new(2030,10,28,12,0,0,DateTimeKind.Utc);var autumn=(await client.GetFromJsonAsync<AgentSupervisionReport>($"/api/companies/{f.Company}/agent-supervision?from=2030-10-27&to=2030-10-27&view=bottlenecks"))!;Assert.Equal(25,(autumn.UntilUtc-autumn.FromUtc).TotalHours);
    }

    [Fact]
    public async Task Bounds_are_explicit_and_counts_and_export_reconcile_only_with_included_permitted_rows()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);
        await factory.SeedAsync(db=>{for(var i=0;i<2001;i++){
            var task=new WorkTask(Guid.NewGuid(),f.Company,"finance.bound","Bounded review "+i,"Retained work",WorkTaskPriority.Normal,f.Agent,null,"user",f.Owner);db.Add(task);db.Entry(task).Property(x=>x.CreatedUtc).CurrentValue=AgentSupervisionFixture.Utc(31,10);
        }return Task.CompletedTask;});
        using var client=AgentSupervisionFixture.Client(factory);var route=Route(f.Company)+"&taskType=finance.bound";var report=(await client.GetFromJsonAsync<AgentSupervisionReport>(route))!;
        Assert.True(report.Partial);Assert.Equal(2000,report.Rows.Count);Assert.Equal(report.Rows.Count,Measure(report,"work_in_progress").Count);Assert.Contains(report.Coverage,x=>x.Contains("2000-row bound"));
        var csv=(await client.GetFromJsonAsync<SupervisionCsv>(route.Replace("agent-supervision?","agent-supervision/export?")))!;Assert.True(csv.Report.Partial);Assert.Equal(report.Rows.Select(x=>x.Key),csv.Report.Rows.Select(x=>x.Key));
    }
    [Fact]
    public async Task Hidden_shared_initiative_is_never_relabelled_as_standalone_work_or_exported()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);
        await factory.SeedAsync(async db=>{
            var sales=new Agent(Guid.NewGuid(),f.Company,"p17-sales-agent","Sales colleague","Sales review","Sales",null,AgentSeniority.Senior,AgentStatus.Active);db.Add(sales);
            var root=await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==f.Task);root.AssignTo(sales.Id);
            db.Entry(root).Property(x=>x.Type).CurrentValue="sales.secret_shared";
            var initiative=await db.OperatingInitiatives.IgnoreQueryFilters().SingleAsync(x=>x.Id==f.Initiative);db.Entry(initiative).Property(x=>x.OwnerAgentId).CurrentValue=sales.Id;
            db.Add(new OperatingInitiativeCollaborator(Guid.NewGuid(),f.Company,f.Initiative,f.Agent,OperatingCollaborationRole.Contributor,OperatingCollaborationPattern.Parallel,1,"Restricted Finance source","Margin receipt"));
            var user=Guid.NewGuid();var membership=Guid.NewGuid();db.AddRange(new User(user,"p17-shared@example.com","Sales manager","dev-header","p17-shared"),new CompanyMembership(membership,f.Company,user,CompanyMembershipRole.Manager,CompanyMembershipStatus.Active),new CompanyResponsibilityAssignment(Guid.NewGuid(),f.Company,ResponsibilityArea.Sales,ResponsibilityAssignmentKind.Primary,membership,null,AgentAutonomyLevel.Level1,null,null));
        });
        using var client=AgentSupervisionFixture.Client(factory,"p17-shared");var report=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"outcomes")))!;Assert.Empty(report.Rows);Assert.Equal(0,Measure(report,"prepared_output").Count);Assert.Equal(0,Measure(report,"prepared_output").Denominator);Assert.DoesNotContain("sales.secret_shared",report.TaskTypes);
        var export=(await client.GetFromJsonAsync<SupervisionCsv>(Route(f.Company,"outcomes").Replace("agent-supervision?","agent-supervision/export?")))!;Assert.DoesNotContain(f.Task.ToString(),export.Content);Assert.DoesNotContain(f.Initiative.ToString(),export.Content);
    }
    [Fact]
    public async Task Native_provider_confirmation_and_settlement_deduplicate_batch_versions_without_inventing_attribution()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);var batchId=Guid.NewGuid();
        await factory.SeedAsync(db=>{
            var now=AgentSupervisionFixture.Utc(31,8);var hash=new string('a',64);var account=new FinanceAccount(Guid.NewGuid(),f.Company,"1930","Fixture cash","asset","SEK",0,now);
            var bank=new CompanyBankAccount(Guid.NewGuid(),f.Company,account.Id,"Fixture bank account","Fixture bank","•••• 1717","SEK");
            var connection=new BankConnection(Guid.NewGuid(),f.Company,"fixture","p17-bank","Provider-free bank",f.Owner,now);
            var batch=new PaymentBatch(batchId,f.Company,"P17-STAGES","Native payment stages",new(2030,3,31),"p17-create",hash,f.Owner,now);
            var approval=ApprovalRequest.CreateForTarget(Guid.NewGuid(),f.Company,ApprovalTargetEntityType.PaymentBatch,batchId,"user",f.Owner,"payment_batch",new Dictionary<string,System.Text.Json.Nodes.JsonNode?>(){["reason"]=System.Text.Json.Nodes.JsonValue.Create("Retained fixture")},null,f.Owner,[]);
            var binding=new PaymentBatchApprovalBinding(Guid.NewGuid(),f.Company,batchId,approval.Id,1,hash,f.Owner,now);db.AddRange(account,bank,connection,batch,approval,binding);
            for(var version=1;version<=2;version++){
                var execution=new PaymentBatchExecution(Guid.NewGuid(),f.Company,batchId,version,binding.Id,connection.Id,bank.Id,"fixture",hash,"p17-execute-"+version,f.Owner,null,now);
                execution.RecordSubmission("fixture-payment-"+version,null,"COMPLETED",true,false,false,now.AddHours(1));execution.MarkSettled(now.AddHours(2));db.Add(execution);
            }return Task.CompletedTask;
        });
        using var client=AgentSupervisionFixture.Client(factory);var report=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"outcomes")))!;
        Assert.Equal(1,Measure(report,"provider_confirmed").Count);Assert.Equal(1,Measure(report,"business_outcome").Count);
        Assert.All(report.Rows.Where(x=>x.WorkId==batchId),x=>{Assert.Null(x.AgentId);Assert.Equal("payment_batch",x.WorkKind);Assert.Equal(2,x.Evidence.Count);});
        var agent=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"outcomes")+$"&agentId={f.Agent}"))!;Assert.Null(Measure(agent,"business_outcome").Count);Assert.DoesNotContain(agent.Rows,x=>x.WorkId==batchId);
    }
    [Fact]
    public async Task Retained_handoff_receipts_and_supervisor_commands_have_their_own_units_and_exact_destinations()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);
        await factory.SeedAsync(async db=>{
            var receipts=await db.CollaborationContributions.IgnoreQueryFilters().Where(x=>x.CompanyId==f.Company&&x.Version==2).OrderBy(x=>x.Sequence).ToArrayAsync();
            var handoff=new CollaborationArtifactHandoff(f.Company,receipts[0].Id,receipts[1].Id,false,"Recorded review dependency");db.Add(handoff);db.Entry(handoff).Property(x=>x.CreatedUtc).CurrentValue=AgentSupervisionFixture.Utc(31,7);
            db.Add(new AgentExecutionControlCommand(f.Company,Guid.NewGuid(),f.Agent,1,true,f.Owner,"Review dependency","hash",AgentSupervisionFixture.Utc(31,8)));
        });
        using var client=AgentSupervisionFixture.Client(factory);var report=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"bottlenecks","blocked_handoffs")))!;
        var row=Assert.Single(report.Rows);Assert.Equal(14400,row.TotalSeconds);Assert.Contains("handoff_receipt",row.EvidenceStage);Assert.Equal(1,Measure(report,"blocked_handoffs").Denominator);
        var authority=(await client.GetFromJsonAsync<AgentSupervisionReport>(Route(f.Company,"authority")))!;var command=Assert.Single(authority.Rows.Where(x=>x.Metric=="interventions"));Assert.Contains("/settings/agents/execution?",command.WorkRoute);Assert.Contains(f.Agent.ToString(),command.WorkRoute);Assert.Null(Measure(authority,"budget_history").Count);
    }
}
