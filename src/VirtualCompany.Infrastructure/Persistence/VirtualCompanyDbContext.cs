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
    public DbSet<BackgroundExecution> BackgroundExecutions => Set<BackgroundExecution>();
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
    public DbSet<CompanyBriefingDeliveryPreference> CompanyBriefingDeliveryPreferences => Set<CompanyBriefingDeliveryPreference>();
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

    internal Guid? CurrentCompanyId => _companyContextAccessor?.CompanyId;

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
        modelBuilder.Entity<CompanyBriefingDeliveryPreference>()
            .HasQueryFilter(preference =>
                CurrentCompanyId.HasValue && preference.CompanyId == CurrentCompanyId.Value);
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
    }
}
