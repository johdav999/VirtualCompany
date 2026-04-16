using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

public sealed class VirtualCompanyDbContext : DbContext
{
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IExecutiveCockpitDashboardCacheInvalidator? _dashboardCacheInvalidator;

    public VirtualCompanyDbContext(
        DbContextOptions<VirtualCompanyDbContext> options,
        ICompanyContextAccessor? companyContextAccessor = null,
        IExecutiveCockpitDashboardCacheInvalidator? dashboardCacheInvalidator = null)
        : base(options)
    {
        _companyContextAccessor = companyContextAccessor;
        _dashboardCacheInvalidator = dashboardCacheInvalidator;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();
    public DbSet<CompanyInvitation> CompanyInvitations => Set<CompanyInvitation>();
    public DbSet<CompanyOutboxMessage> CompanyOutboxMessages => Set<CompanyOutboxMessage>();
    public DbSet<BackgroundExecution> BackgroundExecutions => Set<BackgroundExecution>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<ExecutionExceptionRecord> ExecutionExceptionRecords => Set<ExecutionExceptionRecord>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CompanyOwnedNote> CompanyNotes => Set<CompanyOwnedNote>();
    public DbSet<CompanySetupTemplate> CompanySetupTemplates => Set<CompanySetupTemplate>();
    public DbSet<CompanyKnowledgeDocument> CompanyKnowledgeDocuments => Set<CompanyKnowledgeDocument>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<CompanyKnowledgeChunk> CompanyKnowledgeChunks => Set<CompanyKnowledgeChunk>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<ToolExecutionAttempt> ToolExecutionAttempts => Set<ToolExecutionAttempt>();
    public DbSet<AgentScheduledTrigger> AgentScheduledTriggers => Set<AgentScheduledTrigger>();
    public DbSet<TriggerExecutionAttempt> TriggerExecutionAttempts => Set<TriggerExecutionAttempt>();
    public DbSet<AgentScheduledTriggerEnqueueWindow> AgentScheduledTriggerEnqueueWindows => Set<AgentScheduledTriggerEnqueueWindow>();
    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<AgentTaskCreationDedupeRecord> AgentTaskCreationDedupeRecords => Set<AgentTaskCreationDedupeRecord>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ConversationTaskLink> ConversationTaskLinks => Set<ConversationTaskLink>();
    public DbSet<CompanyBriefing> CompanyBriefings => Set<CompanyBriefing>();
    public DbSet<CompanyBriefingSection> CompanyBriefingSections => Set<CompanyBriefingSection>();
    public DbSet<CompanyBriefingContribution> CompanyBriefingContributions => Set<CompanyBriefingContribution>();
    public DbSet<CompanyBriefingDeliveryPreference> CompanyBriefingDeliveryPreferences => Set<CompanyBriefingDeliveryPreference>();
    public DbSet<CompanyBriefingSeverityRule> CompanyBriefingSeverityRules => Set<CompanyBriefingSeverityRule>();
    public DbSet<UserBriefingPreference> UserBriefingPreferences => Set<UserBriefingPreference>();
    public DbSet<TenantBriefingDefault> TenantBriefingDefaults => Set<TenantBriefingDefault>();
    public DbSet<CompanyBriefingUpdateJob> CompanyBriefingUpdateJobs => Set<CompanyBriefingUpdateJob>();
    public DbSet<CompanyNotification> CompanyNotifications => Set<CompanyNotification>();
    public DbSet<ProactiveMessage> ProactiveMessages => Set<ProactiveMessage>();
    public DbSet<ProactiveMessagePolicyDecision> ProactiveMessagePolicyDecisions => Set<ProactiveMessagePolicyDecision>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowTrigger> WorkflowTriggers => Set<WorkflowTrigger>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<ProcessedWorkflowTriggerEvent> ProcessedWorkflowTriggerEvents => Set<ProcessedWorkflowTriggerEvent>();
    public DbSet<WorkflowException> WorkflowExceptions => Set<WorkflowException>();
    public DbSet<ConditionTriggerEvaluation> ConditionTriggerEvaluations => Set<ConditionTriggerEvaluation>();
    public DbSet<MemoryItem> MemoryItems => Set<MemoryItem>();
    public DbSet<Company> CompanyOnboardingDrafts => Set<Company>();
    public DbSet<ContextRetrieval> ContextRetrievals => Set<ContextRetrieval>();
    public DbSet<ContextRetrievalSource> ContextRetrievalSources => Set<ContextRetrievalSource>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Escalation> Escalations => Set<Escalation>();
    public DbSet<InsightAcknowledgment> InsightAcknowledgments => Set<InsightAcknowledgment>();
    public DbSet<DashboardDepartmentConfig> DashboardDepartmentConfigs => Set<DashboardDepartmentConfig>();
    public DbSet<DashboardWidgetConfig> DashboardWidgetConfigs => Set<DashboardWidgetConfig>();

    internal Guid? CurrentCompanyId => _companyContextAccessor?.CompanyId;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var companiesToInvalidate = CaptureDashboardInvalidationCompanies();
        var result = await base.SaveChangesAsync(cancellationToken);

        if (_dashboardCacheInvalidator is not null)
        {
            foreach (var companyId in companiesToInvalidate)
            {
                await _dashboardCacheInvalidator.InvalidateAsync(companyId, cancellationToken);
            }
        }

        return result;
    }

    private IReadOnlyList<Guid> CaptureDashboardInvalidationCompanies() =>
        ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry =>
                entry.Entity is WorkTask ||
                entry.Entity is ApprovalRequest ||
                entry.Entity is Agent ||
                entry.Entity is ActivityEvent ||
                entry.Entity is ToolExecutionAttempt ||
                entry.Entity is TriggerExecutionAttempt ||
                entry.Entity is WorkflowDefinition ||
                entry.Entity is WorkflowInstance ||
                entry.Entity is WorkflowException ||
                entry.Entity is CompanyBriefing ||
                entry.Entity is CompanyBriefingSection ||
                entry.Entity is CompanyBriefingContribution ||
                entry.Entity is CompanyBriefingSeverityRule ||
                entry.Entity is CompanyBriefingUpdateJob ||
                entry.Entity is UserBriefingPreference ||
                entry.Entity is TenantBriefingDefault ||
                entry.Entity is DashboardDepartmentConfig ||
                entry.Entity is DashboardWidgetConfig ||
                entry.Entity is Alert)
            .Select(entry =>
            {
                var property = entry.Properties.FirstOrDefault(x => x.Metadata.Name == nameof(ICompanyOwnedEntity.CompanyId));
                return property?.CurrentValue is Guid companyId ? companyId : Guid.Empty;
            })
            .Where(companyId => companyId != Guid.Empty)
            .Distinct()
            .ToArray();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VirtualCompanyDbContext).Assembly);
        modelBuilder.Entity<CompanyOwnedNote>()
            .HasQueryFilter(note =>
                CurrentCompanyId.HasValue && note.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<BackgroundExecution>()
            .HasQueryFilter(execution =>
                CurrentCompanyId.HasValue && execution.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ExecutionExceptionRecord>()
            .HasQueryFilter(executionException =>
                CurrentCompanyId.HasValue && executionException.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(auditEvent =>
                CurrentCompanyId.HasValue && auditEvent.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ActivityEvent>()
            .HasQueryFilter(activityEvent =>
                CurrentCompanyId.HasValue && activityEvent.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Agent>()
            .HasQueryFilter(agent =>
                CurrentCompanyId.HasValue && agent.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ToolExecutionAttempt>()
            .HasQueryFilter(attempt =>
                CurrentCompanyId.HasValue && attempt.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<AgentScheduledTrigger>()
            .HasQueryFilter(trigger =>
                CurrentCompanyId.HasValue && trigger.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<TriggerExecutionAttempt>()
            .HasQueryFilter(attempt =>
                CurrentCompanyId.HasValue && attempt.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<AgentScheduledTriggerEnqueueWindow>()
            .HasQueryFilter(window =>
                CurrentCompanyId.HasValue && window.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkTask>()
            .HasQueryFilter(task =>
                CurrentCompanyId.HasValue && task.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<AgentTaskCreationDedupeRecord>()
            .HasQueryFilter(record =>
                CurrentCompanyId.HasValue && record.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ApprovalRequest>()
            .HasQueryFilter(request =>
                CurrentCompanyId.HasValue && request.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Conversation>()
            .HasQueryFilter(conversation =>
                CurrentCompanyId.HasValue && conversation.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Message>()
            .HasQueryFilter(message =>
                CurrentCompanyId.HasValue && message.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ConversationTaskLink>()
            .HasQueryFilter(link =>
                CurrentCompanyId.HasValue && link.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefing>()
            .HasQueryFilter(briefing =>
                CurrentCompanyId.HasValue && briefing.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefingSection>()
            .HasQueryFilter(section =>
                CurrentCompanyId.HasValue && section.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefingContribution>()
            .HasQueryFilter(contribution =>
                CurrentCompanyId.HasValue && contribution.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefingUpdateJob>()
            .HasQueryFilter(job =>
                CurrentCompanyId.HasValue && job.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefingDeliveryPreference>()
            .HasQueryFilter(preference =>
                CurrentCompanyId.HasValue && preference.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyBriefingSeverityRule>()
            .HasQueryFilter(rule =>
                CurrentCompanyId.HasValue && rule.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<UserBriefingPreference>()
            .HasQueryFilter(preference =>
                CurrentCompanyId.HasValue && preference.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<TenantBriefingDefault>()
            .HasQueryFilter(defaults =>
                CurrentCompanyId.HasValue && defaults.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<CompanyNotification>()
            .HasQueryFilter(notification =>
                CurrentCompanyId.HasValue && notification.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ProactiveMessage>()
            .HasQueryFilter(message =>
                CurrentCompanyId.HasValue && message.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ProactiveMessagePolicyDecision>()
            .HasQueryFilter(decision =>
                CurrentCompanyId.HasValue && decision.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkflowDefinition>()
            .HasQueryFilter(definition =>
                CurrentCompanyId.HasValue && (definition.CompanyId == CurrentCompanyId.Value || definition.CompanyId == null));
        modelBuilder.Entity<WorkflowTrigger>()
            .HasQueryFilter(trigger =>
                CurrentCompanyId.HasValue && trigger.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkflowInstance>()
            .HasQueryFilter(instance =>
                CurrentCompanyId.HasValue && instance.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ProcessedWorkflowTriggerEvent>()
            .HasQueryFilter(processedEvent =>
                CurrentCompanyId.HasValue && processedEvent.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<WorkflowException>()
            .HasQueryFilter(workflowException =>
                CurrentCompanyId.HasValue && workflowException.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<ConditionTriggerEvaluation>()
            .HasQueryFilter(evaluation =>
                CurrentCompanyId.HasValue && evaluation.CompanyId == CurrentCompanyId.Value);
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
        modelBuilder.Entity<Alert>()
            .HasQueryFilter(alert =>
                CurrentCompanyId.HasValue && alert.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<Escalation>()
            .HasQueryFilter(escalation =>
                CurrentCompanyId.HasValue && escalation.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<DashboardDepartmentConfig>()
            .HasQueryFilter(config =>
                CurrentCompanyId.HasValue && config.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<DashboardWidgetConfig>()
            .HasQueryFilter(config =>
                CurrentCompanyId.HasValue && config.CompanyId == CurrentCompanyId.Value);
        modelBuilder.Entity<InsightAcknowledgment>()
            .HasQueryFilter(acknowledgment =>
                CurrentCompanyId.HasValue && acknowledgment.CompanyId == CurrentCompanyId.Value);
    }
}
