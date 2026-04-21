using System.Text.Json;
using System.Text.Json.Serialization;
using System.Data.Common;
using System.ComponentModel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using VirtualCompany.Infrastructure;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Activity;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Authorization;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Activity;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

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
var applyMigrationsOnStartup = app.Configuration.GetValue<bool>("DatabaseInitialization:ApplyMigrationsOnStartup");

var failFastOnPendingMigrations = !applyMigrationsOnStartup && StartupMigrationValidation.ShouldFailFastOnPendingMigrations(app.Environment);

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
    var planningBaselineService = scope.ServiceProvider.GetRequiredService<IPlanningBaselineService>();
    await InitializeDatabaseAsync(
        app,
        dbContext,
        logger,
        templateSeeder,
        workflowDefinitionSeeder,
        planningBaselineService,
        applyMigrationsOnStartup,
        failFastOnPendingMigrations,
        builder.Configuration.GetValue<bool?>("SimulationStartup:StopRunningSessionsOnStartup") ?? true);
}

if (TryParseFinanceSeedCliCommand(args, out var seedCommand, out var seedCommandError))
{
    if (seedCommandError is not null)
    {
        Console.Error.WriteLine(seedCommandError);
        Environment.ExitCode = 2;
        return;
    }

    using var scope = app.Services.CreateScope();
    var bootstrapService = scope.ServiceProvider.GetRequiredService<IFinanceSeedBootstrapService>();
    var result = await bootstrapService.GenerateAsync(seedCommand!, app.Lifetime.ApplicationStopping);
    Console.WriteLine(JsonSerializer.Serialize(
        result,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    Environment.ExitCode = result.ValidationErrors.Count == 0 ? 0 : 1;
    return;
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
                    [tool_version] nvarchar(32) NOT NULL CONSTRAINT [DF_tool_executions_tool_version] DEFAULT ('1.0.0'),
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
                    [denial_reason] nvarchar(512) NULL,
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

        if (await SqlServerTableExistsAsync(connection, "tool_executions"))
        {
            if (!await SqlServerColumnExistsAsync(connection, "tool_executions", "tool_version"))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    ALTER TABLE [tool_executions]
                    ADD [tool_version] nvarchar(32) NOT NULL
                        CONSTRAINT [DF_tool_executions_tool_version_startup] DEFAULT ('1.0.0');
                    """);
            }

            if (!await SqlServerColumnExistsAsync(connection, "tool_executions", "denial_reason"))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    ALTER TABLE [tool_executions]
                    ADD [denial_reason] nvarchar(512) NULL;
                    """);
            }
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

        if (await SqlServerColumnExistsAsync(connection, "approval_requests", "ToolExecutionAttemptId") &&
            !await SqlServerColumnIsNullableAsync(connection, "approval_requests", "ToolExecutionAttemptId"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_approval_requests_ToolExecutionAttemptId' AND object_id = OBJECT_ID(N'[approval_requests]'))
                BEGIN
                    DROP INDEX [IX_approval_requests_ToolExecutionAttemptId] ON [approval_requests];
                END
                """);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [approval_requests]
                ALTER COLUMN [ToolExecutionAttemptId] uniqueidentifier NULL;
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

static async Task EnsureSqlServerFinanceSeedSchemaAsync(VirtualCompanyDbContext dbContext)
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
        await EnsureSqlServerFinanceDomainSchemaAsync(dbContext, connection);
        await EnsureSqlServerCompanySimulationSchemaAsync(dbContext, connection);

        if (!await SqlServerColumnExistsAsync(connection, "companies", "finance_seed_status"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [companies] ADD [finance_seed_status] nvarchar(32) NULL;");
        }

        if (!await SqlServerColumnExistsAsync(connection, "companies", "finance_seed_status_updated_at"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [companies] ADD [finance_seed_status_updated_at] datetime2 NULL;");
        }

        if (!await SqlServerColumnExistsAsync(connection, "companies", "finance_seeded_at"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [companies] ADD [finance_seeded_at] datetime2 NULL;");
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [companies]
            SET [finance_seed_status] = COALESCE([finance_seed_status], N'not_seeded'),
                [finance_seed_status_updated_at] = COALESCE([finance_seed_status_updated_at], [UpdatedUtc], [CreatedUtc]),
                [finance_seeded_at] = CASE
                    WHEN [finance_seeded_at] IS NOT NULL THEN [finance_seeded_at]
                    WHEN [finance_seed_status] IN (N'fully_seeded', N'seeded') THEN COALESCE([UpdatedUtc], [CreatedUtc])
                    ELSE NULL
                END;
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                INNER JOIN sys.tables t ON c.object_id = t.object_id
                WHERE t.name = N'companies' AND c.name = N'finance_seed_status')
            BEGIN
                ALTER TABLE [companies]
                ADD CONSTRAINT [DF_companies_finance_seed_status_startup] DEFAULT (N'not_seeded') FOR [finance_seed_status];
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [companies] ALTER COLUMN [finance_seed_status] nvarchar(32) NOT NULL;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [companies] ALTER COLUMN [finance_seed_status_updated_at] datetime2 NOT NULL;");

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.check_constraints
                WHERE name = N'CK_companies_finance_seed_status'
                  AND parent_object_id = OBJECT_ID(N'[companies]'))
            BEGIN
                ALTER TABLE [companies]
                ADD CONSTRAINT [CK_companies_finance_seed_status]
                CHECK ([finance_seed_status] IN (N'not_seeded', N'partially_seeded', N'fully_seeded', N'seeding', N'seeded', N'failed'));
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_companies_finance_seed_status'
                  AND object_id = OBJECT_ID(N'[companies]'))
            BEGIN
                CREATE INDEX [IX_companies_finance_seed_status]
                ON [companies] ([finance_seed_status]);
            END
            """);

        if (!await SqlServerTableExistsAsync(connection, "finance_seed_backfill_runs"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE [finance_seed_backfill_runs] (
                    [id] uniqueidentifier NOT NULL CONSTRAINT [PK_finance_seed_backfill_runs] PRIMARY KEY,
                    [status] nvarchar(64) NOT NULL,
                    [started_at] datetime2 NOT NULL,
                    [completed_at] datetime2 NULL,
                    [scanned_count] int NOT NULL,
                    [queued_count] int NOT NULL,
                    [succeeded_count] int NOT NULL,
                    [skipped_count] int NOT NULL,
                    [failed_count] int NOT NULL,
                    [configuration_snapshot_json] nvarchar(max) NOT NULL,
                    [error_details] nvarchar(2000) NULL
                );
                """);
        }

        if (!await SqlServerTableExistsAsync(connection, "finance_seed_backfill_attempts"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE [finance_seed_backfill_attempts] (
                    [id] uniqueidentifier NOT NULL CONSTRAINT [PK_finance_seed_backfill_attempts] PRIMARY KEY,
                    [run_id] uniqueidentifier NOT NULL,
                    [company_id] uniqueidentifier NOT NULL,
                    [background_execution_id] uniqueidentifier NULL,
                    [idempotency_key] nvarchar(200) NULL,
                    [status] nvarchar(64) NOT NULL,
                    [started_at] datetime2 NOT NULL,
                    [completed_at] datetime2 NULL,
                    [skip_reason] nvarchar(256) NULL,
                    [error_details] nvarchar(2000) NULL,
                    [seed_state_before] nvarchar(32) NOT NULL,
                    [seed_state_after] nvarchar(32) NULL,
                    CONSTRAINT [FK_finance_seed_backfill_attempts_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_finance_seed_backfill_attempts_finance_seed_backfill_runs_run_id] FOREIGN KEY ([run_id]) REFERENCES [finance_seed_backfill_runs] ([id]) ON DELETE CASCADE
                );
                """);
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_finance_seed_backfill_runs_started_at'
                  AND object_id = OBJECT_ID(N'[finance_seed_backfill_runs]'))
            BEGIN
                CREATE INDEX [IX_finance_seed_backfill_runs_started_at]
                ON [finance_seed_backfill_runs] ([started_at]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_finance_seed_backfill_attempts_background_execution_id'
                  AND object_id = OBJECT_ID(N'[finance_seed_backfill_attempts]'))
            BEGIN
                CREATE INDEX [IX_finance_seed_backfill_attempts_background_execution_id]
                ON [finance_seed_backfill_attempts] ([background_execution_id]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_finance_seed_backfill_attempts_company_id'
                  AND object_id = OBJECT_ID(N'[finance_seed_backfill_attempts]'))
            BEGIN
                CREATE INDEX [IX_finance_seed_backfill_attempts_company_id]
                ON [finance_seed_backfill_attempts] ([company_id]);
            END
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_finance_seed_backfill_attempts_run_id_company_id'
                  AND object_id = OBJECT_ID(N'[finance_seed_backfill_attempts]'))
            BEGIN
                CREATE UNIQUE INDEX [IX_finance_seed_backfill_attempts_run_id_company_id]
                ON [finance_seed_backfill_attempts] ([run_id], [company_id]);
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

static async Task EnsureSqlServerFinanceDomainSchemaAsync(VirtualCompanyDbContext dbContext, DbConnection connection)
{
    if (!await SqlServerTableExistsAsync(connection, "finance_accounts"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [finance_accounts] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_finance_accounts] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [code] nvarchar(32) NOT NULL,
                [name] nvarchar(160) NOT NULL,
                [account_type] nvarchar(64) NOT NULL,
                [currency] nvarchar(3) NOT NULL,
                [opening_balance] decimal(18,2) NOT NULL,
                [opened_at] datetime2 NOT NULL,
                [created_at] datetime2 NOT NULL,
                [updated_at] datetime2 NOT NULL,
                CONSTRAINT [AK_finance_accounts_company_id_id] UNIQUE ([company_id], [id]),
                CONSTRAINT [FK_finance_accounts_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );
            """);
    }

    if (!await SqlServerTableExistsAsync(connection, "finance_counterparties"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [finance_counterparties] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_finance_counterparties] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [name] nvarchar(200) NOT NULL,
                [counterparty_type] nvarchar(64) NOT NULL,
                [email] nvarchar(256) NULL,
                [payment_terms] nvarchar(64) NULL,
                [tax_id] nvarchar(64) NULL,
                [credit_limit] decimal(18,2) NULL,
                [preferred_payment_method] nvarchar(64) NULL,
                [default_account_mapping] nvarchar(64) NULL,
                [created_at] datetime2 NOT NULL,
                [updated_at] datetime2 NOT NULL,
                CONSTRAINT [AK_finance_counterparties_company_id_id] UNIQUE ([company_id], [id]),
                CONSTRAINT [FK_finance_counterparties_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );
            """);
    }
    else
    {
        if (!await SqlServerColumnExistsAsync(connection, "finance_counterparties", "payment_terms"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_counterparties] ADD [payment_terms] nvarchar(64) NULL;");
        }

        if (!await SqlServerColumnExistsAsync(connection, "finance_counterparties", "tax_id"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_counterparties] ADD [tax_id] nvarchar(64) NULL;");
        }

        if (!await SqlServerColumnExistsAsync(connection, "finance_counterparties", "credit_limit"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_counterparties] ADD [credit_limit] decimal(18,2) NULL;");
        }

        if (!await SqlServerColumnExistsAsync(connection, "finance_counterparties", "preferred_payment_method"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_counterparties] ADD [preferred_payment_method] nvarchar(64) NULL;");
        }

        if (!await SqlServerColumnExistsAsync(connection, "finance_counterparties", "default_account_mapping"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_counterparties] ADD [default_account_mapping] nvarchar(64) NULL;");
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [finance_counterparties]
            SET
                [payment_terms] = COALESCE(NULLIF([payment_terms], N''), N'Net30'),
                [credit_limit] = COALESCE([credit_limit], 0),
                [preferred_payment_method] = COALESCE(NULLIF([preferred_payment_method], N''), N'bank_transfer'),
                [default_account_mapping] = COALESCE(NULLIF([default_account_mapping], N''), CASE WHEN LOWER([counterparty_type]) = N'customer' THEN N'1100' ELSE N'2000' END)
            WHERE [payment_terms] IS NULL OR [payment_terms] = N'' OR [credit_limit] IS NULL OR [preferred_payment_method] IS NULL OR [preferred_payment_method] = N'' OR [default_account_mapping] IS NULL OR [default_account_mapping] = N'';
            """);
    }

    if (!await SqlServerTableExistsAsync(connection, "finance_policy_configurations"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [finance_policy_configurations] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_finance_policy_configurations] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [approval_currency] nvarchar(3) NOT NULL,
                [invoice_approval_threshold] decimal(18,2) NOT NULL,
                [bill_approval_threshold] decimal(18,2) NOT NULL,
                [require_counterparty_for_transactions] bit NOT NULL CONSTRAINT [DF_finance_policy_configurations_require_counterparty_for_transactions] DEFAULT (1),
                [anomaly_detection_lower_bound] decimal(18,2) NOT NULL CONSTRAINT [DF_finance_policy_configurations_anomaly_detection_lower_bound] DEFAULT (-10000),
                [anomaly_detection_upper_bound] decimal(18,2) NOT NULL CONSTRAINT [DF_finance_policy_configurations_anomaly_detection_upper_bound] DEFAULT (10000),
                [cash_runway_warning_threshold_days] int NOT NULL CONSTRAINT [DF_finance_policy_configurations_cash_runway_warning_threshold_days] DEFAULT (90),
                [cash_runway_critical_threshold_days] int NOT NULL CONSTRAINT [DF_finance_policy_configurations_cash_runway_critical_threshold_days] DEFAULT (30),
                [created_at] datetime2 NOT NULL,
                [updated_at] datetime2 NOT NULL,
                CONSTRAINT [FK_finance_policy_configurations_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );
            """);
    }
    else
    {
        if (!await SqlServerColumnExistsAsync(connection, "finance_policy_configurations", "anomaly_detection_lower_bound"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_policy_configurations] ADD [anomaly_detection_lower_bound] decimal(18,2) NOT NULL CONSTRAINT [DF_finance_policy_configurations_anomaly_detection_lower_bound] DEFAULT (-10000);");
        }

        if (!await SqlServerColumnExistsAsync(connection, "finance_policy_configurations", "anomaly_detection_upper_bound"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_policy_configurations] ADD [anomaly_detection_upper_bound] decimal(18,2) NOT NULL CONSTRAINT [DF_finance_policy_configurations_anomaly_detection_upper_bound] DEFAULT (10000);");
        }

        if (!await SqlServerColumnExistsAsync(connection, "finance_policy_configurations", "cash_runway_warning_threshold_days"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_policy_configurations] ADD [cash_runway_warning_threshold_days] int NOT NULL CONSTRAINT [DF_finance_policy_configurations_cash_runway_warning_threshold_days] DEFAULT (90);");
        }

        if (!await SqlServerColumnExistsAsync(connection, "finance_policy_configurations", "cash_runway_critical_threshold_days"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_policy_configurations] ADD [cash_runway_critical_threshold_days] int NOT NULL CONSTRAINT [DF_finance_policy_configurations_cash_runway_critical_threshold_days] DEFAULT (30);");
        }
    }

    if (!await SqlServerTableExistsAsync(connection, "finance_bills"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [finance_bills] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_finance_bills] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [counterparty_id] uniqueidentifier NOT NULL,
                [bill_number] nvarchar(64) NOT NULL,
                [received_at] datetime2 NOT NULL,
                [due_at] datetime2 NOT NULL,
                [amount] decimal(18,2) NOT NULL,
                [currency] nvarchar(3) NOT NULL,
                [status] nvarchar(32) NOT NULL,
                [created_at] datetime2 NOT NULL,
                [updated_at] datetime2 NOT NULL,
                [document_id] uniqueidentifier NULL,
                CONSTRAINT [AK_finance_bills_company_id_id] UNIQUE ([company_id], [id]),
                CONSTRAINT [FK_finance_bills_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_finance_bills_finance_counterparties_company_id_counterparty_id] FOREIGN KEY ([company_id], [counterparty_id]) REFERENCES [finance_counterparties] ([company_id], [id])
            );
            """);
    }
    else if (!await SqlServerColumnExistsAsync(connection, "finance_bills", "document_id"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_bills] ADD [document_id] uniqueidentifier NULL;");
    }

    if (!await SqlServerTableExistsAsync(connection, "finance_invoices"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [finance_invoices] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_finance_invoices] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [counterparty_id] uniqueidentifier NOT NULL,
                [invoice_number] nvarchar(64) NOT NULL,
                [issued_at] datetime2 NOT NULL,
                [due_at] datetime2 NOT NULL,
                [amount] decimal(18,2) NOT NULL,
                [currency] nvarchar(3) NOT NULL,
                [status] nvarchar(32) NOT NULL,
                [created_at] datetime2 NOT NULL,
                [updated_at] datetime2 NOT NULL,
                [document_id] uniqueidentifier NULL,
                CONSTRAINT [AK_finance_invoices_company_id_id] UNIQUE ([company_id], [id]),
                CONSTRAINT [FK_finance_invoices_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_finance_invoices_finance_counterparties_company_id_counterparty_id] FOREIGN KEY ([company_id], [counterparty_id]) REFERENCES [finance_counterparties] ([company_id], [id])
            );
            """);
    }
    else if (!await SqlServerColumnExistsAsync(connection, "finance_invoices", "document_id"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_invoices] ADD [document_id] uniqueidentifier NULL;");
    }

    if (!await SqlServerTableExistsAsync(connection, "finance_transactions"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [finance_transactions] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_finance_transactions] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [account_id] uniqueidentifier NOT NULL,
                [counterparty_id] uniqueidentifier NULL,
                [invoice_id] uniqueidentifier NULL,
                [bill_id] uniqueidentifier NULL,
                [transaction_at] datetime2 NOT NULL,
                [transaction_type] nvarchar(64) NOT NULL,
                [amount] decimal(18,2) NOT NULL,
                [currency] nvarchar(3) NOT NULL,
                [description] nvarchar(500) NOT NULL,
                [external_reference] nvarchar(100) NOT NULL,
                [created_at] datetime2 NOT NULL,
                [document_id] uniqueidentifier NULL,
                CONSTRAINT [FK_finance_transactions_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_finance_transactions_finance_accounts_company_id_account_id] FOREIGN KEY ([company_id], [account_id]) REFERENCES [finance_accounts] ([company_id], [id]),
                CONSTRAINT [FK_finance_transactions_finance_bills_company_id_bill_id] FOREIGN KEY ([company_id], [bill_id]) REFERENCES [finance_bills] ([company_id], [id]),
                CONSTRAINT [FK_finance_transactions_finance_counterparties_company_id_counterparty_id] FOREIGN KEY ([company_id], [counterparty_id]) REFERENCES [finance_counterparties] ([company_id], [id]),
                CONSTRAINT [FK_finance_transactions_finance_invoices_company_id_invoice_id] FOREIGN KEY ([company_id], [invoice_id]) REFERENCES [finance_invoices] ([company_id], [id])
            );
            """);
    }
    else
    {
        if (!await SqlServerColumnExistsAsync(connection, "finance_transactions", "bill_id"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_transactions] ADD [bill_id] uniqueidentifier NULL;");
        }

        if (!await SqlServerColumnExistsAsync(connection, "finance_transactions", "document_id"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE [finance_transactions] ADD [document_id] uniqueidentifier NULL;");
        }
    }

    if (!await SqlServerTableExistsAsync(connection, "finance_balances"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [finance_balances] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_finance_balances] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [account_id] uniqueidentifier NOT NULL,
                [as_of_at] datetime2 NOT NULL,
                [amount] decimal(18,2) NOT NULL,
                [currency] nvarchar(3) NOT NULL,
                [created_at] datetime2 NOT NULL,
                CONSTRAINT [FK_finance_balances_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_finance_balances_finance_accounts_company_id_account_id] FOREIGN KEY ([company_id], [account_id]) REFERENCES [finance_accounts] ([company_id], [id])
            );
            """);
    }

    if (!await SqlServerTableExistsAsync(connection, "finance_seed_anomalies"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [finance_seed_anomalies] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_finance_seed_anomalies] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [anomaly_type] nvarchar(64) NOT NULL,
                [scenario_profile] nvarchar(64) NOT NULL,
                [affected_record_ids_json] text NOT NULL,
                [expected_detection_metadata_json] text NOT NULL,
                [created_at] datetime2 NOT NULL,
                CONSTRAINT [FK_finance_seed_anomalies_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );
            """);
    }

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF OBJECT_ID(N'[knowledge_documents]', N'U') IS NOT NULL
           AND NOT EXISTS (
                SELECT 1
                FROM sys.key_constraints
                WHERE [type] = N'UQ'
                  AND [name] = N'AK_knowledge_documents_CompanyId_Id'
                  AND [parent_object_id] = OBJECT_ID(N'[knowledge_documents]'))
        BEGIN
            ALTER TABLE [knowledge_documents]
            ADD CONSTRAINT [AK_knowledge_documents_CompanyId_Id] UNIQUE ([CompanyId], [Id]);
        END
        """);

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_accounts_company_id_account_type' AND [object_id] = OBJECT_ID(N'[finance_accounts]'))
        BEGIN
            CREATE INDEX [IX_finance_accounts_company_id_account_type] ON [finance_accounts] ([company_id], [account_type]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_accounts_company_id_code' AND [object_id] = OBJECT_ID(N'[finance_accounts]'))
        BEGIN
            CREATE UNIQUE INDEX [IX_finance_accounts_company_id_code] ON [finance_accounts] ([company_id], [code]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_counterparties_company_id_counterparty_type' AND [object_id] = OBJECT_ID(N'[finance_counterparties]'))
        BEGIN
            CREATE INDEX [IX_finance_counterparties_company_id_counterparty_type] ON [finance_counterparties] ([company_id], [counterparty_type]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_counterparties_company_id_name' AND [object_id] = OBJECT_ID(N'[finance_counterparties]'))
        BEGIN
            CREATE INDEX [IX_finance_counterparties_company_id_name] ON [finance_counterparties] ([company_id], [name]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_counterparties_company_id_counterparty_type_name' AND [object_id] = OBJECT_ID(N'[finance_counterparties]'))
        BEGIN
            CREATE INDEX [IX_finance_counterparties_company_id_counterparty_type_name] ON [finance_counterparties] ([company_id], [counterparty_type], [name]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_counterparties_company_id_email' AND [object_id] = OBJECT_ID(N'[finance_counterparties]'))
        BEGIN
            CREATE INDEX [IX_finance_counterparties_company_id_email] ON [finance_counterparties] ([company_id], [email]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_invoices_company_id_invoice_number' AND [object_id] = OBJECT_ID(N'[finance_invoices]'))
        BEGIN
            CREATE UNIQUE INDEX [IX_finance_invoices_company_id_invoice_number] ON [finance_invoices] ([company_id], [invoice_number]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_invoices_company_id_status_due_at' AND [object_id] = OBJECT_ID(N'[finance_invoices]'))
        BEGIN
            CREATE INDEX [IX_finance_invoices_company_id_status_due_at] ON [finance_invoices] ([company_id], [status], [due_at]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_bills_company_id_bill_number' AND [object_id] = OBJECT_ID(N'[finance_bills]'))
        BEGIN
            CREATE UNIQUE INDEX [IX_finance_bills_company_id_bill_number] ON [finance_bills] ([company_id], [bill_number]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_bills_company_id_status_due_at' AND [object_id] = OBJECT_ID(N'[finance_bills]'))
        BEGIN
            CREATE INDEX [IX_finance_bills_company_id_status_due_at] ON [finance_bills] ([company_id], [status], [due_at]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_balances_company_id_account_id_as_of_at' AND [object_id] = OBJECT_ID(N'[finance_balances]'))
        BEGIN
            CREATE UNIQUE INDEX [IX_finance_balances_company_id_account_id_as_of_at] ON [finance_balances] ([company_id], [account_id], [as_of_at]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_policy_configurations_company_id' AND [object_id] = OBJECT_ID(N'[finance_policy_configurations]'))
        BEGIN
            CREATE UNIQUE INDEX [IX_finance_policy_configurations_company_id] ON [finance_policy_configurations] ([company_id]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_transactions_company_id_account_id_transaction_at' AND [object_id] = OBJECT_ID(N'[finance_transactions]'))
        BEGIN
            CREATE INDEX [IX_finance_transactions_company_id_account_id_transaction_at] ON [finance_transactions] ([company_id], [account_id], [transaction_at]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_transactions_company_id_bill_id' AND [object_id] = OBJECT_ID(N'[finance_transactions]'))
        BEGIN
            CREATE INDEX [IX_finance_transactions_company_id_bill_id] ON [finance_transactions] ([company_id], [bill_id]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_transactions_company_id_counterparty_id_transaction_at' AND [object_id] = OBJECT_ID(N'[finance_transactions]'))
        BEGIN
            CREATE INDEX [IX_finance_transactions_company_id_counterparty_id_transaction_at] ON [finance_transactions] ([company_id], [counterparty_id], [transaction_at]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_transactions_company_id_external_reference' AND [object_id] = OBJECT_ID(N'[finance_transactions]'))
        BEGIN
            CREATE UNIQUE INDEX [IX_finance_transactions_company_id_external_reference] ON [finance_transactions] ([company_id], [external_reference]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_transactions_company_id_invoice_id' AND [object_id] = OBJECT_ID(N'[finance_transactions]'))
        BEGIN
            CREATE INDEX [IX_finance_transactions_company_id_invoice_id] ON [finance_transactions] ([company_id], [invoice_id]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_transactions_company_id_document_id' AND [object_id] = OBJECT_ID(N'[finance_transactions]'))
        BEGIN
            CREATE INDEX [IX_finance_transactions_company_id_document_id] ON [finance_transactions] ([company_id], [document_id]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_invoices_company_id_document_id' AND [object_id] = OBJECT_ID(N'[finance_invoices]'))
        BEGIN
            CREATE INDEX [IX_finance_invoices_company_id_document_id] ON [finance_invoices] ([company_id], [document_id]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_bills_company_id_document_id' AND [object_id] = OBJECT_ID(N'[finance_bills]'))
        BEGIN
            CREATE INDEX [IX_finance_bills_company_id_document_id] ON [finance_bills] ([company_id], [document_id]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_seed_anomalies_company_id_anomaly_type' AND [object_id] = OBJECT_ID(N'[finance_seed_anomalies]'))
        BEGIN
            CREATE INDEX [IX_finance_seed_anomalies_company_id_anomaly_type] ON [finance_seed_anomalies] ([company_id], [anomaly_type]);
        END
        """);

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF OBJECT_ID(N'[knowledge_documents]', N'U') IS NOT NULL
           AND EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[finance_transactions]') AND [name] = N'document_id')
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_finance_transactions_knowledge_documents_company_id_document_id')
        BEGIN
            ALTER TABLE [finance_transactions]
            ADD CONSTRAINT [FK_finance_transactions_knowledge_documents_company_id_document_id]
            FOREIGN KEY ([company_id], [document_id]) REFERENCES [knowledge_documents] ([CompanyId], [Id]);
        END
        IF OBJECT_ID(N'[knowledge_documents]', N'U') IS NOT NULL
           AND EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[finance_invoices]') AND [name] = N'document_id')
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_finance_invoices_knowledge_documents_company_id_document_id')
        BEGIN
            ALTER TABLE [finance_invoices]
            ADD CONSTRAINT [FK_finance_invoices_knowledge_documents_company_id_document_id]
            FOREIGN KEY ([company_id], [document_id]) REFERENCES [knowledge_documents] ([CompanyId], [Id]);
        END
        IF OBJECT_ID(N'[knowledge_documents]', N'U') IS NOT NULL
           AND EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[finance_bills]') AND [name] = N'document_id')
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_finance_bills_knowledge_documents_company_id_document_id')
        BEGIN
            ALTER TABLE [finance_bills]
            ADD CONSTRAINT [FK_finance_bills_knowledge_documents_company_id_document_id]
            FOREIGN KEY ([company_id], [document_id]) REFERENCES [knowledge_documents] ([CompanyId], [Id]);
        END
        """);
}

static async Task EnsureSqlServerCompanySimulationSchemaAsync(VirtualCompanyDbContext dbContext, DbConnection connection)
{
    if (!await SqlServerTableExistsAsync(connection, "company_simulation_states"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [company_simulation_states] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_company_simulation_states] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [status] nvarchar(32) NOT NULL,
                [current_simulated_at] datetime2 NOT NULL,
                [last_progressed_at] datetime2 NULL,
                [generation_enabled] bit NOT NULL CONSTRAINT [DF_company_simulation_states_generation_enabled] DEFAULT (1),
                [seed] int NOT NULL,
                [active_session_id] uniqueidentifier NULL,
                [start_simulated_at] datetime2 NOT NULL,
                [deterministic_configuration_json] nvarchar(max) NULL,
                [paused_at] datetime2 NULL,
                [stopped_at] datetime2 NULL,
                [created_at] datetime2 NOT NULL,
                [updated_at] datetime2 NOT NULL,
                CONSTRAINT [CK_company_simulation_states_status] CHECK (status IN ('running', 'paused', 'stopped')),
                CONSTRAINT [CK_company_simulation_states_active_session] CHECK ((status = 'stopped' AND active_session_id IS NULL) OR (status IN ('running', 'paused') AND active_session_id IS NOT NULL)),
                CONSTRAINT [FK_company_simulation_states_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );
            """);
    }

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_company_simulation_states_company_id'
              AND object_id = OBJECT_ID(N'[company_simulation_states]')
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_company_simulation_states_company_id]
            ON [company_simulation_states] ([company_id]);
        END
        """);

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_company_simulation_states_company_id_active_session_id'
              AND object_id = OBJECT_ID(N'[company_simulation_states]')
        )
        BEGIN
            CREATE INDEX [IX_company_simulation_states_company_id_active_session_id]
            ON [company_simulation_states] ([company_id], [active_session_id]);
        END
        """);

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_company_simulation_states_status_last_progressed_at_company_id'
              AND object_id = OBJECT_ID(N'[company_simulation_states]')
        )
        BEGIN
            CREATE INDEX [IX_company_simulation_states_status_last_progressed_at_company_id]
            ON [company_simulation_states] ([status], [last_progressed_at], [company_id]);
        END
        """);

    if (!await SqlServerTableExistsAsync(connection, "company_simulation_run_histories"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [company_simulation_run_histories] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_company_simulation_run_histories] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [session_id] uniqueidentifier NOT NULL,
                [status] nvarchar(32) NOT NULL,
                [started_at] datetime2 NOT NULL,
                [completed_at] datetime2 NULL,
                [start_simulated_at] datetime2 NOT NULL,
                [current_simulated_at] datetime2 NULL,
                [generation_enabled] bit NOT NULL CONSTRAINT [DF_company_simulation_run_histories_generation_enabled] DEFAULT (1),
                [seed] int NOT NULL,
                [deterministic_configuration_json] nvarchar(max) NULL,
                [injected_anomalies_json] nvarchar(max) NOT NULL CONSTRAINT [DF_company_simulation_run_histories_injected_anomalies_json] DEFAULT (N'[]'),
                [warnings_json] nvarchar(max) NOT NULL CONSTRAINT [DF_company_simulation_run_histories_warnings_json] DEFAULT (N'[]'),
                [errors_json] nvarchar(max) NOT NULL CONSTRAINT [DF_company_simulation_run_histories_errors_json] DEFAULT (N'[]'),
                [created_at] datetime2 NOT NULL,
                [updated_at] datetime2 NOT NULL,
                CONSTRAINT [FK_company_simulation_run_histories_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
            );
            """);
    }

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_company_simulation_run_histories_company_id_session_id'
              AND object_id = OBJECT_ID(N'[company_simulation_run_histories]')
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_company_simulation_run_histories_company_id_session_id]
            ON [company_simulation_run_histories] ([company_id], [session_id]);
        END
        """);

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_company_simulation_run_histories_company_id_started_at'
              AND object_id = OBJECT_ID(N'[company_simulation_run_histories]')
        )
        BEGIN
            CREATE INDEX [IX_company_simulation_run_histories_company_id_started_at]
            ON [company_simulation_run_histories] ([company_id], [started_at]);
        END
        """);

    if (!await SqlServerTableExistsAsync(connection, "company_simulation_run_transitions"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [company_simulation_run_transitions] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_company_simulation_run_transitions] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [run_history_id] uniqueidentifier NOT NULL,
                [session_id] uniqueidentifier NOT NULL,
                [status] nvarchar(32) NOT NULL,
                [transitioned_at] datetime2 NOT NULL,
                [message] nvarchar(4000) NULL,
                [created_at] datetime2 NOT NULL,
                CONSTRAINT [FK_company_simulation_run_transitions_company_simulation_run_histories_run_history_id]
                    FOREIGN KEY ([run_history_id]) REFERENCES [company_simulation_run_histories] ([id]) ON DELETE CASCADE
            );
            """);
    }

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_company_simulation_run_transitions_company_id_session_id_transitioned_at'
              AND object_id = OBJECT_ID(N'[company_simulation_run_transitions]')
        )
        BEGIN
            CREATE INDEX [IX_company_simulation_run_transitions_company_id_session_id_transitioned_at]
            ON [company_simulation_run_transitions] ([company_id], [session_id], [transitioned_at]);
        END
        """);

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_company_simulation_run_transitions_run_history_id'
              AND object_id = OBJECT_ID(N'[company_simulation_run_transitions]')
        )
        BEGIN
            CREATE INDEX [IX_company_simulation_run_transitions_run_history_id]
            ON [company_simulation_run_transitions] ([run_history_id]);
        END
        """);

    if (!await SqlServerTableExistsAsync(connection, "company_simulation_run_day_logs"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [company_simulation_run_day_logs] (
                [id] uniqueidentifier NOT NULL CONSTRAINT [PK_company_simulation_run_day_logs] PRIMARY KEY,
                [company_id] uniqueidentifier NOT NULL,
                [run_history_id] uniqueidentifier NOT NULL,
                [session_id] uniqueidentifier NOT NULL,
                [simulated_date_at] datetime2 NOT NULL,
                [transactions_generated] int NOT NULL,
                [invoices_generated] int NOT NULL,
                [bills_generated] int NOT NULL,
                [recurring_expense_instances_generated] int NOT NULL,
                [alerts_generated] int NOT NULL,
                [injected_anomalies_json] nvarchar(max) NOT NULL CONSTRAINT [DF_company_simulation_run_day_logs_injected_anomalies_json] DEFAULT (N'[]'),
                [warnings_json] nvarchar(max) NOT NULL CONSTRAINT [DF_company_simulation_run_day_logs_warnings_json] DEFAULT (N'[]'),
                [errors_json] nvarchar(max) NOT NULL CONSTRAINT [DF_company_simulation_run_day_logs_errors_json] DEFAULT (N'[]'),
                [created_at] datetime2 NOT NULL,
                [updated_at] datetime2 NOT NULL,
                CONSTRAINT [FK_company_simulation_run_day_logs_company_simulation_run_histories_run_history_id]
                    FOREIGN KEY ([run_history_id]) REFERENCES [company_simulation_run_histories] ([id]) ON DELETE CASCADE
            );
            """);
    }

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_company_simulation_run_day_logs_company_id_created_at'
              AND object_id = OBJECT_ID(N'[company_simulation_run_day_logs]')
        )
        BEGIN
            CREATE INDEX [IX_company_simulation_run_day_logs_company_id_created_at]
            ON [company_simulation_run_day_logs] ([company_id], [created_at]);
        END
        """);

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_company_simulation_run_day_logs_company_id_session_id_simulated_date_at'
              AND object_id = OBJECT_ID(N'[company_simulation_run_day_logs]')
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_company_simulation_run_day_logs_company_id_session_id_simulated_date_at]
            ON [company_simulation_run_day_logs] ([company_id], [session_id], [simulated_date_at]);
        END
        """);

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_company_simulation_run_day_logs_run_history_id'
              AND object_id = OBJECT_ID(N'[company_simulation_run_day_logs]')
        )
        BEGIN
            CREATE INDEX [IX_company_simulation_run_day_logs_run_history_id]
            ON [company_simulation_run_day_logs] ([run_history_id]);
        END
        """);
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

static async Task<bool> SqlServerColumnIsNullableAsync(DbConnection connection, string tableName, string columnName)
{
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT c.is_nullable
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
    return result is bool boolResult
        ? boolResult
        : result is byte byteResult && byteResult == 1;
}

static async Task InitializeDatabaseAsync(
    WebApplication app,
    VirtualCompanyDbContext dbContext,
    ILogger logger,
    CompanySetupTemplateSeeder templateSeeder,
    CompanyWorkflowDefinitionSeeder workflowDefinitionSeeder,
    IPlanningBaselineService planningBaselineService,
    bool applyMigrationsOnStartup,
    bool failFastOnPendingMigrations,
    bool stopRunningSimulationSessionsOnStartup)
{
    if (dbContext.Database.IsRelational())
    {
        await WaitForSqlServerReadyAsync(dbContext, logger, app.Lifetime.ApplicationStopping);
        await EnsureSqlServerFinanceSeedSchemaAsync(dbContext);

        if (applyMigrationsOnStartup)
        {
            await dbContext.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
            await EnsureSqlServerFinanceSeedSchemaAsync(dbContext);
            await EnsureSqlServerAgentExecutionSchemaAsync(dbContext);
            await EnsureSqlServerKnowledgeSchemaAsync(dbContext);
            await EnsureSqlServerDirectChatSchemaAsync(dbContext);
            await EnsureSqlServerAuditEventSchemaAsync(dbContext);
            await EnsureSqlServerBriefingSchemaAsync(dbContext);
        }
        else if (failFastOnPendingMigrations)
        {
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(app.Lifetime.ApplicationStopping);
            StartupMigrationValidation.EnsureNoPendingMigrations(pendingMigrations, logger, app.Environment.EnvironmentName);
        }
    }
    else if (app.Environment.IsDevelopment())
    {
        await dbContext.Database.EnsureCreatedAsync(app.Lifetime.ApplicationStopping);
    }

    if (stopRunningSimulationSessionsOnStartup)
    {
        await StopRunningSimulationSessionsAsync(dbContext, logger, app.Lifetime.ApplicationStopping);
    }

    await templateSeeder.SeedAsync();
    await workflowDefinitionSeeder.SeedAsync();
    await planningBaselineService.BackfillAllCompaniesAsync(app.Lifetime.ApplicationStopping);
}

static async Task StopRunningSimulationSessionsAsync(
    VirtualCompanyDbContext dbContext,
    ILogger logger,
    CancellationToken cancellationToken)
{
    var runningStates = await dbContext.CompanySimulationStates
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(x => x.Status == CompanySimulationStatus.Running)
        .Select(x => new
        {
            x.CompanyId,
            x.ActiveSessionId,
            x.CurrentSimulatedUtc
        })
        .ToListAsync(cancellationToken);

    if (runningStates.Count == 0)
    {
        return;
    }

    var stoppedUtc = DateTime.UtcNow;

    foreach (var state in runningStates)
    {
        if (!state.ActiveSessionId.HasValue)
        {
            continue;
        }

        await dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == state.CompanyId &&
                x.ActiveSessionId == state.ActiveSessionId &&
                x.Status == CompanySimulationStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, CompanySimulationStatus.Stopped)
                    .SetProperty(x => x.StoppedUtc, stoppedUtc)
                    .SetProperty(x => x.ActiveSessionId, (Guid?)null)
                    .SetProperty(x => x.PausedUtc, (DateTime?)null)
                    .SetProperty(x => x.UpdatedUtc, stoppedUtc),
                cancellationToken);

        var runHistoryId = await dbContext.CompanySimulationRunHistories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == state.CompanyId && x.SessionId == state.ActiveSessionId.Value)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        await dbContext.CompanySimulationRunHistories
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == state.CompanyId && x.SessionId == state.ActiveSessionId.Value)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, CompanySimulationStatus.Stopped)
                    .SetProperty(x => x.CompletedUtc, stoppedUtc)
                    .SetProperty(x => x.CurrentSimulatedUtc, state.CurrentSimulatedUtc)
                    .SetProperty(x => x.UpdatedUtc, stoppedUtc),
                cancellationToken);

        if (runHistoryId.HasValue)
        {
            await dbContext.CompanySimulationRunTransitions.AddAsync(
                new CompanySimulationRunTransition(
                    Guid.NewGuid(),
                    state.CompanyId,
                    runHistoryId.Value,
                    state.ActiveSessionId.Value,
                    CompanySimulationStatus.Stopped,
                    stoppedUtc,
                    "Simulation stopped during application startup.",
                    stoppedUtc),
                cancellationToken);
        }
    }

    await dbContext.SaveChangesAsync(cancellationToken);

    logger.LogInformation(
        "Stopped {RunningSimulationCount} persisted running simulation session(s) during application startup.",
        runningStates.Count);
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

static bool TryParseFinanceSeedCliCommand(
    string[] args,
    out FinanceSeedBootstrapCommand? command,
    out string? error)
{
    command = null;
    error = null;

    if (args.Length == 0 ||
        !args[0].Equals("seed-finance", StringComparison.OrdinalIgnoreCase) &&
        !args[0].Equals("finance-seed", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    Guid? companyId = null;
    int? seedValue = null;
    DateTime? seedAnchorUtc = null;
    var replaceExisting = true;

    for (var index = 1; index < args.Length; index++)
    {
        var current = args[index];
        switch (current)
        {
            case "--company-id":
                if (!TryReadNext(args, ref index, out var companyIdValue) ||
                    !Guid.TryParse(companyIdValue, out var parsedCompanyId) ||
                    parsedCompanyId == Guid.Empty)
                {
                    error = "seed-finance requires --company-id <guid>.";
                    return true;
                }

                companyId = parsedCompanyId;
                break;

            case "--seed":
            case "--seed-value":
                if (!TryReadNext(args, ref index, out var seedValueText) ||
                    !int.TryParse(seedValueText, out var parsedSeedValue))
                {
                    error = "seed-finance requires --seed <integer>.";
                    return true;
                }

                seedValue = parsedSeedValue;
                break;

            case "--anchor-utc":
            case "--seed-anchor-utc":
                if (!TryReadNext(args, ref index, out var anchorText) ||
                    !DateTime.TryParse(anchorText, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedAnchor))
                {
                    error = "seed-finance requires --anchor-utc <datetime> when an anchor is supplied.";
                    return true;
                }

                seedAnchorUtc = parsedAnchor.Kind == DateTimeKind.Utc
                    ? parsedAnchor
                    : parsedAnchor.ToUniversalTime();
                break;

            case "--append":
                replaceExisting = false;
                break;

            case "--replace":
            case "--replace-existing":
                replaceExisting = true;
                break;

            default:
                error = $"Unknown seed-finance option '{current}'. Usage: seed-finance --company-id <guid> --seed <integer> [--anchor-utc <datetime>] [--replace|--append]";
                return true;
        }
    }

    if (companyId is null)
    {
        error = "seed-finance requires --company-id <guid>.";
        return true;
    }

    if (seedValue is null)
    {
        error = "seed-finance requires --seed <integer>.";
        return true;
    }

    command = new FinanceSeedBootstrapCommand(companyId.Value, seedValue.Value, seedAnchorUtc, replaceExisting);
    return true;
}

static bool TryReadNext(string[] args, ref int index, out string value)
{
    value = string.Empty;
    if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
    {
        return false;
    }

    value = args[++index];
    return true;
}

public partial class Program;
