using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class ProspectDataProviderRegistry(IEnumerable<IProspectDataProvider> providers) : IProspectDataProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IProspectDataProvider> _providers = providers.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ProspectProviderDescriptor> List() => _providers.Values.Select(x => new ProspectProviderDescriptor(x.Key, x.Key == "first_party" ? "Company-owned data" : x.Key, x.Capabilities, "Available")).ToList();
    public IProspectDataProvider Resolve(string key) => _providers.TryGetValue(key, out var provider) ? provider : throw new LeadGenerationValidationException($"Prospect source '{key}' is not available.");
}

public sealed class FirstPartyProspectDataProvider(VirtualCompanyDbContext db) : IProspectDataProvider
{
    public string Key => "first_party";
    public ProspectProviderCapabilities Capabilities => new(true, true, false, false, false);

    public async Task<ProspectProviderPage> SearchAccountsAsync(Guid companyId, ProspectProviderSearch request, CancellationToken ct)
    {
        var companies = await db.CustomerCompanies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted)
            .OrderBy(x => x.Id).Take(request.Limit)
            .Select(x => new ProspectAccountInput(x.Name, x.Website, null, x.Industry, null, null, null, Key, $"customer-company:{x.Id:D}", x.UpdatedUtc))
            .ToListAsync(ct);
        return new ProspectProviderPage(companies, null, 0m, true);
    }
}

public sealed class CrmLeadAdapterRegistry(IEnumerable<ICrmLeadAdapter> adapters) : ICrmLeadAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, ICrmLeadAdapter> _adapters = adapters.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Keys => _adapters.Keys.OrderBy(x => x).ToList();
    public ICrmLeadAdapter Resolve(string key) => _adapters.TryGetValue(key, out var adapter) ? adapter : throw new LeadGenerationValidationException("The requested CRM is not connected.");
}

public sealed class LeadGenerationService(VirtualCompanyDbContext db, IProspectDataProviderRegistry providers, IApprovalRequestService approvals, ICrmLeadAdapterRegistry crmAdapters, ISalesSourceService sources) : ILeadGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<IcpProfileDto>> ListProfilesAsync(Guid companyId, CancellationToken ct) =>
        (await db.IdealCustomerProfiles.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Name).ThenByDescending(x => x.Version).ToListAsync(ct)).Select(Map).ToList();

    public async Task<IcpProfileDto> CreateProfileAsync(Guid companyId, Guid userId, SaveIcpProfileRequest request, CancellationToken ct)
    {
        var version = (await db.IdealCustomerProfiles.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.Name == request.Name.Trim()).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
        var entity = NewProfile(companyId, userId, request, version, null);
        db.IdealCustomerProfiles.Add(entity); Audit(companyId, userId, "sales.icp.created", "icp", entity.Id, "ICP draft created."); await db.SaveChangesAsync(ct); return Map(entity);
    }

    public async Task<IcpProfileDto> UpdateProfileAsync(Guid companyId, Guid userId, Guid id, SaveIcpProfileRequest r, CancellationToken ct)
    {
        var x = await Profile(companyId, id, ct); x.UpdateDraft(r.Countries, r.Industries, r.EmployeeMin, r.EmployeeMax, r.RevenueMin, r.RevenueMax, r.BuyerRoles, r.Technologies, r.PainHypotheses, r.PositiveCriteria, r.Disqualifiers, userId); Audit(companyId, userId, "sales.icp.updated", "icp", id, "ICP draft updated."); await db.SaveChangesAsync(ct); return Map(x);
    }

    public async Task<IcpProfileDto> ActivateProfileAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct)
    {
        var x = await Profile(companyId, id, ct); x.Activate();
        var previous = await db.IdealCustomerProfiles.IgnoreQueryFilters().Where(p => p.CompanyId == companyId && p.Name == x.Name && p.Id != x.Id && p.Status == LeadGenerationStatuses.Active).ToListAsync(ct);
        foreach (var item in previous) item.Supersede();
        Audit(companyId, userId, "sales.icp.activated", "icp", id, "ICP version activated."); await db.SaveChangesAsync(ct); return Map(x);
    }

    public async Task<IcpProfileDto> CloneProfileAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct)
    {
        var x = await Profile(companyId, id, ct); var version = (await db.IdealCustomerProfiles.IgnoreQueryFilters().Where(p => p.CompanyId == companyId && p.Name == x.Name).MaxAsync(p => (int?)p.Version, ct) ?? 0) + 1;
        var clone = new IdealCustomerProfile(Guid.NewGuid(), companyId, x.Name, version, userId, x.Countries, x.Industries, x.EmployeeMin, x.EmployeeMax, x.RevenueMin, x.RevenueMax, x.BuyerRoles, x.Technologies, x.PainHypotheses, x.PositiveCriteria, x.Disqualifiers, x.Id);
        db.IdealCustomerProfiles.Add(clone); Audit(companyId, userId, "sales.icp.cloned", "icp", clone.Id, "A new ICP draft version was created."); await db.SaveChangesAsync(ct); return Map(clone);
    }

    public async Task ArchiveProfileAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct) { var x = await Profile(companyId, id, ct); x.Archive(); Audit(companyId, userId, "sales.icp.archived", "icp", id, "ICP version archived."); await db.SaveChangesAsync(ct); }
    public async Task<IcpPreviewDto> PreviewProfileAsync(Guid companyId, Guid id, ProspectAccountInput request, CancellationToken ct) => Match(await Profile(companyId, id, ct), request);

    public async Task<SourcePolicyDto> GetSourcePolicyAsync(Guid companyId, CancellationToken ct)
    {
        var x = await db.ProspectSourcePolicies.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.CompanyId == companyId, ct);
        if (x is null) { x = new ProspectSourcePolicy(Guid.NewGuid(), companyId); db.Add(x); await db.SaveChangesAsync(ct); }
        return Map(x);
    }

    public async Task<SourcePolicyDto> UpdateSourcePolicyAsync(Guid companyId, Guid userId, SaveSourcePolicyRequest r, CancellationToken ct)
    {
        var x = await db.ProspectSourcePolicies.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.CompanyId == companyId, ct) ?? new ProspectSourcePolicy(Guid.NewGuid(), companyId);
        if (db.Entry(x).State == EntityState.Detached) db.Add(x);
        var available = providers.List().Select(p => p.Key).Append("csv").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requested = Split(r.EnabledSources); if (requested.Any(s => !available.Contains(s))) throw new LeadGenerationValidationException("One or more selected prospect sources are unavailable.");
        x.Update(r.EnabledSources, r.AllowedCountries, r.AllowedFields, r.PerRunBudget, r.MonthlyBudget, r.ApprovalThreshold, r.RetentionDays, r.RefreshDays); Audit(companyId, userId, "sales.prospect_source_policy.updated", "source_policy", x.Id, "Prospect source policy updated."); await db.SaveChangesAsync(ct); return Map(x);
    }

    public async Task<IReadOnlyList<ProspectingRunDto>> ListRunsAsync(Guid companyId, CancellationToken ct) =>
        (await db.ProspectingRuns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.CreatedUtc).ToListAsync(ct)).Select(Map).ToList();

    public async Task<ProspectingRunDto> CreateRunAsync(Guid companyId, Guid userId, CreateProspectingRunRequest r, CancellationToken ct)
    {
        var profile = await Profile(companyId, r.IcpProfileId, ct); if (profile.Status != LeadGenerationStatuses.Active) throw new LeadGenerationValidationException("Choose an active ideal customer profile.");
        var policy = await Policy(companyId, ct); var requested = Split(r.Sources); var enabled = Split(policy.EnabledSources).ToHashSet(StringComparer.OrdinalIgnoreCase); if (requested.Count == 0 || requested.Any(x => !enabled.Contains(x))) throw new LeadGenerationValidationException("The run includes a source that is not enabled.");
        if (r.EstimatedCost > 0) policy.Reserve(r.EstimatedCost);
        var run = new ProspectingRun(Guid.NewGuid(), companyId, r.IcpProfileId, userId, r.Name, r.AccountLimit, r.ContactLimit, r.Sources, r.Geography, r.FreshnessDays, r.EstimatedCost, r.Schedule);
        db.Add(run); Audit(companyId, userId, "sales.prospecting_run.created", "prospecting_run", run.Id, "Prospecting run planned."); await db.SaveChangesAsync(ct);
        var usesPaidSource = requested.Any(key => providers.List().Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && p.Capabilities.IsPaid));
        if (usesPaidSource || r.EstimatedCost > policy.ApprovalThreshold)
        {
            var approval = await approvals.CreateAsync(companyId, new CreateApprovalRequestCommand("prospecting_run", run.Id, "human", userId, "sales_prospect_data_spend", new Dictionary<string, JsonNode?> { ["estimatedCost"] = r.EstimatedCost, ["sources"] = r.Sources }, RequiredRole: "company_manager"), ct);
            run.SetApproval(approval.Id); await db.SaveChangesAsync(ct);
        }
        return Map(run);
    }

    public async Task<ProspectingRunDto> StartRunAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct)
    {
        var x = await Run(companyId, id, ct);
        if (x.ApprovalId.HasValue)
        {
            var approval = await db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.CompanyId == companyId && a.Id == x.ApprovalId, ct);
            if (approval is null || !approval.Status.ToString().Equals("approved", StringComparison.OrdinalIgnoreCase)) throw new LeadGenerationValidationException("Approve the prospect data spend before starting this run.");
        }
        x.Start(); Audit(companyId, userId, "sales.prospecting_run.started", "prospecting_run", id, "Prospecting run started."); await db.SaveChangesAsync(ct); return Map(x);
    }

    public async Task<ProspectingRunDto> ChangeRunAsync(Guid companyId, Guid userId, Guid id, string action, CancellationToken ct)
    {
        var x = await Run(companyId, id, ct); switch (action.Trim().ToLowerInvariant()) { case "pause": x.Pause(); break; case "cancel": x.Cancel(); break; case "retry": x.Start(); break; default: throw new LeadGenerationValidationException("Unsupported run action."); }
        Audit(companyId, userId, $"sales.prospecting_run.{action.ToLowerInvariant()}", "prospecting_run", id, $"Prospecting run {action.ToLowerInvariant()} requested."); await db.SaveChangesAsync(ct); return Map(x);
    }

    public async Task<ImportResultDto> ImportCsvAsync(Guid companyId, Guid userId, Guid runId, Stream content, string fileName, CancellationToken ct)
    {
        var run = await Run(companyId, runId, ct); if (run.Status is LeadGenerationStatuses.Cancelled or LeadGenerationStatuses.Completed) throw new LeadGenerationValidationException("This run no longer accepts imports.");
        var rows = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadXlsx(content) : await ReadCsvAsync(content, ct);
        if (rows.Count > run.AccountLimit) rows = rows.Take(run.AccountLimit).ToList();
        var imported = 0; var duplicates = 0; var rejected = 0; var errors = new List<string>(); var rowNumber = 1;
        foreach (var row in rows) { rowNumber++; try { var input = Input(row, $"import:{runId:N}:{rowNumber}"); var existing = await FindAccount(companyId, input.SourceKey, input.SourceReference, input.Domain, ct); if (existing is not null) { duplicates++; continue; } var profile = await Profile(companyId, run.IdealCustomerProfileId, ct); var account = NewAccount(companyId, run, input, profile); db.Add(account); await RecordProspectSource(companyId, userId, account, input, "file_import", ct); imported++; } catch (Exception ex) when (ex is ArgumentException or LeadGenerationValidationException) { rejected++; errors.Add($"Row {rowNumber}: {ex.Message}"); } }
        run.Progress(imported, 0, "Imported company-owned data", null, 0); Audit(companyId, userId, "sales.prospect_import.completed", "prospecting_run", runId, $"Imported {imported} prospects; {rejected} rows need correction."); await db.SaveChangesAsync(ct); return new(imported, duplicates, rejected, errors.Take(200).ToList());
    }

    public async Task<ProspectPageDto> ListAccountsAsync(Guid companyId, ProspectQuery q, CancellationToken ct)
    {
        var query = db.ProspectAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(q.Search)) query = query.Where(x => x.Name.Contains(q.Search) || (x.Domain != null && x.Domain.Contains(q.Search)));
        if (!string.IsNullOrWhiteSpace(q.Status)) query = query.Where(x => x.Status == q.Status.ToLower()); if (!string.IsNullOrWhiteSpace(q.Country)) query = query.Where(x => x.Country == q.Country.ToLower()); if (!string.IsNullOrWhiteSpace(q.Source)) query = query.Where(x => x.SourceKey == q.Source.ToLower());
        var total = await query.CountAsync(ct); query = q.Sort == "newest" ? query.OrderByDescending(x => x.CreatedUtc) : query.OrderByDescending(x => x.OverallScore).ThenBy(x => x.Name); var size = Math.Clamp(q.PageSize, 1, 200); var page = Math.Max(1, q.Page);
        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct); var mapped = new List<ProspectAccountDto>(); foreach (var x in items) mapped.Add(await MapAccount(x, ct)); return new(mapped, total, page, size);
    }

    public async Task<ProspectAccountDto?> GetAccountAsync(Guid companyId, Guid id, CancellationToken ct) { var x = await db.ProspectAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.CompanyId == companyId && a.Id == id, ct); return x is null ? null : await MapAccount(x, ct); }

    public async Task<ProspectAccountDto> AddAccountAsync(Guid companyId, Guid userId, Guid runId, ProspectAccountInput input, CancellationToken ct)
    {
        var run = await Run(companyId, runId, ct); var profile = await Profile(companyId, run.IdealCustomerProfileId, ct); var existing = await FindAccount(companyId, input.SourceKey, input.SourceReference, input.Domain, ct); if (existing is not null) return await MapAccount(existing, ct);
        var x = NewAccount(companyId, run, input, profile); db.Add(x); await sources.StageAsync(companyId, new RecordSalesSourceTouchRequest("prospect_account", x.Id, input.SourceKey == "csv" ? SalesSourceCategories.Import : input.SourceKey == "first_party" ? SalesSourceCategories.Research : SalesSourceCategories.Manual, input.SourceKey, input.SourceKey == "csv" ? "file_import" : "prospecting", "discovery", input.SourceReference, input.ObservedUtc, "agent", userId.ToString("D"), Evidence: $"{x.Name} was added to prospecting run {run.Name}."), ct); run.Progress(run.AccountsFound + 1, run.ContactsFound, "Reviewing account matches", null, run.ActualCost); Audit(companyId, userId, "sales.prospect_account.discovered", "prospect_account", x.Id, "Prospect account discovered."); await db.SaveChangesAsync(ct); return await MapAccount(x, ct);
    }

    public async Task<ProspectAccountDto> ReviewAccountAsync(Guid companyId, Guid userId, Guid id, ReviewProspectRequest r, CancellationToken ct)
    {
        var x = await Account(companyId, id, ct); switch (r.Action.Trim().ToLowerInvariant()) { case "accept": await EnsureEligible(companyId, x, null, ct); x.Accept(); break; case "reject": x.Reject(r.Reason ?? "Not a fit"); break; case "research": x.SetResearchBrief(BuildBrief(x, await Signals(companyId, id, ct), [])); break; default: throw new LeadGenerationValidationException("Unsupported review action."); }
        Audit(companyId, userId, $"sales.prospect_account.{r.Action.ToLowerInvariant()}", "prospect_account", id, r.Reason ?? "Prospect reviewed."); await db.SaveChangesAsync(ct); return await MapAccount(x, ct);
    }

    public async Task<ProspectAccountDto> MergeAccountAsync(Guid companyId, Guid userId, Guid sourceId, Guid targetId, CancellationToken ct)
    {
        var source = await Account(companyId, sourceId, ct); var target = await Account(companyId, targetId, ct); if (source.Status == LeadGenerationStatuses.Converted) throw new LeadGenerationValidationException("A converted account cannot be merged.");
        var contacts = await db.ProspectContacts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.ProspectAccountId == sourceId).ToListAsync(ct);
        var signals = await db.ProspectSignals.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.ProspectAccountId == sourceId).ToListAsync(ct);
        foreach (var contact in contacts) contact.ReassignAccount(target.Id);
        foreach (var signal in signals) signal.ReassignAccount(target.Id);
        source.MergeInto(target.Id); Audit(companyId, userId, "sales.prospect_account.merged", "prospect_account", source.Id, $"Merged into {target.Name} with {contacts.Count} contacts and {signals.Count} signals retained."); await db.SaveChangesAsync(ct); return await MapAccount(target, ct);
    }

    public async Task<ProspectContactDto> AddContactAsync(Guid companyId, Guid userId, Guid accountId, SaveProspectContactRequest r, CancellationToken ct)
    {
        _ = await Account(companyId, accountId, ct); var existing = await db.ProspectContacts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SourceKey == r.SourceKey.ToLower() && x.SourceReference == r.SourceReference, ct); if (existing is not null) return Map(existing);
        var x = new ProspectContact(Guid.NewGuid(), companyId, accountId, r.FullName, r.Title, r.BuyingRoles, r.SourceKey, r.SourceReference); x.Enrich(r.Title, r.Department, r.Seniority, r.Email, r.EmailStatus, r.Phone, r.ProfileUrl, r.Confidence, DateTime.UtcNow); db.Add(x); Audit(companyId, userId, "sales.prospect_contact.discovered", "prospect_contact", x.Id, "Buying-group contact discovered."); await db.SaveChangesAsync(ct); return Map(x);
    }

    public async Task<ProspectContactDto> ReviewContactAsync(Guid companyId, Guid userId, Guid id, ReviewProspectRequest r, CancellationToken ct)
    {
        var x = await db.ProspectContacts.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Id == id, ct) ?? throw new KeyNotFoundException();
        switch (r.Action.Trim().ToLowerInvariant()) { case "accept": await EnsureEligible(companyId, await Account(companyId, x.ProspectAccountId, ct), x, ct); x.Accept(); break; case "reject": x.Reject(r.Reason ?? "Not relevant"); break; case "employment_changed": x.MarkEmploymentChanged(); await CancelPendingCampaigns(companyId, x.Email, ct); break; default: throw new LeadGenerationValidationException("Unsupported contact action."); }
        Audit(companyId, userId, $"sales.prospect_contact.{r.Action.ToLowerInvariant()}", "prospect_contact", id, r.Reason ?? "Contact reviewed."); await db.SaveChangesAsync(ct); return Map(x);
    }

    public async Task<ProspectContactDto> MergeContactAsync(Guid companyId, Guid userId, Guid sourceId, Guid targetId, CancellationToken ct)
    {
        var source = await db.ProspectContacts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sourceId, ct) ?? throw new KeyNotFoundException(); var target = await db.ProspectContacts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == targetId, ct) ?? throw new KeyNotFoundException(); if (source.ProspectAccountId != target.ProspectAccountId) throw new LeadGenerationValidationException("Only contacts in the same prospect account can be merged."); source.MergeInto(target.Id); Audit(companyId, userId, "sales.prospect_contact.merged", "prospect_contact", source.Id, $"Duplicate contact merged into {target.FullName}."); await db.SaveChangesAsync(ct); return Map(target);
    }

    public async Task<ProspectSignalDto> AddSignalAsync(Guid companyId, Guid userId, Guid accountId, SaveProspectSignalRequest r, CancellationToken ct)
    {
        _ = await Account(companyId, accountId, ct); var dedupe = $"{accountId:N}:{r.Type.Trim().ToLowerInvariant()}:{r.EventUtc:yyyyMMdd}:{Normalize(r.Summary)}"; var existing = await db.ProspectSignals.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.CompanyId == companyId && s.DedupeKey == dedupe, ct); if (existing is not null) return Map(existing);
        var x = new ProspectSignal(Guid.NewGuid(), companyId, accountId, r.Type, r.SourceKey, r.SourceReference, r.Summary, r.EventUtc, r.Confidence, r.FreshnessDays, dedupe); x.SetRelevance(Relevance(x)); db.Add(x); Audit(companyId, userId, "sales.prospect_signal.recorded", "prospect_signal", x.Id, "Buying signal recorded."); await db.SaveChangesAsync(ct); return Map(x);
    }

    public async Task<ProspectSignalDto> ReviewSignalAsync(Guid companyId, Guid userId, Guid id, string action, CancellationToken ct)
    {
        var x = await db.ProspectSignals.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.CompanyId == companyId && s.Id == id, ct) ?? throw new KeyNotFoundException(); switch (action.Trim().ToLowerInvariant()) { case "dismiss": x.Dismiss(); break; case "contradict": x.Contradict(); break; default: throw new LeadGenerationValidationException("Unsupported signal action."); } Audit(companyId, userId, $"sales.prospect_signal.{action.ToLowerInvariant()}", "prospect_signal", id, "Signal feedback recorded."); await db.SaveChangesAsync(ct); return Map(x);
    }

    public async Task<ProspectAccountDto> RefreshResearchAndScoreAsync(Guid companyId, Guid userId, Guid accountId, CancellationToken ct)
    {
        var x = await Account(companyId, accountId, ct); var contacts = await db.ProspectContacts.IgnoreQueryFilters().Where(c => c.CompanyId == companyId && c.ProspectAccountId == accountId).ToListAsync(ct); var signals = await Signals(companyId, accountId, ct);
        var fresh = signals.Where(s => s.Status == "confirmed" && s.FreshUntilUtc >= DateTime.UtcNow).OrderByDescending(s => s.EventUtc).Take(5).ToList(); var timing = fresh.Count == 0 ? 0 : Math.Min(100m, fresh.Sum(s => s.Relevance * s.Confidence) / Math.Max(1, fresh.Count)); var role = contacts.Count == 0 ? 0 : Math.Min(100m, contacts.Where(c => c.Status != LeadGenerationStatuses.Rejected).SelectMany(c => Split(c.BuyingRoles)).Distinct().Count() * 25m); var confidence = DataConfidence(x, contacts); var overall = x.FitScore * .4m + timing * .25m + role * .2m + confidence * .15m; var band = overall >= 75 ? "High priority" : overall >= 50 ? "Medium priority" : "Low priority";
        x.ApplyScores(timing, role, confidence, overall, band, JsonSerializer.Serialize(new { version = 1, fit = x.FitScore, timing, role, dataConfidence = confidence, overall, calculatedUtc = DateTime.UtcNow }, JsonOptions)); x.SetResearchBrief(BuildBrief(x, signals, contacts)); Audit(companyId, userId, "sales.prospect_account.scored", "prospect_account", x.Id, "Prospect research and score refreshed."); await db.SaveChangesAsync(ct); return await MapAccount(x, ct);
    }

    public async Task<LeadConversionDto> ConvertAsync(Guid companyId, Guid userId, Guid accountId, Guid? contactId, CancellationToken ct)
    {
        var account = await Account(companyId, accountId, ct); ProspectContact? prospectContact = null; if (contactId.HasValue) prospectContact = await db.ProspectContacts.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Id == contactId && c.ProspectAccountId == accountId, ct) ?? throw new LeadGenerationValidationException("The selected contact does not belong to this account.");
        await EnsureEligible(companyId, account, prospectContact, ct); if (account.LeadId.HasValue) return new(account.Id, account.CustomerCompanyId!.Value, prospectContact?.ContactId, account.LeadId.Value, true);
        var customer = await db.CustomerCompanies.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.CompanyId == companyId && !c.IsDeleted && account.Domain != null && c.Website != null && c.Website.Contains(account.Domain), ct); if (customer is null) { customer = new CustomerCompany(Guid.NewGuid(), companyId, account.Name, website: account.Domain, industry: account.Industry); db.Add(customer); }
        Contact? contact = null; if (prospectContact is not null) { if (prospectContact.Status != LeadGenerationStatuses.Accepted) throw new LeadGenerationValidationException("Review and accept the contact first."); if (!string.IsNullOrWhiteSpace(prospectContact.Email)) contact = await db.Contacts.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Email == prospectContact.Email, ct); if (contact is null && !string.IsNullOrWhiteSpace(prospectContact.Email)) { contact = new Contact(Guid.NewGuid(), companyId, prospectContact.FullName, prospectContact.Email, customer.Id, title: prospectContact.Title, phone: prospectContact.Phone); db.Add(contact); } prospectContact.Accept(contact?.Id); }
        var existing = await db.Leads.IgnoreQueryFilters().SingleOrDefaultAsync(l => l.CompanyId == companyId && !l.IsDeleted && l.Status != SalesStatuses.Converted && l.CustomerCompanyId == customer.Id, ct); if (existing is null) { existing = new Lead(Guid.NewGuid(), companyId, account.Name, SalesPipelineStage.NewStageId, primaryContactId: contact?.Id, customerCompanyId: customer.Id, source: $"Prospecting: {account.SourceKey}"); existing.Qualify(account.FitOutcome, account.ScoreBand, account.ScoreBand, "Review research and prepare relevant outreach", userId); db.Add(existing); }
        account.Convert(customer.Id, existing.Id); await sources.StageAsync(companyId, new RecordSalesSourceTouchRequest("lead", existing.Id, SalesSourceCategories.Research, account.SourceKey, "prospecting", "conversion", account.SourceReference, DateTime.UtcNow, "agent", userId.ToString("D"), Evidence: "Approved prospect converted to a canonical lead.", IsConversion: true), ct); Audit(companyId, userId, "sales.prospect_account.converted", "lead", existing.Id, "Approved prospect converted to a canonical lead.", account.SourceKey); await db.SaveChangesAsync(ct); return new(account.Id, customer.Id, contact?.Id, existing.Id, existing.CreatedUtc < account.UpdatedUtc);
    }

    public async Task<IReadOnlyList<SuppressionDto>> ListSuppressionsAsync(Guid companyId, CancellationToken ct) => (await db.SalesSuppressions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive && (!x.ExpiresUtc.HasValue || x.ExpiresUtc > DateTime.UtcNow)).OrderByDescending(x => x.CreatedUtc).ToListAsync(ct)).Select(Map).ToList();
    public async Task<SuppressionDto> AddSuppressionAsync(Guid companyId, Guid userId, SaveSuppressionRequest r, CancellationToken ct) { var existing = await db.SalesSuppressions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ScopeType == r.ScopeType.ToLower() && x.ScopeValue == r.ScopeValue.ToLower() && x.IsActive, ct); if (existing is not null) return Map(existing); var x = new SalesSuppression(Guid.NewGuid(), companyId, r.ScopeType, r.ScopeValue, r.Reason, r.Source, userId, r.ExpiresUtc); db.Add(x); await CancelPendingCampaigns(companyId, r.ScopeType.Equals("email", StringComparison.OrdinalIgnoreCase) ? r.ScopeValue : null, ct); Audit(companyId, userId, "sales.suppression.created", "sales_suppression", x.Id, r.Reason); await db.SaveChangesAsync(ct); return Map(x); }
    public async Task RemoveSuppressionAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct) { var x = await db.SalesSuppressions.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.CompanyId == companyId && s.Id == id, ct) ?? throw new KeyNotFoundException(); x.Deactivate(); Audit(companyId, userId, "sales.suppression.deactivated", "sales_suppression", id, "Suppression deactivated."); await db.SaveChangesAsync(ct); }

    public async Task<LeadGenerationMetricsDto> GetMetricsAsync(Guid companyId, CancellationToken ct)
    {
        var items = await db.ProspectAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).ToListAsync(ct); var reviewed = items.Count(x => x.Status is LeadGenerationStatuses.Accepted or LeadGenerationStatuses.Rejected or LeadGenerationStatuses.Converted); var accepted = items.Count(x => x.Status is LeadGenerationStatuses.Accepted or LeadGenerationStatuses.Converted); var completeness = items.Count == 0 ? 0 : items.Average(x => (decimal)new[] { x.Domain is not null, x.Country is not null, x.Industry is not null, x.Employees.HasValue, x.Revenue.HasValue }.Count(v => v) / 5m * 100m);
        return new(items.Count(x => x.Status == LeadGenerationStatuses.Candidate), items.Count(x => x.Status == LeadGenerationStatuses.Qualified), accepted, items.Count(x => x.Status == LeadGenerationStatuses.Converted), items.Count(x => x.Status == LeadGenerationStatuses.Rejected), reviewed == 0 ? 0 : decimal.Round(accepted * 100m / reviewed, 2), decimal.Round(completeness, 2), items.GroupBy(x => x.SourceKey).ToDictionary(g => g.Key, g => g.Count()));
    }

    public async Task<byte[]> ExportCsvAsync(Guid companyId, CancellationToken ct)
    {
        var items = await db.ProspectAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.Status != LeadGenerationStatuses.Merged).OrderByDescending(x => x.OverallScore).ToListAsync(ct); var sb = new StringBuilder("Name,Domain,Country,Industry,Employees,Revenue,Status,Score,Priority,Source\r\n"); foreach (var x in items) sb.AppendLine(string.Join(',', Csv(x.Name), Csv(x.Domain), Csv(x.Country), Csv(x.Industry), x.Employees, x.Revenue?.ToString(CultureInfo.InvariantCulture), Csv(x.Status), x.OverallScore.ToString(CultureInfo.InvariantCulture), Csv(x.ScoreBand), Csv(x.SourceKey))); return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<CrmDeliveryStatusDto> GetCrmStatusAsync(Guid companyId, CancellationToken ct)
    {
        var statuses = new List<CrmAdapterStatus>(); foreach (var key in crmAdapters.Keys) statuses.Add(await crmAdapters.Resolve(key).GetStatusAsync(companyId, ct)); return new(true, statuses, true);
    }

    public async Task<CrmSyncResultDto> SyncLeadAsync(Guid companyId, Guid userId, Guid accountId, string providerKey, CancellationToken ct)
    {
        var account = await Account(companyId, accountId, ct); if (!account.LeadId.HasValue) throw new LeadGenerationValidationException("Create the internal lead before synchronizing it to a CRM."); await EnsureEligible(companyId, account, null, ct); var result = await crmAdapters.Resolve(providerKey).UpsertLeadAsync(companyId, account.LeadId.Value, $"prospect:{companyId:N}:{account.Id:N}:{providerKey.ToLowerInvariant()}", ct); Audit(companyId, userId, "sales.prospect.crm_synced", "lead", account.LeadId.Value, $"Lead synchronized to {providerKey}.", providerKey); await db.SaveChangesAsync(ct); return result;
    }

    public async Task ProcessRunAsync(Guid companyId, Guid runId, CancellationToken ct)
    {
        var run = await Run(companyId, runId, ct); if (run.Status != LeadGenerationStatuses.Running) return; var profile = await Profile(companyId, run.IdealCustomerProfileId, ct); var keys = Split(run.Sources).Where(x => x != "csv").ToList();
        try { foreach (var key in keys) { var provider = providers.Resolve(key); var page = await provider.SearchAccountsAsync(companyId, new(profile.Id, run.AccountLimit - run.AccountsFound, run.Cursor, profile.Countries, profile.Industries), ct); foreach (var input in page.Accounts) { if (run.AccountsFound >= run.AccountLimit) break; if (await FindAccount(companyId, input.SourceKey, input.SourceReference, input.Domain, ct) is not null) continue; var account=NewAccount(companyId, run, input, profile); db.Add(account); await RecordProspectSource(companyId, run.OwnerUserId, account, input, "prospecting", ct); run.Progress(run.AccountsFound + 1, run.ContactsFound, $"Searching {key}", page.NextCursor, run.ActualCost + page.Cost); } await db.SaveChangesAsync(ct); } run.Complete(); (await Policy(companyId, ct)).Reconcile(run.EstimatedCost, run.ActualCost); await db.SaveChangesAsync(ct); }
        catch (Exception ex) { run.Fail(ex.Message); await db.SaveChangesAsync(ct); }
    }

    private IdealCustomerProfile NewProfile(Guid companyId, Guid userId, SaveIcpProfileRequest r, int version, Guid? previous) => new(Guid.NewGuid(), companyId, r.Name, version, userId, r.Countries, r.Industries, r.EmployeeMin, r.EmployeeMax, r.RevenueMin, r.RevenueMax, r.BuyerRoles, r.Technologies, r.PainHypotheses, r.PositiveCriteria, r.Disqualifiers, previous);
    private Task<SalesSourceTouchDto> RecordProspectSource(Guid companyId, Guid actorId, ProspectAccount account, ProspectAccountInput input, string channel, CancellationToken ct) => sources.StageAsync(companyId, new RecordSalesSourceTouchRequest("prospect_account", account.Id, input.SourceKey == "csv" ? SalesSourceCategories.Import : SalesSourceCategories.Research, input.SourceKey, channel, "discovery", input.SourceReference, input.ObservedUtc, "agent", actorId.ToString("D"), Evidence: $"{account.Name} discovered through {input.SourceKey}."), ct);
    private ProspectAccount NewAccount(Guid companyId, ProspectingRun run, ProspectAccountInput input, IdealCustomerProfile profile) { var x = new ProspectAccount(Guid.NewGuid(), companyId, run.Id, profile.Id, input.Name, input.Domain, input.Country, input.Industry, input.Employees, input.Revenue, input.SourceKey, input.SourceReference, input.ObservedUtc ?? DateTime.UtcNow); x.SetCanonical(null, input.Country, input.Industry, input.Employees, input.Revenue, input.Technologies, input.ObservedUtc ?? DateTime.UtcNow); var match = Match(profile, input); x.ApplyEvaluation(match.Outcome, match.FitScore, JsonSerializer.Serialize(match, JsonOptions)); return x; }
    private static IcpPreviewDto Match(IdealCustomerProfile p, ProspectAccountInput a) { var c = new List<IcpCriterionDto>(); var hard = false; decimal points = 0, available = 0; Evaluate("Country", p.Countries, a.Country, 20); Evaluate("Industry", p.Industries, a.Industry, 35); Range("Company size", p.EmployeeMin, p.EmployeeMax, a.Employees, 25); Range("Revenue", p.RevenueMin, p.RevenueMax, a.Revenue, 20); if (Split(p.Disqualifiers).Any(d => ($"{a.Name} {a.Industry} {a.Technologies}").Contains(d, StringComparison.OrdinalIgnoreCase))) { hard = true; c.Add(new("Exclusions", "Disqualified", "The account matches an explicit exclusion.")); } var score = available == 0 ? 0 : decimal.Round(points / available * 100, 2); return new(hard ? "disqualified" : score >= 60 ? "matched" : score >= 30 ? "review" : "not_matched", hard ? 0 : score, c);
        void Evaluate(string name, string targets, string? value, decimal weight) { var list = Split(targets); if (list.Count == 0) return; available += weight; if (string.IsNullOrWhiteSpace(value)) c.Add(new(name, "Unknown", "No evidence is available.")); else if (list.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase))) { points += weight; c.Add(new(name, "Matched", $"{value} matches the profile.")); } else c.Add(new(name, "Not matched", $"{value} is outside the profile.")); }
        void Range<T>(string name, T? min, T? max, T? value, decimal weight) where T : struct, IComparable<T> { if (!min.HasValue && !max.HasValue) return; available += weight; if (!value.HasValue) c.Add(new(name, "Unknown", "No evidence is available.")); else if ((!min.HasValue || value.Value.CompareTo(min.Value) >= 0) && (!max.HasValue || value.Value.CompareTo(max.Value) <= 0)) { points += weight; c.Add(new(name, "Matched", "The value is inside the target range.")); } else c.Add(new(name, "Not matched", "The value is outside the target range.")); }
    }
    private async Task EnsureEligible(Guid companyId, ProspectAccount a, ProspectContact? c, CancellationToken ct) { var scopes = new List<(string, string?)> { ("domain", a.Domain), ("company", a.Name), ("email", c?.Email), ("person", c?.FullName) }; foreach (var (type, value) in scopes.Where(x => !string.IsNullOrWhiteSpace(x.Item2))) if (await db.SalesSuppressions.IgnoreQueryFilters().AnyAsync(s => s.CompanyId == companyId && s.IsActive && (!s.ExpiresUtc.HasValue || s.ExpiresUtc > DateTime.UtcNow) && s.ScopeType == type && s.ScopeValue == value!.ToLower(), ct)) throw new LeadGenerationValidationException($"This {type} is suppressed from sales activity."); if (a.Status is LeadGenerationStatuses.Rejected or LeadGenerationStatuses.Merged or LeadGenerationStatuses.Stale) throw new LeadGenerationValidationException("This account is not eligible for sales activity."); if (c?.EmploymentStatus == "changed") throw new LeadGenerationValidationException("The contact no longer works at this account."); }
    private async Task CancelPendingCampaigns(Guid companyId, string? email, CancellationToken ct) { if (string.IsNullOrWhiteSpace(email)) return; var contactIds = await db.Contacts.IgnoreQueryFilters().Where(c => c.CompanyId == companyId && c.Email == email.ToLower()).Select(c => c.Id).ToListAsync(ct); var steps = await db.SalesSequenceExecutionSteps.IgnoreQueryFilters().Where(s => s.CompanyId == companyId && contactIds.Contains(s.ContactId) && s.Status == SalesStatuses.Pending).ToListAsync(ct); foreach (var step in steps) step.Cancel(SalesStopReasons.EligibilityBlocked, "lead-generation-eligibility"); }
    private async Task<IdealCustomerProfile> Profile(Guid companyId, Guid id, CancellationToken ct) => await db.IdealCustomerProfiles.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct) ?? throw new KeyNotFoundException("Ideal customer profile was not found.");
    private async Task<ProspectSourcePolicy> Policy(Guid companyId, CancellationToken ct) { var p = await db.ProspectSourcePolicies.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId, ct); if (p is not null) return p; p = new ProspectSourcePolicy(Guid.NewGuid(), companyId); db.Add(p); await db.SaveChangesAsync(ct); return p; }
    private async Task<ProspectingRun> Run(Guid companyId, Guid id, CancellationToken ct) => await db.ProspectingRuns.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct) ?? throw new KeyNotFoundException("Prospecting run was not found.");
    private async Task<ProspectAccount> Account(Guid companyId, Guid id, CancellationToken ct) => await db.ProspectAccounts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct) ?? throw new KeyNotFoundException("Prospect account was not found.");
    private async Task<ProspectAccount?> FindAccount(Guid companyId, string source, string reference, string? domain, CancellationToken ct) => await db.ProspectAccounts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && ((x.SourceKey == source.ToLower() && x.SourceReference == reference) || (domain != null && x.Domain == domain.ToLower().Replace("https://", "").Replace("http://", "").TrimEnd('/'))), ct);
    private async Task<List<ProspectSignal>> Signals(Guid companyId, Guid accountId, CancellationToken ct) => await db.ProspectSignals.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.ProspectAccountId == accountId).OrderByDescending(x => x.EventUtc).ToListAsync(ct);
    private async Task<ProspectAccountDto> MapAccount(ProspectAccount x, CancellationToken ct) { var contacts = await db.ProspectContacts.IgnoreQueryFilters().AsNoTracking().Where(c => c.CompanyId == x.CompanyId && c.ProspectAccountId == x.Id).OrderBy(c => c.FullName).ToListAsync(ct); var signals = await db.ProspectSignals.IgnoreQueryFilters().AsNoTracking().Where(s => s.CompanyId == x.CompanyId && s.ProspectAccountId == x.Id).OrderByDescending(s => s.EventUtc).ToListAsync(ct); return new(x.Id, x.ProspectingRunId, x.IdealCustomerProfileId, x.Name, x.Domain, x.Country, x.Industry, x.Employees, x.Revenue, x.Technologies, x.SourceKey, Label(x.Status), x.FitOutcome, x.FitScore, x.TimingScore, x.RoleScore, x.DataConfidenceScore, x.OverallScore, x.ScoreBand, x.EvaluationJson, x.ResearchBriefJson, x.RejectionReason, x.LeadId, x.LastObservedUtc, contacts.Select(Map).ToList(), signals.Select(Map).ToList(), Allowed(x)); }
    private SourcePolicyDto Map(ProspectSourcePolicy x) => new(x.Id, x.Version, x.EnabledSources, x.AllowedCountries, x.AllowedFields, x.PerRunBudget, x.MonthlyBudget, x.ApprovalThreshold, x.RetentionDays, x.RefreshDays, x.ReservedThisMonth, x.ActualThisMonth, providers.List());
    private static IcpProfileDto Map(IdealCustomerProfile x) => new(x.Id, x.Name, x.Version, Label(x.Status), x.Countries, x.Industries, x.EmployeeMin, x.EmployeeMax, x.RevenueMin, x.RevenueMax, x.BuyerRoles, x.Technologies, x.PainHypotheses, x.PositiveCriteria, x.Disqualifiers, x.UpdatedUtc, x.ActivatedUtc);
    private static ProspectingRunDto Map(ProspectingRun x) => new(x.Id, x.IdealCustomerProfileId, x.Name, Label(x.Status), x.CurrentStep, x.AccountLimit, x.ContactLimit, x.AccountsFound, x.ContactsFound, x.Sources, x.Geography, x.EstimatedCost, x.ActualCost, x.FailureSummary, x.CreatedUtc, x.StartedUtc, x.CompletedUtc);
    private static ProspectContactDto Map(ProspectContact x) => new(x.Id, x.ProspectAccountId, x.FullName, x.Title, x.Department, x.Seniority, x.BuyingRoles, x.Email, Label(x.EmailStatus), x.Phone, x.ProfileUrl, Label(x.EmploymentStatus), x.Confidence, Label(x.Status), x.RejectionReason, x.ContactId);
    private static ProspectSignalDto Map(ProspectSignal x) => new(x.Id, Label(x.SignalType), x.SourceKey, x.Summary, x.EventUtc, x.FreshUntilUtc, x.Confidence, x.Relevance, Label(x.Status));
    private static SuppressionDto Map(SalesSuppression x) => new(x.Id, Label(x.ScopeType), x.ScopeValue, x.Reason, x.Source, x.CreatedUtc, x.ExpiresUtc);
    private static IReadOnlyList<string> Allowed(ProspectAccount x) => x.Status switch { LeadGenerationStatuses.Converted or LeadGenerationStatuses.Merged => ["View lead"], LeadGenerationStatuses.Rejected => ["Request research"], LeadGenerationStatuses.Accepted => ["Add contact", "Refresh research", "Create lead", "Export"], _ => ["Accept", "Reject", "Add contact", "Refresh research"] };
    private static string BuildBrief(ProspectAccount x, IReadOnlyList<ProspectSignal> signals, IReadOnlyList<ProspectContact> contacts) => JsonSerializer.Serialize(new { version = 1, generatedUtc = DateTime.UtcNow, confirmedFacts = new[] { $"{x.Name} is categorized as {x.Industry ?? "industry unknown"}.", x.Country is null ? "Location is not yet verified." : $"The account is located in {x.Country}." }, hypotheses = string.IsNullOrWhiteSpace(x.Industry) ? Array.Empty<string>() : new[] { $"The account may have needs common to {x.Industry} organizations; validate this in discovery." }, recentSignals = signals.Where(s => s.Status == "confirmed" && s.FreshUntilUtc >= DateTime.UtcNow).Select(s => new { s.Summary, s.EventUtc, s.SourceKey }).ToArray(), buyingGroup = contacts.Select(c => new { c.FullName, c.Title, c.BuyingRoles, c.Confidence }).ToArray(), unknowns = new[] { x.Employees.HasValue ? null : "Company size", x.Revenue.HasValue ? null : "Revenue", contacts.Count > 0 ? null : "Buying group" }.Where(v => v is not null), sources = new[] { new { x.SourceKey, x.SourceReference, x.LastObservedUtc } } }, JsonOptions);
    private static decimal Relevance(ProspectSignal x) { var baseScore = x.SignalType switch { "funding" or "expansion" or "hiring" => 80m, "leadership_change" or "technology_adoption" => 65m, _ => 40m }; var age = Math.Max(0, (DateTime.UtcNow - x.EventUtc).TotalDays); return Math.Max(0, baseScore * (decimal)Math.Max(0.1, 1 - age / 365)); }
    private static decimal DataConfidence(ProspectAccount a, IReadOnlyList<ProspectContact> c) { decimal score = 0; if (a.Domain is not null) score += 20; if (a.Country is not null) score += 15; if (a.Industry is not null) score += 20; if (a.Employees.HasValue) score += 15; if (a.Revenue.HasValue) score += 10; if (c.Any(x => x.EmailStatus == "verified")) score += 20; return score; }
    private void Audit(Guid companyId, Guid userId, string action, string type, Guid id, string rationale, string? source = null) => db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, "human", userId, action, type, id.ToString("D"), "succeeded", rationale, dataSources: source is null ? null : [source]));
    private static List<string> Split(string? value) => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).Distinct().ToList();
    private static string Normalize(string value) => string.Join('-', value.ToLowerInvariant().Where(char.IsLetterOrDigit).Take(100));
    private static string Label(string value) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' '));
    private static string Csv(object? value) { var x = value?.ToString() ?? ""; x = x.Length > 0 && "=+-@".Contains(x[0]) ? $"'{x}" : x; return $"\"{x.Replace("\"", "\"\"")}\""; }
    private static ProspectAccountInput Input(IReadOnlyDictionary<string, string> row, string reference) { string Get(string key) => row.TryGetValue(key, out var value) ? value.Trim() : ""; var name = Get("name"); if (name.Length == 0) throw new LeadGenerationValidationException("Name is required."); int? employees = int.TryParse(Get("employees"), out var e) ? e : null; decimal? revenue = decimal.TryParse(Get("revenue"), NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : null; return new(name, Get("domain"), Get("country"), Get("industry"), employees, revenue, Get("technologies"), "csv", reference); }
    private static async Task<List<Dictionary<string, string>>> ReadCsvAsync(Stream stream, CancellationToken ct) { using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true); var text = await reader.ReadToEndAsync(ct); if (text.Length > 10_000_000) throw new LeadGenerationValidationException("Import files must be 10 MB or smaller."); var lines = ParseCsv(text); if (lines.Count < 2) return []; var headers = lines[0].Select(x => x.Trim().ToLowerInvariant()).ToList(); return lines.Skip(1).Where(x => x.Any(v => !string.IsNullOrWhiteSpace(v))).Select(values => headers.Select((h, i) => (h, v: i < values.Count ? Neutralize(values[i]) : "")).ToDictionary(x => x.h, x => x.v)).ToList(); }
    private static List<List<string>> ParseCsv(string text) { var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false; for (var i = 0; i < text.Length; i++) { var ch = text[i]; if (ch == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else quoted = !quoted; } else if (ch == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); } else if ((ch == '\n' || ch == '\r') && !quoted) { if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; row.Add(field.ToString()); field.Clear(); rows.Add(row); row = []; } else field.Append(ch); } if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); } return rows; }
    private static List<Dictionary<string, string>> ReadXlsx(Stream stream) { using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true); var shared = new List<string>(); var sharedEntry = archive.GetEntry("xl/sharedStrings.xml"); if (sharedEntry is not null) { using var s = sharedEntry.Open(); var doc = XDocument.Load(s); shared = doc.Descendants().Where(x => x.Name.LocalName == "si").Select(x => string.Concat(x.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value))).ToList(); } var sheet = archive.GetEntry("xl/worksheets/sheet1.xml") ?? throw new LeadGenerationValidationException("The workbook has no first worksheet."); using var ss = sheet.Open(); var xml = XDocument.Load(ss); var rows = xml.Descendants().Where(x => x.Name.LocalName == "row").Select(r => r.Elements().Where(c => c.Name.LocalName == "c").Select(c => { var raw = c.Descendants().FirstOrDefault(v => v.Name.LocalName == "v")?.Value ?? ""; return c.Attribute("t")?.Value == "s" && int.TryParse(raw, out var i) && i < shared.Count ? shared[i] : raw; }).ToList()).ToList(); if (rows.Count < 2) return []; var headers = rows[0].Select(x => x.Trim().ToLowerInvariant()).ToList(); return rows.Skip(1).Select(values => headers.Select((h, i) => (h, v: i < values.Count ? Neutralize(values[i]) : "")).ToDictionary(x => x.h, x => x.v)).ToList(); }
    private static string Neutralize(string value) => value.Length > 0 && "=+-@".Contains(value[0]) ? $"'{value}" : value;
}
