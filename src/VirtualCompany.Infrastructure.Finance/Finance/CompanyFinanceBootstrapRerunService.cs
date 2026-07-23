using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceBootstrapRerunService : IFinanceBootstrapRerunService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IPlanningBaselineService _planningBaselineService;
    private readonly IFinanceApprovalTaskService _financeApprovalTaskService;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ILogger<CompanyFinanceBootstrapRerunService> _logger;

    public CompanyFinanceBootstrapRerunService(
        VirtualCompanyDbContext dbContext,
        IPlanningBaselineService planningBaselineService,
        IFinanceApprovalTaskService financeApprovalTaskService,
        ICompanyContextAccessor? companyContextAccessor,
        ILogger<CompanyFinanceBootstrapRerunService> logger)
    {
        _dbContext = dbContext;
        _planningBaselineService = planningBaselineService;
        _financeApprovalTaskService = financeApprovalTaskService;
        _companyContextAccessor = companyContextAccessor;
        _logger = logger;
    }

    public async Task<FinanceBootstrapRerunResultDto> RerunAsync(
        RerunFinanceBootstrapCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);

        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? $"finance-bootstrap-rerun:{command.CompanyId:N}"
            : command.CorrelationId.Trim();

        if (!command.RerunPlanningBackfill && !command.RerunApprovalBackfill)
        {
            throw new ArgumentException("At least one finance bootstrap rerun operation must be enabled.", nameof(command));
        }

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == command.CompanyId, cancellationToken);
        if (company is null)
        {
            throw new KeyNotFoundException($"Company '{command.CompanyId}' was not found.");
        }

        FinanceBootstrapRerunResultDto result;
        var strategy = _dbContext.Database.IsRelational()
            ? _dbContext.Database.CreateExecutionStrategy()
            : null;

        if (strategy is null)
        {
            result = await ExecuteInternalAsync(command, correlationId, cancellationToken);
        }
        else
        {
            result = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                var rerunResult = await ExecuteInternalAsync(command, correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return rerunResult;
            });
        }

        using var scope = _logger.BeginScope(ExecutionLogScope.ForBackground(correlationId, command.CompanyId));
        _logger.LogInformation(
            "Completed finance bootstrap rerun for company {CompanyId}. PlanningBackfillRan={PlanningBackfillRan} PlanningRowsInserted={PlanningRowsInserted} ApprovalBackfillRan={ApprovalBackfillRan} ApprovalTasksCreated={ApprovalTasksCreated} ApprovalTasksSkipped={ApprovalTasksSkipped}.",
            command.CompanyId,
            result.PlanningBackfillRan,
            result.PlanningRowsInserted,
            result.ApprovalBackfillRan,
            result.ApprovalBackfill.CreatedCount,
            result.ApprovalBackfill.SkippedExistingCount);
        return result;
    }

    private async Task<FinanceBootstrapRerunResultDto> ExecuteInternalAsync(
        RerunFinanceBootstrapCommand command,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var planningRowsInserted = 0;
        if (command.RerunPlanningBackfill)
        {
            planningRowsInserted = await _planningBaselineService.EnsureBaselineAsync(command.CompanyId, cancellationToken);
        }

        var approvalBackfill = command.RerunApprovalBackfill
            ? await _financeApprovalTaskService.BackfillApprovalTasksAsync(
                new BackfillFinanceApprovalTasksCommand(command.CompanyId, command.BatchSize, correlationId, IncludePayments: true),
                cancellationToken)
            : CreateSkippedApprovalBackfillResult(command.CompanyId, correlationId);

        var seedState = await _dbContext.Companies
            .IgnoreQueryFilters()
            .Where(x => x.Id == command.CompanyId)
            .Select(x => x.FinanceSeedStatus)
            .SingleAsync(cancellationToken);
        var completedAtUtc = DateTime.UtcNow;
        var summary = BuildSummary(command, planningRowsInserted, approvalBackfill);

        return new FinanceBootstrapRerunResultDto(
            command.CompanyId,
            correlationId,
            seedState,
            command.RerunPlanningBackfill,
            command.RerunApprovalBackfill,
            planningRowsInserted,
            approvalBackfill,
            completedAtUtc,
            summary);
    }

    private static FinanceApprovalTaskBackfillResultDto CreateSkippedApprovalBackfillResult(Guid companyId, string correlationId) =>
        new(companyId, correlationId, 0, 0, 0, 0, 0, 0, 0, 0);

    private static string BuildSummary(RerunFinanceBootstrapCommand command, int planningRowsInserted, FinanceApprovalTaskBackfillResultDto approvalBackfill) =>
        $"Planning backfill {(command.RerunPlanningBackfill ? "ran" : "skipped")} and inserted {planningRowsInserted} rows; approval backfill {(command.RerunApprovalBackfill ? "ran" : "skipped")} and created {approvalBackfill.CreatedCount} tasks while skipping {approvalBackfill.SkippedExistingCount} existing tasks.";

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid scopedCompanyId && scopedCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Finance bootstrap reruns are scoped to the active company context.");
        }
    }
}
