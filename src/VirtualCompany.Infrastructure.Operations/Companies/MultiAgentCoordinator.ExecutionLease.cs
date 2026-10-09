using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class MultiAgentCoordinator
{
    public async Task<MultiAgentCollaborationResultDto> ExecuteAsync(StartMultiAgentCollaborationCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        // Preserve the existing validation/audit path for invalid requests, before acquiring anything.
        if (command.CompanyId == Guid.Empty || command.CoordinatorAgentId == Guid.Empty || string.IsNullOrWhiteSpace(command.Objective) ||
            command.Workers is null || command.Workers.Count == 0) return await ExecuteCoreAsync(command, ct);
        var scope = await _visibility.ResolveAsync(command.CompanyId, ct);
        var requiredAgents = command.Workers.Select(x => x.AgentId).Append(command.CoordinatorAgentId).Distinct().ToArray();
        if (await scope.Agents(_dbContext.Agents).CountAsync(x => requiredAgents.Contains(x.Id), ct) != requiredAgents.Length ||
            SourceTask(command) is Guid source && !await scope.Tasks(_dbContext.WorkTasks).AnyAsync(x => x.CompanyId == command.CompanyId && x.Id == source, ct))
            throw new UnauthorizedAccessException("The collaboration contains work outside the current responsibility and access scope.");
        var correlation = EnsureCorrelationId(command.CorrelationId); command = command with { CorrelationId = correlation };
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { command.Objective, command.CoordinatorAgentId, command.Workers, command.InputPayload }))));
        var lease = await _dbContext.CollaborationExecutionLeases.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Key == correlation, ct);
        if (lease is null) { lease = new(command.CompanyId, correlation, fingerprint); _dbContext.Add(lease); }
        if (lease.Fingerprint != fingerprint)
            throw new MultiAgentCollaborationValidationException(new Dictionary<string,string[]> { ["correlationId"] = ["This execution identity belongs to different work. Use a new identity for revised work."] });
        var token = Guid.NewGuid();
        if (!lease.TryClaim(token, DateTime.UtcNow, TimeSpan.FromSeconds(ResolveLimits(command.Limits).MaxRuntimeSeconds + 30)))
            throw new CollaborationAlreadyRunningException();
        try { await _dbContext.SaveChangesAsync(ct); }
        catch (DbUpdateException) { _dbContext.Entry(lease).State = EntityState.Detached; throw new CollaborationAlreadyRunningException(); }
        var leaseId = lease.Id; _dbContext.Entry(lease).State = EntityState.Detached;
        try { return await ExecuteCoreAsync(command, ct); }
        finally
        {
            // A crashed/expired execution cannot release a newer claim. No pending task changes
            // are saved from a failed execution as part of releasing the lease.
            await _dbContext.CollaborationExecutionLeases.Where(x => x.CompanyId == command.CompanyId && x.Id == leaseId && x.Token == token)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Token, (Guid?)null).SetProperty(x => x.ExpiresUtc, (DateTime?)null), CancellationToken.None);
        }
    }
}
