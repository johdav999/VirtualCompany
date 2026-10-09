namespace VirtualCompany.Api.Controllers;
public sealed record ApplySalesPresentationPresetRequest(Guid? PresetId, Guid? PresetVersionId, Guid? PresenterAgentId, string? Goal, string? Audience, int? DurationMinutes, string? DemoScenario, string? Language, string? ControlMode, bool ReplaceActive, long? ExpectedActiveRunVersion)
{
}
