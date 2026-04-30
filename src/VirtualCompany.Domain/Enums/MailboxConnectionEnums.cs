namespace VirtualCompany.Domain.Enums;

public enum MailboxProvider
{
    Gmail = 1,
    Microsoft365 = 2
}

public enum MailboxConnectionStatus
{
    Pending = 1,
    Active = 2,
    TokenExpired = 3,
    Revoked = 4,
    Failed = 5,
    Disconnected = 6
}

public enum MailboxFolderSelectionMode
{
    Include = 1,
    Exclude = 2
}

public enum EmailCandidateDecision
{
    Candidate = 1,
    NotCandidate = 2
}

public enum BillSourceType
{
    PdfAttachment = 1,
    DocxAttachment = 2,
    EmailBodyOnly = 3
}

public enum BillDetectionRuleMatch
{
    SenderMatch = 1,
    FolderMatch = 2,
    KeywordMatch = 3,
    AttachmentPresent = 4
}

public static class MailboxProviderValues
{
    public const string Gmail = "gmail";
    public const string Microsoft365 = "microsoft365";

    private static readonly string[] AllowedStorageValues =
    [
        Gmail,
        Microsoft365
    ];

    public static IReadOnlyList<string> AllowedValues => AllowedStorageValues;

    public static string ToStorageValue(this MailboxProvider provider) =>
        provider switch
        {
            MailboxProvider.Gmail => Gmail,
            MailboxProvider.Microsoft365 => Microsoft365,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), "Unsupported mailbox provider.")
        };

    public static MailboxProvider Parse(string value) =>
        Normalize(value) switch
        {
            Gmail => MailboxProvider.Gmail,
            Microsoft365 => MailboxProvider.Microsoft365,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported mailbox provider.")
        };

    public static void EnsureSupported(MailboxProvider provider, string parameterName)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Unsupported mailbox provider.");
        }
    }

    public static string BuildCheckConstraintSql(string columnName) => BuildCheckConstraintSql(columnName, AllowedValues);

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();

    private static string BuildCheckConstraintSql(string columnName, IReadOnlyList<string> allowedValues)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name is required.", nameof(columnName));
        }

        return $"{columnName} IN ({string.Join(", ", allowedValues.Select(value => $"'{value}'"))})";
    }
}

public static class MailboxConnectionStatusValues
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string TokenExpired = "token_expired";
    public const string Revoked = "revoked";
    public const string Failed = "failed";
    public const string Disconnected = "disconnected";

    private static readonly string[] AllowedStorageValues =
    [
        Pending,
        Active,
        TokenExpired,
        Revoked,
        Failed,
        Disconnected
    ];

    public static IReadOnlyList<string> AllowedValues => AllowedStorageValues;

    public static string ToStorageValue(this MailboxConnectionStatus status) =>
        status switch
        {
            MailboxConnectionStatus.Pending => Pending,
            MailboxConnectionStatus.Active => Active,
            MailboxConnectionStatus.TokenExpired => TokenExpired,
            MailboxConnectionStatus.Revoked => Revoked,
            MailboxConnectionStatus.Failed => Failed,
            MailboxConnectionStatus.Disconnected => Disconnected,
            _ => throw new ArgumentOutOfRangeException(nameof(status), "Unsupported mailbox connection status.")
        };

    public static MailboxConnectionStatus Parse(string value) =>
        Normalize(value) switch
        {
            Pending => MailboxConnectionStatus.Pending,
            Active => MailboxConnectionStatus.Active,
            TokenExpired => MailboxConnectionStatus.TokenExpired,
            Revoked => MailboxConnectionStatus.Revoked,
            Failed => MailboxConnectionStatus.Failed,
            Disconnected => MailboxConnectionStatus.Disconnected,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported mailbox connection status.")
        };

    public static void EnsureSupported(MailboxConnectionStatus status, string parameterName)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Unsupported mailbox connection status.");
        }
    }

    public static string BuildCheckConstraintSql(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name is required.", nameof(columnName));
        }

        return $"{columnName} IN ({string.Join(", ", AllowedValues.Select(value => $"'{value}'"))})";
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
}

public static class MailboxFolderSelectionModeValues
{
    public static void EnsureSupported(MailboxFolderSelectionMode mode, string parameterName)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Unsupported mailbox folder selection mode.");
        }
    }
}

public static class EmailCandidateDecisionValues
{
    public const string Candidate = "candidate";
    public const string NotCandidate = "not_candidate";

    private static readonly string[] AllowedStorageValues =
    [
        Candidate,
        NotCandidate
    ];

    public static IReadOnlyList<string> AllowedValues => AllowedStorageValues;

    public static string ToStorageValue(this EmailCandidateDecision decision) =>
        decision switch
        {
            EmailCandidateDecision.Candidate => Candidate,
            EmailCandidateDecision.NotCandidate => NotCandidate,
            _ => throw new ArgumentOutOfRangeException(nameof(decision), "Unsupported email candidate decision.")
        };

    public static EmailCandidateDecision Parse(string value) =>
        Normalize(value) switch
        {
            Candidate => EmailCandidateDecision.Candidate,
            NotCandidate => EmailCandidateDecision.NotCandidate,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported email candidate decision.")
        };

    public static void EnsureSupported(EmailCandidateDecision decision, string parameterName)
    {
        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Unsupported email candidate decision.");
        }
    }

    public static string BuildCheckConstraintSql(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name is required.", nameof(columnName));
        }

        return $"{columnName} IN ({string.Join(", ", AllowedValues.Select(value => $"'{value}'"))})";
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
}

public static class BillSourceTypeValues
{
    public const string PdfAttachment = "pdf_attachment";
    public const string DocxAttachment = "docx_attachment";
    public const string EmailBodyOnly = "email_body_only";

    private static readonly string[] AllowedStorageValues =
    [
        PdfAttachment,
        DocxAttachment,
        EmailBodyOnly
    ];

    public static IReadOnlyList<string> AllowedValues => AllowedStorageValues;

    public static string ToStorageValue(this BillSourceType sourceType) =>
        sourceType switch
        {
            BillSourceType.PdfAttachment => PdfAttachment,
            BillSourceType.DocxAttachment => DocxAttachment,
            BillSourceType.EmailBodyOnly => EmailBodyOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType), "Unsupported bill source type.")
        };

    public static BillSourceType Parse(string value) =>
        Normalize(value) switch
        {
            PdfAttachment => BillSourceType.PdfAttachment,
            DocxAttachment => BillSourceType.DocxAttachment,
            EmailBodyOnly => BillSourceType.EmailBodyOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported bill source type.")
        };

    public static void EnsureSupported(BillSourceType sourceType, string parameterName)
    {
        if (!Enum.IsDefined(sourceType))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Unsupported bill source type.");
        }
    }

    public static string BuildCheckConstraintSql(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name is required.", nameof(columnName));
        }

        return $"{columnName} IN ({string.Join(", ", AllowedValues.Select(value => $"'{value}'"))})";
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
}

public static class BillDetectionRuleMatchValues
{
    public const string SenderMatch = "sender_match";
    public const string FolderMatch = "folder_match";
    public const string KeywordMatch = "keyword_match";
    public const string AttachmentPresent = "attachment_present";

    public static string ToStorageValue(this BillDetectionRuleMatch match) =>
        match switch
        {
            BillDetectionRuleMatch.SenderMatch => SenderMatch,
            BillDetectionRuleMatch.FolderMatch => FolderMatch,
            BillDetectionRuleMatch.KeywordMatch => KeywordMatch,
            BillDetectionRuleMatch.AttachmentPresent => AttachmentPresent,
            _ => throw new ArgumentOutOfRangeException(nameof(match), "Unsupported bill detection rule match.")
        };

    public static BillDetectionRuleMatch Parse(string value) =>
        Normalize(value) switch
        {
            SenderMatch => BillDetectionRuleMatch.SenderMatch,
            FolderMatch => BillDetectionRuleMatch.FolderMatch,
            KeywordMatch => BillDetectionRuleMatch.KeywordMatch,
            AttachmentPresent => BillDetectionRuleMatch.AttachmentPresent,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported bill detection rule match.")
        };

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
}
