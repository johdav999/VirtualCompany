# Repository-local one-time refactor; preserves the native typed validation boundary.
$commandPath = 'src/VirtualCompany.Infrastructure.Finance/Finance/CompanyFinanceCommandService.Planning.cs'
$command = Get-Content $commandPath -Raw
$start = $command.IndexOf('    private async Task ValidatePlanningDimensionAsync(')
$end = $command.IndexOf('    private static bool IsDuplicatePlanningEntry(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Native dimension validation boundary not found.' }
$replacement = @'
    private async Task ValidatePlanningDimensionAsync(Guid companyId, Guid financeAccountId, Guid? costCenterId,
        DateOnly effectiveDate, CancellationToken cancellationToken)
    {
        try { await FinancePlanningDimensionValidator.ValidateAsync(_dbContext, companyId, financeAccountId, costCenterId, effectiveDate, cancellationToken); }
        catch (ArgumentException ex) { throw CreateValidationException(nameof(FinancePlanningEntryUpsertDto.CostCenterId), ex.Message); }
    }

'@
Set-Content $commandPath ($command.Substring(0, $start) + $replacement + $command.Substring($end))
$servicePath = 'src/VirtualCompany.Infrastructure.Finance/Finance/FinanceRollingPlanningService.Forecasts.cs'
$service = Get-Content $servicePath -Raw
$start = $service.IndexOf('    private async Task ValidateDimension(')
$end = $service.IndexOf('    private static FinanceForecastRevisionSummary Summary(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Forecast dimension validation boundary not found.' }
$service = $service.Substring(0, $start) + $service.Substring($end)
$service = $service.Replace('await ValidateDimension(company, account.Id, a.CostCenterId, DateOnly.FromDateTime(a.MonthUtc), ct);', 'await FinancePlanningDimensionValidator.ValidateAsync(db, company, account.Id, a.CostCenterId, DateOnly.FromDateTime(a.MonthUtc), ct);')
Set-Content $servicePath $service
