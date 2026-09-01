using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/agents/{agentId:guid}/finance/tool-plans")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceAgentToolPlansController : ControllerBase
{
    [HttpPost]
    public Task<FinanceToolPlan> Create(
        Guid companyId,
        Guid agentId,
        FinanceToolPlanCommand command,
        [FromServices] IFinanceToolPlanner planner,
        CancellationToken cancellationToken) =>
        planner.PlanAsync(new FinanceToolPlanRequest(
            companyId,
            agentId,
            command.UserRequest,
            command.Context,
            command.TaskId,
            command.ConversationId,
            command.CorrelationId,
            command.References), cancellationToken);

    [HttpPost("execute")]
    public Task<FinanceConversationExecutionResult> Execute(
        Guid companyId,
        Guid agentId,
        ExecuteFinanceConversationCommand command,
        [FromServices] IFinanceConversationExecutionService execution,
        CancellationToken cancellationToken) =>
        execution.ExecuteAsync(new ExecuteFinanceConversationRequest(
            companyId,
            agentId,
            command.UserRequest,
            command.IdempotencyKey,
            command.Context,
            command.TaskId,
            command.ConversationId,
            command.CorrelationId,
            command.References,
            command.TimeoutSeconds), cancellationToken);

    [HttpPost("preview-mutation")]
    public Task<FinanceMutationPreviewResult> PreviewMutation(
        Guid companyId,
        Guid agentId,
        PreviewFinanceMutationCommand command,
        [FromServices] IFinanceMutationHandoffService handoff,
        CancellationToken cancellationToken) =>
        handoff.PreviewAsync(new PreviewFinanceMutationRequest(
            companyId, agentId, command.UserRequest, command.Context, command.TaskId,
            command.ConversationId, command.CorrelationId, command.References), cancellationToken);

    [HttpPost("confirm-mutation")]
    public Task<FinanceMutationConfirmationResult> ConfirmMutation(
        Guid companyId,
        Guid agentId,
        ConfirmFinanceMutationCommand command,
        [FromServices] IFinanceMutationHandoffService handoff,
        CancellationToken cancellationToken) =>
        handoff.ConfirmAsync(new ConfirmFinanceMutationRequest(
            companyId, agentId, command.ConfirmationToken, command.CorrelationId), cancellationToken);

    [HttpPost("reconcile-mutation")]
    public Task<FinanceMutationConfirmationResult> ReconcileMutation(
        Guid companyId,
        Guid agentId,
        ReconcileFinanceMutationCommand command,
        [FromServices] IFinanceMutationHandoffService handoff,
        CancellationToken cancellationToken) =>
        handoff.ReconcileAsync(new ReconcileFinanceMutationRequest(
            companyId, agentId, command.ConfirmationToken), cancellationToken);

    [HttpPost("runs")]
    public Task<FinanceConversationRunDto> StartRun(
        Guid companyId, Guid agentId, StartFinanceConversationRunCommand command,
        [FromServices] IFinanceConversationRunService runs, CancellationToken cancellationToken) =>
        runs.StartAsync(new StartFinanceConversationRunRequest(companyId, agentId, command.UserRequest,
            command.IdempotencyKey, command.Context, command.TaskId, command.ConversationId,
            command.WorkflowInstanceId, command.DelegationAuthorityId, command.CorrelationId,
            command.References), cancellationToken);

    [HttpGet("runs/{runId:guid}")]
    public Task<FinanceConversationRunDto> GetRun(
        Guid companyId, Guid agentId, Guid runId,
        [FromServices] IFinanceConversationRunService runs, CancellationToken cancellationToken) =>
        runs.GetAsync(companyId, agentId, runId, cancellationToken);

    [HttpGet("runs")]
    public Task<FinanceConversationRunListResult> ListRuns(
        Guid companyId, Guid agentId, [FromQuery] int take,
        [FromServices] IFinanceConversationRunService runs, CancellationToken cancellationToken) =>
        runs.ListAsync(companyId, agentId, take <= 0 ? 20 : take, cancellationToken);

    [HttpPost("runs/{runId:guid}/steps/{stepId}/confirm")]
    public Task<FinanceConversationRunDto> ConfirmRunStep(
        Guid companyId, Guid agentId, Guid runId, string stepId, ConfirmFinanceConversationRunStepCommand command,
        [FromServices] IFinanceConversationRunService runs, CancellationToken cancellationToken) =>
        runs.ConfirmStepAsync(new ConfirmFinanceConversationRunStepRequest(companyId, agentId, runId, stepId,
            command.ExpectedStepVersion), cancellationToken);

    [HttpPost("runs/{runId:guid}/cancel")]
    public Task<FinanceConversationRunDto> CancelRun(
        Guid companyId, Guid agentId, Guid runId, CancelFinanceConversationRunCommand command,
        [FromServices] IFinanceConversationRunService runs, CancellationToken cancellationToken) =>
        runs.CancelAsync(new CancelFinanceConversationRunRequest(companyId, agentId, runId, command.Reason), cancellationToken);

    [HttpPost("runs/{runId:guid}/supersede")]
    public Task<FinanceConversationRunDto> SupersedeRun(
        Guid companyId, Guid agentId, Guid runId, SupersedeFinanceConversationRunCommand command,
        [FromServices] IFinanceConversationRunService runs, CancellationToken cancellationToken) =>
        runs.SupersedeAsync(new SupersedeFinanceConversationRunRequest(companyId, agentId, runId,
            new StartFinanceConversationRunRequest(companyId, agentId, command.UserRequest,
                command.IdempotencyKey, command.Context, command.TaskId, command.ConversationId,
                command.WorkflowInstanceId, command.DelegationAuthorityId, command.CorrelationId,
                command.References), command.Reason), cancellationToken);
}

public sealed record FinanceToolPlanCommand(
    string UserRequest,
    IReadOnlyList<FinanceToolPlanContextItem>? Context = null,
    Guid? TaskId = null,
    Guid? ConversationId = null,
    string? CorrelationId = null,
    IReadOnlyList<FinancePlanningReference>? References = null);

public sealed record ExecuteFinanceConversationCommand(
    string UserRequest,
    string IdempotencyKey,
    IReadOnlyList<FinanceToolPlanContextItem>? Context = null,
    Guid? TaskId = null,
    Guid? ConversationId = null,
    string? CorrelationId = null,
    IReadOnlyList<FinancePlanningReference>? References = null,
    int? TimeoutSeconds = null);

public sealed record PreviewFinanceMutationCommand(
    string UserRequest,
    IReadOnlyList<FinanceToolPlanContextItem>? Context = null,
    Guid? TaskId = null,
    Guid? ConversationId = null,
    string? CorrelationId = null,
    IReadOnlyList<FinancePlanningReference>? References = null);

public sealed record ConfirmFinanceMutationCommand(
    string ConfirmationToken,
    string? CorrelationId = null);

public sealed record ReconcileFinanceMutationCommand(string ConfirmationToken);

public sealed record StartFinanceConversationRunCommand(
    string UserRequest, string IdempotencyKey, IReadOnlyList<FinanceToolPlanContextItem>? Context = null,
    Guid? TaskId = null, Guid? ConversationId = null, Guid? WorkflowInstanceId = null,
    Guid? DelegationAuthorityId = null, string? CorrelationId = null,
    IReadOnlyList<FinancePlanningReference>? References = null);

public sealed record ConfirmFinanceConversationRunStepCommand(long ExpectedStepVersion);
public sealed record CancelFinanceConversationRunCommand(string Reason);
public sealed record SupersedeFinanceConversationRunCommand(
    string UserRequest, string IdempotencyKey, string Reason,
    IReadOnlyList<FinanceToolPlanContextItem>? Context = null, Guid? TaskId = null,
    Guid? ConversationId = null, Guid? WorkflowInstanceId = null, Guid? DelegationAuthorityId = null,
    string? CorrelationId = null, IReadOnlyList<FinancePlanningReference>? References = null);
