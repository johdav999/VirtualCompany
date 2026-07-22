using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260715172240_AddSharedAgentAiCapabilities")]
public partial class AddSharedAgentAiCapabilities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE [agent_ai_quality_events] (
                [Id] uniqueidentifier NOT NULL,
                [CompanyId] uniqueidentifier NOT NULL,
                [AgentId] uniqueidentifier NOT NULL,
                [CapabilityId] nvarchar(100) NOT NULL,
                [OrchestrationRunId] uniqueidentifier NULL,
                [EventType] nvarchar(64) NOT NULL,
                [EventIdentity] nvarchar(200) NOT NULL,
                [ReasonCode] nvarchar(100) NULL,
                [Comment] nvarchar(1000) NULL,
                [Confidence] decimal(5,4) NULL,
                [CorrelationId] nvarchar(128) NOT NULL,
                [OccurredUtc] datetime2 NOT NULL,
                CONSTRAINT [PK_agent_ai_quality_events] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_agent_ai_quality_events_agents_AgentId] FOREIGN KEY ([AgentId]) REFERENCES [agents] ([Id]) ON DELETE NO ACTION,
                CONSTRAINT [FK_agent_ai_quality_events_companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );

            CREATE TABLE [agent_handoffs] (
                [Id] uniqueidentifier NOT NULL,
                [CompanyId] uniqueidentifier NOT NULL,
                [Type] nvarchar(100) NOT NULL,
                [Version] nvarchar(32) NOT NULL,
                [RequestingAgentId] uniqueidentifier NOT NULL,
                [ReceivingAgentId] uniqueidentifier NOT NULL,
                [Objective] nvarchar(1000) NOT NULL,
                [RequestedOutcome] nvarchar(1000) NOT NULL,
                [Status] nvarchar(32) NOT NULL,
                [DueUtc] datetime2 NULL,
                [EvidenceJson] nvarchar(max) NOT NULL,
                [RelatedTaskId] uniqueidentifier NULL,
                [ApprovalRequestId] uniqueidentifier NULL,
                [CompletionSummary] nvarchar(2000) NULL,
                [Confidence] decimal(5,4) NULL,
                [FailureReason] nvarchar(1000) NULL,
                [CorrelationId] nvarchar(128) NOT NULL,
                [CreatedUtc] datetime2 NOT NULL,
                [UpdatedUtc] datetime2 NOT NULL,
                [CompletedUtc] datetime2 NULL,
                [ConcurrencyVersion] bigint NOT NULL,
                CONSTRAINT [PK_agent_handoffs] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_agent_handoffs_agents_ReceivingAgentId] FOREIGN KEY ([ReceivingAgentId]) REFERENCES [agents] ([Id]) ON DELETE NO ACTION,
                CONSTRAINT [FK_agent_handoffs_agents_RequestingAgentId] FOREIGN KEY ([RequestingAgentId]) REFERENCES [agents] ([Id]) ON DELETE NO ACTION,
                CONSTRAINT [FK_agent_handoffs_companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );

            CREATE TABLE [agent_memory_candidates] (
                [Id] uniqueidentifier NOT NULL,
                [CompanyId] uniqueidentifier NOT NULL,
                [ProposingAgentId] uniqueidentifier NOT NULL,
                [MemoryType] nvarchar(64) NOT NULL,
                [Scope] nvarchar(64) NOT NULL,
                [Content] nvarchar(4000) NOT NULL,
                [EvidenceJson] nvarchar(max) NOT NULL,
                [Confidence] decimal(5,4) NOT NULL,
                [Sensitivity] nvarchar(32) NOT NULL,
                [Fingerprint] nvarchar(128) NOT NULL,
                [Status] nvarchar(32) NOT NULL,
                [OrchestrationRunId] uniqueidentifier NULL,
                [ActivatedMemoryItemId] uniqueidentifier NULL,
                [ReviewerUserId] uniqueidentifier NULL,
                [ReviewReason] nvarchar(500) NULL,
                [ExpiresUtc] datetime2 NOT NULL,
                [CreatedUtc] datetime2 NOT NULL,
                [UpdatedUtc] datetime2 NOT NULL,
                [ReviewedUtc] datetime2 NULL,
                [ConcurrencyVersion] bigint NOT NULL,
                CONSTRAINT [PK_agent_memory_candidates] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_agent_memory_candidates_agents_ProposingAgentId] FOREIGN KEY ([ProposingAgentId]) REFERENCES [agents] ([Id]) ON DELETE NO ACTION,
                CONSTRAINT [FK_agent_memory_candidates_companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );

            CREATE TABLE [agent_orchestration_runs] (
                [Id] uniqueidentifier NOT NULL,
                [CompanyId] uniqueidentifier NOT NULL,
                [AgentId] uniqueidentifier NOT NULL,
                [ActorUserId] uniqueidentifier NULL,
                [TaskId] uniqueidentifier NULL,
                [ConversationId] uniqueidentifier NULL,
                [CapabilityId] nvarchar(100) NOT NULL,
                [CapabilityVersion] nvarchar(32) NOT NULL,
                [PromptVersion] nvarchar(32) NOT NULL,
                [SchemaVersion] nvarchar(32) NOT NULL,
                [Status] nvarchar(32) NOT NULL,
                [Provider] nvarchar(100) NULL,
                [Model] nvarchar(200) NULL,
                [Confidence] decimal(5,4) NULL,
                [Summary] nvarchar(2000) NULL,
                [ResultJson] nvarchar(max) NULL,
                [SourceIdsJson] nvarchar(max) NULL,
                [FailureCode] nvarchar(100) NULL,
                [FailureMessage] nvarchar(1000) NULL,
                [CorrelationId] nvarchar(128) NOT NULL,
                [InputTokens] int NULL,
                [OutputTokens] int NULL,
                [LatencyMilliseconds] bigint NULL,
                [CreatedUtc] datetime2 NOT NULL,
                [StartedUtc] datetime2 NOT NULL,
                [UpdatedUtc] datetime2 NOT NULL,
                [CompletedUtc] datetime2 NULL,
                [Version] bigint NOT NULL,
                CONSTRAINT [PK_agent_orchestration_runs] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_agent_orchestration_runs_agents_AgentId] FOREIGN KEY ([AgentId]) REFERENCES [agents] ([Id]) ON DELETE NO ACTION,
                CONSTRAINT [FK_agent_orchestration_runs_companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );

            CREATE INDEX [IX_agent_ai_quality_events_AgentId] ON [agent_ai_quality_events] ([AgentId]);
            CREATE INDEX [IX_agent_ai_quality_events_CompanyId_AgentId_CapabilityId_OccurredUtc] ON [agent_ai_quality_events] ([CompanyId], [AgentId], [CapabilityId], [OccurredUtc]);
            CREATE UNIQUE INDEX [IX_agent_ai_quality_events_CompanyId_EventIdentity] ON [agent_ai_quality_events] ([CompanyId], [EventIdentity]);
            CREATE UNIQUE INDEX [IX_agent_handoffs_CompanyId_CorrelationId] ON [agent_handoffs] ([CompanyId], [CorrelationId]);
            CREATE INDEX [IX_agent_handoffs_CompanyId_ReceivingAgentId_Status_DueUtc] ON [agent_handoffs] ([CompanyId], [ReceivingAgentId], [Status], [DueUtc]);
            CREATE INDEX [IX_agent_handoffs_CompanyId_RequestingAgentId_CreatedUtc] ON [agent_handoffs] ([CompanyId], [RequestingAgentId], [CreatedUtc]);
            CREATE INDEX [IX_agent_handoffs_ReceivingAgentId] ON [agent_handoffs] ([ReceivingAgentId]);
            CREATE INDEX [IX_agent_handoffs_RequestingAgentId] ON [agent_handoffs] ([RequestingAgentId]);
            CREATE UNIQUE INDEX [IX_agent_memory_candidates_CompanyId_Fingerprint] ON [agent_memory_candidates] ([CompanyId], [Fingerprint]);
            CREATE INDEX [IX_agent_memory_candidates_CompanyId_Status_ExpiresUtc] ON [agent_memory_candidates] ([CompanyId], [Status], [ExpiresUtc]);
            CREATE INDEX [IX_agent_memory_candidates_ProposingAgentId] ON [agent_memory_candidates] ([ProposingAgentId]);
            CREATE INDEX [IX_agent_orchestration_runs_AgentId] ON [agent_orchestration_runs] ([AgentId]);
            CREATE INDEX [IX_agent_orchestration_runs_CompanyId_AgentId_CreatedUtc] ON [agent_orchestration_runs] ([CompanyId], [AgentId], [CreatedUtc]);
            CREATE INDEX [IX_agent_orchestration_runs_CompanyId_CapabilityId_Status] ON [agent_orchestration_runs] ([CompanyId], [CapabilityId], [Status]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "agent_ai_quality_events");
        migrationBuilder.DropTable(name: "agent_handoffs");
        migrationBuilder.DropTable(name: "agent_memory_candidates");
        migrationBuilder.DropTable(name: "agent_orchestration_runs");
    }
}
