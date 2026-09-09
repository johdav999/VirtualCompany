using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Sales;

namespace VirtualCompany.Web.Tests;

public sealed class SalesPresentationDeckWorkflowTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AgentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string PowerPointContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    [Fact]
    public void Session_is_required_before_upload_controls_are_available()
    {
        var handler = new DeckHandler();
        using var context = CreateContext(handler);
        var cut = Render(context, sessionId: Guid.Empty, actions: []);

        Assert.Contains("Save the session to unlock", cut.Markup);
        Assert.Empty(cut.FindAll("input[type='file']"));
        Assert.Equal(0, handler.ImportCalls);
    }

    [Fact]
    public void Invalid_extension_and_oversized_file_are_rejected_before_transport()
    {
        var handler = new DeckHandler();
        using var context = CreateContext(handler);
        var cut = Render(context, maximumBytes: 4);
        var input = cut.FindComponent<InputFile>();

        input.UploadFiles(InputFileContent.CreateFromText("data", "notes.pdf", contentType: "application/pdf"));
        Assert.Contains("Choose a PowerPoint .pptx file", cut.Markup);

        input.UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3, 4, 5], "large.pptx", contentType: PowerPointContentType));
        Assert.Contains("exceeds the 4 B limit", cut.Markup);
        Assert.Equal(0, handler.ImportCalls);
    }

    [Fact]
    public void Empty_file_is_rejected_before_transport()
    {
        var handler = new DeckHandler();
        using var context = CreateContext(handler);
        var cut = Render(context);

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([], "empty.pptx", contentType: PowerPointContentType));

        Assert.Contains("selected file is empty", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.ImportCalls);
    }

    [Fact]
    public void Valid_upload_polls_to_processed_three_slide_state()
    {
        var handler = new DeckHandler();
        handler.GetResponses.Enqueue(Deck("processing"));
        handler.GetResponses.Enqueue(Deck("processed", slideCount: 3));
        using var context = CreateContext(handler);
        var refreshes = 0;
        var cut = Render(context, refresh: () => refreshes++);

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1, 2, 3], "wellheld-overview.pptx", contentType: PowerPointContentType));
        Assert.Equal("wellheld-overview", cut.Find("#presentation-deck-title-input").GetAttribute("value"));
        cut.Find(".presentation-upload-actions .sales-button--primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, handler.ImportCalls);
            Assert.Contains("Processing completed with 3 slide", cut.Markup);
            Assert.Contains("Make active", cut.Markup);
        });
        Assert.Equal("wellheld-overview.pptx", handler.UploadedFileName);
        Assert.True(refreshes >= 2);
    }

    [Fact]
    public void Retry_is_visible_only_for_authoritatively_retryable_failures()
    {
        var retryable = PreparationDeck("failed", canRetry: true, failureSummary: "Rendering could not start.");
        var permanent = PreparationDeck("failed", canRetry: false, version: 2, failureSummary: "This file is protected.");
        var handler = new DeckHandler();
        handler.GetResponses.Enqueue(Deck("processed", slideCount: 3));
        using var context = CreateContext(handler);
        var cut = Render(context, decks: [permanent, retryable]);

        Assert.Single(cut.FindAll("button"), x => x.TextContent.Contains("Retry processing", StringComparison.Ordinal));
        cut.FindAll("button").Single(x => x.TextContent.Contains("Retry processing", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, handler.RetryCalls);
            Assert.Contains("Processing completed with 3 slide", cut.Markup);
        });
    }

    [Fact]
    public void Activation_requires_explicit_confirmation_and_marks_only_one_deck_active()
    {
        var candidate = PreparationDeck("processed", slideCount: 3);
        var current = PreparationDeck("processed", slideCount: 3, version: 2, isActive: true);
        var handler = new DeckHandler { ActivationResponse = Deck("processed", slideCount: 3, isActive: true) };
        using var context = CreateContext(handler);
        var cut = Render(context, decks: [current, candidate]);

        cut.FindAll("button").Single(x => x.TextContent.Contains("Make active", StringComparison.Ordinal)).Click();
        Assert.Equal(0, handler.ActivationCalls);
        Assert.Contains("Make this the active presentation", cut.Markup);

        cut.FindAll("button").Single(x => x.TextContent.Contains("Confirm activation", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Equal(1, handler.ActivationCalls));
        Assert.Contains("presentation is active", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Single(cut.FindAll(".presentation-deck-item--active"));
    }

    [Fact]
    public void Processed_zero_slide_and_permanent_failure_states_have_no_invalid_actions()
    {
        var zero = PreparationDeck("processed", slideCount: 0);
        var failed = PreparationDeck("failed", canRetry: false, version: 2, failureSummary: "The file is password protected.");
        using var context = CreateContext(new DeckHandler());
        var cut = Render(context, decks: [failed, zero]);

        Assert.Contains("returned no slides", cut.Markup);
        Assert.Contains("password protected", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), x => x.TextContent.Contains("Make active", StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll("button"), x => x.TextContent.Contains("Retry processing", StringComparison.Ordinal));
    }

    [Fact]
    public void Activation_conflict_is_explained_and_refreshes_authoritative_state()
    {
        var handler = new DeckHandler { ConflictOnActivate = true };
        using var context = CreateContext(handler);
        var refreshes = 0;
        var cut = Render(context, decks: [PreparationDeck("processed", slideCount: 3)], refresh: () => refreshes++);

        cut.FindAll("button").Single(x => x.TextContent.Contains("Make active", StringComparison.Ordinal)).Click();
        cut.FindAll("button").Single(x => x.TextContent.Contains("Confirm activation", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, handler.ActivationCalls);
            Assert.Equal(1, refreshes);
            Assert.Contains("changed", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("reload", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Disposal_cancels_inflight_polling_without_an_orphan_request()
    {
        var handler = new DeckHandler { HoldGetUntilCancelled = true };
        using var context = CreateContext(handler);
        var cut = Render(context);
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1, 2, 3], "deck.pptx", contentType: PowerPointContentType));

        var uploadTask = cut.Find(".presentation-upload-actions .sales-button--primary").ClickAsync(new MouseEventArgs());
        await handler.GetStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cut.Instance.DisposeAsync();
        await uploadTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(handler.GetObservedCancellation);
        Assert.Equal(1, handler.ImportCalls);
    }

    private static TestContext CreateContext(DeckHandler handler)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var transport = new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        context.Services.AddSingleton(new SalesPresentationDeckApiClient(transport, false));
        context.Services.AddSingleton(new SalesPresentationPreparationTelemetry());
        return context;
    }

    private static IRenderedComponent<SalesPresentationDeckWorkflow> Render(
        TestContext context,
        Guid? sessionId = null,
        long maximumBytes = 1024,
        IReadOnlyList<SalesPresentationPreparationDeckViewModel>? decks = null,
        IReadOnlyList<string>? actions = null,
        Action? refresh = null) =>
        context.RenderComponent<SalesPresentationDeckWorkflow>(parameters => parameters
            .Add(x => x.CompanyId, CompanyId)
            .Add(x => x.SessionId, sessionId ?? SessionId)
            .Add(x => x.AgentId, AgentId)
            .Add(x => x.PresenterDisplayName, "Alex")
            .Add(x => x.MaximumUploadBytes, maximumBytes)
            .Add(x => x.Decks, decks ?? [])
            .Add(x => x.AllowedActions, actions ?? ["upload_deck", "retry_processing", "activate_deck"])
            .Add(x => x.PollInterval, TimeSpan.FromMilliseconds(1))
            .Add(x => x.MaximumPollAttempts, 4)
            .Add(x => x.RefreshRequested, refresh ?? (() => { })));

    private static SalesPresentationPreparationDeckViewModel PreparationDeck(
        string status, int slideCount = 0, bool canRetry = false, int version = 1,
        bool isActive = false, string? failureSummary = null) =>
        new(Guid.Parse($"{version:D8}-4444-4444-4444-444444444444"), AgentId, version,
            $"Welheld v{version}", $"welheld-v{version}.pptx", status, 1, 1, slideCount,
            status == "failed" ? "renderer_unavailable" : null, failureSummary, canRetry,
            isActive, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow, status == "processed" ? DateTime.UtcNow : null,
            status == "failed" ? DateTime.UtcNow : null, isActive ? DateTime.UtcNow : null, 1);

    private static SalesPresentationDeckViewModel Deck(
        string status, int slideCount = 0, bool canRetry = false, bool isActive = false) =>
        new(Guid.Parse("00000001-4444-4444-4444-444444444444"), CompanyId, SessionId, AgentId,
            1, "Welheld", "wellheld.pptx", PowerPointContentType, 3, new string('a', 64),
            "hidden/storage/key", null, status, 1, 1, slideCount, null, null, null,
            status == "failed" ? "renderer_unavailable" : null,
            status == "failed" ? "Rendering could not start." : null, canRetry, isActive, 0, false,
            Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow,
            status == "processing" ? DateTime.UtcNow.AddMinutes(-1) : null,
            status == "processed" ? DateTime.UtcNow : null,
            status == "failed" ? DateTime.UtcNow : null,
            isActive ? DateTime.UtcNow : null, 1, []);

    private sealed class DeckHandler : HttpMessageHandler
    {
        public Queue<SalesPresentationDeckViewModel> GetResponses { get; } = [];
        public TaskCompletionSource GetStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldGetUntilCancelled { get; init; }
        public bool GetObservedCancellation { get; private set; }
        public bool ConflictOnActivate { get; init; }
        public int ImportCalls { get; private set; }
        public int RetryCalls { get; private set; }
        public int ActivationCalls { get; private set; }
        public string? UploadedFileName { get; private set; }
        public SalesPresentationDeckViewModel? ActivationResponse { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/decks", StringComparison.Ordinal))
            {
                ImportCalls++;
                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                UploadedFileName = multipart.Single(x => x.Headers.ContentDisposition!.Name == "file")
                    .Headers.ContentDisposition!.FileName!.Trim('"');
                return Json(Deck("pending_scan"), HttpStatusCode.Accepted);
            }
            if (request.Method == HttpMethod.Get && path.Contains("/decks/", StringComparison.Ordinal))
            {
                GetStarted.TrySetResult();
                if (HoldGetUntilCancelled)
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException) { GetObservedCancellation = true; throw; }
                }
                return Json(GetResponses.Count > 0 ? GetResponses.Dequeue() : Deck("processing"));
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/retry", StringComparison.Ordinal))
            {
                RetryCalls++;
                return Json(Deck("pending_scan"), HttpStatusCode.Accepted);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/activate", StringComparison.Ordinal))
            {
                ActivationCalls++;
                if (ConflictOnActivate)
                    return new HttpResponseMessage(HttpStatusCode.Conflict)
                    {
                        Content = new StringContent("{\"title\":\"Deck state changed\",\"status\":409}", Encoding.UTF8, "application/problem+json")
                    };
                return Json(ActivationResponse ?? Deck("processed", 3, isActive: true), HttpStatusCode.Accepted);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = JsonContent.Create(value)
        };
    }
}
