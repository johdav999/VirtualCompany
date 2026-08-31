using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingCloseServiceTests
{
    [Fact]
    public async Task Start_replay_generates_one_retained_graph_and_enforces_dependency_evidence_and_concurrency()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = TemplateInput(evidenceRequired: true);
        var template = await fixture.Service.CreateTemplateAsync(new(fixture.CompanyId, input,
            "template-create-1", fixture.ActorId, "test"), default);
        template = await fixture.Service.ActivateTemplateAsync(new(fixture.CompanyId, template.Id,
            template.Versions.Single().Id, template.Version, "template-activate-1", fixture.ActorId, "test"), default);

        var started = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.PeriodId, template.Id,
            template.ActiveVersionId, "close-start-1", fixture.ActorId, "test"), default);
        var replay = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.PeriodId, template.Id,
            template.ActiveVersionId, "close-start-1", fixture.ActorId, "test"), default);
        var equivalentStart = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.PeriodId, template.Id,
            template.ActiveVersionId, "close-start-equivalent", fixture.ActorId, "test"), default);

        Assert.Equal(started.Id, replay.Id);
        Assert.Equal(started.Id, equivalentStart.Id);
        Assert.Equal(2, started.Tasks.Count);
        Assert.All(started.Tasks, task => Assert.Equal(fixture.ActorId, task.OwnerUserId));
        Assert.Equal(2, await fixture.Db.AccountingCloseTasks.CountAsync());
        Assert.Equal(2, await fixture.Db.AccountingCloseOperations.CountAsync(x => x.Action == "start_close"));
        Assert.Single(await fixture.Db.AccountingCloseTaskDependencies.ToListAsync());
        Assert.Equal(1, started.TemplateVersionNumber);

        var predecessor = started.Tasks.Single(x => x.Key == "reconcile_bank");
        var dependent = started.Tasks.Single(x => x.Key == "review_close");
        var blocked = await Assert.ThrowsAsync<AccountingCloseException>(() => fixture.Service.CompleteTaskAsync(new(
            fixture.CompanyId, started.Id, dependent.Id, dependent.Version, null, null, null,
            "complete-dependent-before-predecessor", fixture.ActorId, "test"), default));
        Assert.Equal(AccountingCloseReasonCodes.PredecessorIncomplete, blocked.ReasonCode);

        var inaccessible = await Assert.ThrowsAsync<AccountingCloseException>(() => fixture.Service.CompleteTaskAsync(new(
            fixture.CompanyId, started.Id, predecessor.Id, predecessor.Version, null,
            [new(fixture.OtherCompanyDocumentId, "reconciliation")], null,
            "complete-with-cross-company-evidence", fixture.ActorId, "test"), default));
        Assert.Equal(AccountingCloseReasonCodes.EvidenceAccessDenied, inaccessible.ReasonCode);

        var afterPredecessor = await fixture.Service.CompleteTaskAsync(new(fixture.CompanyId, started.Id,
            predecessor.Id, predecessor.Version, null, [new(fixture.CompanyDocumentId, "reconciliation")],
            "Bank reconciliation retained.", "complete-predecessor-1", fixture.ActorId, "test"), default);
        Assert.False(string.IsNullOrWhiteSpace(afterPredecessor.Tasks.Single(x => x.Id == predecessor.Id)
            .Evidence.Single().ContentHash));
        dependent = afterPredecessor.Tasks.Single(x => x.Key == "review_close");
        var completed = await fixture.Service.CompleteTaskAsync(new(fixture.CompanyId, started.Id,
            dependent.Id, dependent.Version, null, null, "Reviewed.", "complete-dependent-1",
            fixture.ActorId, "test"), default);
        var completionReplay = await fixture.Service.CompleteTaskAsync(new(fixture.CompanyId, started.Id,
            dependent.Id, dependent.Version, null, null, "Reviewed.", "complete-dependent-1",
            fixture.ActorId, "test"), default);

        Assert.Equal(AccountingCloseInstanceStatuses.Completed, completed.Status);
        Assert.Equal(completed.Id, completionReplay.Id);
        Assert.Equal(2, completed.CompletedTaskCount);
        Assert.Contains(completed.History, x => x.Action == "started");
        Assert.Equal(2, completed.History.Count(x => x.Action == "completed" && x.CloseTaskId.HasValue));

        var stale = await Assert.ThrowsAsync<AccountingCloseException>(() => fixture.Service.ReopenTaskAsync(new(
            fixture.CompanyId, completed.Id, dependent.Id, 1, "Correction required", "stale-reopen-1",
            fixture.ActorId, "test"), default));
        Assert.Equal(AccountingCloseReasonCodes.VersionConflict, stale.ReasonCode);

        var newVersion = await fixture.Service.CreateTemplateVersionAsync(new(fixture.CompanyId, template.Id,
            template.Version, input, "template-version-2", fixture.ActorId, "test"), default);
        var retained = await fixture.Service.GetAsync(new(fixture.CompanyId, completed.Id), default);
        Assert.Equal(2, newVersion.LatestVersionNumber);
        Assert.Equal(1, retained.TemplateVersionNumber);
        Assert.Equal(["reconcile_bank", "review_close"], retained.Tasks.Select(x => x.Key));
    }

    [Fact]
    public async Task Cyclic_template_is_rejected_before_persistence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = new AccountingCloseTemplateInput("MONTH_END", "Month end", null, 0m, null,
        [
            new("review", "Review", 1,
            [
                new("a", "A", null, 1, 0, null, null, false, null, null, null, ["b"]),
                new("b", "B", null, 2, 0, null, null, false, null, null, null, ["a"])
            ])
        ]);

        var exception = await Assert.ThrowsAsync<AccountingCloseException>(() => fixture.Service.CreateTemplateAsync(
            new(fixture.CompanyId, input, "cyclic-template-1", fixture.ActorId, "test"), default));

        Assert.Equal(AccountingCloseReasonCodes.DependencyCycle, exception.ReasonCode);
        Assert.Empty(await fixture.Db.AccountingCloseTemplates.ToListAsync());
    }

    [Fact]
    public async Task Invalid_task_shape_is_rejected_with_a_stable_reason_before_domain_construction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = new AccountingCloseTemplateInput("MONTH_END", "Month end", null, 0m, null,
        [
            new("review", "Review", 1,
            [new("review_task", " ", null, 0, 367, null, null, false, null, -1m,
                [new(" ", " ", 0)])])
        ]);

        var exception = await Assert.ThrowsAsync<AccountingCloseException>(() => fixture.Service.CreateTemplateAsync(
            new(fixture.CompanyId, input, "invalid-template-shape", fixture.ActorId, "test"), default));

        Assert.Equal(AccountingCloseReasonCodes.InvalidTemplate, exception.ReasonCode);
        Assert.Empty(await fixture.Db.AccountingCloseTemplates.ToListAsync());
    }

    [Fact]
    public async Task Materiality_amount_cannot_be_omitted_and_below_threshold_completion_does_not_require_sign_off()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = new AccountingCloseTemplateInput("MATERIAL_CLOSE", "Material close", null, 100m, null,
        [
            new("review", "Review", 1,
            [
                new("immaterial_review", "Immaterial review", null, 1, 0, null, null, false,
                    "finance_approver", null),
                new("material_review", "Material review", null, 2, 0, null, null, false,
                    "finance_approver", null)
            ])
        ]);
        var template = await fixture.Service.CreateTemplateAsync(new(fixture.CompanyId, input,
            "material-template-create", fixture.ActorId, "test"), default);
        template = await fixture.Service.ActivateTemplateAsync(new(fixture.CompanyId, template.Id,
            template.Versions.Single().Id, template.Version, "material-template-activate",
            fixture.ActorId, "test"), default);
        var close = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.PeriodId, template.Id,
            template.ActiveVersionId, "material-close-start", fixture.ActorId, "test"), default);
        var immaterialTask = close.Tasks.Single(x => x.Key == "immaterial_review");
        var materialTask = close.Tasks.Single(x => x.Key == "material_review");
        Assert.All(close.Tasks, task => Assert.Null(task.ApprovalRequestId));

        var missing = await Assert.ThrowsAsync<AccountingCloseException>(() => fixture.Service.CompleteTaskAsync(new(
            fixture.CompanyId, close.Id, materialTask.Id, materialTask.Version, null, null, null,
            "material-close-missing-amount", fixture.ActorId, "test"), default));
        Assert.Equal(AccountingCloseReasonCodes.ReportedAmountRequired, missing.ReasonCode);

        var belowThreshold = await fixture.Service.CompleteTaskAsync(new(fixture.CompanyId, close.Id, immaterialTask.Id,
            immaterialTask.Version, 99m, null, null, "material-close-below-threshold", fixture.ActorId, "test"), default);
        Assert.Equal(AccountingCloseInstanceStatuses.Active, belowThreshold.Status);
        Assert.Equal(99m, belowThreshold.Tasks.Single(x => x.Id == immaterialTask.Id).ReportedAmount);
        Assert.Empty(await fixture.Db.ApprovalRequests.ToListAsync());

        var signOffRequired = await Assert.ThrowsAsync<AccountingCloseException>(() => fixture.Service.CompleteTaskAsync(new(
            fixture.CompanyId, close.Id, materialTask.Id, materialTask.Version, 101m, null, null,
            "material-close-above-threshold", fixture.ActorId, "test"), default));
        Assert.Equal(AccountingCloseReasonCodes.SignOffRequired, signOffRequired.ReasonCode);
        var awaitingSignOff = await fixture.Service.GetAsync(new(fixture.CompanyId, close.Id), default);
        materialTask = awaitingSignOff.Tasks.Single(x => x.Id == materialTask.Id);
        Assert.NotNull(materialTask.ApprovalRequestId);
        Assert.Contains(awaitingSignOff.History, x => x.CloseTaskId == materialTask.Id && x.Action == "sign_off_requested");

        var approval = await fixture.Db.ApprovalRequests.Include(x => x.Steps)
            .SingleAsync(x => x.Id == materialTask.ApprovalRequestId);
        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, fixture.ActorId, "Material item approved.");
        await fixture.Db.SaveChangesAsync();
        var completed = await fixture.Service.CompleteTaskAsync(new(fixture.CompanyId, close.Id, materialTask.Id,
            materialTask.Version, 101m, null, null, "material-close-approved", fixture.ActorId, "test"), default);
        Assert.Equal(AccountingCloseInstanceStatuses.Completed, completed.Status);
    }

    [Fact]
    public async Task Cross_company_assignment_is_rejected_and_cancelled_task_can_be_reopened()
    {
        await using var fixture = await Fixture.CreateAsync();
        var template = await fixture.Service.CreateTemplateAsync(new(fixture.CompanyId, TemplateInput(false),
            "assignment-template-create", fixture.ActorId, "test"), default);
        template = await fixture.Service.ActivateTemplateAsync(new(fixture.CompanyId, template.Id,
            template.Versions.Single().Id, template.Version, "assignment-template-activate",
            fixture.ActorId, "test"), default);
        var close = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.PeriodId, template.Id,
            template.ActiveVersionId, "assignment-close-start", fixture.ActorId, "test"), default);
        var task = close.Tasks.Single(x => x.Key == "reconcile_bank");

        var outsideCompany = await Assert.ThrowsAsync<AccountingCloseException>(() => fixture.Service.AssignTaskAsync(new(
            fixture.CompanyId, close.Id, task.Id, task.Version, Guid.NewGuid(), "cross-company-owner",
            fixture.ActorId, "test"), default));
        Assert.Equal(AccountingCloseReasonCodes.OwnerOutsideCompany, outsideCompany.ReasonCode);

        var unassignedMemberId = Guid.NewGuid();
        var memberService = fixture.ServiceFor(unassignedMemberId, CompanyMembershipRole.Employee);
        var forbidden = await Assert.ThrowsAsync<AccountingCloseException>(() => memberService.CompleteTaskAsync(new(
            fixture.CompanyId, close.Id, task.Id, task.Version, null, null, null, "unassigned-completion",
            unassignedMemberId, "test"), default));
        Assert.Equal(AccountingCloseReasonCodes.CompletionForbidden, forbidden.ReasonCode);

        var cancelled = await fixture.Service.CancelTaskAsync(new(fixture.CompanyId, close.Id, task.Id,
            task.Version, "Temporarily removed from the plan", "cancel-close-task", fixture.ActorId, "test"), default);
        task = cancelled.Tasks.Single(x => x.Id == task.Id);
        Assert.Equal(AccountingCloseTaskStatuses.Cancelled, task.Status);
        Assert.Contains("reopen", task.AllowedActions);

        var reopened = await fixture.Service.ReopenTaskAsync(new(fixture.CompanyId, close.Id, task.Id,
            task.Version, "Task is required again", "reopen-cancelled-close-task", fixture.ActorId, "test"), default);
        task = reopened.Tasks.Single(x => x.Id == task.Id);
        Assert.Equal(AccountingCloseTaskStatuses.Reopened, task.Status);
        Assert.Equal(WorkTaskStatus.InProgress, (await fixture.Db.WorkTasks.FindAsync(task.WorkTaskId))!.Status);
    }

    [Fact]
    public async Task Exact_pending_sign_off_blocks_completion_and_approved_sign_off_allows_it()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = new AccountingCloseTemplateInput("SIGNED_CLOSE", "Signed close", null, 0m, null,
        [
            new("review", "Review", 1,
            [new("controller_review", "Controller review", null, 1, 0, null, null, true,
                "finance_approver", null)])
        ]);
        var template = await fixture.Service.CreateTemplateAsync(new(fixture.CompanyId, input,
            "signed-template-create", fixture.ActorId, "test"), default);
        template = await fixture.Service.ActivateTemplateAsync(new(fixture.CompanyId, template.Id,
            template.Versions.Single().Id, template.Version, "signed-template-activate",
            fixture.ActorId, "test"), default);
        var close = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.PeriodId, template.Id,
            template.ActiveVersionId, "signed-close-start", fixture.ActorId, "test"), default);
        var task = Assert.Single(close.Tasks);

        var blocked = await Assert.ThrowsAsync<AccountingCloseException>(() => fixture.Service.CompleteTaskAsync(new(
            fixture.CompanyId, close.Id, task.Id, task.Version, null, null, null,
            "signed-close-complete-pending", fixture.ActorId, "test"), default));
        Assert.Equal(AccountingCloseReasonCodes.SignOffRequired, blocked.ReasonCode);

        var approval = await fixture.Db.ApprovalRequests.Include(x => x.Steps)
            .SingleAsync(x => x.Id == task.ApprovalRequestId);
        Assert.Equal(ApprovalTargetEntityType.AccountingCloseTask.ToStorageValue(), approval.TargetEntityType);
        Assert.Equal(task.Id, approval.TargetEntityId);
        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, fixture.ActorId, "Approved for test.");
        await fixture.Db.SaveChangesAsync();
        var completed = await fixture.Service.CompleteTaskAsync(new(fixture.CompanyId, close.Id, task.Id,
            task.Version, null, null, null, "signed-close-complete-approved", fixture.ActorId, "test"), default);
        Assert.Equal(AccountingCloseInstanceStatuses.Completed, completed.Status);
    }

    [Fact]
    public async Task Close_entities_are_tenant_filtered()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.AccountingCloseOperations.Add(
            new AccountingCloseOperation(Guid.NewGuid(), fixture.CompanyId, "test", "company-a", new string('a', 64), Guid.NewGuid(), 1, fixture.Now));
        fixture.Db.CompanyAccountingClosePolicies.Add(new CompanyAccountingClosePolicy(Guid.NewGuid(),
            fixture.CompanyId, 100m, "SEK", 72, fixture.ActorId, fixture.Now));
        await fixture.Db.SaveChangesAsync();
        await fixture.SeedOtherCompanyOperationAsync();

        var operations = await fixture.Db.AccountingCloseOperations.AsNoTracking().ToListAsync();
        var policies = await fixture.Db.CompanyAccountingClosePolicies.AsNoTracking().ToListAsync();

        Assert.Equal("company-a", Assert.Single(operations).IdempotencyKey);
        Assert.Equal(fixture.CompanyId, Assert.Single(policies).CompanyId);
    }

    private static AccountingCloseTemplateInput TemplateInput(bool evidenceRequired) =>
        new("MONTH_END", "Month-end close", "Accountable monthly close", 0m, null,
        [
            new("reconciliation", "Reconciliation", 1,
            [
                new("reconcile_bank", "Reconcile bank", null, 1, -1, null, null, false, null, null,
                    evidenceRequired ? [new("reconciliation", "Retained bank reconciliation", 1)] : null),
                new("review_close", "Review close", null, 2, 0, null, null, false, null, null,
                    null, ["reconcile_bank"])
            ])
        ]);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext db, AccountingCloseService service,
            Guid companyId, Guid otherCompanyId, Guid actorId, Guid periodId, Guid companyDocumentId,
            Guid otherCompanyDocumentId, DateTime now)
        {
            _connection = connection; Db = db; Service = service; CompanyId = companyId;
            OtherCompanyId = otherCompanyId; ActorId = actorId; PeriodId = periodId;
            CompanyDocumentId = companyDocumentId; OtherCompanyDocumentId = otherCompanyDocumentId; Now = now;
        }
        public VirtualCompanyDbContext Db { get; }
        public AccountingCloseService Service { get; }
        public Guid CompanyId { get; }
        public Guid OtherCompanyId { get; }
        public Guid ActorId { get; }
        public Guid PeriodId { get; }
        public Guid CompanyDocumentId { get; }
        public Guid OtherCompanyDocumentId { get; }
        public DateTime Now { get; }

        public AccountingCloseService ServiceFor(Guid userId, CompanyMembershipRole role) => new(Db,
            new MembershipResolver(new(Guid.NewGuid(), CompanyId, userId, "Company A", role,
                CompanyMembershipStatus.Active)), new ApprovalStub(Db), new KnowledgeAccessStub(), new AuditStub(),
            new AccountingCloseTelemetry(NullLogger<AccountingCloseTelemetry>.Instance), new FixedClock(Now));

        public async Task SeedOtherCompanyOperationAsync()
        {
            var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(_connection).Options;
            await using var otherDb = new VirtualCompanyDbContext(options,
                new TestCompanyContextAccessor(OtherCompanyId, Guid.NewGuid()));
            otherDb.AccountingCloseOperations.Add(new AccountingCloseOperation(Guid.NewGuid(), OtherCompanyId,
                "test", "company-b", new string('b', 64), Guid.NewGuid(), 1, Now));
            otherDb.CompanyAccountingClosePolicies.Add(new CompanyAccountingClosePolicy(Guid.NewGuid(),
                OtherCompanyId, 200m, "SEK", 48, Guid.NewGuid(), Now));
            await otherDb.SaveChangesAsync();
        }

        public static async Task<Fixture> CreateAsync()
        {
            var companyId = Guid.NewGuid(); var otherCompanyId = Guid.NewGuid(); var actorId = Guid.NewGuid();
            var periodId = Guid.NewGuid(); var companyDocumentId = Guid.NewGuid(); var otherDocumentId = Guid.NewGuid();
            var now = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
            var context = new TestCompanyContextAccessor(companyId, actorId);
            var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
            var db = new VirtualCompanyDbContext(options, context); await db.Database.EnsureCreatedAsync();
            db.Companies.AddRange(new Company(companyId, "Company A"), new Company(otherCompanyId, "Company B"));
            db.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "August 2026",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            db.CompanyKnowledgeDocuments.Add(Document(companyDocumentId, companyId, "Company A evidence"));
            await db.SaveChangesAsync();
            await using (var otherDb = new VirtualCompanyDbContext(options,
                new TestCompanyContextAccessor(otherCompanyId, Guid.NewGuid())))
            {
                otherDb.CompanyKnowledgeDocuments.Add(Document(otherDocumentId, otherCompanyId, "Company B evidence"));
                await otherDb.SaveChangesAsync();
            }
            var membership = new MembershipResolver(new(actorId, companyId, actorId, "Company A",
                CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            var service = new AccountingCloseService(db, membership, new ApprovalStub(db), new KnowledgeAccessStub(),
                new AuditStub(), new AccountingCloseTelemetry(NullLogger<AccountingCloseTelemetry>.Instance),
                new FixedClock(now));
            return new(connection, db, service, companyId, otherCompanyId, actorId, periodId,
                companyDocumentId, otherDocumentId, now);
        }

        private static CompanyKnowledgeDocument Document(Guid id, Guid companyId, string title) => new(id,
            companyId, title, CompanyKnowledgeDocumentType.Report, $"close/{id:N}", null, "evidence.pdf",
            "application/pdf", ".pdf", 128, accessScope: new(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class MembershipResolver(ResolvedCompanyMembershipContext member) : ICompanyMembershipContextResolver
    {
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult<ResolvedCompanyMembershipContext?>(member);
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<ResolvedCompanyMembershipContext?>(companyId == member.CompanyId ? member : null);
    }

    private sealed class ApprovalStub(VirtualCompanyDbContext db) : IApprovalRequestService
    {
        public Task<IReadOnlyList<ApprovalRequestDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApprovalRequestDto> GetAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<ApprovalRequestDto> CreateAsync(Guid companyId, CreateApprovalRequestCommand command, CancellationToken cancellationToken)
        {
            var definitions = (command.Steps ?? []).Select(x => new ApprovalStepDefinition(x.SequenceNo,
                ApprovalStepApproverTypeValues.Parse(x.ApproverType), x.ApproverRef)).ToArray();
            var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), companyId,
                ApprovalTargetEntityTypeValues.Parse(command.TargetEntityType), command.TargetEntityId,
                command.RequestedByActorType, command.RequestedByActorId, command.ApprovalType,
                command.ThresholdContext ?? [], command.RequiredRole, command.RequiredUserId, definitions);
            db.ApprovalRequests.Add(approval); await db.SaveChangesAsync(cancellationToken);
            var steps = approval.Steps.Select(x => new ApprovalStepDto(x.Id, x.SequenceNo,
                x.ApproverType.ToStorageValue(), x.ApproverRef, x.Status.ToStorageValue())).ToArray();
            return new ApprovalRequestDto(approval.Id, companyId, approval.TargetEntityType, approval.TargetEntityId,
                approval.RequestedByActorType, approval.RequestedByActorId, approval.ApprovalType,
                approval.RequiredRole, approval.RequiredUserId, approval.Status.ToStorageValue(),
                approval.ThresholdContext, steps, steps.FirstOrDefault(), null, null, string.Empty,
                string.Empty, [], null, approval.CreatedUtc);
        }
        public Task<ApprovalDecisionResultDto> DecideAsync(Guid companyId, ApprovalDecisionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class KnowledgeAccessStub : IKnowledgeAccessPolicyEvaluator
    {
        public bool CanAccess(CompanyKnowledgeAccessContext accessContext, CompanyKnowledgeDocument document) =>
            accessContext.CompanyId == document.CompanyId;
    }

    private sealed class AuditStub : IAuditEventWriter
    {
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? resolvedCompanyId) => CompanyId = resolvedCompanyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
}
