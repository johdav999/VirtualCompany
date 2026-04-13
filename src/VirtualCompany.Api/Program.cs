using System.Text.Json;
using System.Text.Json.Serialization;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using VirtualCompany.Infrastructure;
using VirtualCompany.Infrastructure.Authorization;
using VirtualCompany.Infrastructure.Persistence;
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
    if (dbContext.Database.IsRelational())
    {
        if (applyMigrationsOnStartup)
        {
            await dbContext.Database.MigrateAsync();
            await EnsureSqlServerAgentExecutionSchemaAsync(dbContext);
            await EnsureSqlServerKnowledgeSchemaAsync(dbContext);
        }
        else
        {
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
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
        await dbContext.Database.EnsureCreatedAsync();
    }

    await templateSeeder.SeedAsync();
    await workflowDefinitionSeeder.SeedAsync();
}

app.MapVirtualCompanyHealthEndpoints();
app.MapControllers();

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
                    CONSTRAINT [DF_agents_trigger_logic_json_startup] DEFAULT (N'{{}}');
                """);
        }

        if (!await SqlServerColumnExistsAsync(connection, "agents", "working_hours_json"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [agents]
                ADD [working_hours_json] nvarchar(max) NOT NULL
                    CONSTRAINT [DF_agents_working_hours_json_startup] DEFAULT (N'{{}}');
                """);
        }

        if (!await SqlServerTableExistsAsync(connection, "tool_execution_attempts"))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE [tool_execution_attempts] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_tool_execution_attempts] PRIMARY KEY,
                    [CompanyId] uniqueidentifier NOT NULL,
                    [AgentId] uniqueidentifier NOT NULL,
                    [ToolName] nvarchar(100) NOT NULL,
                    [ActionType] nvarchar(32) NOT NULL,
                    [Scope] nvarchar(100) NULL,
                    [Status] nvarchar(32) NOT NULL,
                    [ApprovalRequestId] uniqueidentifier NULL,
                    [request_payload_json] nvarchar(max) NOT NULL CONSTRAINT [DF_tool_execution_attempts_request_payload_json] DEFAULT (N'{{}}'),
                    [policy_decision_json] nvarchar(max) NOT NULL CONSTRAINT [DF_tool_execution_attempts_policy_decision_json] DEFAULT (N'{{}}'),
                    [result_payload_json] nvarchar(max) NOT NULL CONSTRAINT [DF_tool_execution_attempts_result_payload_json] DEFAULT (N'{{}}'),
                    [CreatedUtc] datetime2 NOT NULL,
                    [UpdatedUtc] datetime2 NOT NULL,
                    [ExecutedUtc] datetime2 NULL,
                    CONSTRAINT [FK_tool_execution_attempts_companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
                );
                """);

            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_tool_execution_attempts_CompanyId_AgentId_CreatedUtc] ON [tool_execution_attempts] ([CompanyId], [AgentId], [CreatedUtc]);""");
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX [IX_tool_execution_attempts_CompanyId_Status_CreatedUtc] ON [tool_execution_attempts] ([CompanyId], [Status], [CreatedUtc]);""");
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
                    [threshold_context_json] nvarchar(max) NOT NULL CONSTRAINT [DF_approval_requests_threshold_context_json] DEFAULT (N'{{}}'),
                    [policy_decision_json] nvarchar(max) NOT NULL CONSTRAINT [DF_approval_requests_policy_decision_json] DEFAULT (N'{{}}'),
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
                    CONSTRAINT [DF_approval_requests_decision_chain_json_startup] DEFAULT (N'{}');
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
                [metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_knowledge_documents_metadata_json] DEFAULT (N'{{}}'),
                [access_scope_json] nvarchar(max) NOT NULL CONSTRAINT [DF_knowledge_documents_access_scope_json] DEFAULT (N'{{}}'),
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
                [metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_knowledge_chunks_metadata_json] DEFAULT (N'{{}}'),
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

public partial class Program;
