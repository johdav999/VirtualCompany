using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/statement-imports")]
public sealed class BankStatementImportsController : ControllerBase
{
    private readonly IBankStatementImportCenterService _service;
    private readonly ICompanyContextAccessor _companyContext;
    public BankStatementImportsController(IBankStatementImportCenterService service, ICompanyContextAccessor companyContext)
    { _service = service; _companyContext = companyContext; }

    [HttpGet]
    public Task<BankStatementImportWorkspaceDto> GetAsync(Guid companyId, CancellationToken cancellationToken) =>
        _service.GetWorkspaceAsync(companyId, cancellationToken);

    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<BankStatementImportJobDto>> GetJobAsync(Guid companyId, Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await _service.GetJobAsync(companyId, jobId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    public async Task<ActionResult<BankStatementImportJobDto>> PreviewAsync(Guid companyId,
        [FromForm] PreviewBankStatementImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
            return ProblemFor(new BankStatementImportOperationException(BankStatementImportReasonCodes.MalformedFile,
                "Select a non-empty CAMT, PAIN.002, or CSV file."));
        try
        {
            await using var stream = request.File.OpenReadStream();
            return Ok(await _service.PreviewAsync(new PreviewBankStatementImportCommand(companyId,
                request.BankAccountId, request.File.FileName, request.File.ContentType, request.File.Length, stream,
                request.CsvMappingProfileId, request.CsvMappingProfileVersion, UserId(), CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsHandled(exception)) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{jobId:guid}/commit")]
    public async Task<ActionResult<BankStatementImportJobDto>> CommitAsync(Guid companyId, Guid jobId,
        CommitBankStatementImportRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await _service.CommitAsync(new(companyId, jobId, request.ExpectedVersion,
            UserId(), CorrelationId()), cancellationToken)); }
        catch (Exception exception) when (IsHandled(exception)) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{jobId:guid}/rows/{rowId:guid}/decision")]
    public async Task<ActionResult<BankStatementImportJobDto>> DecideAsync(Guid companyId, Guid jobId, Guid rowId,
        DecideBankStatementImportRowRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await _service.DecideConflictAsync(new(companyId, jobId, rowId, request.ExpectedVersion,
            request.Decision, request.Reason, UserId(), CorrelationId()), cancellationToken)); }
        catch (Exception exception) when (IsHandled(exception)) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("csv-profiles")]
    public async Task<ActionResult<BankStatementCsvMappingProfileDto>> CreateCsvProfileAsync(Guid companyId,
        CreateBankStatementCsvMappingProfileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var delimiter = request.Delimiter switch { "tab" => '\t', { Length: 1 } value => value[0], _ => '\0' };
            return Ok(await _service.CreateCsvProfileAsync(new(companyId, request.Name, delimiter,
                request.CultureName, request.DateFormat, request.HasHeader, request.BookingDateColumn,
                request.ValueDateColumn, request.AmountColumn, request.DebitColumn, request.CreditColumn,
                request.CurrencyColumn, request.ReferenceColumn, request.CounterpartyColumn,
                request.ExternalReferenceColumn, request.AccountIdentifierColumn, request.DefaultCurrency,
                UserId(), CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsHandled(exception)) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("csv-profiles/{profileId:guid}/versions")]
    public async Task<ActionResult<BankStatementCsvMappingProfileDto>> CreateCsvProfileVersionAsync(Guid companyId,
        Guid profileId, CreateBankStatementCsvMappingProfileVersionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var delimiter = request.Delimiter switch { "tab" => '\t', { Length: 1 } value => value[0], _ => '\0' };
            return Ok(await _service.CreateCsvProfileVersionAsync(new(companyId, profileId, request.ExpectedCurrentVersion,
                delimiter, request.CultureName, request.DateFormat, request.HasHeader, request.BookingDateColumn,
                request.ValueDateColumn, request.AmountColumn, request.DebitColumn, request.CreditColumn,
                request.CurrencyColumn, request.ReferenceColumn, request.CounterpartyColumn,
                request.ExternalReferenceColumn, request.AccountIdentifierColumn, request.DefaultCurrency,
                UserId(), CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsHandled(exception)) { return ProblemFor(exception); }
    }

    private Guid UserId() => _companyContext.UserId is { } id && id != Guid.Empty ? id :
        throw new UnauthorizedAccessException("A resolved user is required for statement imports.");
    private string CorrelationId() => Request.Headers.TryGetValue("X-Correlation-Id", out var value) ?
        value.ToString() : HttpContext.TraceIdentifier;
    private static bool IsHandled(Exception exception) => exception is BankStatementImportOperationException or
        ArgumentException or InvalidOperationException or KeyNotFoundException or DbUpdateConcurrencyException;
    private ActionResult ProblemFor(Exception exception)
    {
        var operation = exception as BankStatementImportOperationException;
        var reason = operation?.ReasonCode ?? (exception is InvalidOperationException or DbUpdateConcurrencyException
            ? BankStatementImportReasonCodes.VersionConflict : "invalid_request");
        var status = exception is KeyNotFoundException ? StatusCodes.Status404NotFound :
            operation?.IsConflict == true || reason == BankStatementImportReasonCodes.VersionConflict ? StatusCodes.Status409Conflict :
            reason == BankStatementImportReasonCodes.ScanUnavailable ? StatusCodes.Status503ServiceUnavailable :
            reason == BankStatementImportReasonCodes.MalwareBlocked ? StatusCodes.Status422UnprocessableEntity :
            StatusCodes.Status400BadRequest;
        var details = new ProblemDetails { Title = "Statement import action could not be completed",
            Detail = operation?.SafeMessage ?? exception.Message, Status = status, Instance = HttpContext.Request.Path };
        details.Extensions["reasonCode"] = reason;
        return StatusCode(status, details);
    }
}

public sealed class PreviewBankStatementImportRequest
{
    public Guid BankAccountId { get; init; }
    public Guid? CsvMappingProfileId { get; init; }
    public int? CsvMappingProfileVersion { get; init; }
    public IFormFile? File { get; init; }
}
public sealed record CommitBankStatementImportRequest(long ExpectedVersion);
public sealed record DecideBankStatementImportRowRequest(long ExpectedVersion, string Decision, string Reason);
public sealed record CreateBankStatementCsvMappingProfileRequest(string Name, string Delimiter,
    string CultureName, string DateFormat, bool HasHeader, string BookingDateColumn,
    string? ValueDateColumn, string? AmountColumn, string? DebitColumn, string? CreditColumn,
    string? CurrencyColumn, string ReferenceColumn, string? CounterpartyColumn,
    string? ExternalReferenceColumn, string? AccountIdentifierColumn, string? DefaultCurrency);
public sealed record CreateBankStatementCsvMappingProfileVersionRequest(int ExpectedCurrentVersion,
    string Delimiter, string CultureName, string DateFormat, bool HasHeader, string BookingDateColumn,
    string? ValueDateColumn, string? AmountColumn, string? DebitColumn, string? CreditColumn,
    string? CurrencyColumn, string ReferenceColumn, string? CounterpartyColumn,
    string? ExternalReferenceColumn, string? AccountIdentifierColumn, string? DefaultCurrency);
