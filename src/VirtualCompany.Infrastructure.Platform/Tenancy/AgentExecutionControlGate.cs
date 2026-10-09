using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
namespace VirtualCompany.Infrastructure.Tenancy;

public sealed class AgentExecutionControlGate(VirtualCompanyDbContext db, TimeProvider clock) : IAgentExecutionControlGate
{
    public async Task<IExecutionControlFence> FenceAsync(Guid companyId, CancellationToken ct)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.");
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Execution admission requires its own committed transaction.");
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            // All new scope changes and admissions lock the same company row. The configuration lock
            // also serializes legacy pause/emergency-stop commands with admission.
            if (db.Database.IsSqlServer())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM companies WITH (UPDLOCK,HOLDLOCK) WHERE id={companyId}", ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM company_operating_configurations WITH (UPDLOCK,HOLDLOCK) WHERE company_id={companyId}", ct);
            }
            return new Fence(tx);
        }
        catch { await tx.DisposeAsync(); throw; }
    }
    public async Task<bool> IsPausedAsync(Guid companyId, Guid? agentId, CancellationToken ct, Guid? taskId=null)
    {
        var config = await db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId,ct);
        if(config?.IsPaused == true || config?.EmergencyStopped == true || await db.AgentExecutionControls.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x=>x.CompanyId==companyId && agentId.HasValue && x.ScopeId==agentId && x.Paused,ct)) return true;
        var visited=new HashSet<Guid>();
        while(taskId.HasValue)
        {
            if(!visited.Add(taskId.Value)||visited.Count>32)return true;
            var task=await db.WorkTasks.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==taskId,ct);
            if(task is null)return true;
            if(task.AssignedAgentId.HasValue&&await db.AgentExecutionControls.IgnoreQueryFilters().AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.ScopeId==task.AssignedAgentId&&x.Paused,ct))return true;
            taskId=task.ParentTaskId;
        }
        return false;
    }
    public async Task<Guid> AdmitAsync(Guid companyId, Guid? agentId, string boundary, string businessKey, CancellationToken ct, Guid? taskId=null)
    {
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length>64 || string.IsNullOrWhiteSpace(businessKey) || businessKey.Length>200) throw new ArgumentException("A bounded durable step identity is required.");
        await using var fence=await FenceAsync(companyId,ct);
        if (await IsPausedAsync(companyId,agentId,ct,taskId)) throw new ExecutionPausedException();
        if (await db.AgentExecutionAdmissions.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==companyId&&x.Boundary==boundary&&x.BusinessKey==businessKey,ct))
            throw new ExecutionControlConflictException("This step was already admitted. Review its recorded outcome; automatic resend is unavailable.");
        var row=new AgentExecutionAdmission(companyId,agentId,boundary,businessKey,clock.GetUtcNow().UtcDateTime);
        db.AgentExecutionAdmissions.Add(row); await db.SaveChangesAsync(ct); await fence.CommitAsync(ct); return row.Id;
    }
    public async Task AcknowledgeAsync(Guid companyId, Guid admissionId, bool confirmed, CancellationToken ct)
    {
        var row=await db.AgentExecutionAdmissions.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==companyId&&x.Id==admissionId,ct);
        row.Acknowledge(confirmed,clock.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(ct);
    }
    private sealed class Fence(IDbContextTransaction tx) : IExecutionControlFence
    {
        public Task CommitAsync(CancellationToken ct)=>tx.CommitAsync(ct);
        public ValueTask DisposeAsync()=>tx.DisposeAsync();
    }
}
