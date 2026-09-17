using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Sales;

public partial class SalesPresentationPresets : IAsyncDisposable
{
    [Inject] private SalesPresentationPresetApiClient PresetApi { get; set; } = default!;
    [Inject] private SalesNarrationApiClient NarrationApi { get; set; } = default!;
    private NarrationRevision? SlideNarration;
    private string? SlideNarrationError;
    private static readonly string[] Filters = ["all", "draft", "processing", "needs_review", "published", "failed", "archived"];
    private readonly Dictionary<Guid, string> authoritativeStates = [];
    private CancellationTokenSource lifetime = new();
    private CancellationTokenSource? upload;
    private IReadOnlyList<SalesPresentationPresetListItemViewModel> Items = [];
    private IReadOnlyList<CompanyAgentSummaryViewModel> SalesAgents = [];
    private IReadOnlyList<SalesPresentationPresetSlideViewModel> Slides = [];
    private IReadOnlyDictionary<string, string[]> FieldErrors = new Dictionary<string, string[]>();
    private SalesPresentationPresetViewModel? Selected;
    private SalesPresentationPresetVersionViewModel? PreviewVersion;
    private SalesPresentationPresetReadinessViewModel? Readiness;
    private PresentationPresetEditModel Form = new();
    private Confirmation? PendingConfirmation;
    private string Search = string.Empty;
    private string Filter = "all";
    private string EditorTab = "overview";
    private string? ErrorMessage, StatusMessage, AssetStatusMessage, ConflictMessage;
    private bool IsLoading = true, IsBusy, ShowCreate, ShowAdHoc;
    private Guid actorUserId;
    private bool CanCreate => ResolvedCompanyId.HasValue && actorUserId != Guid.Empty;

    private IReadOnlyList<SalesPresentationPresetListItemViewModel> FilteredItems => Items.Where(x =>
        (Filter == "all" || ItemState(x) == Filter) &&
        (string.IsNullOrWhiteSpace(Search) || x.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase) ||
         (x.Description?.Contains(Search, StringComparison.CurrentCultureIgnoreCase) ?? false) || OwnerName(x.OwnerUserId).Contains(Search, StringComparison.CurrentCultureIgnoreCase))).ToArray();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true; ErrorMessage = null;
        try
        {
            if (!await ResolveCompanyAsync(lifetime.Token) || ResolvedCompanyId is not Guid companyId) return;
            var context = await OnboardingApiClient.GetCurrentUserContextAsync(companyId, lifetime.Token);
            actorUserId = context?.User.Id ?? Guid.Empty;
            try
            {
                var roster = await AgentApiClient.GetRosterAsync(companyId, lifetime.Token);
                SalesAgents = roster.Where(x => x.Department.Equals("Sales", StringComparison.OrdinalIgnoreCase) && x.Status.Equals("active", StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            catch (OnboardingApiException)
            {
                SalesAgents = [];
            }
            Items = await PresetApi.ListAsync(companyId, includeArchived: true, ct: lifetime.Token);
            await RefreshAuthoritativeStatesAsync(companyId);
            if (Selected is not null && Items.All(x => x.Id != Selected.Id)) ClearSelection();
            else if (Selected is not null) await SelectAsync(Selected.Id);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (SalesPresentationPresetApiException ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    private async Task SelectAsync(Guid id)
    {
        if (ResolvedCompanyId is not Guid companyId) return;
        try
        {
            Selected = await PresetApi.GetAsync(companyId, id, lifetime.Token);
            PreviewVersion = Selected?.CurrentDraft ?? Selected?.CurrentPublished ?? Selected?.Versions.FirstOrDefault();
            ConflictMessage = null; FieldErrors = new Dictionary<string, string[]>();
            PopulateForm();
            await LoadVersionAsync();
        }
        catch (SalesPresentationPresetApiException ex) { ErrorMessage = ex.Message; }
    }

    private async Task PreviewVersionAsync(SalesPresentationPresetVersionViewModel version) { PreviewVersion = version; PopulateForm(); await LoadVersionAsync(); }
    private async Task LoadVersionAsync()
    {
        if (ResolvedCompanyId is not Guid companyId || Selected is null || PreviewVersion is null) { Slides = []; Readiness = null; return; }
        Slides = await PresetApi.GetSlidesAsync(companyId, Selected.Id, PreviewVersion.Id, lifetime.Token);
        Readiness = await PresetApi.GetReadinessAsync(companyId, Selected.Id, PreviewVersion.Id, lifetime.Token);
        SlideNarration = null; SlideNarrationError = null;
        var narrationVersion = PreviewVersion.Id;
        try
        {
            var workspace = await NarrationApi.GetPresetAsync(companyId, narrationVersion, lifetime.Token);
            if (PreviewVersion?.Id == narrationVersion)
                SlideNarration = workspace.Revisions.OrderByDescending(x => x.CreatedUtc).FirstOrDefault();
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or System.Text.Json.JsonException)
        {
            if (PreviewVersion?.Id == narrationVersion)
                SlideNarrationError = "Spoken dialogue could not load. Open Script & audio to check access and refresh.";
        }
    }

    private async Task NotesSavedAsync(SalesPresentationPresetViewModel updated)
    {
        if (Selected?.Id != updated.Id) return;
        var versionId = PreviewVersion?.Id;
        Selected = updated;
        PreviewVersion = updated.Versions.FirstOrDefault(x => x.Id == versionId);
        await LoadVersionAsync();
    }

    private async Task ChangeTabAsync(string tab)
    {
        EditorTab = tab;
        if (tab == "powerpoint")
            try { await LoadVersionAsync(); }
            catch (SalesPresentationPresetApiException ex) { ErrorMessage = ex.Message; }
    }

    private void BeginCreate()
    {
        FieldErrors = new Dictionary<string, string[]>();
        Form = PresentationPresetEditModel.Create(actorUserId, CurrentUserName());
        ShowCreate = true;
    }
    private void CloseCreate() => ShowCreate = false;
    private void BeginAdHoc() => ShowAdHoc = true;
    private void CloseAdHoc() => ShowAdHoc = false;

    private async Task CreateAsync()
    {
        if (ResolvedCompanyId is not Guid companyId || !ValidateForm()) return;
        await MutateAsync(async () =>
        {
            var created = await PresetApi.CreateAsync(companyId, Form.ToCreateRequest(), lifetime.Token);
            ShowCreate = false; StatusMessage = "Presentation preset created.";
            await LoadAsync(); await SelectAsync(created.Id);
        });
    }

    private void EditSelectedDraft()
    {
        if (IsBusy || PreviewVersion?.Lifecycle != "draft" || Selected?.Lifecycle == "archived") return;
        EditorTab = "settings";
    }

    private async Task SaveAsync()
    {
        if (ResolvedCompanyId is not Guid companyId || Selected?.CurrentDraft is not { } draft ||
            PreviewVersion?.Id != draft.Id || PreviewVersion.Lifecycle != "draft" || !ValidateForm()) return;
        await MutateAsync(async () =>
        {
            Selected = await PresetApi.UpdateDraftAsync(companyId, Selected.Id, Form.ToUpdateRequest(Selected.ConcurrencyVersion, draft.ConcurrencyVersion), lifetime.Token);
            PreviewVersion = Selected.CurrentDraft; StatusMessage = "Draft saved."; await LoadVersionAsync(); await RefreshListAsync();
        });
    }

    private async Task UploadAsync(IBrowserFile file)
    {
        if (ResolvedCompanyId is not Guid companyId || Selected?.CurrentDraft is not { } draft) return;
        if (!file.Name.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)) { AssetStatusMessage = "Choose a PowerPoint .pptx file."; return; }
        if (file.Size is 0 or > 28_311_552) { AssetStatusMessage = file.Size == 0 ? "The selected file is empty." : "The selected file exceeds the 27 MB limit."; return; }
        upload?.Cancel(); upload?.Dispose(); upload = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        IsBusy = true; AssetStatusMessage = "Uploading PowerPoint…";
        try
        {
            await using var stream = file.OpenReadStream(28_311_552, upload.Token);
            await PresetApi.ImportAssetAsync(companyId, Selected.Id, draft.Id, file.Name,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/vnd.openxmlformats-officedocument.presentationml.presentation" : file.ContentType, stream, upload.Token);
            AssetStatusMessage = "Upload accepted. Processing has started.";
            await PollProcessingAsync(companyId, Selected.Id, draft.Id, upload.Token);
        }
        catch (OperationCanceledException) when (upload.IsCancellationRequested) { AssetStatusMessage = "Upload monitoring cancelled. Reload to check whether the server accepted the file."; }
        catch (SalesPresentationPresetApiException ex) { ApplyApiError(ex); AssetStatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task PollProcessingAsync(Guid companyId, Guid presetId, Guid versionId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(attempt == 0 ? 200 : 1500, ct);
            Selected = await PresetApi.GetAsync(companyId, presetId, ct);
            PreviewVersion = Selected?.Versions.FirstOrDefault(x => x.Id == versionId);
            var status = PreviewVersion?.Asset?.Status;
            await LoadVersionAsync();
            if (status is "processed" or "failed" or "blocked")
            {
                AssetStatusMessage = status == "processed" ? $"Processing completed with {PreviewVersion?.Asset?.SlideCount ?? 0} slides." : PreviewVersion?.Asset?.FailureSummary ?? "Processing could not finish.";
                await RefreshListAsync(); return;
            }
            await InvokeAsync(StateHasChanged);
        }
        AssetStatusMessage = "Processing is still running. Reload later for the latest status.";
    }

    private void CancelUpload() => upload?.Cancel();
    private async Task RetryAsync()
    {
        if (ResolvedCompanyId is not Guid companyId || Selected is null || PreviewVersion is null) return;
        await MutateAsync(async () => { await PresetApi.RetryAssetAsync(companyId, Selected.Id, PreviewVersion.Id, lifetime.Token); AssetStatusMessage = "A new processing attempt was queued."; await PollProcessingAsync(companyId, Selected.Id, PreviewVersion.Id, lifetime.Token); });
    }

    private void ConfirmPublish()
    {
        if (Selected?.CurrentDraft is not { } draft) return;
        PendingConfirmation = new("Publish version", $"Publish v{draft.VersionNumber}? Published versions cannot be edited.", "Publish", PublishAsync);
    }
    private async Task PublishAsync()
    {
        if (ResolvedCompanyId is not Guid companyId || Selected?.CurrentDraft is not { } draft) return;
        await MutateAsync(async () => { Selected = await PresetApi.PublishAsync(companyId, Selected.Id, draft.Id, Selected.ConcurrencyVersion, draft.ConcurrencyVersion, lifetime.Token); PreviewVersion = Selected.CurrentPublished; StatusMessage = "Preset published as an immutable version."; await LoadVersionAsync(); await RefreshListAsync(); });
    }
    private async Task CreateNextDraftAsync()
    {
        if (ResolvedCompanyId is not Guid companyId || Selected is null) return;
        await MutateAsync(async () => { Selected = await PresetApi.CreateNextDraftAsync(companyId, Selected.Id, Selected.ConcurrencyVersion, lifetime.Token); PreviewVersion = Selected.CurrentDraft; PopulateForm(); StatusMessage = "A new editable draft was created."; await LoadVersionAsync(); await RefreshListAsync(); });
    }
    private async Task DuplicateAsync()
    {
        if (ResolvedCompanyId is not Guid companyId || Selected is null || PreviewVersion is null) return;
        var source = PreviewVersion;
        var request = new SalesPresentationPresetCreateRequest($"{Selected.Name} copy", Selected.Description, actorUserId, source.DefaultPresenterAgentId, source.Goal, source.Audience, source.DurationMinutes, source.DemoScenario, source.ControlMode, source.Language, source.AllowedContextTypes, source.RequiredKnowledgeScope, source.BehaviorSettingsJson);
        await MutateAsync(async () => { var duplicate = await PresetApi.CreateAsync(companyId, request, lifetime.Token); StatusMessage = "Preset defaults duplicated into a new draft. Add its PowerPoint source before publishing."; await LoadAsync(); await SelectAsync(duplicate.Id); });
    }
    private void ConfirmArchive()
    {
        if (Selected is null) return;
        PendingConfirmation = new("Archive preset", $"This preset is used {Selected.WhereUsedCount} time(s). Historical references remain available, but it cannot be used for new presentations.", "Archive preset", ArchiveAsync);
    }
    private async Task ArchiveAsync()
    {
        if (ResolvedCompanyId is not Guid companyId || Selected is null) return;
        await MutateAsync(async () => { Selected = await PresetApi.ArchiveAsync(companyId, Selected.Id, Selected.ConcurrencyVersion, "Archived from the presentation preset library.", lifetime.Token); PreviewVersion = Selected.CurrentPublished ?? Selected.Versions.FirstOrDefault(); StatusMessage = "Preset archived. Historical versions remain available."; await LoadVersionAsync(); await RefreshListAsync(); });
    }

    private async Task MutateAsync(Func<Task> action)
    {
        IsBusy = true; ErrorMessage = null; ConflictMessage = null; FieldErrors = new Dictionary<string, string[]>();
        try { await action(); PendingConfirmation = null; }
        catch (SalesPresentationPresetConflictApiException ex) { ConflictMessage = ex.Message; PendingConfirmation = null; }
        catch (SalesPresentationPresetApiException ex) { ApplyApiError(ex); ErrorMessage = ex.Message; PendingConfirmation = null; }
        finally { IsBusy = false; }
    }
    private void ApplyApiError(SalesPresentationPresetApiException ex) { if (ex.Errors is not null) FieldErrors = ex.Errors; }
    private async Task ReloadSelectedAsync() { if (Selected is not null) await SelectAsync(Selected.Id); }
    private async Task RefreshListAsync()
    {
        if (ResolvedCompanyId is not Guid companyId) return;
        Items = await PresetApi.ListAsync(companyId, includeArchived: true, ct: lifetime.Token);
        await RefreshAuthoritativeStatesAsync(companyId);
    }
    private async Task RefreshAuthoritativeStatesAsync(Guid companyId)
    {
        authoritativeStates.Clear();
        foreach (var item in Items)
        {
            var detail = await PresetApi.GetAsync(companyId, item.Id, lifetime.Token);
            if (detail?.CurrentDraft is not { } draft) continue;
            var ready = await PresetApi.GetReadinessAsync(companyId, item.Id, draft.Id, lifetime.Token);
            if (ready is not null) authoritativeStates[item.Id] = ready.IsReady ? "draft" : "needs_review";
        }
    }
    private void ClearSelection() { Selected = null; PreviewVersion = null; Readiness = null; Slides = []; }
    private bool ValidateForm()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Form.Name)) errors["name"] = ["Enter a preset name."];
        if (string.IsNullOrWhiteSpace(Form.Goal)) errors["goal"] = ["Describe the presentation goal."];
        if (string.IsNullOrWhiteSpace(Form.Audience)) errors["audience"] = ["Describe the intended audience."];
        if (Form.DurationMinutes is < 1 or > 480) errors["durationMinutes"] = ["Choose a duration between 1 and 480 minutes."];
        if (!Form.AllowSalesMeeting && !Form.AllowCampaign && !Form.AllowAdHoc) errors["allowedContextTypes"] = ["Choose at least one supported situation."];
        FieldErrors = errors; return errors.Count == 0;
    }
    private void PopulateForm()
    {
        if (Selected is null || PreviewVersion is null) return;
        Form = PresentationPresetEditModel.From(Selected, PreviewVersion, OwnerName(Selected.OwnerUserId));
    }
    private void CorrectBlocker(string action)
    {
        EditorTab=action is "upload_asset" or "retry_processing" ? "powerpoint" : "settings";
        if (action == "retry_processing") _ = RetryAsync();
        else StatusMessage = action == "upload_asset" ? "Choose a PowerPoint file in the source section." : "Update the highlighted reusable defaults, then save the draft.";
    }
    private Task SearchChanged(ChangeEventArgs e) { Search = e.Value?.ToString() ?? string.Empty; return Task.CompletedTask; }
    private void SetFilter(string filter) => Filter = filter;
    private string FilterClass(string filter) => $"preset-filter{(Filter == filter ? " active" : string.Empty)}";
    private int FilterCount(string filter) => filter == "all" ? Items.Count : Items.Count(x => ItemState(x) == filter);
    private static string FilterLabel(string value) => value switch { "all" => "All", "needs_review" => "Needs review", _ => StateLabel(value) };
    private string RowClass(SalesPresentationPresetListItemViewModel item) => $"preset-row{(Selected?.Id == item.Id ? " selected" : string.Empty)}";
    private string ItemState(SalesPresentationPresetListItemViewModel item)
    {
        if (item.Lifecycle == "archived") return "archived";
        if (item.AssetStatus is "pending_scan" or "processing") return "processing";
        if (item.AssetStatus is "failed" or "blocked") return "failed";
        if (authoritativeStates.TryGetValue(item.Id, out var state)) return state;
        return item.CurrentPublishedVersion.HasValue ? "published" : "draft";
    }
    private static string StateLabel(string value) => value switch { "needs_review" => "Needs review", "pending_scan" => "Queued", "processed" => "Processed", _ => char.ToUpperInvariant(value[0]) + value[1..].Replace('_', ' ') };
    private static string BadgeClass(string state) => $"sales-badge sales-badge--{state.Replace('_','-')}";
    private string OwnerName(Guid id) => id == actorUserId ? CurrentUserName() : "Company member";
    private string CurrentUserName() => "You";
    private string? PresenterName(Guid? id) => id is null ? null : SalesAgents.FirstOrDefault(x => x.Id == id)?.DisplayName ?? "Unavailable presenter";
    private string BuildPath(string path) => ResolvedCompanyId is Guid id ? $"{path}?companyId={id:D}" : path;
    private void CancelConfirmation() => PendingConfirmation = null;
    private async Task RunConfirmationAsync() { if (PendingConfirmation is { } confirmation) await confirmation.Action(); }

    public async ValueTask DisposeAsync()
    {
        upload?.Cancel(); upload?.Dispose(); lifetime.Cancel(); lifetime.Dispose(); await Task.CompletedTask;
    }
    private sealed record Confirmation(string Title, string Message, string ActionLabel, Func<Task> Action);
}

public sealed class PresentationPresetEditModel
{
    public string Name { get; set; } = ""; public string? Description { get; set; } public Guid OwnerUserId { get; set; } public string OwnerDisplayName { get; set; } = "You";
    public string PresenterValue { get; set; } = ""; public Guid? PresenterId => Guid.TryParse(PresenterValue, out var id) ? id : null;
    public string Goal { get; set; } = ""; public string Audience { get; set; } = ""; public int DurationMinutes { get; set; } = 30; public string? DemoScenario { get; set; }
    public string ControlMode { get; set; } = "assisted"; public string Language { get; set; } = "en"; public bool AllowSalesMeeting { get; set; } = true; public bool AllowCampaign { get; set; } public bool AllowAdHoc { get; set; } = true;
    public int RetentionDays { get; set; } = 365;
    private string? OriginalBehavior { get; set; }
    public bool PauseForQuestions { get; set; } = true; public bool ShowSpeakerNotes { get; set; } = true; public string? RequiredKnowledgeScope { get; set; }
    public static PresentationPresetEditModel Create(Guid owner, string ownerName) => new() { OwnerUserId = owner, OwnerDisplayName = ownerName };
    public static PresentationPresetEditModel From(SalesPresentationPresetViewModel preset, SalesPresentationPresetVersionViewModel v, string ownerName) => new() { Name = preset.Name, Description = preset.Description, OwnerUserId = preset.OwnerUserId, OwnerDisplayName = ownerName, PresenterValue = v.DefaultPresenterAgentId?.ToString() ?? "", Goal = v.Goal, Audience = v.Audience, DurationMinutes = v.DurationMinutes, DemoScenario = v.DemoScenario, ControlMode = v.ControlMode, Language = v.Language, AllowSalesMeeting = v.AllowedContextTypes.Contains("sales_meeting"), AllowCampaign = v.AllowedContextTypes.Contains("campaign_activity"), AllowAdHoc = v.AllowedContextTypes.Contains("ad_hoc"), RequiredKnowledgeScope = v.RequiredKnowledgeScope, OriginalBehavior = v.BehaviorSettingsJson, PauseForQuestions = ReadFlag(v.BehaviorSettingsJson, "pauseForQuestions"), ShowSpeakerNotes = ReadFlag(v.BehaviorSettingsJson, "showSpeakerNotes"), RetentionDays = ReadRetention(v.BehaviorSettingsJson) };
    private IReadOnlyList<string> Contexts => new[] { AllowSalesMeeting ? "sales_meeting" : null, AllowCampaign ? "campaign_activity" : null, AllowAdHoc ? "ad_hoc" : null }.Where(x => x is not null).Cast<string>().ToArray();
    private static bool ReadFlag(string? json, string key) { using var doc = System.Text.Json.JsonDocument.Parse(json ?? "{}"); return !doc.RootElement.TryGetProperty(key, out var value) || value.ValueKind != System.Text.Json.JsonValueKind.False; }
    private static int ReadRetention(string? json) { using var doc = System.Text.Json.JsonDocument.Parse(json ?? "{}"); return doc.RootElement.TryGetProperty("retentionDays", out var value) && value.TryGetInt32(out var days) ? days : 365; }
    private string Behavior
    {
        get
        {
            var settings = System.Text.Json.Nodes.JsonNode.Parse(OriginalBehavior ?? "{}")!.AsObject();
            settings["pauseForQuestions"] = PauseForQuestions;
            settings["showSpeakerNotes"] = ShowSpeakerNotes;
            settings["retentionDays"] = RetentionDays;
            return settings.ToJsonString();
        }
    }
    public SalesPresentationPresetCreateRequest ToCreateRequest() => new(Name.Trim(), Description?.Trim(), OwnerUserId, PresenterId, Goal.Trim(), Audience.Trim(), DurationMinutes, DemoScenario?.Trim(), ControlMode, Language, Contexts, RequiredKnowledgeScope, Behavior);
    public SalesPresentationPresetUpdateRequest ToUpdateRequest(long presetVersion, long draftVersion) => new(presetVersion, draftVersion, Name.Trim(), Description?.Trim(), OwnerUserId, PresenterId, Goal.Trim(), Audience.Trim(), DurationMinutes, DemoScenario?.Trim(), ControlMode, Language, Contexts, RequiredKnowledgeScope, Behavior);
}
