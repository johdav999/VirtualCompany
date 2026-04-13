using System.Collections.ObjectModel;
using VirtualCompany.Mobile.Services;
using VirtualCompany.Shared.Mobile;

namespace VirtualCompany.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileCompanionApiClient apiClient = new(new HttpClient());
    private string activeUserScope = "anonymous";
    private Guid? activeCompanyId;
    private DirectConversationDto? activeConversation;
    private MobileLocalCache? localCache;

    public ObservableCollection<MobileApprovalListItemDto> PendingApprovals { get; } = [];
    public ObservableCollection<MobileAlertListItemDto> Notifications { get; } = [];
    public ObservableCollection<MobileConversationSummaryDto> Conversations { get; } = [];
    public ObservableCollection<MobileMessageListItemDto> Messages { get; } = [];
    public ObservableCollection<CompanyMembershipDto> Memberships { get; } = [];
    public ObservableCollection<CompanyAgentSummaryDto> Agents { get; } = [];
    public ObservableCollection<MobileCompanyStatusMetricDto> SummaryKpis { get; } = [];
    public ObservableCollection<MobileTaskFollowUpSummaryDto> TaskFollowUps { get; } = [];
    public ObservableCollection<BriefingSourceReferenceDto> BriefingHighlights { get; } = [];

	public MainPage()
	{
		InitializeComponent();
        BindingContext = this;
        ProductDirectionLabel.Text = MobileCompanionScope.ProductDirectionDescription;
        SupportedFeaturesLabel.Text = MobileCompanionScope.CompanionScopeDescription;
        ResponsiveBridgeLabel.Text = MobileCompanionScope.ResponsiveWebBridgeMessage;
        ApiReuseLabel.Text = MobileCompanionScope.SharedBackendApiReuseMessage;
        WebFirstAdministrationLabel.Text = MobileCompanionScope.WebFirstAdministrationMessage;
        Loaded += async (_, _) => await BootstrapAsync();
	}

    private async void OnSignInClicked(object? sender, EventArgs e) => await BootstrapAsync(force: true);

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        if (!TryGetActiveCompany(out var companyId))
        {
            return;
        }

        await RunOperationAsync("Loading inbox...", async () =>
        {
            await LoadCompanionAsync(companyId);
            StatusLabel.Text = "Companion refreshed.";
        });
    }

    private async Task BootstrapAsync(bool force = false)
    {
        if (!ApplyConnectionSettings(force))
        {
            return;
        }

        await RunOperationAsync("Signing in...", async () =>
        {
            var context = await apiClient.GetCurrentUserAsync();
            activeUserScope = context.User.Id == Guid.Empty ? context.User.Email : context.User.Id.ToString("N");
            Memberships.ReplaceWith(context.Memberships);
            CompanyPicker.ItemsSource = Memberships;

            var rememberedCompanyId = Preferences.Default.Get("mobile.selectedCompanyId", string.Empty);
            var selectedCompany = Memberships.FirstOrDefault(x =>
                x.CompanyId.ToString().Equals(rememberedCompanyId, StringComparison.OrdinalIgnoreCase))
                ?? context.ActiveCompany?.ToMembership()
                ?? (Memberships.Count == 1 ? Memberships[0] : null);

            if (selectedCompany is null)
            {
                CompanyStatusLabel.Text = "Choose a company to continue.";
                StatusLabel.Text = context.CompanySelectionRequired
                    ? "Select a company."
                    : "No company memberships were returned for this user.";
                return;
            }

            CompanyPicker.SelectedItem = selectedCompany;
            await SelectCompanyAsync(selectedCompany.CompanyId);
        });
    }

    private async void OnCompanySelectionChanged(object? sender, EventArgs e)
    {
        if (CompanyPicker.SelectedItem is CompanyMembershipDto membership)
        {
            await RunOperationAsync($"Switching to {membership.CompanyName}...", async () =>
            {
                await SelectCompanyAsync(membership.CompanyId);
            });
        }
    }

    private async Task SelectCompanyAsync(Guid companyId)
    {
        var selection = await apiClient.SelectCompanyAsync(companyId);
        activeCompanyId = selection.CompanyId;
        localCache = new MobileLocalCache(activeUserScope, selection.CompanyId);
        apiClient.SetCompanyContext(selection.CompanyId);
        Preferences.Default.Set("mobile.selectedCompanyId", selection.CompanyId.ToString());
        await FlushPendingWritesAsync(selection.CompanyId);
        await LoadCompanionAsync(selection.CompanyId);
        StatusLabel.Text = $"{selection.ActiveCompany.CompanyName} selected.";
    }

    private async Task LoadCompanionAsync(Guid companyId)
    {
        var inboxTask = apiClient.GetInboxAsync(companyId);
        var mobileSummaryTask = apiClient.GetMobileSummaryAsync(companyId);
        var briefingTask = apiClient.GetLatestBriefingsAsync(companyId);
        var agentsTask = apiClient.GetAgentsAsync(companyId);
        var conversationsTask = apiClient.GetDirectConversationsAsync(companyId);

        try
        {
            // Agent summaries power chat selection only; mobile does not expose hiring or configuration.
            await Task.WhenAll(inboxTask, mobileSummaryTask, briefingTask, agentsTask, conversationsTask);
        }
        catch (MobileCompanionApiException) when (localCache is not null)
        {
            await RestoreCachedCompanionAsync();
            StatusLabel.Text = "Offline. Showing last successful mobile sync.";
            return;
        }

        var inbox = await inboxTask;
        PendingApprovals.ReplaceWith(inbox.PendingApprovals);
        Notifications.ReplaceWith(inbox.Alerts);
        InboxSummaryLabel.Text = $"{inbox.UnreadCount} unread";
        ApprovalSummaryLabel.Text = $"{inbox.PendingApprovalCount} pending approval(s).";
        if (localCache is not null)
        {
            await localCache.SaveAsync("inbox", inbox);
        }

        var mobileSummary = await mobileSummaryTask;
        CompanyStatusHeadlineLabel.Text = mobileSummary.CompanyStatus.Headline;
        CompanyStatusLabel.Text = $"{mobileSummary.CompanyStatus.CompanyName}: {mobileSummary.CompanyStatus.Subtitle}";
        SummaryKpis.ReplaceWith(mobileSummary.CompanyStatus.Metrics);
        TaskFollowUps.ReplaceWith(mobileSummary.TaskFollowUps);
        if (localCache is not null)
        {
            await localCache.SaveAsync("summary", mobileSummary);
        }

        var briefing = await briefingTask;
        if (briefing.Id is null)
        {
            BriefingTitleLabel.Text = "No daily briefing available.";
            BriefingSummaryLabel.Text = "Refresh later after the briefing pipeline runs.";
            BriefingHighlights.Clear();
        }
        else
        {
            BriefingTitleLabel.Text = briefing.Title;
            BriefingSummaryLabel.Text = briefing.Summary;
            BriefingHighlights.ReplaceWith(briefing.Highlights.Select(x => new BriefingSourceReferenceDto { Label = x }));
        }
        if (localCache is not null)
        {
            await localCache.SaveAsync("briefing", briefing);
        }

        Agents.ReplaceWith(await agentsTask);
        AgentPicker.ItemsSource = Agents;

        var conversations = await conversationsTask;
        Conversations.ReplaceWith(conversations.Items);
        if (localCache is not null)
        {
            await localCache.SaveAsync("conversations", conversations);
        }
    }

    private async Task RestoreCachedCompanionAsync()
    {
        if (localCache is null)
        {
            return;
        }

        if (await localCache.TryReadAsync<MobileInboxDto>("inbox") is { } inbox)
        {
            PendingApprovals.ReplaceWith(inbox.PendingApprovals);
            Notifications.ReplaceWith(inbox.Alerts);
            InboxSummaryLabel.Text = $"{inbox.UnreadCount} unread";
            ApprovalSummaryLabel.Text = $"{inbox.PendingApprovalCount} pending approval(s).";
        }

        if (await localCache.TryReadAsync<MobileHomeSummaryResponse>("summary") is { } mobileSummary)
        {
            CompanyStatusHeadlineLabel.Text = mobileSummary.CompanyStatus.Headline;
            CompanyStatusLabel.Text = $"{mobileSummary.CompanyStatus.CompanyName}: {mobileSummary.CompanyStatus.Subtitle}";
            SummaryKpis.ReplaceWith(mobileSummary.CompanyStatus.Metrics);
            TaskFollowUps.ReplaceWith(mobileSummary.TaskFollowUps);
        }

        if (await localCache.TryReadAsync<MobileBriefingDto>("briefing") is { } briefing)
        {
            BriefingTitleLabel.Text = briefing.Title ?? "No daily briefing available.";
            BriefingSummaryLabel.Text = briefing.Summary ?? "Refresh later after the briefing pipeline runs.";
            BriefingHighlights.ReplaceWith(briefing.Highlights.Select(x => new BriefingSourceReferenceDto { Label = x }));
        }

        if (await localCache.TryReadAsync<MobileConversationPageDto>("conversations") is { } conversations)
        {
            Conversations.ReplaceWith(conversations.Items);
        }
    }

    private async void OnApproveClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ApprovalInboxItemDto approval })
        {
            await DecideApprovalAsync(approval, "approve");
        }
    }

    private async void OnRejectClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ApprovalInboxItemDto approval })
        {
            await DecideApprovalAsync(approval, "reject");
        }
    }

    private async Task DecideApprovalAsync(MobileApprovalListItemDto approval, string decision)
    {
        if (!TryGetActiveCompany(out var companyId))
        {
            return;
        }

        if (approval.CurrentStep is null)
        {
            StatusLabel.Text = "This approval does not have a current decision step.";
            return;
        }

        await RunOperationAsync($"{decision} approval...", async () =>
        {
            var clientRequestId = Guid.NewGuid();
            var request = new ApprovalDecisionCommandDto
            {
                ApprovalId = approval.Id,
                StepId = approval.CurrentStep.Id,
                Decision = decision,
                ClientRequestId = clientRequestId
            };

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet && localCache is not null)
            {
                await localCache.QueueAsync("pending-approvals", new PendingMobileApprovalDecision
                {
                    ClientRequestId = clientRequestId,
                    ApprovalId = approval.Id,
                    StepId = approval.CurrentStep.Id,
                    Decision = decision
                });
                StatusLabel.Text = $"Offline. Approval {decision} queued for retry.";
                return;
            }

            await apiClient.DecideApprovalAsync(
                companyId,
                approval.Id,
                request);
            var inbox = await apiClient.GetInboxAsync(companyId);
            PendingApprovals.ReplaceWith(inbox.PendingApprovals);
            Notifications.ReplaceWith(inbox.Alerts);
            ApprovalSummaryLabel.Text = $"{inbox.PendingApprovalCount} pending approval(s).";
            StatusLabel.Text = $"Approval {decision}d.";
        });
    }

    private async void OnMarkReadClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: MobileAlertListItemDto notification })
        {
            await SetNotificationStatusAsync(notification, "read");
        }
    }

    private async void OnMarkActionedClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: MobileAlertListItemDto notification })
        {
            await SetNotificationStatusAsync(notification, "actioned");
        }
    }

    private async Task SetNotificationStatusAsync(MobileAlertListItemDto notification, string status)
    {
        if (!TryGetActiveCompany(out var companyId))
        {
            return;
        }

        await RunOperationAsync("Updating alert...", async () =>
        {
            var updated = await apiClient.SetNotificationStatusAsync(companyId, notification.Id, status);
            var index = Notifications.IndexOf(notification);
            if (index >= 0)
            {
                Notifications[index] = new MobileAlertListItemDto
                {
                    Id = updated.Id,
                    Type = updated.Type,
                    Priority = updated.Priority,
                    Title = updated.Title,
                    Summary = updated.Body,
                    Status = updated.Status,
                    RelatedEntityType = updated.RelatedEntityType,
                    RelatedEntityId = updated.RelatedEntityId,
                    CreatedAt = updated.CreatedAt,
                    ReadAt = updated.ReadAt
                };
            }

            StatusLabel.Text = "Alert updated.";
        });
    }

    private void OnAgentSelectionChanged(object? sender, EventArgs e)
    {
        if (AgentPicker.SelectedItem is CompanyAgentSummaryDto agent)
        {
            AgentIdEntry.Text = agent.Id.ToString();
        }
    }

    private async void OnOpenChatClicked(object? sender, EventArgs e)
    {
        if (!TryGetActiveCompany(out var companyId))
        {
            return;
        }

        if (!Guid.TryParse(AgentIdEntry.Text, out var agentId))
        {
            StatusLabel.Text = "Enter a valid agent ID.";
            return;
        }

        await RunOperationAsync("Opening chat...", async () =>
        {
            activeConversation = await apiClient.OpenConversationAsync(companyId, agentId);
            AgentIdEntry.Text = activeConversation.AgentId.ToString();
            await LoadMessagesAsync(companyId, activeConversation.Id);
            StatusLabel.Text = $"Chat opened with {activeConversation.AgentDisplayName}.";
        });
    }

    private async void OnLoadConversationsClicked(object? sender, EventArgs e)
    {
        if (!TryGetActiveCompany(out var companyId))
        {
            return;
        }

        await RunOperationAsync("Loading conversations...", async () =>
        {
            var page = await apiClient.GetDirectConversationsAsync(companyId);
            Conversations.ReplaceWith(page.Items);
            StatusLabel.Text = "Recent conversations loaded.";
        });
    }

    private async void OnConversationClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: MobileConversationSummaryDto conversation })
        {
            return;
        }

        if (!TryGetActiveCompany(out var companyId))
        {
            return;
        }

        await RunOperationAsync("Loading chat...", async () =>
        {
            activeConversation = new DirectConversationDto { Id = conversation.Id, AgentId = conversation.AgentId, AgentDisplayName = conversation.AgentDisplayName, AgentRoleName = conversation.AgentRoleName };
            AgentIdEntry.Text = conversation.AgentId.ToString();
            await LoadMessagesAsync(companyId, conversation.Id);
            StatusLabel.Text = $"Chat loaded with {conversation.AgentDisplayName}.";
        });
    }

    private async void OnSendMessageClicked(object? sender, EventArgs e)
    {
        if (!TryGetActiveCompany(out var companyId))
        {
            return;
        }

        if (activeConversation is null)
        {
            StatusLabel.Text = "Open or select a chat before sending.";
            return;
        }

        var body = MessageEditor.Text?.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            StatusLabel.Text = "Enter a message.";
            return;
        }

        await RunOperationAsync("Sending message...", async () =>
        {
            var clientRequestId = Guid.NewGuid();
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet && localCache is not null)
            {
                await localCache.QueueAsync("pending-chat", new PendingMobileChatSend
                {
                    ClientRequestId = clientRequestId,
                    ConversationId = activeConversation.Id,
                    Body = body
                });
                Messages.Add(new MobileMessageListItemDto { Id = clientRequestId, SenderType = "pending", Body = body, CreatedAt = DateTime.UtcNow });
                MessageEditor.Text = string.Empty;
                StatusLabel.Text = "Offline. Message queued for retry.";
                return;
            }

            var result = await apiClient.SendMessageAsync(companyId, activeConversation.Id, body, clientRequestId);
            activeConversation = result.Conversation;
            if (result.HumanMessage is not null)
            {
                Messages.Add(new MobileMessageListItemDto { Id = result.HumanMessage.Id, SenderType = result.HumanMessage.SenderType, Body = result.HumanMessage.Body, CreatedAt = result.HumanMessage.CreatedAt });
            }

            if (result.AgentMessage is not null)
            {
                Messages.Add(new MobileMessageListItemDto { Id = result.AgentMessage.Id, SenderType = result.AgentMessage.SenderType, Body = result.AgentMessage.Body, CreatedAt = result.AgentMessage.CreatedAt });
            }
            MessageEditor.Text = string.Empty;
            StatusLabel.Text = "Message sent.";
        });
    }

    private async Task LoadMessagesAsync(Guid companyId, Guid conversationId)
    {
        var page = await apiClient.GetMessagesAsync(companyId, conversationId);
        Messages.ReplaceWith(page.Items);
        if (localCache is not null)
        {
            await localCache.SaveAsync($"messages.{conversationId:N}", page);
        }
    }

    private bool ApplyConnectionSettings(bool force)
    {
        var storedBaseUrl = Preferences.Default.Get("mobile.apiBaseUrl", ApiBaseUrlEntry.Text?.Trim() ?? string.Empty);
        var baseUrl = force ? ApiBaseUrlEntry.Text?.Trim() : storedBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseAddress))
        {
            StatusLabel.Text = "Enter a valid API base URL.";
            return false;
        }

        var subject = DevelopmentSubjectEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(subject))
        {
            StatusLabel.Text = "Enter a development subject.";
            return false;
        }

        Preferences.Default.Set("mobile.apiBaseUrl", baseAddress.ToString());
        Preferences.Default.Set("mobile.devSubject", subject);
        apiClient.BaseAddress = baseAddress;
        apiClient.SetDevelopmentIdentity(subject, DevelopmentEmailEntry.Text?.Trim(), DevelopmentDisplayNameEntry.Text?.Trim());
        return true;
    }

    private bool TryGetActiveCompany(out Guid companyId)
    {
        if (activeCompanyId is Guid selectedCompanyId)
        {
            companyId = selectedCompanyId;
            return true;
        }

        companyId = default;
        StatusLabel.Text = "Select a company first.";
        return false;
    }

    private async Task RunOperationAsync(string progressMessage, Func<Task> operation)
    {
        StatusLabel.Text = progressMessage;
        try
        {
            await operation();
        }
        catch (UnauthorizedAccessException)
        {
            StatusLabel.Text = "Session expired or unauthorized. Sign in again.";
        }
        catch (MobileCompanionApiException ex)
        {
            StatusLabel.Text = ex.Message;
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Operation canceled.";
        }
    }

    private async Task FlushPendingWritesAsync(Guid companyId)
    {
        if (localCache is null || Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            return;
        }

        var pendingApprovals = await localCache.ReadQueueAsync<PendingMobileApprovalDecision>("pending-approvals");
        foreach (var pending in pendingApprovals)
        {
            await apiClient.DecideApprovalAsync(
                companyId,
                pending.ApprovalId,
                new ApprovalDecisionCommandDto
                {
                    ApprovalId = pending.ApprovalId,
                    StepId = pending.StepId,
                    Decision = pending.Decision,
                    ClientRequestId = pending.ClientRequestId
                });
        }

        if (pendingApprovals.Count > 0)
        {
            await localCache.ClearQueueAsync("pending-approvals");
        }

        var pendingChat = await localCache.ReadQueueAsync<PendingMobileChatSend>("pending-chat");
        foreach (var pending in pendingChat)
        {
            await apiClient.SendMessageAsync(companyId, pending.ConversationId, pending.Body, pending.ClientRequestId);
        }

        if (pendingChat.Count > 0)
        {
            await localCache.ClearQueueAsync("pending-chat");
        }
    }

    private static CompanyMembershipDto ToMembership(this ResolvedCompanyContextDto context) =>
        new()
        {
            MembershipId = context.MembershipId,
            CompanyId = context.CompanyId,
            CompanyName = context.CompanyName,
            MembershipRole = context.MembershipRole,
            Status = context.Status
        };
}

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
