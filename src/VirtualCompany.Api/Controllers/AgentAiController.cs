using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/agents")]
[Authorize(Policy = CompanyPolicies.CompanyManager)]
[RequireCompanyContext]
public sealed class AgentAiController : ControllerBase
{
    [HttpGet("{agentId:guid}/ai-runs/{runId:guid}")]
    public async Task<ActionResult<AgentReasoningResult>> GetRun(Guid companyId, Guid agentId, Guid runId,
        [FromServices] IAgentReasoningGateway service, CancellationToken ct)
    { var result = await service.GetRunAsync(companyId, agentId, runId, ct); return result is null ? NotFound() : result; }

    [HttpPost("{agentId:guid}/questions")]
    public Task<AgentQuestionAnswerDto> Ask(Guid companyId, Guid agentId, AskAgentQuestionCommand command,
        [FromServices] IAgentQuestionAnsweringService service, CancellationToken ct) => service.AskAsync(companyId, agentId, command, ct);

    [HttpPost("{agentId:guid}/briefings/{cadence}")]
    public Task<AgentRoleBriefingDto> Briefing(Guid companyId, Guid agentId, string cadence,
        [FromServices] IAgentRoleBriefingService service, CancellationToken ct) => service.GenerateAsync(companyId, agentId, cadence, ct);

    [HttpGet("{agentId:guid}/priorities")]
    public Task<IReadOnlyList<AgentWorkPriorityItem>> Priorities(Guid companyId, Guid agentId, [FromQuery] int take,
        [FromServices] IAgentWorkPrioritizationService service, CancellationToken ct) => service.PrioritizeAsync(companyId, agentId, take <= 0 ? 20 : take, ct);

    [HttpPost("{agentId:guid}/plans")]
    public Task<AgentPlanDto> GeneratePlan(Guid companyId, Guid agentId, GenerateAgentPlanCommand command,
        [FromServices] IAgentPlanningService service, CancellationToken ct) => service.GenerateAsync(companyId, agentId, command, ct);

    [HttpPost("{agentId:guid}/plans/{runId:guid}/commit")]
    public Task<AgentPlanDto> CommitPlan(Guid companyId, Guid agentId, Guid runId,
        [FromServices] IAgentPlanningService service, CancellationToken ct) => service.CommitAsync(companyId, agentId, runId, ct);

    [HttpPost("{agentId:guid}/exceptions/{exceptionId:guid}/interpret")]
    public Task<AgentExceptionInterpretationDto> Interpret(Guid companyId, Guid agentId, Guid exceptionId,
        [FromServices] IAgentExceptionInterpretationService service, CancellationToken ct) => service.InterpretAsync(companyId, agentId, exceptionId, ct);

    [HttpGet("handoffs")]
    public Task<IReadOnlyList<AgentHandoffDto>> Handoffs(Guid companyId, [FromQuery] Guid? agentId,
        [FromServices] IAgentHandoffService service, CancellationToken ct) => service.ListAsync(companyId, agentId, ct);

    [HttpPost("{agentId:guid}/handoffs")]
    public Task<AgentHandoffDto> CreateHandoff(Guid companyId, Guid agentId, CreateAgentHandoffCommand command,
        [FromServices] IAgentHandoffService service, CancellationToken ct) => service.CreateAsync(companyId, agentId, command, ct);

    [HttpPost("handoffs/{handoffId:guid}/transition")]
    public Task<AgentHandoffDto> TransitionHandoff(Guid companyId, Guid handoffId, TransitionAgentHandoffCommand command,
        [FromServices] IAgentHandoffService service, CancellationToken ct) => service.TransitionAsync(companyId, handoffId, command, ct);

    [HttpGet("memory-candidates")]
    public Task<IReadOnlyList<AgentMemoryCandidateDto>> MemoryCandidates(Guid companyId, [FromQuery] string? status,
        [FromServices] IAgentMemoryCandidateService service, CancellationToken ct) => service.ListAsync(companyId, status, ct);

    [HttpPost("{agentId:guid}/memory-candidates")]
    public Task<AgentMemoryCandidateDto> ProposeMemory(Guid companyId, Guid agentId, ProposeAgentMemoryCommand command,
        [FromServices] IAgentMemoryCandidateService service, CancellationToken ct) => service.ProposeAsync(companyId, agentId, command, ct);

    [HttpPost("memory-candidates/{candidateId:guid}/review")]
    public Task<AgentMemoryCandidateDto> ReviewMemory(Guid companyId, Guid candidateId, ReviewAgentMemoryCommand command,
        [FromServices] IAgentMemoryCandidateService service, CancellationToken ct) => service.ReviewAsync(companyId, candidateId, command, ct);

    [HttpPost("ai-feedback")]
    public async Task<IActionResult> RecordFeedback(Guid companyId, RecordAgentAiFeedbackCommand command,
        [FromServices] IAgentAiQualityService service, CancellationToken ct)
    { await service.RecordAsync(companyId, command, ct); return NoContent(); }

    [HttpGet("ai-quality")]
    public Task<AgentAiQualityMetricsDto> Quality(Guid companyId, [FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc,
        [FromQuery] Guid? agentId, [FromQuery] string? capabilityId, [FromServices] IAgentAiQualityService service, CancellationToken ct) =>
        service.GetMetricsAsync(companyId, fromUtc, toUtc, agentId, capabilityId, ct);
}
