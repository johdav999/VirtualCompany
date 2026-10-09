using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class ApprovalTargetHandlerTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TaskId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Composition_resolves_one_scoped_handler_per_supported_target_in_its_owning_module()
    {
        using var scope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();
        var owners = new Dictionary<string, ApprovalTargetEntityType[]>
        {
            ["Operations"] = [ApprovalTargetEntityType.Task, ApprovalTargetEntityType.Workflow,
                ApprovalTargetEntityType.Action, ApprovalTargetEntityType.OperatingPlan,
                ApprovalTargetEntityType.OperatingDecision, ApprovalTargetEntityType.AnnualPlanVersion],
            ["Sales"] = [ApprovalTargetEntityType.SalesMeetingInvitation, ApprovalTargetEntityType.SalesMeetingChangeRequest,
                ApprovalTargetEntityType.SalesMeetingChangeProposal, ApprovalTargetEntityType.MarketingChannelAction],
            ["Finance"] = [ApprovalTargetEntityType.FinanceIntegrationWrite,
                ApprovalTargetEntityType.AccountingProviderSwitchMappingDecision, ApprovalTargetEntityType.AccountingProviderSwitchCutoverPlan,
                ApprovalTargetEntityType.AccountingProviderSwitchActivation, ApprovalTargetEntityType.AccountingProviderSwitchClosure,
                ApprovalTargetEntityType.VatReturn, ApprovalTargetEntityType.TreasurySource]
        };
        var supported = owners.Values.SelectMany(x => x).ToHashSet();
        foreach (var (owner, targets) in owners)
        foreach (var target in targets)
        {
            var handler = Assert.Single(scope.ServiceProvider.GetKeyedServices<IApprovalTargetHandler>(target));
            Assert.Equal($"VirtualCompany.Infrastructure.{owner}", handler.GetType().Assembly.GetName().Name);
            Assert.Same(handler, scope.ServiceProvider.GetRequiredKeyedService<IApprovalTargetHandler>(target));
            Assert.NotSame(handler, secondScope.ServiceProvider.GetRequiredKeyedService<IApprovalTargetHandler>(target));
            Assert.False(await handler.ExistsAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        }

        // These targets use their own module workflows; generic approval creation must not acquire authority over them.
        foreach (var target in Enum.GetValues<ApprovalTargetEntityType>().Except(supported))
            Assert.Null(scope.ServiceProvider.GetKeyedService<IApprovalTargetHandler>(target));
        Assert.Null(scope.ServiceProvider.GetService<IApprovalTargetHandler>());
        Assert.Same(scope.ServiceProvider.GetRequiredKeyedService<IApprovalTargetHandler>(ApprovalTargetEntityType.FinanceIntegrationWrite),
            scope.ServiceProvider.GetRequiredKeyedService<IApprovalTargetHandler>(ApprovalTargetEntityTypeValues.Parse("fortnox_write")));
    }

    [Fact]
    public async Task Review_fingerprint_matches_retained_legacy_material_and_detects_business_changes()
    {
        await SeedTaskAsync();
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(CompanyId);
        var hasher = scope.ServiceProvider.GetRequiredService<ApprovalReviewMaterialHasher>();
        var approval = CreateApproval(CompanyId);

        // Golden digest of the pre-extraction task review envelope, including nested objects and ordered arrays.
        const string legacyHash = "571D9AF8B171F6B6F9E1610A2232FF06AD9687501773AFAFAC852E936C7B0DAD";
        Assert.Equal(legacyHash, await hasher.ComputeHashAsync(approval, default));
        approval.ThresholdContext.Clear();
        approval.ThresholdContext["steps"] = new JsonArray(2, 1);
        approval.ThresholdContext["reason"] = JsonValue.Create("review");
        Assert.Equal(legacyHash, await hasher.ComputeHashAsync(approval, default));
        approval.ThresholdContext["steps"] = new JsonArray(1, 2);
        Assert.NotEqual(legacyHash, await hasher.ComputeHashAsync(approval, default));

        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var task = await db.WorkTasks.SingleAsync(x => x.Id == TaskId);
        task.UpdateStatus(WorkTaskStatus.Blocked);
        await db.SaveChangesAsync();
        Assert.Equal(legacyHash, await hasher.ComputeHashAsync(CreateApproval(CompanyId), default));
        task.InputPayload["amount"] = JsonValue.Create(126);
        db.Entry(task).Property(x => x.InputPayload).IsModified = true;
        await db.SaveChangesAsync();
        Assert.NotEqual(legacyHash, await hasher.ComputeHashAsync(CreateApproval(CompanyId), default));
    }

    [Fact]
    public async Task Foreign_company_cannot_find_review_or_mutate_a_loaded_target()
    {
        await SeedTaskAsync();
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(CompanyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var task = await db.WorkTasks.SingleAsync(x => x.Id == TaskId);
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IApprovalTargetHandler>(ApprovalTargetEntityType.Task);
        var foreign = CreateApproval(Guid.NewGuid());
        Assert.True(await handler.ExistsAsync(CompanyId, TaskId, default));
        Assert.False(await handler.ExistsAsync(foreign.CompanyId, TaskId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.GetReviewMaterialAsync(foreign, default));
        foreign.ApproveCurrentStep(foreign.CurrentActionableStep!.Id, Guid.NewGuid(), "Approved");
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ApplyDecisionAsync(foreign, default));
        Assert.Equal(WorkTaskStatus.AwaitingApproval, task.Status);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Target_handler_waits_for_final_decision_and_leaves_persistence_to_coordinator()
    {
        await SeedTaskAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(CompanyId);
            var handler = scope.ServiceProvider.GetRequiredKeyedService<IApprovalTargetHandler>(ApprovalTargetEntityType.Task);
            var approval = CreateApproval(CompanyId);
            Assert.Null(await handler.ApplyDecisionAsync(approval, default));
            approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, Guid.NewGuid(), "Approved");
            var transition = await handler.ApplyDecisionAsync(approval, default);
            Assert.NotNull(transition);
            Assert.Equal("awaiting_approval", transition.PreviousState);
            Assert.Equal("in_progress", transition.CurrentState);
            Assert.True(scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>().ChangeTracker.HasChanges());
        }

        using var independent = _factory.Services.CreateScope();
        independent.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(CompanyId);
        var persisted = await independent.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>()
            .WorkTasks.AsNoTracking().SingleAsync(x => x.Id == TaskId);
        Assert.Equal(WorkTaskStatus.AwaitingApproval, persisted.Status);
    }

    private Task SeedTaskAsync() => _factory.SeedAsync(db =>
    {
        db.Companies.Add(new Company(CompanyId, "Handler regression"));
        db.WorkTasks.Add(new WorkTask(TaskId, CompanyId, "approval.test", "Retained proposal", "Review source evidence.",
            WorkTaskPriority.High, null, null, "user", null,
            new Dictionary<string, JsonNode?>
            {
                ["amount"] = JsonValue.Create(125),
                ["options"] = new JsonObject { ["z"] = false, ["a"] = "kept" }
            }, status: WorkTaskStatus.AwaitingApproval));
        return Task.CompletedTask;
    });

    private static ApprovalRequest CreateApproval(Guid companyId) => ApprovalRequest.CreateForTarget(
        Guid.NewGuid(), companyId, ApprovalTargetEntityType.Task, TaskId, "user", Guid.NewGuid(), "threshold",
        new Dictionary<string, JsonNode?> { ["reason"] = JsonValue.Create("review"), ["steps"] = new JsonArray(2, 1) },
        "owner", null, []);
}
