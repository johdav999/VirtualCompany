using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Application.Documents;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/guided-work-sessions")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class GuidedWorkSessionsController : ControllerBase
{
    private readonly IGuidedWorkSessionService _service;
    private readonly IGuidedWorkshopDocumentService _documents;
    public GuidedWorkSessionsController(IGuidedWorkSessionService service,IGuidedWorkshopDocumentService documents) { _service = service; _documents=documents; }

    [HttpPost]
    public Task<ActionResult<GuidedWorkSessionDto>> Start(Guid companyId, [FromBody] StartGuidedWorkSessionCommand command, CancellationToken ct) =>
        Execute(() => _service.StartAsync(companyId, command, ct));
    [HttpGet("options")]
    public Task<ActionResult<IReadOnlyList<GuidedArtifactOptionDto>>> Options(Guid companyId,[FromQuery] Guid agentId,CancellationToken ct)=>
        Execute(()=>_service.ListArtifactOptionsAsync(companyId,agentId,ct));
    [HttpGet]
    public Task<ActionResult<GuidedWorkSessionListDto>> List(Guid companyId, [FromQuery] string? status, [FromQuery] string? artifactType,
        [FromQuery] int? skip, [FromQuery] int? take, CancellationToken ct) =>
        Execute(() => _service.ListAsync(companyId, new(status, artifactType, skip, take), ct));
    [HttpGet("{sessionId:guid}")]
    public Task<ActionResult<GuidedWorkSessionDto>> Get(Guid companyId, Guid sessionId, CancellationToken ct) =>
        Execute(() => _service.GetAsync(companyId, sessionId, ct));
    [HttpPost("{sessionId:guid}/turns")]
    public Task<ActionResult<GuidedWorkTurnResultDto>> Turn(Guid companyId, Guid sessionId, [FromBody] AddGuidedWorkTurnCommand command, CancellationToken ct) =>
        Execute(() => _service.AddTurnAsync(companyId, sessionId, command, ct));
    [HttpPost("{sessionId:guid}/voice/messages")]
    public Task<ActionResult<GuidedWorkMessageDto>> VoiceMessage(Guid companyId, Guid sessionId, [FromBody] RecordGuidedVoiceAgentMessageCommand command, CancellationToken ct) =>
        Execute(() => _service.RecordVoiceAgentMessageAsync(companyId, sessionId, command, ct));
    [HttpPut("{sessionId:guid}/fields/{*path}")]
    public Task<ActionResult<GuidedWorkSessionDto>> Correct(Guid companyId, Guid sessionId, string path,
        [FromBody] CorrectGuidedDraftFieldCommand command, CancellationToken ct) =>
        Execute(() => _service.CorrectFieldAsync(companyId, sessionId, path, command, ct));
    [HttpPost("{sessionId:guid}/fields/status")]
    public Task<ActionResult<GuidedWorkSessionDto>> ChangeStatuses(Guid companyId, Guid sessionId,
        [FromBody] ChangeGuidedDraftFieldStatusesCommand command, CancellationToken ct) =>
        Execute(() => _service.ChangeFieldStatusesAsync(companyId, sessionId, command, ct));
    [HttpPost("{sessionId:guid}/review")]
    public Task<ActionResult<GuidedWorkReviewDto>> Review(Guid companyId, Guid sessionId, [FromBody] PrepareGuidedWorkReviewCommand command, CancellationToken ct) =>
        Execute(() => _service.PrepareReviewAsync(companyId, sessionId, command, ct));
    [HttpPost("{sessionId:guid}/commit")]
    public Task<ActionResult<GuidedWorkCommitResultDto>> Commit(Guid companyId, Guid sessionId, [FromBody] ConfirmGuidedWorkCommitCommand command, CancellationToken ct) =>
        Execute(() => _service.ConfirmCommitAsync(companyId, sessionId, command, ct));
    [HttpPost("{sessionId:guid}/cancel")]
    public Task<ActionResult<GuidedWorkSessionDto>> Cancel(Guid companyId, Guid sessionId, [FromBody] CancelGuidedWorkSessionCommand command, CancellationToken ct) =>
        Execute(() => _service.CancelAsync(companyId, sessionId, command, ct));
    [HttpGet("{sessionId:guid}/documents")]
    public Task<ActionResult<IReadOnlyList<GuidedWorkshopDocumentDto>>> Documents(Guid companyId,Guid sessionId,CancellationToken ct)=>
        Execute(()=>_documents.ListAsync(companyId,sessionId,ct));
    [HttpPost("{sessionId:guid}/documents")]
    [Authorize(Policy=CompanyPolicies.CompanyManager)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10*1024*1024)]
    public async Task<ActionResult<GuidedWorkshopDocumentDto>> UploadDocument(Guid companyId,Guid sessionId,[FromForm] IFormFile? file,[FromForm] string? title,CancellationToken ct)
    {
        if(file is null)return BadRequest(new ValidationProblemDetails(new Dictionary<string,string[]>{{"file",["Choose a document to upload."]}}));
        try
        {
            await using var stream=file.OpenReadStream();
            var result=await _documents.UploadAsync(companyId,sessionId,new(title??Path.GetFileNameWithoutExtension(file.FileName),file.FileName,file.ContentType,file.Length,stream),ct);
            return Ok(result);
        }
        catch(CompanyDocumentValidationException ex){return BadRequest(new ValidationProblemDetails(new Dictionary<string,string[]>(ex.Errors,StringComparer.OrdinalIgnoreCase)){Title="Document validation failed",Status=400});}
        catch(CompanyDocumentOperationException ex){return Problem(title:ex.Title,detail:ex.Detail,statusCode:ex.StatusCode);}
        catch(GuidedWorkValidationException ex){return BadRequest(new ValidationProblemDetails(new Dictionary<string,string[]>(ex.Errors,StringComparer.OrdinalIgnoreCase)){Title="Guided work validation failed",Status=400});}
        catch(GuidedWorkConflictException ex){return Problem(title:"Guided session conflict",detail:ex.Message,statusCode:409);}
        catch(KeyNotFoundException){return NotFound();}
        catch(UnauthorizedAccessException){return Forbid();}
    }

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (GuidedWorkValidationException ex)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))
            {
                Title = "Guided work validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (GuidedWorkConflictException ex) { return Problem(title: "Guided session conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict); }
        catch (GuidedCheckpointUnavailableException ex) { return Problem(title: "Guided dialogue unavailable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (GuidedArtifactNotEligibleException ex) { return Problem(title: "Workshop permission required", detail: ex.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) { return Problem(title: "Guided session conflict", detail: "The session changed. Refresh it and try again.", statusCode: StatusCodes.Status409Conflict); }
    }
}
