namespace VirtualCompany.Web.Services;

/// <summary>Only compares authorized snapshots within this user's circuit. No durable business state.</summary>
public sealed class TodayPriorityChanges
{
    private readonly Dictionary<(Guid Company, string Lens), (DateTime Checked, Dictionary<string, string> Items)> snapshots = [];
    public bool HasSeen(Guid companyId, string? lens) => snapshots.Keys.Any(x => x.Company == companyId && (lens is null || x.Lens == lens));

    public (int Changed, int Removed, DateTime? PreviousCheck) Observe(TodayWorkspaceViewModel workspace, bool updateBaseline = true)
    {
        var scope = (workspace.CompanyId, workspace.ActiveLens);
        var items = workspace.Priorities.ToDictionary(x => x.Key,
            x => $"{x.WhatHappened}|{x.WhyItMatters}|{x.ObservedAtUtc:O}|{x.DueUtc:O}|{x.SourceState}|{x.DecisionRequired}|{x.Rank}");
        snapshots.TryGetValue(scope, out var prior);
        // A fresh session/day starts without pretending to have a persisted daily baseline.
        var hasPrior = prior.Items is not null && workspace.GeneratedAtUtc.Date == prior.Checked.Date;
        var changed = hasPrior ? items.Count(x => !prior.Items!.TryGetValue(x.Key, out var old) || old != x.Value) : 0;
        var removed = hasPrior ? prior.Items!.Keys.Count(x => !items.ContainsKey(x)) : 0;
        if (updateBaseline)
        {
            if (snapshots.Count >= 20 && !snapshots.ContainsKey(scope)) snapshots.Clear();
            snapshots[scope] = (workspace.GeneratedAtUtc, items);
        }
        return (changed, removed, hasPrior ? prior.Checked : null);
    }
}
