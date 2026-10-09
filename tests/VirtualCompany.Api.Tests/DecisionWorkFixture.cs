using System.Net.Http.Json;
using VirtualCompany.Application.Tasks;
using DecisionWorkSourceRef = VirtualCompany.Application.Tasks.DecisionWorkSourceRef;
using DecisionWorkInput = VirtualCompany.Application.Tasks.DecisionWorkInput;
using DecisionWorkDocument = VirtualCompany.Application.Tasks.DecisionWorkDocument;
using DecisionWorkPreview = VirtualCompany.Application.Tasks.DecisionWorkPreview;
using ConfirmDecisionWork = VirtualCompany.Application.Tasks.ConfirmDecisionWork;
namespace VirtualCompany.Api.Tests;
public sealed record DecisionWorkFixture(StrategicScenarioFixture Planning, Guid ScenarioId)
{
    public Guid Company => Planning.Annual.Quarter.Company.Company;
    public string Root => $"/api/companies/{Company:D}/decision-work";
    public DecisionWorkSourceRef Source(string kind) => kind switch {
        "month" => new(kind, Planning.Annual.Quarter.Snapshots[2], "support.volume"),
        "quarter" => new(kind, Planning.Annual.ReviewId, Planning.Annual.Quarter.Goal.ToString("D")),
        "annual" => new(kind, Planning.PlanId, "decision"), _ => new("scenario", ScenarioId, "0") };
    public DecisionWorkInput Input(string kind = "scenario") => new(Source(kind), "Review service capacity", Planning.Annual.Quarter.Company.Owner,
        new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc), "Record a reviewed capacity recommendation with source evidence.",
        [Planning.Annual.Quarter.Company.Manager], "Internal review only. No customer contact, spend or publication.");
    public static async Task<DecisionWorkFixture> Seed(TestWebApplicationFactory f, StrategicScenarioFixture? existing = null)
    { var p = existing ?? await StrategicScenarioFixture.Seed(f); using var h = p.Annual.Client(f); var d = await StrategicScenarioFixture.Save(h, p); return new(p, d.Summary.Id); }
    public static async Task<DecisionWorkDocument> Create(HttpClient h, DecisionWorkFixture s, DecisionWorkInput? input = null)
    { input ??= s.Input(); var p = await AnnualPlanningFixture.Read<DecisionWorkPreview>(await h.PostAsync(s.Root + "/preview", JsonContent.Create(input))); return await AnnualPlanningFixture.Read<DecisionWorkDocument>(await h.PostAsync(s.Root, JsonContent.Create(new ConfirmDecisionWork(Guid.NewGuid(), input, p.Fingerprint)))); }
}
