namespace VirtualCompany.Api.Controllers;
public sealed record CreateAdHocSalesPresentationRequest(Guid ClientRequestId, Guid PresetVersionId, Guid? CustomerCompanyId, Guid? ContactId, Guid? LeadId, Guid? DealId, Guid? PresenterAgentId, string? Goal, string? Audience, int? DurationMinutes, string? DemoScenario, string? Language, string? ControlMode, string RuntimeStrategy)
{
}
