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