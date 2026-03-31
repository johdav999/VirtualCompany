using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

public sealed class VirtualCompanyDbContext : DbContext
{
    private readonly ICompanyContextAccessor? _companyContextAccessor;

    public VirtualCompanyDbContext(
        DbContextOptions<VirtualCompanyDbContext> options,
        ICompanyContextAccessor? companyContextAccessor = null)
        : base(options)
    {
        _companyContextAccessor = companyContextAccessor;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();
    public DbSet<CompanyInvitation> CompanyInvitations => Set<CompanyInvitation>();
    public DbSet<CompanyOutboxMessage> CompanyOutboxMessages => Set<CompanyOutboxMessage>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CompanyOwnedNote> CompanyNotes => Set<CompanyOwnedNote>();
    public DbSet<CompanySetupTemplate> CompanySetupTemplates => Set<CompanySetupTemplate>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Company> CompanyOnboardingDrafts => Set<Company>();

    internal Guid? CurrentCompanyId => _companyContextAccessor?.CompanyId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VirtualCompanyDbContext).Assembly);
        modelBuilder.Entity<CompanyOwnedNote>()
            .HasQueryFilter(note =>
                CurrentCompanyId.HasValue && note.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Agent>()
            .HasQueryFilter(agent =>
                CurrentCompanyId.HasValue && agent.CompanyId == CurrentCompanyId.Value);
    }
}