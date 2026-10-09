namespace VirtualCompany.Application.Cockpit;

public sealed record MonthlyReviewMeasureDto(string Key, string Label, decimal? Actual, decimal? Prior,
    string? Unit, string Definition, string Kind, string SourceType, string SourceLink,
    decimal? Target, Guid? TargetId, int? TargetVersion, string TargetAvailability,
    decimal? Change, decimal? TargetVariance, string Explanation);
public sealed record MonthlyReviewDto(string CalculationVersion, IReadOnlyList<MonthlyReviewMeasureDto> Measures,
    string Readiness, string CloseReadiness, string HistoryLimitation,
    IReadOnlyList<Guid>? WorkSourceIds = null);
public sealed record SaveMonthlyReviewCommand(string Lens, int Year, int Month, Guid RequestId,
    string? ReviewNotes = null);
public sealed record RefreshMonthlyReviewCommand(int ExpectedRevision, Guid RequestId, string? ReviewNotes = null);
public sealed record MonthlyReviewSnapshotSummaryDto(Guid Id, Guid CompanyId, Guid SeriesId, int Revision,
    Guid? PreviousId, string Lens, int Year, int Month, DateTime AsOfUtc, DateTime SavedAtUtc,
    string Coverage, string CalculationVersion);
public sealed record MonthlyReviewSnapshotDto(MonthlyReviewSnapshotSummaryDto Summary, MonthlyWorkspaceDto Workspace,
    string ReviewNotes, IReadOnlyList<string> Changes, string Checksum, string Retention);
public sealed record MonthlyReviewHistoryDto(IReadOnlyList<MonthlyReviewSnapshotSummaryDto> Items,
    int Skip, int Take, bool HasMore);
public sealed record MonthlyReviewExportDto(string FileName, string Csv);
public sealed class MonthlyReviewConflictException(string message) : Exception(message);
public sealed class MonthlyReviewReproductionException(string message) : Exception(message);

// Open/export are explicit reproduction commands so failed integrity checks can persist business audit evidence.
public interface IMonthlyReviewSnapshotService
{
    Task<MonthlyReviewHistoryDto> ListAsync(Guid companyId, string lens, int year, int month, int skip, int take, CancellationToken ct);
    Task<MonthlyReviewSnapshotDto> SaveAsync(Guid companyId, SaveMonthlyReviewCommand command, CancellationToken ct);
    Task<MonthlyReviewSnapshotDto> OpenAsync(Guid companyId, Guid id, CancellationToken ct);
    Task<MonthlyReviewSnapshotDto> RefreshAsync(Guid companyId, Guid id, RefreshMonthlyReviewCommand command, CancellationToken ct);
    Task<MonthlyReviewExportDto> ExportAsync(Guid companyId, Guid id, CancellationToken ct);
}
