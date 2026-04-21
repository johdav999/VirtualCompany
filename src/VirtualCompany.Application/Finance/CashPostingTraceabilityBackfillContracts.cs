namespace VirtualCompany.Application.Finance;

public sealed record BackfillCashPostingTraceabilityCommand(
    Guid CompanyId,
    int BatchSize = 250,
    string? CorrelationId = null);

public sealed record CashPostingTraceabilityBackfillResultDto(
    Guid CompanyId,
    string CorrelationId,
    int MigratedRecordCount,
    int BackfilledRecordCount,
    int SkippedRecordCount,
    int ConflictCount);

public interface ICashPostingTraceabilityBackfillService
{
    Task<CashPostingTraceabilityBackfillResultDto> BackfillAsync(BackfillCashPostingTraceabilityCommand command, CancellationToken cancellationToken);
}