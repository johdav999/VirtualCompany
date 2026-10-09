using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class MarketingManagementService
{
    private sealed record Stored(MarketingManagementReport Report, MarketingBudgetAssumptions Assumptions, MarketingBudgetResult Result);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static MarketingBudgetProposalSummary Summary(MarketingBudgetProposalRevision r) => new(r.Id, r.SeriesId,
        r.Revision, r.PreviousId, r.AccountableUserId, Utc(r.SavedAtUtc), Utc(r.SourceAsOfUtc), r.Year, r.Month, r.Currency);
    private static MarketingBudgetProposal Reproduce(MarketingBudgetProposalRevision r)
    {
        if (Encoding.UTF8.GetByteCount(r.Payload) > 1024 * 1024 || Hash(r.Payload) != r.Checksum)
            throw new InvalidDataException("Proposal integrity check failed.");
        Stored? stored;
        try { stored = JsonSerializer.Deserialize<Stored>(r.Payload); }
        catch (JsonException ex) { throw new InvalidDataException("Proposal payload is unreadable.", ex); }
        if (stored?.Report is not { } report || stored.Assumptions is null || stored.Result is null ||
            report.Query is null || report.Campaigns is null || report.Plans is null || report.Channels is null || report.Costs is null ||
            report.Attribution is null || report.Models is null || report.Experiments is null || report.Coverage is null ||
            report.CompanyId != r.CompanyId || report.CalculationVersion != Version || report.Query.Year != r.Year ||
            report.Query.Month != r.Month || report.Query.Currency != r.Currency || report.AsOfUtc != Utc(r.SourceAsOfUtc))
            throw new InvalidDataException("Retained report context cannot be reproduced.");
        MarketingBudgetResult result;
        try { result = MarketingBudgetCalculation.Calculate(stored.Assumptions, report); }
        catch (ArgumentException ex) { throw new InvalidDataException("Retained budget assumptions are invalid.", ex); }
        if (JsonSerializer.Serialize(result) != JsonSerializer.Serialize(stored.Result))
            throw new InvalidDataException("Retained budget results differ from their assumptions.");
        return new(Summary(r), report, stored.Assumptions, result, r.Checksum,
            "Private immutable revisions retained until company deletion. Current Marketing access required. Source links open current records; the included report is the original snapshot.");
    }
    public async Task<MarketingBudgetProposal> OpenAsync(Guid company, Guid id, CancellationToken ct)
    {
        var scope = await Authorize(company, ct);
        var row = await db.MarketingBudgetProposalRevisions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == id && x.AccountableUserId == scope.UserId, ct);
        return row is null ? throw new KeyNotFoundException() : Reproduce(row);
    }
    public async Task<IReadOnlyList<MarketingBudgetProposalSummary>> HistoryAsync(Guid company, int skip, CancellationToken ct)
    {
        if (skip is < 0 or > 10000) throw new ArgumentException("History page is outside the supported range.");
        var scope = await Authorize(company, ct);
        return (await db.MarketingBudgetProposalRevisions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == company && x.AccountableUserId == scope.UserId)
            .OrderByDescending(x => x.SavedAtUtc).ThenByDescending(x => x.Id).Skip(skip).Take(20).ToListAsync(ct))
            .Select(Summary).ToArray();
    }
    public async Task<MarketingBudgetProposal> SaveAsync(Guid company, SaveMarketingBudgetProposal command, CancellationToken ct)
    {
        var scope = await Authorize(company, ct);
        if (command.RequestId == Guid.Empty || command.Query is null || command.Assumptions is null)
            throw new ArgumentException("A request identity, report cohort and assumptions are required.");
        var currency = Currency(command.Query.Currency) ?? throw new ArgumentException("Choose one currency before proposing a budget.");
        var query = command.Query with { Currency = currency };
        var retry = await db.MarketingBudgetProposalRevisions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == company && x.AccountableUserId == scope.UserId && x.RequestId == command.RequestId, ct);
        if (retry is not null)
        {
            var original = Reproduce(retry);
            if (original.Summary.PreviousId != command.PreviousId || original.Report.Query != query ||
                JsonSerializer.Serialize(original.Assumptions) != JsonSerializer.Serialize(command.Assumptions) ||
                command.ExpectedRevision != (original.Summary.PreviousId.HasValue ? original.Summary.Revision - 1 : (int?)null))
                throw new InvalidOperationException("Request identity already contains different planning assumptions.");
            return original;
        }
        MarketingBudgetProposal? previous = null;
        if (command.PreviousId.HasValue)
        {
            previous = await OpenAsync(company, command.PreviousId.Value, ct);
            if (previous.Report.Query != query || command.ExpectedRevision != previous.Summary.Revision ||
                await db.MarketingBudgetProposalRevisions.IgnoreQueryFilters()
                    .AnyAsync(x => x.CompanyId == company && x.PreviousId == command.PreviousId, ct))
                throw new InvalidOperationException("Proposal changed. Open its latest revision.");
        }
        else if (command.ExpectedRevision.HasValue) throw new ArgumentException("An expected revision requires a predecessor.");
        var report = await ReportAsync(company, query, ct);
        var result = MarketingBudgetCalculation.Calculate(command.Assumptions, report);
        await Authorize(company, ct);
        var payload = JsonSerializer.Serialize(new Stored(report, command.Assumptions, result));
        var row = new MarketingBudgetProposalRevision(company, scope.UserId, command.RequestId,
            previous?.Summary.SeriesId ?? Guid.NewGuid(), (previous?.Summary.Revision ?? 0) + 1, command.PreviousId,
            query.Year, query.Month, currency, clock.GetUtcNow().UtcDateTime, report.AsOfUtc, payload, Hash(payload));
        db.MarketingBudgetProposalRevisions.Add(row);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), company, "user", scope.UserId, "marketing.budget.proposal_saved",
            "marketing_budget_proposal", row.Id.ToString("D"), "succeeded",
            $"Saved revision {row.Revision}; {result.ProposedTotal} {result.Currency}; source snapshot {report.AsOfUtc:O}; model {query.ModelId}; {report.Costs.Count} costs and {report.Attribution.Count} outcome runs. Campaign budgets, content and spend authority unchanged."));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.MarketingBudgetProposalRevisions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == company &&
                x.AccountableUserId == scope.UserId && x.RequestId == command.RequestId, ct)) return await SaveAsync(company, command, ct);
            if (command.PreviousId.HasValue && await db.MarketingBudgetProposalRevisions.IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == company && x.PreviousId == command.PreviousId, ct))
                throw new InvalidOperationException("Another revision was saved. Reload before retrying.");
            throw;
        }
        return Reproduce(row);
    }

    public async Task<MarketingManagementExport> ExportAsync(Guid company, MarketingManagementQuery query, CancellationToken ct)
    {
        var report = await ReportAsync(company, query, ct); // Fresh authorization and the identical definition, never a client-supplied total.
        var csv = new StringBuilder();
        void Row(params object?[] cells) => csv.AppendLine(string.Join(",", cells.Select(Escape)));
        Row("Company", company, "Year", report.Query.Year, "Month", report.Query.Month, "Timezone", report.Timezone,
            "StartUtc", report.StartUtc, "EndUtcExclusive", report.EndUtc, "AsOfUtc", report.AsOfUtc,
            "Currency", report.Query.Currency, "Campaign", report.Query.CampaignId, "SegmentVersion", report.Query.SegmentVersionId,
            "ModelVersionId", report.Query.ModelId, "Calculation", report.CalculationVersion);
        foreach (var coverage in report.Coverage) Row("Coverage", coverage);
        Row("Channel", "Currency", "KnownCost", "IncludedTouches", "UnknownCosts", "AttributedTouches", "Unit", "AttributedValue", "CostPerAttributedUnit", "Coverage");
        foreach (var channel in report.Channels)
        {
            if (channel.Economics.Count == 0) Row(channel.Channel, channel.Currency, channel.KnownCost, channel.IncludedTouches,
                channel.UnknownCostTouches, channel.AttributedTouches, null, null, null, channel.Coverage);
            foreach (var value in channel.Economics) Row(channel.Channel, channel.Currency, channel.KnownCost, channel.IncludedTouches,
                channel.UnknownCostTouches, channel.AttributedTouches, value.Unit, value.AttributedValue, value.CostPerAttributedUnit, channel.Coverage);
        }
        Row("TouchId", "SubjectType", "SubjectId", "Channel", "Currency", "Cost", "SourceVersion", "Included", "OccurredUtc", "Source", "Coverage");
        foreach (var cost in report.Costs) Row(cost.Id, cost.SubjectType, cost.SubjectId, cost.Channel, cost.Currency, cost.Cost,
            cost.SourceVersion, cost.Included, cost.OccurredUtc, cost.SourceReference, cost.Coverage);
        Row("RunId", "Model", "ModelId", "ModelVersion", "ConfiguredLookbackDays", "StartUtc", "EndUtc", "Unit", "Value", "Included", "Coverage", "Limitations");
        foreach (var run in report.Attribution)
        {
            Row(run.Id, run.Model, run.ModelId, run.ModelVersion, run.LookbackDays, run.StartUtc, run.EndUtc,
                run.Unit, run.Value, run.Included, run.Coverage, run.Limitations);
            foreach (var allocation in run.Allocations) Row("Allocation", run.Id, allocation.TouchId, allocation.Weight, allocation.Value, allocation.EvidenceVersion);
        }
        foreach (var model in report.Models) Row("Model", model.Id, model.Name, model.ModelType, model.Version, model.LookbackDays, model.RulesJson, model.Limitations);
        foreach (var campaign in report.Campaigns)
        {
            Row("Campaign", campaign.Id, campaign.Name, campaign.Currency, campaign.RecordedBudget, "Current recorded context");
            foreach (var segment in campaign.Segments) Row("SegmentLink", campaign.Id, segment.LinkId, segment.SegmentVersionId, segment.Version, segment.Rationale, "Current campaign association, not segment-specific outcomes");
        }
        foreach (var plan in report.Plans)
        {
            Row("Plan", plan.Id, plan.Name, plan.Currency, plan.Ceiling, "Recorded planning ceiling, not spend authority");
            foreach (var allocation in plan.Allocations) Row("PlanAllocation", plan.Id, allocation.CampaignId, allocation.Amount, allocation.Currency);
        }
        foreach (var experiment in report.Experiments)
        {
            Row("Experiment", experiment.Id, experiment.Name, experiment.CampaignId, experiment.Hypothesis, experiment.PrimaryMetric, experiment.GuardrailMetric,
                experiment.MinimumSample, experiment.StartUtc, experiment.EndUtc, experiment.RecordedExposures, experiment.Coverage);
            if (experiment.Decision is { } decision) Row("ExperimentDecision", experiment.Id, decision.Id, decision.Decision, decision.SampleSize,
                decision.ContaminationRate, decision.GuardrailBreached, decision.CausalEligible, decision.EvidenceJson, decision.Limitations);
        }
        foreach (var health in report.SourceHealth ?? []) Row("SourceHealth", health.Provider, health.Name, health.ConnectionState, health.HealthState, health.CheckedUtc, health.Coverage);
        return new($"marketing-management-{query.Year:D4}-{query.Month:D2}.csv", csv.ToString());
    }
    private static string Escape(object? value)
    {
        var text = value is DateTime date ? Utc(date).ToString("O", System.Globalization.CultureInfo.InvariantCulture) :
            Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
