using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class DecisionWorkService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver memberships,
    IMonthlyReviewSnapshotService monthly, IQuarterlyPlanningService quarterly, IAnnualPlanningService annual,
    IStrategicScenarioService scenarios, ICompanyTaskCommandService tasks, ICompanyTaskQueryService taskQueries,
    IApprovalRequestService approvals, IAuditEventWriter audit, TimeProvider clock) : IDecisionWorkService
{
    public const string ReviewRule = "Human work review is required before starting or completing this follow-up. Sending, publication and financial actions require their own current policy and action approval.";
    private IQueryable<DecisionWorkOrigin> Origins(Guid company) => db.Set<DecisionWorkOrigin>().Where(x => x.CompanyId == company);
    private async Task<ResolvedCompanyMembershipContext> Require(Guid company, CancellationToken ct)
    {
        var m = await memberships.ResolveAsync(company, ct) ?? throw new UnauthorizedAccessException();
        if (m.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager)) throw new UnauthorizedAccessException();
        return m;
    }
    private async Task<DecisionWorkPerson[]> People(Guid company, CancellationToken ct)
    {
        var ids = db.CompanyMemberships.Where(x => x.CompanyId == company && x.Status == CompanyMembershipStatus.Active).Select(x => x.UserId);
        return await db.Users.Where(x => ids.Contains(x.Id)).OrderBy(x => x.DisplayName).Select(x => new DecisionWorkPerson(x.Id, x.DisplayName)).ToArrayAsync(ct);
    }

    public async Task<DecisionWorkContext> ContextAsync(Guid company, DecisionWorkSourceRef reference, CancellationToken ct)
    {
        await Require(company, ct); var source = await Source(company, reference, ct); var work = new List<DecisionWorkDocument>();
        foreach (var task in await Origins(company).Where(x => x.SourceKind == reference.Kind && x.SourceVersionId == reference.VersionId && x.ItemKey == reference.ItemKey).Select(x => x.TaskId).ToListAsync(ct))
            work.Add(await OpenAsync(company, task, ct));
        return new(company, source, await People(company, ct), work);
    }
    public async Task<DecisionWorkPreview> PreviewAsync(Guid company, DecisionWorkInput input, CancellationToken ct)
    {
        await Require(company, ct); Validate(input); var source = await Source(company, input.Source, ct);
        if (!source.IsLatest) throw new CompanyOperatingConcurrencyException("A newer source revision exists. Open and review it before creating more work. Existing work keeps its original snapshot.");
        var people = await People(company, ct);
        var owner = people.SingleOrDefault(x => x.Id == input.OwnerUserId) ?? throw new ArgumentException("Choose an active company member as owner.");
        var collaborators = input.ProposedCollaborators.Select(id => people.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Every proposed collaborator must be an active company member.")).OrderBy(x => x.Id).ToArray();
        var fingerprint = Hash(JsonSerializer.Serialize(new { company, input, source.Fingerprint, source.Version, owner, collaborators, ReviewRule }));
        return new(company, input, source, owner.Name, collaborators, ReviewRule, fingerprint);
    }
    public async Task<DecisionWorkDocument> CreateAsync(Guid company, ConfirmDecisionWork command, CancellationToken ct)
    {
        var member = await Require(company, ct); Validate(command.Input);
        if (command.RequestId == Guid.Empty || command.ExpectedFingerprint?.Length != 64) throw new ArgumentException("Preview this work before confirming.");
        var commandHash = Hash(JsonSerializer.Serialize(new { command.Input, command.ExpectedFingerprint }));
        var existing = await Existing(company, command, ct);
        if (existing != null) return await Replay(company, existing, member.UserId, commandHash, ct);
        return await RetryDeadlock(async () => await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                existing = await Existing(company, command, ct);
                if (existing != null) { await tx.CommitAsync(ct); return await Replay(company, existing, member.UserId, commandHash, ct); }
                var preview = await PreviewAsync(company, command.Input, ct);
                if (preview.Fingerprint != command.ExpectedFingerprint) throw new CompanyOperatingConcurrencyException("Source, owner or proposal changed. Preview the work again.");
                // Native task command enforces assignment, business scope and platform-event/outbox behavior.
                var task = await tasks.CreateTaskAsync(company, new CreateTaskCommand("follow_up", preview.Input.Objective,
                    WorkDescription(preview.Input), "normal", preview.Input.DueUtc, null,
                    new() { ["decisionWorkSourceKind"] = JsonValue.Create(preview.Source.Reference.Kind), ["decisionWorkSourceId"] = JsonValue.Create(preview.Source.Reference.VersionId),
                        ["decisionWorkSourceVersion"] = JsonValue.Create(preview.Source.Version) }, RationaleSummary: "Explicit human handoff from a retained management decision; no execution authority inherited."), ct);
                var json = JsonSerializer.Serialize(preview); var originId = Guid.NewGuid(); var origin = new DecisionWorkOrigin { Id = originId, CompanyId = company, TaskId = task.Id,
                    RequestId = command.RequestId, CreatedByUserId = member.UserId, OwnerUserId = preview.Input.OwnerUserId,
                    SourceKind = preview.Source.Reference.Kind, SourceVersionId = preview.Source.Reference.VersionId, ItemKey = preview.Source.Reference.ItemKey,
                    SourceVersion = preview.Source.Version, SourceFingerprint = preview.Source.Fingerprint,
                    MonthlySnapshotId = preview.Source.Reference.Kind == "month" ? preview.Source.Reference.VersionId : null,
                    QuarterReviewId = preview.Source.Reference.Kind == "quarter" ? preview.Source.Reference.VersionId : null,
                    AnnualPlanId = preview.Source.Reference.Kind == "annual" ? preview.Source.Reference.VersionId : null,
                    ScenarioId = preview.Source.Reference.Kind == "scenario" ? preview.Source.Reference.VersionId : null,
                    Objective = preview.Input.Objective, AcceptanceOutcome = preview.Input.AcceptanceOutcome, ProposedConstraints = preview.Input.ProposedConstraints,
                    DueUtc = preview.Input.DueUtc, CreatedUtc = clock.GetUtcNow().UtcDateTime, CommandHash = commandHash, PreviewJson = json, PreviewChecksum = Hash(json),
                    Collaborators = preview.Collaborators.Select(x => new DecisionWorkCollaborator { Id = Guid.NewGuid(), CompanyId = company, OriginId = originId, UserId = x.Id, Name = x.Name }).ToList() };
                db.Add(origin);
                await audit.WriteAsync(new(company, "user", member.UserId, "decision.work.created", AuditTargetTypes.WorkTask, task.Id.ToString("N"), "succeeded",
                    "Created accountable follow-up from the retained decision. Work review and action approvals remain separate.",
                    DataSources: [preview.Source.Reference.Kind, "tasks"], Metadata: new Dictionary<string, string?> { ["sourceId"] = origin.SourceVersionId.ToString(), ["sourceVersion"] = origin.SourceVersion.ToString(), ["sourceFingerprint"] = origin.SourceFingerprint, ["ownerUserId"] = origin.OwnerUserId.ToString() }), ct);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return await OpenAsync(company, task.Id, ct);
            }
            catch (DbUpdateException ex) when (!IsDeadlock(ex))
            {
                await tx.RollbackAsync(ct); await tx.DisposeAsync(); db.ChangeTracker.Clear();
                existing = await Existing(company, command, ct);
                if (existing != null) return await Replay(company, existing, member.UserId, commandHash, ct);
                throw;
            }
        }), ct);
    }
    private async Task<DecisionWorkOrigin?> Existing(Guid company, ConfirmDecisionWork c, CancellationToken ct) =>
        await Origins(company).AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == c.RequestId, ct) ??
        await Origins(company).AsNoTracking().SingleOrDefaultAsync(x => x.SourceKind == c.Input.Source.Kind && x.SourceVersionId == c.Input.Source.VersionId && x.ItemKey == c.Input.Source.ItemKey, ct);
    private Task<DecisionWorkDocument> Replay(Guid company, DecisionWorkOrigin o, Guid actor, string hash, CancellationToken ct)
    {
        if (o.CreatedByUserId != actor || o.CommandHash != hash) throw new CompanyOperatingConcurrencyException("This source finding already has owned work or the retry material differs. Open its existing work.");
        return OpenAsync(company, o.TaskId, ct);
    }
    public async Task<DecisionWorkDocument> OpenAsync(Guid company, Guid task, CancellationToken ct)
    {
        await Require(company, ct); var origin = await Origins(company).AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == task, ct) ?? throw new KeyNotFoundException();
        var detail = await taskQueries.GetByIdAsync(company, new(task), ct);
        var source = await Source(company, new(origin.SourceKind, origin.SourceVersionId, origin.ItemKey), ct);
        if (Hash(origin.PreviewJson) != origin.PreviewChecksum) throw new InvalidDataException("The retained handoff snapshot failed its integrity check.");
        var preview = JsonSerializer.Deserialize<DecisionWorkPreview>(origin.PreviewJson) ?? throw new InvalidDataException("Retained work snapshot is unavailable.");
        if (preview.CompanyId != company || preview.Source.Reference != source.Reference || preview.Source.Version != origin.SourceVersion || preview.Source.Fingerprint != origin.SourceFingerprint ||
            preview.Input.OwnerUserId != origin.OwnerUserId || preview.Input.Objective != origin.Objective || preview.Input.AcceptanceOutcome != origin.AcceptanceOutcome || preview.Input.DueUtc != origin.DueUtc)
            throw new InvalidDataException("Retained work and relational provenance disagree.");
        return new(company, task, preview, origin.CreatedUtc, detail.Status, origin.ApprovalId,
            $"/work?companyId={company:D}&taskId={task:D}", $"/work/source?companyId={company:D}&taskId={task:D}",
            source.Fingerprint != origin.SourceFingerprint ? "Source governance changed since creation. The work snapshot and authority remain unchanged. " + source.RevisionNotice : source.RevisionNotice);
    }
    public async Task<DecisionWorkDocument> SubmitReviewAsync(Guid company, Guid task, CancellationToken ct)
    {
        var member = await Require(company, ct); _ = await OpenAsync(company, task, ct);
        return await RetryDeadlock(async () => await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var origin = await Origins(company).SingleAsync(x => x.TaskId == task, ct);
            if (!origin.ApprovalId.HasValue)
            {
                var approval = await approvals.CreateAsync(company, new("task", task, "user", member.UserId, "planning_work_review",
                    new() { ["sourceFingerprint"] = JsonValue.Create(origin.SourceFingerprint), ["acceptanceOutcome"] = JsonValue.Create(origin.AcceptanceOutcome),
                        ["authorityNotice"] = JsonValue.Create(ReviewRule) }, RequiredRole: "owner"), ct);
                origin.ApprovalId = approval.Id;
                await audit.WriteAsync(new(company, "user", member.UserId, "decision.work.review_requested", AuditTargetTypes.WorkTask, task.ToString("N"), "pending",
                    "Submitted internal work to canonical human review; no tool or external action dispatched.", DataSources: ["tasks", "approvals"]), ct);
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct); return await OpenAsync(company, task, ct);
        }), ct);
    }
    private static bool IsDeadlock(Exception exception) => exception is SqlException { Number: 1205 } ||
        exception.InnerException != null && IsDeadlock(exception.InnerException);
    private async Task<DecisionWorkDocument> RetryDeadlock(Func<Task<DecisionWorkDocument>> operation, CancellationToken ct)
    {
        // A serializable source check can lose a SQL Server lock conversion race. Retry the
        // whole disposed transaction, including authorization and native commands, never a partial save.
        for (var attempt = 0; ; attempt++)
        {
            try { return await operation(); }
            catch (Exception ex) when (attempt < 2 && IsDeadlock(ex))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), ct);
            }
        }
    }
    private static void Validate(DecisionWorkInput i)
    {
        if (i.Source == null || i.Source.VersionId == Guid.Empty || string.IsNullOrWhiteSpace(i.Source.ItemKey) || i.Source.ItemKey.Length > 128 ||
            string.IsNullOrWhiteSpace(i.Objective) || i.Objective.Length > 200 || i.OwnerUserId == Guid.Empty ||
            i.DueUtc.Kind != DateTimeKind.Utc || i.DueUtc.Year is < 2000 or > 2100 || string.IsNullOrWhiteSpace(i.AcceptanceOutcome) || i.AcceptanceOutcome.Length > 2000 ||
            i.ProposedConstraints == null || i.ProposedConstraints.Length > 2000 || i.ProposedCollaborators == null || i.ProposedCollaborators.Count > 20 ||
            i.ProposedCollaborators.Any(x => x == Guid.Empty) || i.ProposedCollaborators.Distinct().Count() != i.ProposedCollaborators.Count)
            throw new ArgumentException("Enter an objective, active owner, UTC due date, acceptance outcome and valid proposed collaborators.");
        if (WorkDescription(i).Length > 4000)
            throw new ArgumentException("Shorten the outcome and constraints so the complete work description fits within 4000 characters.");
    }
    private static string WorkDescription(DecisionWorkInput input) => $"Acceptance outcome: {input.AcceptanceOutcome}\nProposed constraints: {input.ProposedConstraints}\n{ReviewRule}";
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private async Task<DecisionWorkSource> Source(Guid c, DecisionWorkSourceRef r, CancellationToken ct)
    {
        if (r.VersionId == Guid.Empty) throw new ArgumentException("Choose a saved source version.");
        int version; string fp, title, timezone, path, json; Guid? owner; DateTime saved; bool latest;
        switch (r.Kind)
        {
            case "month":
                var m = await monthly.OpenAsync(c, r.VersionId, ct);
                var finding = m.Workspace.Review?.Measures.SingleOrDefault(x => x.Key == r.ItemKey) ?? throw new KeyNotFoundException("Review finding not found.");
                version = m.Summary.Revision; fp = m.Checksum; title = finding.Label; owner = null; saved = m.Summary.SavedAtUtc; timezone = m.Workspace.Period.Timezone;
                path = $"/dashboard?companyId={c:D}&period=month&lens={m.Summary.Lens}&year={m.Summary.Year}&month={m.Summary.Month}&snapshot={r.VersionId:D}";
                json = JsonSerializer.Serialize(new { finding, m.ReviewNotes, m.Summary, m.Workspace.SourceCoverage });
                latest = !await db.Set<MonthlyReviewSnapshot>().AnyAsync(x => x.CompanyId == c && x.PreviousId == r.VersionId, ct); break;
            case "quarter":
                var q = await quarterly.OpenAsync(c, r.VersionId, ct);
                var objective = q.Review.Objectives.SingleOrDefault(x => x.Objective.GoalId.ToString("D") == r.ItemKey) ?? throw new KeyNotFoundException("Quarter objective not found.");
                if (objective.EvidenceState.Contains("protected", StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Source evidence is protected.");
                version = q.Summary.Revision; fp = q.Review.Fingerprint; title = objective.Name; owner = objective.Objective.OwnerUserId; saved = q.Summary.SavedUtc; timezone = q.Review.Period.Timezone;
                path = $"/dashboard?companyId={c:D}&period=quarter&year={q.Summary.FiscalYear}&quarter={q.Summary.Quarter}&review={r.VersionId:D}";
                json = JsonSerializer.Serialize(new { objective, q.Review.Period, q.Summary.Notes });
                latest = !await db.Set<QuarterlyReview>().AnyAsync(x => x.CompanyId == c && x.PreviousId == r.VersionId, ct); break;
            case "annual":
                var a = await annual.OpenAsync(c, r.VersionId, ct);
                if (r.ItemKey == "decision") { title = "Annual planning decision"; owner = a.Summary.AuthorUserId; json = JsonSerializer.Serialize(new { a.Summary, a.Plan.Input.Notes, a.Governance, a.Plan.BudgetTotal, a.Plan.AllocationTotal, a.Plan.ReviewBlocks }); }
                else { var goal = a.Plan.Objectives.SingleOrDefault(x => x.Input.GoalId.ToString("D") == r.ItemKey) ?? throw new KeyNotFoundException("Annual objective not found."); title = goal.Name; owner = goal.Input.OwnerUserId; json = JsonSerializer.Serialize(goal); }
                version = a.Summary.Version; fp = Hash(a.Plan.Fingerprint + ":" + a.Summary.StateRevision); saved = a.Summary.SavedUtc; timezone = a.Plan.Period.Timezone;
                path = $"/dashboard?companyId={c:D}&period=year&year={a.Summary.FiscalYear}&plan={r.VersionId:D}";
                latest = !await db.Set<AnnualPlanVersion>().AnyAsync(x => x.CompanyId == c && x.PreviousId == r.VersionId, ct); break;
            case "scenario":
                var s = await scenarios.OpenAsync(c, r.VersionId, ct);
                if (!int.TryParse(r.ItemKey, out var index) || index < 0 || index >= s.Scenario.Checkpoints.Count) throw new KeyNotFoundException("Scenario checkpoint not found.");
                var checkpoint = s.Scenario.Checkpoints[index]; version = s.Summary.Revision; fp = s.Scenario.Fingerprint; title = checkpoint.Title; owner = checkpoint.OwnerId;
                saved = s.Summary.SavedUtc; timezone = s.Scenario.Source.Timezone;
                path = $"/dashboard?companyId={c:D}&period=multiyear&scenario={r.VersionId:D}";
                json = JsonSerializer.Serialize(new { checkpoint, s.Scenario.Source, Result = s.Scenario.Years.Single(x => x.Year == checkpoint.Year), s.Scenario.Warnings, s.Scenario.Limitations });
                latest = !await db.Set<StrategicScenarioVersion>().AnyAsync(x => x.CompanyId == c && x.PreviousId == r.VersionId, ct); break;
            default: throw new ArgumentException("Choose a monthly finding, quarter objective, annual decision or scenario checkpoint.");
        }
        return new(c, r, version, fp, title, owner, saved, timezone, path, json, latest,
            latest ? "This is the latest saved source revision. Work retains the snapshot captured at creation." : "A newer source revision exists. Existing work and authority are unchanged; inspect the original retained version.");
    }
}
