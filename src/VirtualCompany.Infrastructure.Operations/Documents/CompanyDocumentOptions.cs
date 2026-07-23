namespace VirtualCompany.Infrastructure.Documents;

public sealed class CompanyDocumentOptions
{
    public const string SectionName = "CompanyDocuments";

    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
    public CompanyDocumentStorageOptions Storage { get; set; } = new();
}

public sealed class CompanyDocumentStorageOptions
{
    public string RootPath { get; set; } = "App_Data/object-storage";
    public string? BaseUri { get; set; }
}

public sealed class KnowledgeChunkingOptions
{
    public const string SectionName = "KnowledgeChunking";

    public int TargetChunkLength { get; set; } = 1200;
    public int OverlapLength { get; set; } = 200;
    public int MaxChunkCountPerDocument { get; set; } = 256;
    public string StrategyVersion { get; set; } = "paragraph-overlap-v1";
}

public sealed class KnowledgeEmbeddingOptions
{
    public const string SectionName = "KnowledgeEmbeddings";

    public string Provider { get; set; } = "deterministic";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "text-embedding-3-small";
    // Persist a deployment label or provider-specific version tag when no immutable model revision is returned by the API.
    public string? ModelVersion { get; set; } = "deterministic-v1";
    public int Dimensions { get; set; } = 256;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class KnowledgeIndexingOptions
{
    public const string SectionName = "KnowledgeIndexing";

    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 5;
    public int PollIntervalSeconds { get; set; } = 10;
    public int ClaimTimeoutSeconds { get; set; } = 300;
    public int MaxAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
}