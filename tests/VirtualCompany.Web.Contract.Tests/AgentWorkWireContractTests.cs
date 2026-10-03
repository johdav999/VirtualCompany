using Microsoft.EntityFrameworkCore;
using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class AgentWorkWireContractTests
{
    [Fact]
    public async Task Per_state_pages_show_older_active_work_despite_newer_completed_history_and_page_each_lane_after_filters()
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var human = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Users.Add(new User(human, "kanban-wire@example.com", "Kanban owner", "dev-header", "kanban-wire"));
            db.Companies.Add(new Company(company, "Kanban wire"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, human, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });
        var fixture = await AgentWorkLifecycleFixture.SeedAsync(factory, company, human);
        await fixture.SeedPagingHistoryAsync(factory);
        using var http = factory.CreateClient(); http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", "kanban-wire");
        http.DefaultRequestHeaders.Add("X-Dev-Auth-Email", "kanban-wire@example.com");
        var client = new AgentWorkApiClient(new CompanyApiTransport(http));
        var query = new AgentWorkQuery(company, "finance", fixture.AgentId, "Kanban");
        var global = (await client.ListAsync(query))!;
        Assert.Equal(8, global.StateCounts[AgentWorkStates.Active]);
        Assert.Equal(24, global.Items.Count); Assert.All(global.Items, x => Assert.Equal(AgentWorkStates.Completed, x.State));
        var board = (await client.ListAsync(query with { PerState = true }))!;
        Assert.Equal(63, board.Total); Assert.Equal(8, board.Items.Count(x => x.State == AgentWorkStates.Active));
        Assert.Equal(24, board.Items.Count(x => x.State == AgentWorkStates.Completed)); Assert.True(board.HasNext);
        Assert.Equal(board.Items.Count, board.Items.Select(x => (x.Kind, x.Id)).Distinct().Count());
        Assert.All(board.Items, x => { Assert.Equal("finance", x.Responsibility); Assert.Contains(x.Agents, a => a.Id == fixture.AgentId); });
        foreach (var state in new[] { AgentWorkStates.Active, AgentWorkStates.Completed })
            Assert.Equal(board.Items.Where(x => x.State == state).OrderByDescending(x => x.UpdatedUtc).Select(x => x.Id), board.Items.Where(x => x.State == state).Select(x => x.Id));
        var second = (await client.ListAsync(query with { PerState = true, State = AgentWorkStates.Completed, Skip = 24 }))!;
        var last = (await client.ListAsync(query with { PerState = true, State = AgentWorkStates.Completed, Skip = 48 }))!;
        Assert.Equal(24, second.Items.Count); Assert.True(second.HasNext);
        Assert.Equal(7, last.Items.Count); Assert.False(last.HasNext); Assert.Equal(55, last.Total);
        Assert.All(second.Items.Concat(last.Items), x => Assert.Equal(AgentWorkStates.Completed, x.State));
        Assert.Empty(board.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.Empty(second.Items.Select(x => x.Id).Intersect(last.Items.Select(x => x.Id)));
        var bounded = (await client.ListAsync(query with { PerState = true, Take = 2 }))!;
        Assert.Equal(4, bounded.Items.Count); Assert.Equal(63, bounded.Total);
        Assert.Null(await client.ListAsync(query with { CompanyId = Guid.NewGuid(), PerState = true }));
        var empty = (await client.ListAsync(query with { PerState = true, Objective = "absent objective" }))!;
        Assert.Empty(empty.Items); Assert.Equal(0, empty.Total); Assert.False(empty.HasNext);
    }

    [Fact]
    public async Task Typed_board_detail_and_Work_read_real_shared_outcome_and_authoritative_task_identity()
    {
        using var factory=new TestWebApplicationFactory(); var company=Guid.NewGuid(); var human=Guid.NewGuid();
        await factory.SeedAsync(db=>{ db.Users.Add(new User(human,"p10-wire@example.com","Wire owner","dev-header","p10-wire"));
            db.Companies.Add(new Company(company,"P10 wire")); db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(),company,human,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));return Task.CompletedTask; });
        var fixture=await AgentWorkLifecycleFixture.SeedAsync(factory,company,human);
        using var http=factory.CreateClient(); http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject","p10-wire");http.DefaultRequestHeaders.Add("X-Dev-Auth-Email","p10-wire@example.com");
        var transport=new CompanyApiTransport(http);var client=new AgentWorkApiClient(transport);
        var board=(await client.ListAsync(new(company,Objective:"P10")))!;
        Assert.All(AgentWorkStates.All,x=>Assert.True(board.StateCounts[x]>0));
        var detail=(await client.GetAsync(company,"initiative",fixture.SharedId))!;
        Assert.Equal(AgentWorkStates.Active,detail.State);Assert.Equal(2,detail.Agents.Count);Assert.Null(detail.CompletedUtc);
        Assert.Contains(detail.Outputs,x=>x.Summary.Contains("Draft only"));Assert.DoesNotContain(board.Items,x=>x.Id==fixture.SharedTaskId);
        var task=(await client.GetAsync(company,"task",fixture.CompletedTaskId))!;
        var work=await new TaskApiClient(http,false).GetAsync(company,fixture.CompletedTaskId);
        Assert.Equal(task.Id,work!.Id); Assert.Equal(task.SourceState,work.Status);Assert.Equal(task.Title,work.Title);
        Assert.Null(await client.GetAsync(Guid.NewGuid(),"initiative",fixture.SharedId));
    }
}
