using VirtualCompany.Application.Tasks;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyTaskCommandService : ICompanyTaskCommandService
{
    private readonly ICompanyTaskService _legacyTaskService;

    public CompanyTaskCommandService(ICompanyTaskService legacyTaskService)
    {
        _legacyTaskService = legacyTaskService;
    }

    public async Task<TaskCommandResultDto> CreateTaskAsync(
        Guid companyId,
        CreateTaskCommand command,
        CancellationToken cancellationToken)
    {
        var task = await _legacyTaskService.CreateTaskAsync(companyId, command, cancellationToken);
        return ToCommandResult(task);
    }

    public async Task<TaskCommandResultDto> CreateSubtaskAsync(
        Guid companyId,
        Guid parentTaskId,
        CreateSubtaskCommand command,
        CancellationToken cancellationToken)
    {
        var task = await _legacyTaskService.CreateSubtaskAsync(companyId, parentTaskId, command, cancellationToken);
        return ToCommandResult(task);
    }

    public async Task<TaskCommandResultDto> UpdateStatusAsync(
        Guid companyId,
        Guid taskId,
        UpdateTaskStatusCommand command,
        CancellationToken cancellationToken)
    {
        var task = await _legacyTaskService.UpdateStatusAsync(companyId, taskId, command, cancellationToken);
        return ToCommandResult(task);
    }

    public async Task<TaskCommandResultDto> ReassignAsync(
        Guid companyId,
        Guid taskId,
        ReassignTaskCommand command,
        CancellationToken cancellationToken)
    {
        var task = await _legacyTaskService.ReassignAsync(companyId, taskId, command, cancellationToken);
        return ToCommandResult(task);
    }

    private static TaskCommandResultDto ToCommandResult(TaskDetailDto task) =>
        new(
            task.Id,
            task.CompanyId,
            task.Status,
            task.UpdatedAt);
}
