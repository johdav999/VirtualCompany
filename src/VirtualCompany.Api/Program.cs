using System.Text.Json;
using System.Text.Json.Serialization;
using System.Data.Common;
using System.ComponentModel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using VirtualCompany.Infrastructure;
using VirtualCompany.Application.Activity;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Authorization;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Activity;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);
const string DevelopmentCorsPolicy = "DevelopmentWebClient";

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

if (builder.Environment.IsDevelopment())
{
    builder.Services
        .AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".data-protection")))
        .SetApplicationName("VirtualCompany.Api");
}

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy(DevelopmentCorsPolicy, policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IActivityEventPublisher, SignalRActivityEventPublisher>();
builder.Services.AddVirtualCompanyInfrastructure(builder.Configuration);
builder.Services.AddCompanyAuthorization(builder.Environment);
builder.Services.AddVirtualCompanyRateLimiting(builder.Configuration);

var app = builder.Build();
var applyMigrationsOnStartup =
    app.Environment.IsDevelopment() ||
    app.Configuration.GetValue<bool>("DatabaseInitialization:ApplyMigrationsOnStartup");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(DevelopmentCorsPolicy);
app.UseAuthentication();
app.UseMiddleware<CompanyContextResolutionMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitialization");
    var templateSeeder = scope.ServiceProvider.GetRequiredService<CompanySetupTemplateSeeder>();
    var workflowDefinitionSeeder = scope.ServiceProvider.GetRequiredService<CompanyWorkflowDefinitionSeeder>();
    await InitializeDatabaseAsync(
        app,
        dbContext,
        logger,
        templateSeeder,
        workflowDefinitionSeeder,
        applyMigrationsOnStartup);
}

app.MapVirtualCompanyHealthEndpoints();
app.MapControllers();
app.MapHub<ActivityFeedHub>(ActivityFeedHub.Route).RequireAuthorization(CompanyPolicies.AuthenticatedUser);

app.Run();

static async Task EnsureSqlServerAgentExecutionSchemaAsync(VirtualCompanyDbContext dbContext)
{
    var providerName = dbContext.Database.ProviderName;
    if (!string.Equals(providerName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
    {
        return;
    }

    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
    if (shouldCloseConnection)
    {
        await connection.OpenAsync();
    }

    try
    {
        if (!await SqlServerColumnExistsAsync(connection, "agents", "autonomy_level"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [agents]
                ADD [autonomy_level] nvarchar(32) NOT NULL
                    CONSTRAINT [DF_agents_autonomy_level_startup] DEFAULT (N'level_0');
                """);
        }

        if (!await SqlServerColumnExistsAsync(connection, "agents", "role_brief"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [agents]
                ADD [role_brief] nvarchar(4000) NULL;
                """);
        }

        if (!await SqlServerColumnExistsAsync(connection, "agents", "trigger_logic_json"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [agents]
                ADD [trigger_logic_json] nvarchar(max) NOT NULL
                    CONSTRAINT [DF_agents_trigger_logic_json_startup] DEFAULT (NCHAR(123) + NCHAR(125));
                """);
        }

        if (!await SqlServerColumnExistsAsync(connection, "agents", "working_hours_json"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [agents]
                ADD [working_hours_json] nvarchar(max) NOT NULL
                    CONSTRAINT [DF_agents_working_hours_json_startup] DEFAULT (NCHAR(123) + NCHAR(125));
                """);
        }

        var hasToolExecutions = await SqlServerTableExistsAsync(connection, "tool_executions");
        var hasLegacyToolExecutionAttempts = await SqlServerTableExistsAsync(connection, "tool_execution_attempts");
        if (!hasToolExecutions && !hasLegacyToolExecutionAttempts)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE [tool_executions] (
                    [id] uniqueidentifier NOT NULL CONSTRAINT [PK_tool_executions] PRIMARY KEY,
                    [company_id] uniqueidentifier NOT NULL,
                    [agent_id] uniqueidentifier NOT NULL,
                    [tool_name] nvarchar(100) NOT NULL,
                    [task_id] uniqueidentifier NULL,
                    [workflow_instance_id] uniqueidentifier NULL,
                    [correlation_id] nvarchar(128) NULL,
                    [action_type] nvarchar(32) NOT NULL,
                    [scope] nvarchar(100) NULL,
                    [status] nvarchar(32) NOT NULL,
                    [approval_request_id] uniqueidentifier NULL,
                    [request_json] nvarchar(max) NOT NULL CONSTRAINT [DF_tool_executions_request_json] DEFAULT (NCHAR(123) + NCHAR(125)),
                    [policy_decision_json] nvarchar(max) NOT NULL CONSTRAINT [DF_tool_executions_policy_decision_json] DEFAULT (NCHAR(123) + NCHAR(125)),
                    [response_json] nvarchar(max) NOT NULL CONSTRAINT [DF_tool_executions_response_json] DEFAULT (NCHAR(123) + NCHAR(125)),
                    [started_at] datetime2 NOT NULL,
                    [completed_at] datetime2 NULL,
                    [created_at] datetime2 NOT NULL,
                    [updated_at] datetime2 NOT NULL,
                    [executed_at] datetime2 NULL,
                    CONSTRAINT [FK_tool_executions_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
                );
                """);

            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_tool_executions_company_id_agent_id_started_at] ON [tool_executions] ([company_id], [agent_id], [started_at]);""");
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_tool_executions_company_id_status_started_at] ON [tool_executions] ([company_id], [status], [started_at]);""");
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_tool_executions_company_id_correlation_id] ON [tool_executions] ([company_id], [correlation_id]);""");
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_tool_executions_company_id_task_id] ON [tool_executions] ([company_id], [task_id]);""");
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_tool_executions_company_id_workflow_instance_id] ON [tool_executions] ([company_id], [workflow_instance_id]);""");
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_tool_executions_company_id_started_at] ON [tool_executions] ([company_id], [started_at]);""");
        }

        if (!await SqlServerTableExistsAsync(connection, "approval_requests"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE [approval_requests] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_approval_requests] PRIMARY KEY,
                    [CompanyId] uniqueidentifier NOT NULL,
                    [AgentId] uniqueidentifier NOT NULL,
                    [ToolExecutionAttemptId] uniqueidentifier NOT NULL,
                    [RequestedByUserId] uniqueidentifier NOT NULL,
                    [ToolName] nvarchar(100) NOT NULL,
                    [ActionType] nvarchar(32) NOT NULL,
                    [ApprovalTarget] nvarchar(100) NULL,
                    [Status] nvarchar(32) NOT NULL,
                    [threshold_context_json] nvarchar(max) NOT NULL CONSTRAINT [DF_approval_requests_threshold_context_json] DEFAULT (NCHAR(123) + NCHAR(125)),
                    [policy_decision_json] nvarchar(max) NOT NULL CONSTRAINT [DF_approval_requests_policy_decision_json] DEFAULT (NCHAR(123) + NCHAR(125)),
                    [CreatedUtc] datetime2 NOT NULL,
                    [UpdatedUtc] datetime2 NOT NULL,
                    CONSTRAINT [FK_approval_requests_companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
                );
                """);

            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_approval_requests_CompanyId_Status_CreatedUtc] ON [approval_requests] ([CompanyId], [Status], [CreatedUtc]);""");
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX [IX_approval_requests_ToolExecutionAttemptId] ON [approval_requests] ([ToolExecutionAttemptId]);""");
        }

        if (!await SqlServerColumnExistsAsync(connection, "approval_requests", "entity_type"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [approval_requests]
                ADD [entity_type] nvarchar(32) NOT NULL
                    CONSTRAINT [DF_approval_requests_entity_type_startup] DEFAULT (N'action');
                """);
        }

        if (!await SqlServerColumnExistsAsync(connection, "approval_requests", "entity_id"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [approval_requests]
                ADD [entity_id] uniqueidentifier NOT NULL
                    CONSTRAINT [DF_approval_requests_entity_id_startup] DEFAULT ('00000000-0000-0000-0000-000000000000');
                """);
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [approval_requests]
            SET [entity_id] = [ToolExecutionAttemptId]
            WHERE [entity_id] = '00000000-0000-0000-0000-000000000000'
              AND [ToolExecutionAttemptId] IS NOT NULL;
            """);

        if (!await SqlServerColumnExistsAsync(connection, "approval_requests", "requested_by_actor_type"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [approval_requests]
                ADD [requested_by_actor_type] nvarchar(64) NOT NULL
                    CONSTRAINT [DF_approval_requests_requested_by_actor_type_startup] DEFAULT (N'user');
                """);
        }

        if (!await SqlServerColumnExistsAsync(connection, "approval_requests", "requested_by_actor_id"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [approval_requests]
                ADD [requested_by_actor_id] uniqueidentifier NOT NULL
                    CONSTRAINT [DF_approval_requests_requested_by_actor_id_startup] DEFAULT ('00000000-0000-0000-0000-000000000000');
                """);
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [approval_requests]
            SET [requested_by_actor_id] = [RequestedByUserId]
            WHERE [requested_by_actor_id] = '00000000-0000-0000-0000-000000000000';
            """);

        if (!await SqlServerColumnExistsAsync(connection, "approval_requests", "approval_type"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [approval_requests]
                ADD [approval_type] nvarchar(100) NOT NULL
                    CONSTRAINT [DF_approval_requests_approval_type_startup] DEFAULT (N'threshold');
                """);
        }

        if (!await SqlServerColumnExistsAsync(connection, "approval_requests", "required_role"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE [approval_requests] ADD [required_role] nvarchar(100) NULL;""");
        }

        if (!await SqlServerColumnExistsAsync(connection, "approval_requests", "required_user_id"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE [approval_requests] ADD [required_user_id] uniqueidentifier NULL;""");
        }

        if (!await SqlServerColumnExistsAsync(connection, "approval_requests", "decision_summary"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE [approval_requests] ADD [decision_summary] nvarchar(2000) NULL;""");
        }

        if (!await SqlServerColumnExistsAsync(connection, "approval_requests", "decided_at"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE [approval_requests] ADD [decided_at] datetime2 NULL;""");
        }

        if (!await SqlServerColumnExistsAsync(connection, "approval_requests", "decision_chain_json"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [approval_requests]
                ADD [decision_chain_json] nvarchar(max) NOT NULL
                    CONSTRAINT [DF_approval_requests_decision_chain_json_startup] DEFAULT (NCHAR(123) + NCHAR(125));
                """);
        }

        if (!await SqlServerTableExistsAsync(connection, "approval_steps"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE [approval_steps] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_approval_steps] PRIMARY KEY,
                    [ApprovalId] uniqueidentifier NOT NULL,
                    [sequence_no] int NOT NULL,
                    [approver_type] nvarchar(32) NOT NULL,
                    [approver_ref] nvarchar(200) NOT NULL,
                    [Status] nvarchar(32) NOT NULL,
                    [decided_by_user_id] uniqueidentifier NULL,
                    [decided_at] datetime2 NULL,
                    [comment] nvarchar(2000) NULL,
                    CONSTRAINT [FK_approval_steps_approval_requests_ApprovalId]
                        FOREIGN KEY ([ApprovalId]) REFERENCES [approval_requests] ([Id]) ON DELETE CASCADE
                );
                """);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO [approval_steps] ([Id], [ApprovalId], [sequence_no], [approver_type], [approver_ref], [Status])
                SELECT NEWID(), [Id], 1, N'role', COALESCE(NULLIF([ApprovalTarget], N''), N'finance_approver'), [Status]
                FROM [approval_requests]
                WHERE [Status] IN (N'pending', N'approved', N'rejected');
                """);
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_approval_requests_CompanyId_TargetEntity' AND object_id = OBJECT_ID(N'[approval_requests]'))
            BEGIN
                CREATE INDEX [IX_approval_requests_CompanyId_TargetEntity]
                ON [approval_requests] ([CompanyId], [entity_type], [entity_id]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_approval_requests_ToolExecutionAttemptId' AND object_id = OBJECT_ID(N'[approval_requests]'))
            BEGIN
                CREATE UNIQUE INDEX [IX_approval_requests_ToolExecutionAttemptId]
                ON [approval_requests] ([ToolExecutionAttemptId])
                WHERE [ToolExecutionAttemptId] IS NOT NULL;
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_approval_steps_ApprovalId_sequence_no' AND object_id = OBJECT_ID(N'[approval_steps]'))
            BEGIN
                CREATE UNIQUE INDEX [IX_approval_steps_ApprovalId_sequence_no]
                ON [approval_steps] ([ApprovalId], [sequence_no]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_approval_steps_Status' AND object_id = OBJECT_ID(N'[approval_steps]'))
            BEGIN
                CREATE INDEX [IX_approval_steps_Status]
                ON [approval_steps] ([Status]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_approval_steps_ApprovalId_Status_sequence_no' AND object_id = OBJECT_ID(N'[approval_steps]'))
            BEGIN
                CREATE INDEX [IX_approval_steps_ApprovalId_Status_sequence_no]
                ON [approval_steps] ([ApprovalId], [Status], [sequence_no]);
            END
            """);
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task EnsureSqlServerKnowledgeSchemaAsync(VirtualCompanyDbContext dbContext)
{
    var providerName = dbContext.Database.ProviderName;
    if (!string.Equals(providerName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
    {
        return;
    }

    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
    if (shouldCloseConnection)
    {
        await connection.OpenAsync();
    }

    try
    {
        if (await SqlServerTableExistsAsync(connection, "knowledge_documents"))
        {
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [knowledge_documents] (
                [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_knowledge_documents] PRIMARY KEY,
                [CompanyId] uniqueidentifier NOT NULL,
                [Title] nvarchar(200) NOT NULL,
                [DocumentType] nvarchar(32) NOT NULL,
                [SourceType] nvarchar(32) NOT NULL,
                [SourceRef] nvarchar(512) NULL,
                [StorageKey] nvarchar(1024) NOT NULL,
                [StorageUrl] nvarchar(2048) NULL,
                [OriginalFileName] nvarchar(255) NOT NULL,
                [ContentType] nvarchar(255) NULL,
                [FileExtension] nvarchar(16) NOT NULL,
                [FileSizeBytes] bigint NOT NULL,
                [metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_knowledge_documents_metadata_json] DEFAULT (NCHAR(123) + NCHAR(125)),
                [access_scope_json] nvarchar(max) NOT NULL CONSTRAINT [DF_knowledge_documents_access_scope_json] DEFAULT (NCHAR(123) + NCHAR(125)),
                [IngestionStatus] nvarchar(32) NOT NULL CONSTRAINT [DF_knowledge_documents_IngestionStatus] DEFAULT ('uploaded'),
                [FailureCode] nvarchar(100) NULL,
                [FailureMessage] nvarchar(2000) NULL,
                [FailureAction] nvarchar(500) NULL,
                [FailureTechnicalDetail] nvarchar(4000) NULL,
                [ExtractedText] nvarchar(max) NULL,
                [IndexingStatus] nvarchar(32) NOT NULL CONSTRAINT [DF_knowledge_documents_IndexingStatus] DEFAULT ('not_indexed'),
                [IndexingFailureCode] nvarchar(100) NULL,
                [IndexingFailureMessage] nvarchar(2000) NULL,
                [EmbeddingProvider] nvarchar(100) NULL,
                [EmbeddingModel] nvarchar(200) NULL,
                [EmbeddingModelVersion] nvarchar(100) NULL,
                [EmbeddingDimensions] int NULL,
                [CurrentChunkSetFingerprint] nvarchar(128) NULL,
                [CurrentChunkSetVersion] int NOT NULL CONSTRAINT [DF_knowledge_documents_CurrentChunkSetVersion] DEFAULT (0),
                [ActiveChunkCount] int NOT NULL CONSTRAINT [DF_knowledge_documents_ActiveChunkCount] DEFAULT (0),
                [CanRetry] bit NOT NULL CONSTRAINT [DF_knowledge_documents_CanRetry] DEFAULT (0),
                [CreatedUtc] datetime2 NOT NULL,
                [UpdatedUtc] datetime2 NOT NULL,
                [UploadedUtc] datetime2 NULL,
                [ProcessingStartedUtc] datetime2 NULL,
                [ProcessedUtc] datetime2 NULL,
                [FailedUtc] datetime2 NULL,
                [IndexedUtc] datetime2 NULL,
                [IndexingFailedUtc] datetime2 NULL,
                [IndexingRequestedUtc] datetime2 NULL,
                [IndexingStartedUtc] datetime2 NULL,
                CONSTRAINT [FK_knowledge_documents_companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_knowledge_documents_CompanyId_CreatedUtc] ON [knowledge_documents] ([CompanyId], [CreatedUtc]);""");
        await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_knowledge_documents_CompanyId_IngestionStatus] ON [knowledge_documents] ([CompanyId], [IngestionStatus]);""");
        await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_knowledge_documents_CompanyId_IndexingStatus] ON [knowledge_documents] ([CompanyId], [IndexingStatus]);""");
        await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_knowledge_documents_CompanyId_IndexingStatus_IndexingRequestedUtc] ON [knowledge_documents] ([CompanyId], [IndexingStatus], [IndexingRequestedUtc]);""");
        await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_knowledge_documents_CompanyId_IndexingStatus_IndexingStartedUtc] ON [knowledge_documents] ([CompanyId], [IndexingStatus], [IndexingStartedUtc]);""");

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [knowledge_chunks] (
                [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_knowledge_chunks] PRIMARY KEY,
                [CompanyId] uniqueidentifier NOT NULL,
                [DocumentId] uniqueidentifier NOT NULL,
                [ChunkSetVersion] int NOT NULL,
                [ChunkIndex] int NOT NULL,
                [IsActive] bit NOT NULL CONSTRAINT [DF_knowledge_chunks_IsActive] DEFAULT (1),
                [Content] nvarchar(max) NOT NULL,
                [Embedding] nvarchar(max) NOT NULL,
                [metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_knowledge_chunks_metadata_json] DEFAULT (NCHAR(123) + NCHAR(125)),
                [SourceReference] nvarchar(1024) NOT NULL,
                [StartOffset] int NULL,
                [EndOffset] int NULL,
                [ContentHash] nvarchar(64) NOT NULL,
                [EmbeddingProvider] nvarchar(100) NULL,
                [EmbeddingModel] nvarchar(200) NOT NULL,
                [EmbeddingModelVersion] nvarchar(100) NULL,
                [EmbeddingDimensions] int NOT NULL,
                [CreatedUtc] datetime2 NOT NULL,
                CONSTRAINT [FK_knowledge_chunks_companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [companies] ([Id]),
                CONSTRAINT [FK_knowledge_chunks_knowledge_documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [knowledge_documents] ([Id]) ON DELETE CASCADE
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_knowledge_chunks_CompanyId_CreatedUtc] ON [knowledge_chunks] ([CompanyId], [CreatedUtc]);""");
        await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_knowledge_chunks_CompanyId_IsActive_DocumentId] ON [knowledge_chunks] ([CompanyId], [IsActive], [DocumentId]);""");
        await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_knowledge_chunks_CompanyId_DocumentId_ChunkSetVersion_IsActive] ON [knowledge_chunks] ([CompanyId], [DocumentId], [ChunkSetVersion], [IsActive]);""");
        await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX [IX_knowledge_chunks_DocumentId_ChunkSetVersion_ChunkIndex] ON [knowledge_chunks] ([DocumentId], [ChunkSetVersion], [ChunkIndex]);""");
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task EnsureSqlServerDirectChatSchemaAsync(VirtualCompanyDbContext dbContext)
{
    var providerName = dbContext.Database.ProviderName;
    if (!string.Equals(providerName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
    {
        return;
    }

    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
    if (shouldCloseConnection)
    {
        await connection.OpenAsync();
    }

    try
    {
        if (!await SqlServerTableExistsAsync(connection, "conversations"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE [conversations] (
                    [id] uniqueidentifier NOT NULL CONSTRAINT [PK_conversations] PRIMARY KEY,
                    [company_id] uniqueidentifier NOT NULL,
                    [channel_type] nvarchar(64) NOT NULL,
                    [subject] nvarchar(200) NULL,
                    [created_by_user_id] uniqueidentifier NOT NULL,
                    [agent_id] uniqueidentifier NULL,
                    [metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_conversations_metadata_json] DEFAULT (NCHAR(123) + NCHAR(125)),
                    [created_at] datetime2 NOT NULL,
                    [updated_at] datetime2 NOT NULL,
                    CONSTRAINT [FK_conversations_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_conversations_users_created_by_user_id] FOREIGN KEY ([created_by_user_id]) REFERENCES [users] ([Id]),
                    CONSTRAINT [FK_conversations_agents_agent_id] FOREIGN KEY ([agent_id]) REFERENCES [agents] ([Id])
                );
                """);

            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX [IX_conversations_company_id_channel_type_created_by_user_id_agent_id] ON [conversations] ([company_id], [channel_type], [created_by_user_id], [agent_id]) WHERE [agent_id] IS NOT NULL;""");
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_conversations_company_id_updated_at] ON [conversations] ([company_id], [updated_at]);""");
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_conversations_company_id_agent_id] ON [conversations] ([company_id], [agent_id]);""");
        }

        if (!await SqlServerTableExistsAsync(connection, "messages"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE [messages] (
                    [id] uniqueidentifier NOT NULL CONSTRAINT [PK_messages] PRIMARY KEY,
                    [company_id] uniqueidentifier NOT NULL,
                    [conversation_id] uniqueidentifier NOT NULL,
                    [sender_type] nvarchar(64) NOT NULL,
                    [sender_id] uniqueidentifier NULL,
                    [message_type] nvarchar(64) NOT NULL,
                    [body] nvarchar(max) NOT NULL,
                    [structured_payload] nvarchar(max) NOT NULL CONSTRAINT [DF_messages_structured_payload] DEFAULT (NCHAR(123) + NCHAR(125)),
                    [created_at] datetime2 NOT NULL,
                    CONSTRAINT [FK_messages_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_messages_conversations_conversation_id] FOREIGN KEY ([conversation_id]) REFERENCES [conversations] ([id]) ON DELETE CASCADE
                );
                """);

            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_messages_company_id_conversation_id] ON [messages] ([company_id], [conversation_id]);""");
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_messages_conversation_id_created_at] ON [messages] ([conversation_id], [created_at]);""");
        }
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task EnsureSqlServerAuditEventSchemaAsync(VirtualCompanyDbContext dbContext)
{
    var providerName = dbContext.Database.ProviderName;
    if (!string.Equals(providerName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
    {
        return;
    }

    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
    if (shouldCloseConnection)
    {
        await connection.OpenAsync();
    }

    try
    {
        if (!await SqlServerTableExistsAsync(connection, "audit_events"))
        {
            return;
        }

        if (!await SqlServerColumnExistsAsync(connection, "audit_events", "data_sources_used_json"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [audit_events]
                ADD [data_sources_used_json] nvarchar(max) NOT NULL
                    CONSTRAINT [DF_audit_events_data_sources_used_json_startup] DEFAULT (N'[]');
                """);
        }

        if (!await SqlServerColumnExistsAsync(connection, "audit_events", "RelatedAgentId"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE [audit_events] ADD [RelatedAgentId] uniqueidentifier NULL;""");
        }

        if (!await SqlServerColumnExistsAsync(connection, "audit_events", "RelatedTaskId"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE [audit_events] ADD [RelatedTaskId] uniqueidentifier NULL;""");
        }

        if (!await SqlServerColumnExistsAsync(connection, "audit_events", "RelatedWorkflowInstanceId"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE [audit_events] ADD [RelatedWorkflowInstanceId] uniqueidentifier NULL;""");
        }

        if (!await SqlServerColumnExistsAsync(connection, "audit_events", "RelatedApprovalRequestId"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE [audit_events] ADD [RelatedApprovalRequestId] uniqueidentifier NULL;""");
        }

        if (!await SqlServerColumnExistsAsync(connection, "audit_events", "RelatedToolExecutionAttemptId"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE [audit_events] ADD [RelatedToolExecutionAttemptId] uniqueidentifier NULL;""");
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [audit_events]
            SET [RelatedAgentId] = COALESCE(
                [RelatedAgentId],
                CASE WHEN [ActorType] = N'agent' THEN [ActorId] ELSE NULL END,
                CASE WHEN [TargetType] = N'agent' THEN TRY_CONVERT(uniqueidentifier, [TargetId]) ELSE NULL END,
                TRY_CONVERT(uniqueidentifier, JSON_VALUE([metadata_json], '$.agentId')))
            WHERE [RelatedAgentId] IS NULL;
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [audit_events]
            SET [RelatedTaskId] = COALESCE(
                [RelatedTaskId],
                CASE WHEN [TargetType] = N'work_task' THEN TRY_CONVERT(uniqueidentifier, [TargetId]) ELSE NULL END,
                TRY_CONVERT(uniqueidentifier, JSON_VALUE([metadata_json], '$.taskId')),
                TRY_CONVERT(uniqueidentifier, JSON_VALUE([metadata_json], '$.workTaskId')))
            WHERE [RelatedTaskId] IS NULL;
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [audit_events]
            SET [RelatedWorkflowInstanceId] = COALESCE(
                [RelatedWorkflowInstanceId],
                CASE WHEN [TargetType] = N'workflow_instance' THEN TRY_CONVERT(uniqueidentifier, [TargetId]) ELSE NULL END,
                TRY_CONVERT(uniqueidentifier, JSON_VALUE([metadata_json], '$.workflowInstanceId')))
            WHERE [RelatedWorkflowInstanceId] IS NULL;
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [audit_events]
            SET [RelatedApprovalRequestId] = COALESCE(
                [RelatedApprovalRequestId],
                CASE WHEN [TargetType] = N'approval_request' THEN TRY_CONVERT(uniqueidentifier, [TargetId]) ELSE NULL END,
                TRY_CONVERT(uniqueidentifier, JSON_VALUE([metadata_json], '$.approvalRequestId')))
            WHERE [RelatedApprovalRequestId] IS NULL;
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [audit_events]
            SET [RelatedToolExecutionAttemptId] = COALESCE(
                [RelatedToolExecutionAttemptId],
                CASE WHEN [TargetType] = N'agent_tool_execution' THEN TRY_CONVERT(uniqueidentifier, [TargetId]) ELSE NULL END,
                TRY_CONVERT(uniqueidentifier, JSON_VALUE([metadata_json], '$.toolExecutionId')),
                TRY_CONVERT(uniqueidentifier, JSON_VALUE([metadata_json], '$.toolExecutionAttemptId')))
            WHERE [RelatedToolExecutionAttemptId] IS NULL;
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_audit_events_CompanyId_ActorType_ActorId' AND object_id = OBJECT_ID(N'[audit_events]'))
            BEGIN
                CREATE INDEX [IX_audit_events_CompanyId_ActorType_ActorId]
                ON [audit_events] ([CompanyId], [ActorType], [ActorId]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_audit_events_CompanyId_RelatedAgentId_OccurredUtc' AND object_id = OBJECT_ID(N'[audit_events]'))
            BEGIN
                CREATE INDEX [IX_audit_events_CompanyId_RelatedAgentId_OccurredUtc]
                ON [audit_events] ([CompanyId], [RelatedAgentId], [OccurredUtc]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_audit_events_CompanyId_RelatedTaskId_OccurredUtc' AND object_id = OBJECT_ID(N'[audit_events]'))
            BEGIN
                CREATE INDEX [IX_audit_events_CompanyId_RelatedTaskId_OccurredUtc]
                ON [audit_events] ([CompanyId], [RelatedTaskId], [OccurredUtc]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_audit_events_CompanyId_RelatedWorkflowInstanceId_OccurredUtc' AND object_id = OBJECT_ID(N'[audit_events]'))
            BEGIN
                CREATE INDEX [IX_audit_events_CompanyId_RelatedWorkflowInstanceId_OccurredUtc]
                ON [audit_events] ([CompanyId], [RelatedWorkflowInstanceId], [OccurredUtc]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_audit_events_CompanyId_RelatedApprovalRequestId_OccurredUtc' AND object_id = OBJECT_ID(N'[audit_events]'))
            BEGIN
                CREATE INDEX [IX_audit_events_CompanyId_RelatedApprovalRequestId_OccurredUtc]
                ON [audit_events] ([CompanyId], [RelatedApprovalRequestId], [OccurredUtc]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_audit_events_CompanyId_RelatedToolExecutionAttemptId_OccurredUtc' AND object_id = OBJECT_ID(N'[audit_events]'))
            BEGIN
                CREATE INDEX [IX_audit_events_CompanyId_RelatedToolExecutionAttemptId_OccurredUtc]
                ON [audit_events] ([CompanyId], [RelatedToolExecutionAttemptId], [OccurredUtc]);
            END
            """);
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task EnsureSqlServerBriefingSchemaAsync(VirtualCompanyDbContext dbContext)
{
    var providerName = dbContext.Database.ProviderName;
    if (!string.Equals(providerName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
    {
        return;
    }

    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
    if (shouldCloseConnection)
    {
        await connection.OpenAsync();
    }

    try
    {
        if (!await SqlServerTableExistsAsync(connection, "company_briefing_update_jobs"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE [company_briefing_update_jobs] (
                    [id] uniqueidentifier NOT NULL CONSTRAINT [pk_company_briefing_update_jobs] PRIMARY KEY,
                    [company_id] uniqueidentifier NOT NULL,
                    [trigger_type] nvarchar(32) NOT NULL,
                    [briefing_type] nvarchar(32) NULL,
                    [event_type] nvarchar(100) NULL,
                    [correlation_id] nvarchar(128) NOT NULL,
                    [idempotency_key] nvarchar(300) NOT NULL,
                    [status] nvarchar(32) NOT NULL,
                    [attempt_count] int NOT NULL CONSTRAINT [DF_company_briefing_update_jobs_attempt_count_startup] DEFAULT (0),
                    [max_attempts] int NOT NULL CONSTRAINT [DF_company_briefing_update_jobs_max_attempts_startup] DEFAULT (5),
                    [next_attempt_at] datetime2 NULL,
                    [last_error_code] nvarchar(256) NULL,
                    [last_error] nvarchar(4000) NULL,
                    [last_error_details] nvarchar(max) NULL,
                    [last_failure_at] datetime2 NULL,
                    [started_at] datetime2 NULL,
                    [completed_at] datetime2 NULL,
                    [final_failed_at] datetime2 NULL,
                    [created_at] datetime2 NOT NULL,
                    [updated_at] datetime2 NOT NULL,
                    [source_metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_company_briefing_update_jobs_source_metadata_json_startup] DEFAULT (NCHAR(123) + NCHAR(125)),
                    CONSTRAINT [fk_company_briefing_update_jobs_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
                );
                """);
        }

        if (!await SqlServerColumnExistsAsync(connection, "company_briefing_update_jobs", "max_attempts"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [company_briefing_update_jobs]
                ADD [max_attempts] int NOT NULL
                    CONSTRAINT [DF_company_briefing_update_jobs_max_attempts_startup] DEFAULT (5);
                """);
        }

        if (!await SqlServerColumnExistsAsync(connection, "company_briefing_update_jobs", "last_error_code"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [company_briefing_update_jobs] ADD [last_error_code] nvarchar(256) NULL;");
        }

        if (!await SqlServerColumnExistsAsync(connection, "company_briefing_update_jobs", "last_error_details"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [company_briefing_update_jobs] ADD [last_error_details] nvarchar(max) NULL;");
        }

        if (!await SqlServerColumnExistsAsync(connection, "company_briefing_update_jobs", "last_failure_at"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [company_briefing_update_jobs] ADD [last_failure_at] datetime2 NULL;");
        }

        if (!await SqlServerColumnExistsAsync(connection, "company_briefing_update_jobs", "started_at"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [company_briefing_update_jobs] ADD [started_at] datetime2 NULL;");
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_company_briefing_update_jobs_company_id_idempotency_key' AND object_id = OBJECT_ID(N'[company_briefing_update_jobs]'))
            BEGIN
                CREATE UNIQUE INDEX [ix_company_briefing_update_jobs_company_id_idempotency_key]
                ON [company_briefing_update_jobs] ([company_id], [idempotency_key]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_company_briefing_update_jobs_status_next_attempt_at_started_at_created_at' AND object_id = OBJECT_ID(N'[company_briefing_update_jobs]'))
            BEGIN
                CREATE INDEX [ix_company_briefing_update_jobs_status_next_attempt_at_started_at_created_at]
                ON [company_briefing_update_jobs] ([status], [next_attempt_at], [started_at], [created_at]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_company_briefing_update_jobs_company_id_status_created_at' AND object_id = OBJECT_ID(N'[company_briefing_update_jobs]'))
            BEGIN
                CREATE INDEX [ix_company_briefing_update_jobs_company_id_status_created_at]
                ON [company_briefing_update_jobs] ([company_id], [status], [created_at]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_company_briefing_update_jobs_company_id_event_type_created_at' AND object_id = OBJECT_ID(N'[company_briefing_update_jobs]'))
            BEGIN
                CREATE INDEX [ix_company_briefing_update_jobs_company_id_event_type_created_at]
                ON [company_briefing_update_jobs] ([company_id], [event_type], [created_at]);
            END
            """);

        if (!await SqlServerTableExistsAsync(connection, "insight_acknowledgments"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE [insight_acknowledgments] (
                    [id] uniqueidentifier NOT NULL CONSTRAINT [PK_insight_acknowledgments] PRIMARY KEY,
                    [company_id] uniqueidentifier NOT NULL,
                    [user_id] uniqueidentifier NOT NULL,
                    [insight_key] nvarchar(200) NOT NULL,
                    [acknowledged_at] datetime2 NOT NULL,
                    [created_at] datetime2 NOT NULL,
                    [updated_at] datetime2 NOT NULL,
                    CONSTRAINT [FK_insight_acknowledgments_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_insight_acknowledgments_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([Id]) ON DELETE CASCADE
                );
                """);
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_insight_acknowledgments_company_id_user_id_insight_key' AND object_id = OBJECT_ID(N'[insight_acknowledgments]'))
            BEGIN
                CREATE UNIQUE INDEX [IX_insight_acknowledgments_company_id_user_id_insight_key]
                ON [insight_acknowledgments] ([company_id], [user_id], [insight_key]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_insight_acknowledgments_company_id_user_id_acknowledged_at' AND object_id = OBJECT_ID(N'[insight_acknowledgments]'))
            BEGIN
                CREATE INDEX [IX_insight_acknowledgments_company_id_user_id_acknowledged_at]
                ON [insight_acknowledgments] ([company_id], [user_id], [acknowledged_at]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_insight_acknowledgments_user_id' AND object_id = OBJECT_ID(N'[insight_acknowledgments]'))
            BEGIN
                CREATE INDEX [IX_insight_acknowledgments_user_id]
                ON [insight_acknowledgments] ([user_id]);
            END
            """);
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}
static async Task<bool> SqlServerTableExistsAsync(DbConnection connection, string tableName)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT OBJECT_ID(@tableName, 'U');";

    var parameter = command.CreateParameter();
    parameter.ParameterName = "@tableName";
    parameter.Value = tableName;
    command.Parameters.Add(parameter);

    var result = await command.ExecuteScalarAsync();
    return result is not DBNull && result is not null;
}

static async Task<bool> SqlServerColumnExistsAsync(DbConnection connection, string tableName, string columnName)
{
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT 1
        FROM sys.columns AS c
        INNER JOIN sys.tables AS t ON c.object_id = t.object_id
        WHERE t.name = @tableName AND c.name = @columnName;
        """;

    var tableParameter = command.CreateParameter();
    tableParameter.ParameterName = "@tableName";
    tableParameter.Value = tableName;
    command.Parameters.Add(tableParameter);

    var columnParameter = command.CreateParameter();
    columnParameter.ParameterName = "@columnName";
    columnParameter.Value = columnName;
    command.Parameters.Add(columnParameter);

    var result = await command.ExecuteScalarAsync();
    return result is not DBNull && result is not null;
}

static async Task InitializeDatabaseAsync(
    WebApplication app,
    VirtualCompanyDbContext dbContext,
    ILogger logger,
    CompanySetupTemplateSeeder templateSeeder,
    CompanyWorkflowDefinitionSeeder workflowDefinitionSeeder,
    bool applyMigrationsOnStartup)
{
    if (dbContext.Database.IsRelational())
    {
        await WaitForSqlServerReadyAsync(dbContext, logger, app.Lifetime.ApplicationStopping);

        if (applyMigrationsOnStartup)
        {
            await dbContext.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
            await EnsureSqlServerAgentExecutionSchemaAsync(dbContext);
            await EnsureSqlServerKnowledgeSchemaAsync(dbContext);
            await EnsureSqlServerDirectChatSchemaAsync(dbContext);
            await EnsureSqlServerAuditEventSchemaAsync(dbContext);
            await EnsureSqlServerBriefingSchemaAsync(dbContext);
        }
        else
        {
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(app.Lifetime.ApplicationStopping);
            if (pendingMigrations.Any())
            {
                var pendingList = string.Join(", ", pendingMigrations);
                logger.LogCritical(
                    "Database schema is not up to date. Pending migrations: {PendingMigrations}. Enable DatabaseInitialization:ApplyMigrationsOnStartup or run 'dotnet ef database update' before starting the API.",
                    pendingList);
                throw new InvalidOperationException(
                    $"Database schema is not up to date. Pending migrations: {pendingList}. Enable DatabaseInitialization:ApplyMigrationsOnStartup or run 'dotnet ef database update' before starting the API.");
            }
        }
    }
    else if (app.Environment.IsDevelopment())
    {
        await dbContext.Database.EnsureCreatedAsync(app.Lifetime.ApplicationStopping);
    }

    await templateSeeder.SeedAsync();
    await workflowDefinitionSeeder.SeedAsync();
}

static async Task WaitForSqlServerReadyAsync(
    VirtualCompanyDbContext dbContext,
    ILogger logger,
    CancellationToken cancellationToken)
{
    var providerName = dbContext.Database.ProviderName;
    if (!string.Equals(providerName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
    {
        return;
    }

    var connectionString = dbContext.Database.GetConnectionString();
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("The VirtualCompanyDb connection string is not configured.");
    }

    var builder = new SqlConnectionStringBuilder(connectionString)
    {
        ConnectRetryCount = 0
    };

    var maxAttempts = 8;
    var delay = TimeSpan.FromSeconds(2);

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return;
        }
        catch (Exception ex) when (IsTransientSqlStartupException(ex) && attempt < maxAttempts)
        {
            logger.LogWarning(
                ex,
                "SQL Server was not ready during application startup. Retrying database initialization in {DelaySeconds}s (attempt {Attempt}/{MaxAttempts}).",
                delay.TotalSeconds,
                attempt,
                maxAttempts);

            await Task.Delay(delay, cancellationToken);
        }
    }

    throw new InvalidOperationException("SQL Server did not become ready before database initialization completed.");
}

static bool IsTransientSqlStartupException(Exception exception)
{
    if (exception is SqlException sqlException)
    {
        foreach (SqlError error in sqlException.Errors)
        {
            if (error.Number is 10054 or 233 or 4060 or 18456 or 258 or 53 or -2)
            {
                return true;
            }
        }
    }

    if (exception is Win32Exception win32Exception && win32Exception.NativeErrorCode == 10054)
    {
        return true;
    }

    return exception.InnerException is not null && IsTransientSqlStartupException(exception.InnerException);
}

public partial class Program;
