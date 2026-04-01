namespace VirtualCompany.Domain.Enums;

public enum CompanyKnowledgeDocumentType
{
    General = 1,
    Policy = 2,
    Procedure = 3,
    Reference = 4,
    Report = 5
}

public enum CompanyKnowledgeDocumentSourceType
{
    Upload = 1
}

public enum CompanyKnowledgeDocumentIngestionStatus
{
    Uploaded = 1,
    PendingScan = 2,
    ScanClean = 3,
    Processing = 4,
    Processed = 5,
    Blocked = 6,
    Failed = 7
}

public static class CompanyKnowledgeDocumentTypeValues
{
    public const string General = "general";
    public const string Policy = "policy";
    public const string Procedure = "procedure";
    public const string Reference = "reference";
    public const string Report = "report";

    private static readonly IReadOnlyDictionary<CompanyKnowledgeDocumentType, string> Values = new Dictionary<CompanyKnowledgeDocumentType, string>
    {
        [CompanyKnowledgeDocumentType.General] = General,
        [CompanyKnowledgeDocumentType.Policy] = Policy,
        [CompanyKnowledgeDocumentType.Procedure] = Procedure,
        [CompanyKnowledgeDocumentType.Reference] = Reference,
        [CompanyKnowledgeDocumentType.Report] = Report
    };

    private static readonly IReadOnlyDictionary<string, CompanyKnowledgeDocumentType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this CompanyKnowledgeDocumentType value) =>
        Values.TryGetValue(value, out var storageValue)
            ? storageValue
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage());

    public static bool TryParse(string? value, out CompanyKnowledgeDocumentType documentType)
    {
        documentType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (ReverseValues.TryGetValue(value.Trim(), out documentType))
        {
            return true;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out documentType) && Values.ContainsKey(documentType);
    }

    public static CompanyKnowledgeDocumentType Parse(string value)
    {
        if (TryParse(value, out var documentType))
        {
            return documentType;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));
    }

    public static void EnsureSupported(CompanyKnowledgeDocumentType value, string paramName)
    {
        _ = value.ToStorageValue();
    }

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Document type is required. Allowed values: {allowedValues}."
            : $"Unsupported document type '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}

public static class CompanyKnowledgeDocumentSourceTypeValues
{
    public const string Upload = "upload";

    private static readonly IReadOnlyDictionary<CompanyKnowledgeDocumentSourceType, string> Values = new Dictionary<CompanyKnowledgeDocumentSourceType, string>
    {
        [CompanyKnowledgeDocumentSourceType.Upload] = Upload
    };

    private static readonly IReadOnlyDictionary<string, CompanyKnowledgeDocumentSourceType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this CompanyKnowledgeDocumentSourceType value) =>
        Values.TryGetValue(value, out var storageValue)
            ? storageValue
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported document source type.");

    public static CompanyKnowledgeDocumentSourceType Parse(string value) =>
        ReverseValues.TryGetValue(value.Trim(), out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported document source type.");
}

public static class CompanyKnowledgeDocumentIngestionStatusValues
{
    public const string Uploaded = "uploaded";
    public const string PendingScan = "pending_scan";
    public const string ScanClean = "scan_clean";
    public const string Processing = "processing";
    public const string Processed = "processed";
    public const string Blocked = "blocked";
    public const string Failed = "failed";

    private static readonly IReadOnlyDictionary<CompanyKnowledgeDocumentIngestionStatus, string> Values =
        new Dictionary<CompanyKnowledgeDocumentIngestionStatus, string>
        {
            [CompanyKnowledgeDocumentIngestionStatus.Uploaded] = Uploaded,
            [CompanyKnowledgeDocumentIngestionStatus.PendingScan] = PendingScan,
            [CompanyKnowledgeDocumentIngestionStatus.ScanClean] = ScanClean,
            [CompanyKnowledgeDocumentIngestionStatus.Processing] = Processing,
            [CompanyKnowledgeDocumentIngestionStatus.Processed] = Processed,
            [CompanyKnowledgeDocumentIngestionStatus.Blocked] = Blocked,
            [CompanyKnowledgeDocumentIngestionStatus.Failed] = Failed
        };

    private static readonly IReadOnlyDictionary<string, CompanyKnowledgeDocumentIngestionStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, CompanyKnowledgeDocumentIngestionStatus> LegacyAliases =
        new Dictionary<string, CompanyKnowledgeDocumentIngestionStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["unsupported"] = CompanyKnowledgeDocumentIngestionStatus.Failed
            ,["queued_for_processing"] = CompanyKnowledgeDocumentIngestionStatus.ScanClean
        };

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this CompanyKnowledgeDocumentIngestionStatus value) =>
        Values.TryGetValue(value, out var storageValue)
            ? storageValue
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage());

    public static bool TryParse(string? value, out CompanyKnowledgeDocumentIngestionStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (LegacyAliases.TryGetValue(value.Trim(), out status))
        {
            return true;
        }

        if (ReverseValues.TryGetValue(value.Trim(), out status))
        {
            return true;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out status) && Values.ContainsKey(status);
    }

    public static CompanyKnowledgeDocumentIngestionStatus Parse(string value)
    {
        if (TryParse(value, out var status))
        {
            return status;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));
    }

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Document ingestion status is required. Allowed values: {allowedValues}."
            : $"Unsupported document ingestion status '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}