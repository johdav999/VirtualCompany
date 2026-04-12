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
    public DbSet<CompanyKnowledgeDocument> CompanyKnowledgeDocuments => Set<CompanyKnowledgeDocument>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<CompanyKnowledgeChunk> CompanyKnowledgeChunks => Set<CompanyKnowledgeChunk>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<ToolExecutionAttempt> ToolExecutionAttempts => Set<ToolExecutionAttempt>();
    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowTrigger> WorkflowTriggers => Set<WorkflowTrigger>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowException> WorkflowExceptions => Set<WorkflowException>();
    public DbSet<MemoryItem> MemoryItems => Set<MemoryItem>();
    public DbSet<Company> CompanyOnboardingDrafts => Set<Company>();
    public DbSet<ContextRetrieval> ContextRetrievals => Set<ContextRetrieval>();
    public DbSet<ContextRetrievalSource> ContextRetrievalSources => Set<ContextRetrievalSource>();

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
        modelBuilder.Entity<ToolExecutionAttempt>()
            .HasQueryFilter(attempt =>
                CurrentCompanyId.HasValue && attempt.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkTask>()
            .HasQueryFilter(task =>
                CurrentCompanyId.HasValue && task.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ApprovalRequest>()
            .HasQueryFilter(request =>
                CurrentCompanyId.HasValue && request.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkflowDefinition>()
            .HasQueryFilter(definition =>
                CurrentCompanyId.HasValue && (definition.CompanyId == CurrentCompanyId.Value || definition.CompanyId == null));
        modelBuilder.Entity<WorkflowTrigger>()
            .HasQueryFilter(trigger =>
                CurrentCompanyId.HasValue && trigger.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkflowInstance>()
            .HasQueryFilter(instance =>
                CurrentCompanyId.HasValue && instance.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkflowException>()
            .HasQueryFilter(workflowException =>
                CurrentCompanyId.HasValue && workflowException.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyKnowledgeDocument>()
            .HasQueryFilter(document =>
                CurrentCompanyId.HasValue && document.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyKnowledgeChunk>()
            .HasQueryFilter(chunk =>
                CurrentCompanyId.HasValue && chunk.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<MemoryItem>()
            .HasQueryFilter(memoryItem =>
                CurrentCompanyId.HasValue && memoryItem.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ContextRetrieval>()
            .HasQueryFilter(retrieval =>
                CurrentCompanyId.HasValue && retrieval.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ContextRetrievalSource>()
            .HasQueryFilter(source =>
                CurrentCompanyId.HasValue && source.CompanyId == CurrentCompanyId.Value);
    }
}