using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
namespace VirtualCompany.Api.Tests;

[Trait("Category","SqlServer")]
public sealed class ExecutionControlSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Migration_round_trip_preserves_previous_work_and_policy_and_serializes_pause_with_admission()
    {
        using var f=TestWebApplicationFactory.CreateSqlServer(TimeProvider.System);var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(f,company,owner);var p=await AuthorityExplanationFixture.SeedAsync(f,company,owner);
        await f.SeedAsync(async db=>{
            var policy=new TaskTypePolicy(company,p.AgentId,"finance.fixture");db.Add(policy);await db.SaveChangesAsync();
            var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var controlMigration=Array.FindIndex(migrations,x=>x.EndsWith("AddAgentExecutionControls",StringComparison.Ordinal));
            Assert.True(controlMigration>0,"Execution-control migration and its predecessor must be applied.");
            await db.GetService<IMigrator>().MigrateAsync(migrations[controlMigration-1]);db.ChangeTracker.Clear();
            Assert.True(await db.TaskTypePolicies.IgnoreQueryFilters().AnyAsync(x=>x.Id==policy.Id));Assert.True(await db.WorkTasks.IgnoreQueryFilters().AnyAsync(x=>x.Id==p.TaskId));
            await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();Assert.Empty(await db.AgentExecutionControls.IgnoreQueryFilters().ToListAsync());
            Assert.Equal(migrations,(await db.Database.GetAppliedMigrationsAsync()).ToArray());
        });
        using var first=f.Services.CreateScope();using var second=f.Services.CreateScope();
        var firstGate=first.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>();var secondGate=second.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>();
        await using(var fence=await firstGate.FenceAsync(company,default)){
            var waiting=secondGate.AdmitAsync(company,p.AgentId,"tool","race-after-pause",default);
            var db=first.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();var control=new AgentExecutionControl(company,p.AgentId);control.Change(true,DateTime.UtcNow);db.Add(control);await db.SaveChangesAsync();
            await fence.CommitAsync(default);await Assert.ThrowsAsync<ExecutionPausedException>(()=>waiting);
        }
        await f.SeedAsync(async db=>{Assert.Empty(await db.AgentExecutionAdmissions.IgnoreQueryFilters().ToListAsync());Assert.True(await db.TaskTypePolicies.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company));});
    }
}
