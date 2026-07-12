BEGIN TRANSACTION;
CREATE TABLE [SalesAcquisitionCampaigns] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Category] nvarchar(64) NOT NULL,
    [Provider] nvarchar(120) NOT NULL,
    [ExternalReference] nvarchar(256) NULL,
    [Budget] decimal(18,2) NULL,
    [Currency] nvarchar(3) NULL,
    [StartsUtc] datetime2 NULL,
    [EndsUtc] datetime2 NULL,
    [Status] nvarchar(32) NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    [UpdatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_SalesAcquisitionCampaigns] PRIMARY KEY ([Id])
);

CREATE TABLE [SalesContactPermissions] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [ContactId] uniqueidentifier NOT NULL,
    [Channel] nvarchar(32) NOT NULL,
    [Address] nvarchar(320) NOT NULL,
    [Status] nvarchar(32) NOT NULL,
    [LegalBasis] nvarchar(64) NOT NULL,
    [SourceReference] nvarchar(512) NOT NULL,
    [ObservedUtc] datetime2 NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    [UpdatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_SalesContactPermissions] PRIMARY KEY ([Id])
);

CREATE TABLE [SalesSourceAttributions] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [SubjectType] nvarchar(40) NOT NULL,
    [SubjectId] uniqueidentifier NOT NULL,
    [OriginalTouchId] uniqueidentifier NOT NULL,
    [FirstTouchId] uniqueidentifier NOT NULL,
    [LastTouchId] uniqueidentifier NOT NULL,
    [ConversionTouchId] uniqueidentifier NULL,
    [TouchCount] int NOT NULL,
    [TotalAcquisitionCost] decimal(18,2) NOT NULL,
    [Currency] nvarchar(3) NULL,
    [CreatedUtc] datetime2 NOT NULL,
    [UpdatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_SalesSourceAttributions] PRIMARY KEY ([Id])
);

CREATE TABLE [SalesSourceTouches] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [CampaignId] uniqueidentifier NULL,
    [SubjectType] nvarchar(40) NOT NULL,
    [SubjectId] uniqueidentifier NOT NULL,
    [Category] nvarchar(64) NOT NULL,
    [Provider] nvarchar(120) NOT NULL,
    [Channel] nvarchar(64) NOT NULL,
    [InteractionType] nvarchar(64) NOT NULL,
    [SourceReference] nvarchar(512) NOT NULL,
    [Evidence] nvarchar(1000) NULL,
    [LandingPage] nvarchar(512) NULL,
    [Referrer] nvarchar(512) NULL,
    [UtmSource] nvarchar(120) NULL,
    [UtmMedium] nvarchar(120) NULL,
    [UtmCampaign] nvarchar(200) NULL,
    [UtmContent] nvarchar(200) NULL,
    [UtmTerm] nvarchar(200) NULL,
    [Cost] decimal(18,2) NULL,
    [Currency] nvarchar(3) NULL,
    [ActorType] nvarchar(32) NOT NULL,
    [ActorReference] nvarchar(160) NULL,
    [MetadataJson] nvarchar(max) NULL,
    [DedupeKey] nvarchar(1000) NOT NULL,
    [ObservedUtc] datetime2 NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_SalesSourceTouches] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SalesSourceTouches_SalesAcquisitionCampaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [SalesAcquisitionCampaigns] ([Id]) ON DELETE SET NULL
);

DECLARE @var sysname;
SELECT @var = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesSourceTouches]') AND [c].[name] = N'DedupeKey');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [SalesSourceTouches] DROP CONSTRAINT [' + @var + '];');
ALTER TABLE [SalesSourceTouches] ALTER COLUMN [DedupeKey] nvarchar(64) NOT NULL;

CREATE INDEX [IX_SalesAcquisitionCampaigns_CompanyId_Name] ON [SalesAcquisitionCampaigns] ([CompanyId], [Name]);

CREATE INDEX [IX_SalesAcquisitionCampaigns_CompanyId_Provider_ExternalReference] ON [SalesAcquisitionCampaigns] ([CompanyId], [Provider], [ExternalReference]);

CREATE UNIQUE INDEX [IX_SalesContactPermissions_CompanyId_ContactId_Channel_Address] ON [SalesContactPermissions] ([CompanyId], [ContactId], [Channel], [Address]);

CREATE UNIQUE INDEX [IX_SalesSourceAttributions_CompanyId_SubjectType_SubjectId] ON [SalesSourceAttributions] ([CompanyId], [SubjectType], [SubjectId]);

CREATE INDEX [IX_SalesSourceTouches_CampaignId] ON [SalesSourceTouches] ([CampaignId]);

CREATE UNIQUE INDEX [IX_SalesSourceTouches_CompanyId_DedupeKey] ON [SalesSourceTouches] ([CompanyId], [DedupeKey]);

CREATE INDEX [IX_SalesSourceTouches_CompanyId_SubjectType_SubjectId_ObservedUtc] ON [SalesSourceTouches] ([CompanyId], [SubjectType], [SubjectId], [ObservedUtc]);

UPDATE agent_templates SET tool_permissions_json = N'{"allowed":["sales.plan_prospecting_run","sales.start_prospecting_run","sales.list_prospects","sales.research_prospect","sales.recommend_prospect_decision"],"actions":["read","recommend","execute"],"denied":[],"deniedActions":[]}', data_scopes_json = N'{"read":["sales","prospecting"],"recommend":["sales","prospecting"],"execute":["sales","prospecting"],"write":[]}' WHERE [TemplateId] = N'sales';
UPDATE agents SET tool_permissions_json = N'{"allowed":["sales.plan_prospecting_run","sales.start_prospecting_run","sales.list_prospects","sales.research_prospect","sales.recommend_prospect_decision"],"actions":["read","recommend","execute"],"denied":[],"deniedActions":[]}', data_scopes_json = N'{"read":["sales","prospecting"],"recommend":["sales","prospecting"],"execute":["sales","prospecting"],"write":[]}' WHERE [TemplateId] = N'sales';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260712160431_AddSalesSourceAttribution', N'9.0.14');

COMMIT;
GO

