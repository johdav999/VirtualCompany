using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/prospecting")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class LeadGenerationController(ICompanyContextAccessor context, ILeadGenerationService service, IIcpSuggestionService suggestions) : ControllerBase
{
    [HttpGet("icp")] public Task<IReadOnlyList<IcpProfileDto>> Profiles(CancellationToken ct) => service.ListProfilesAsync(CompanyId, ct);
    [HttpPost("icp/suggest")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<IcpSuggestionDto> SuggestIcp(SuggestIcpRequest request, CancellationToken ct) => suggestions.SuggestAsync(CompanyId, UserId, request, ct);
    [HttpPost("icp")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<IcpProfileDto> CreateProfile(SaveIcpProfileRequest request, CancellationToken ct) => service.CreateProfileAsync(CompanyId, UserId, request, ct);
    [HttpPut("icp/{id:guid}")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<IcpProfileDto> UpdateProfile(Guid id, SaveIcpProfileRequest request, CancellationToken ct) => service.UpdateProfileAsync(CompanyId, UserId, id, request, ct);
    [HttpPost("icp/{id:guid}/activate")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<IcpProfileDto> Activate(Guid id, CancellationToken ct) => service.ActivateProfileAsync(CompanyId, UserId, id, ct);
    [HttpPost("icp/{id:guid}/clone")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<IcpProfileDto> Clone(Guid id, CancellationToken ct) => service.CloneProfileAsync(CompanyId, UserId, id, ct);
    [HttpDelete("icp/{id:guid}")][Authorize(Policy = CompanyPolicies.CompanyManager)] public async Task<IActionResult> Archive(Guid id, CancellationToken ct) { await service.ArchiveProfileAsync(CompanyId, UserId, id, ct); return NoContent(); }
    [HttpPost("icp/{id:guid}/preview")] public Task<IcpPreviewDto> Preview(Guid id, ProspectAccountInput request, CancellationToken ct) => service.PreviewProfileAsync(CompanyId, id, request, ct);

    [HttpGet("sources")] public Task<SourcePolicyDto> Sources(CancellationToken ct) => service.GetSourcePolicyAsync(CompanyId, ct);
    [HttpPut("sources")][Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)] public Task<SourcePolicyDto> SaveSources(SaveSourcePolicyRequest request, CancellationToken ct) => service.UpdateSourcePolicyAsync(CompanyId, UserId, request, ct);

    [HttpGet("runs")] public Task<IReadOnlyList<ProspectingRunDto>> Runs(CancellationToken ct) => service.ListRunsAsync(CompanyId, ct);
    [HttpPost("runs")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectingRunDto> CreateRun(CreateProspectingRunRequest request, CancellationToken ct) => service.CreateRunAsync(CompanyId, UserId, request, ct);
    [HttpPost("runs/{id:guid}/start")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectingRunDto> Start(Guid id, CancellationToken ct) => service.StartRunAsync(CompanyId, UserId, id, ct);
    [HttpPost("runs/{id:guid}/{action}")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectingRunDto> RunAction(Guid id, string action, CancellationToken ct) => service.ChangeRunAsync(CompanyId, UserId, id, action, ct);
    [HttpPost("runs/{id:guid}/import")][Authorize(Policy = CompanyPolicies.CompanyManager)][RequestSizeLimit(10_000_000)] public async Task<ImportResultDto> Import(Guid id, IFormFile file, CancellationToken ct) { if (file.Length == 0 || !(file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))) throw new LeadGenerationValidationException("Upload a non-empty CSV or XLSX file."); await using var stream = file.OpenReadStream(); return await service.ImportCsvAsync(CompanyId, UserId, id, stream, file.FileName, ct); }

    [HttpGet("accounts")] public Task<ProspectPageDto> Accounts([FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? country, [FromQuery] string? source, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string sort = "score", CancellationToken ct = default) => service.ListAccountsAsync(CompanyId, new(search, status, country, source, page, pageSize, sort), ct);
    [HttpGet("accounts/{id:guid}")] public async Task<ActionResult<ProspectAccountDto>> Account(Guid id, CancellationToken ct) => await service.GetAccountAsync(CompanyId, id, ct) is { } result ? Ok(result) : NotFound();
    [HttpPost("runs/{runId:guid}/accounts")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectAccountDto> AddAccount(Guid runId, ProspectAccountInput request, CancellationToken ct) => service.AddAccountAsync(CompanyId, UserId, runId, request, ct);
    [HttpPost("accounts/{id:guid}/review")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectAccountDto> ReviewAccount(Guid id, ReviewProspectRequest request, CancellationToken ct) => service.ReviewAccountAsync(CompanyId, UserId, id, request, ct);
    [HttpPost("accounts/{id:guid}/merge")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectAccountDto> MergeAccount(Guid id, MergeProspectRequest request, CancellationToken ct) => service.MergeAccountAsync(CompanyId, UserId, id, request.TargetId, ct);
    [HttpPost("accounts/{id:guid}/contacts")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectContactDto> AddContact(Guid id, SaveProspectContactRequest request, CancellationToken ct) => service.AddContactAsync(CompanyId, UserId, id, request, ct);
    [HttpPost("contacts/{id:guid}/review")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectContactDto> ReviewContact(Guid id, ReviewProspectRequest request, CancellationToken ct) => service.ReviewContactAsync(CompanyId, UserId, id, request, ct);
    [HttpPost("contacts/{id:guid}/merge")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectContactDto> MergeContact(Guid id, MergeProspectRequest request, CancellationToken ct) => service.MergeContactAsync(CompanyId, UserId, id, request.TargetId, ct);
    [HttpPost("accounts/{id:guid}/signals")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectSignalDto> AddSignal(Guid id, SaveProspectSignalRequest request, CancellationToken ct) => service.AddSignalAsync(CompanyId, UserId, id, request, ct);
    [HttpPost("signals/{id:guid}/review")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectSignalDto> ReviewSignal(Guid id, SignalReviewRequest request, CancellationToken ct) => service.ReviewSignalAsync(CompanyId, UserId, id, request.Action, ct);
    [HttpPost("accounts/{id:guid}/refresh")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<ProspectAccountDto> Refresh(Guid id, CancellationToken ct) => service.RefreshResearchAndScoreAsync(CompanyId, UserId, id, ct);
    [HttpPost("accounts/{id:guid}/convert")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<LeadConversionDto> Convert(Guid id, [FromQuery] Guid? contactId, CancellationToken ct) => service.ConvertAsync(CompanyId, UserId, id, contactId, ct);

    [HttpGet("suppressions")] public Task<IReadOnlyList<SuppressionDto>> Suppressions(CancellationToken ct) => service.ListSuppressionsAsync(CompanyId, ct);
    [HttpPost("suppressions")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<SuppressionDto> Suppress(SaveSuppressionRequest request, CancellationToken ct) => service.AddSuppressionAsync(CompanyId, UserId, request, ct);
    [HttpDelete("suppressions/{id:guid}")][Authorize(Policy = CompanyPolicies.CompanyManager)] public async Task<IActionResult> Unsuppress(Guid id, CancellationToken ct) { await service.RemoveSuppressionAsync(CompanyId, UserId, id, ct); return NoContent(); }
    [HttpGet("metrics")] public Task<LeadGenerationMetricsDto> Metrics(CancellationToken ct) => service.GetMetricsAsync(CompanyId, ct);
    [HttpGet("export.csv")] public async Task<IActionResult> Export(CancellationToken ct) => File(await service.ExportCsvAsync(CompanyId, ct), "text/csv", $"prospects-{DateTime.UtcNow:yyyyMMdd}.csv");
    [HttpGet("crm")] public Task<CrmDeliveryStatusDto> CrmStatus(CancellationToken ct) => service.GetCrmStatusAsync(CompanyId, ct);
    [HttpPost("accounts/{id:guid}/crm/{providerKey}/sync")][Authorize(Policy = CompanyPolicies.CompanyManager)] public Task<CrmSyncResultDto> SyncCrm(Guid id, string providerKey, CancellationToken ct) => service.SyncLeadAsync(CompanyId, UserId, id, providerKey, ct);

    private Guid CompanyId => context.CompanyId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A company is required.");
    private Guid UserId => context.UserId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A user is required.");
}
