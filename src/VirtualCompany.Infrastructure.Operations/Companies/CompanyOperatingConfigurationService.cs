using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOperatingConfigurationService : ICompanyOperatingConfigurationService
{
    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly IAuditEventWriter _audit;
    private readonly ICorrelationContextAccessor _correlation;

    public CompanyOperatingConfigurationService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver memberships,
        IAuditEventWriter audit, ICorrelationContextAccessor correlation)
    { _db = db; _memberships = memberships; _audit = audit; _correlation = correlation; }

    public async Task<CompanyOperatingConfigurationDto> GetAsync(Guid companyId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, ct); return Map(await GetOrCreateAsync(companyId, false, ct));
    }

    public async Task<CompanyOperatingConfigurationDto> UpdateAsync(Guid companyId, UpdateCompanyOperatingConfigurationCommand command, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct); var config = await GetOrCreateAsync(companyId, true, ct); EnsureVersion(config.Version, command.ExpectedVersion);
        if (command.CoordinatorAgentId.HasValue && !await _db.Agents.AnyAsync(x => x.CompanyId == companyId && x.Id == command.CoordinatorAgentId && x.Status == AgentStatus.Active, ct)) throw Validation("coordinatorAgentId", "The coordinator must be an active agent in this company.");
        CompanyAutonomyLevel autonomy; try { autonomy = CompanyAutonomyLevelValues.Parse(command.AutonomyLevel); } catch { throw Validation("autonomyLevel", "Autonomy must be recommend, organize, operate internally, or controlled execution."); }
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(command.Timezone); }
        catch (TimeZoneNotFoundException) { throw Validation("timezone", "The timezone is not available on this server."); }
        catch (InvalidTimeZoneException) { throw Validation("timezone", "The timezone definition is invalid."); }
        try { config.Update(command.CoordinatorAgentId, autonomy, command.Timezone, command.DailyCycleHour, command.MinimumCycleIntervalMinutes, command.MaximumCyclesPerDay, command.MaximumInitiativesPerCycle, command.MaximumTasksPerCycle, command.MaximumCollaborators, command.MaximumRuntimeSeconds, command.MaximumModelCallsPerCycle, command.MaximumToolCallsPerCycle, command.MaximumMonetaryBudgetPerCycle); }
        catch (ArgumentException ex) { throw Validation("configuration", ex.Message); }
        try { config.UpdateRollingLimits(command.MaximumTasksPerDay ?? config.MaximumTasksPerDay, command.MaximumModelCallsPerDay ?? config.MaximumModelCallsPerDay, command.MaximumToolCallsPerDay ?? config.MaximumToolCallsPerDay, command.MaximumMonetaryBudgetPerDay ?? config.MaximumMonetaryBudgetPerDay); }
        catch (ArgumentException ex) { throw Validation("dailyLimits", ex.Message); }
        await WriteAuditAsync(member, config, "company.operating_configuration.updated", "Company operating configuration updated.", command.CorrelationId, ct); await SaveAsync(ct); return Map(config);
    }

    public async Task<CompanyOperatingConfigurationDto> PauseAsync(Guid companyId, PauseCompanyOperationCommand command, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct); var config = await GetOrCreateAsync(companyId, true, ct); EnsureVersion(config.Version, command.ExpectedVersion);
        try { config.Pause(command.Reason); } catch (ArgumentException ex) { throw Validation("reason", ex.Message); }
        await WriteAuditAsync(member, config, "company.operation.paused", "Company operation paused.", command.CorrelationId, ct); await SaveAsync(ct); return Map(config);
    }

    public async Task<CompanyOperatingConfigurationDto> ResumeAsync(Guid companyId, ResumeCompanyOperationCommand command, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct); var config = await GetOrCreateAsync(companyId, true, ct); EnsureVersion(config.Version, command.ExpectedVersion);
        try { config.Resume(); } catch (InvalidOperationException ex) { throw Validation("emergencyStop", ex.Message); }
        await WriteAuditAsync(member, config, "company.operation.resumed", "Company operation resumed.", command.CorrelationId, ct); await SaveAsync(ct); return Map(config);
    }

    public async Task<CompanyOperatingConfigurationDto> EmergencyStopAsync(Guid companyId, EmergencyStopCompanyOperationCommand command, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct); var config = await GetOrCreateAsync(companyId, true, ct); EnsureVersion(config.Version, command.ExpectedVersion);
        try { config.EmergencyStop(command.Reason); } catch (ArgumentException ex) { throw Validation("reason", ex.Message); }
        await WriteAuditAsync(member, config, "company.operation.emergency_stopped", "Company operation was stopped immediately.", command.CorrelationId, ct); await SaveAsync(ct); return Map(config);
    }

    public async Task<CompanyOperatingConfigurationDto> ClearEmergencyStopAsync(Guid companyId, ClearEmergencyStopCommand command, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct); var config = await GetOrCreateAsync(companyId, true, ct); EnsureVersion(config.Version, command.ExpectedVersion); config.ClearEmergencyStop();
        await WriteAuditAsync(member, config, "company.operation.emergency_stop_cleared", "The emergency stop was cleared; operation remains paused until explicitly resumed.", command.CorrelationId, ct); await SaveAsync(ct); return Map(config);
    }

    private async Task<CompanyOperatingConfiguration> GetOrCreateAsync(Guid companyId, bool tracked, CancellationToken ct)
    {
        var query = tracked ? _db.CompanyOperatingConfigurations.AsQueryable() : _db.CompanyOperatingConfigurations.AsNoTracking();
        var existing = await query.SingleOrDefaultAsync(x => x.CompanyId == companyId, ct); if (existing is not null) return existing;
        var created = new CompanyOperatingConfiguration(Guid.NewGuid(), companyId);
        if (!tracked) return created;
        _db.CompanyOperatingConfigurations.Add(created); return created;
    }
    private async Task SaveAsync(CancellationToken ct) { try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw new CompanyOperatingConcurrencyException("The company operating settings changed. Refresh and try again."); } }
    private static void EnsureVersion(int actual, int expected) { if (expected > 0 && actual != expected) throw new CompanyOperatingConcurrencyException("The company operating settings changed. Refresh and try again."); }
    private async Task<ResolvedCompanyMembershipContext> RequireMemberAsync(Guid companyId, CancellationToken ct) => await _memberships.ResolveAsync(companyId, ct) ?? throw new UnauthorizedAccessException("Active company membership is required.");
    private async Task<ResolvedCompanyMembershipContext> RequireManagerAsync(Guid companyId, CancellationToken ct) { var m = await RequireMemberAsync(companyId, ct); if (m.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager)) throw new UnauthorizedAccessException("Company manager access is required."); return m; }
    private async Task WriteAuditAsync(ResolvedCompanyMembershipContext member, CompanyOperatingConfiguration config, string action, string summary, string? correlationId, CancellationToken ct) => await _audit.WriteAsync(new AuditEventWriteRequest(config.CompanyId, AuditActorTypes.User, member.UserId, action, "company_operating_configuration", config.Id.ToString("N"), AuditEventOutcomes.Succeeded, summary, Metadata: new Dictionary<string, string?> { ["autonomyLevel"] = config.AutonomyLevel.ToStorageValue(), ["paused"] = config.IsPaused.ToString(), ["version"] = config.Version.ToString() }, CorrelationId: string.IsNullOrWhiteSpace(correlationId) ? _correlation.CorrelationId : correlationId), ct);
    private static CompanyOperatingValidationException Validation(string key, string message) => new(new Dictionary<string, string[]> { [key] = [message] });
    private static CompanyOperatingConfigurationDto Map(CompanyOperatingConfiguration x) => new(x.Id, x.CompanyId, x.CoordinatorAgentId, x.AutonomyLevel.ToStorageValue(), x.Timezone, x.DailyCycleHour, x.MinimumCycleIntervalMinutes, x.MaximumCyclesPerDay, x.MaximumInitiativesPerCycle, x.MaximumTasksPerCycle, x.MaximumCollaborators, x.MaximumRuntimeSeconds, x.MaximumModelCallsPerCycle, x.MaximumToolCallsPerCycle, x.MaximumMonetaryBudgetPerCycle, x.MaximumTasksPerDay, x.MaximumModelCallsPerDay, x.MaximumToolCallsPerDay, x.MaximumMonetaryBudgetPerDay, x.IsPaused, x.PauseReason, x.EmergencyStopped, x.EmergencyStopReason, x.EmergencyStoppedUtc, x.Version, x.CreatedUtc, x.UpdatedUtc);
}
