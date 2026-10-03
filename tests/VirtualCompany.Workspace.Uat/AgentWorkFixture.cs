using Microsoft.EntityFrameworkCore;
using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

internal static class AgentWorkFixture
{
    public static async Task SeedAsync(TestWebApplicationFactory factory)
    {
        var company=Guid.Parse("11111111-1111-1111-1111-111111111111");Guid human=default;
        await factory.SeedAsync(async db=>human=await db.Users.Where(x=>x.Email=="p01-owner@example.com").Select(x=>x.Id).SingleAsync());
        var fixture = await AgentWorkLifecycleFixture.SeedAsync(factory,company,human);
        await fixture.SeedPagingHistoryAsync(factory);
        await factory.SeedAsync(db=>{ for(var i=1;i<=26;i++)db.WorkTasks.Add(new WorkTask(Guid.NewGuid(),company,"sales_review",$"P10 page review {i:00}",
            "Internal pagination acceptance fixture",WorkTaskPriority.Normal,null,null,"user",human,rationaleSummary:"Retained pagination review evidence"));return Task.CompletedTask; });
    }
}
