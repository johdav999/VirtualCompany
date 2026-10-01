using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;

// Explicit opt-in: bounded synthetic provider compatibility probe, no microphone,
// customer records, media publication, stored PCM, or modified room configuration.
if (!args.Contains("--live")) { Console.WriteLine("Pass --live to run one billable synthetic Realtime greeting probe."); return; }
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
var options = new SharedRealtimeAgentOptions { Enabled = true };
using var gateway = new OpenAiRealtimeAgentSessionGateway(new Factory(), Options.Create(options),
    new ProbeLogger());
var company = Guid.NewGuid(); var user = Guid.NewGuid(); var agent = Guid.NewGuid(); var turn = Guid.NewGuid();
var timer = Stopwatch.StartNew();
var checking = args.Contains("--checking");
var heard = checking ? "How does your company onboard customers?" : "Welcome to the meeting, Alex!";
string? session = null;
try
{
    var connection = await gateway.CreatePcmSessionAsync(new(company, user, agent, "synthetic_dialogue_probe",
        "Choose an available tool for the confirmed input. No company facts. Start means restart from slide one; resume means saved position. " +
        "Select for the latest user message, not an earlier unanswered message. Company-specific questions require ask_grounded_question; " +
        "explicitly general education uses explain_general. Pause only on a request to pause. " +
        "Politeness or silence never authorizes playback; ambiguous assent needs clarification. Tool arguments must be empty.",
        new[] { ("respond_social", "Reply to a greeting"), ("start_presentation", "Start or restart approved narration from slide one"),
            ("pause_presentation", "Pause narration"), ("resume_presentation", "Continue saved narration"),
            ("clarify_input", "Clarify ambiguous assent"), ("wait_for_reply", "Keep listening, no action"),
            ("ask_grounded_question", "Retrieve approved evidence for company-specific questions including onboarding"),
            ("explain_general", "Explain explicitly general concepts, never company-specific facts") }
            .Select(x => new RealtimeAgentToolDefinition(x.Item1, x.Item2, "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false,\"required\":[]}", "recommend")).ToArray(),
        TimeSpan.FromMinutes(1), ManualInputCommit: true, ConversationProfile: true), timeout.Token);
    session = connection.ProviderSessionId;
    await gateway.AddConfirmedTurnAsync(session, turn, heard, timeout.Token);
    await gateway.RequestResponseAsync(session, new(turn, false, 512, KeepProviderContext: true, AutomaticToolChoice: true), timeout.Token);
    string? call = null;
    await foreach (var output in gateway.ReceiveOutputAsync(session, timeout.Token))
    {
        if (output.ProviderEventJson is null) continue;
        RealtimeAgentEvent normalized;
        try { normalized = await gateway.NormalizeEventAsync(new(session, output.ProviderEventId!, output.Sequence, output.ProviderEventJson), timeout.Token); }
        catch (RealtimeAgentEventException ex) when (ex.Code == "unsupported_event") { continue; }
        if (normalized.Type == RealtimeAgentEventTypes.ToolInvocation)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { selected = normalized.ToolName, correlated = output.TurnId == turn }));
            if (output.TurnId != turn || normalized.ToolName != (checking ? "ask_grounded_question" : "respond_social")) throw new InvalidOperationException("Unexpected probe routing.");
            call = normalized.ToolCallId;
        }
        if (call is not null && normalized.Type == RealtimeAgentEventTypes.UsageUpdated) break;
    }
    if (call is null) throw new InvalidOperationException("No complete social proposal received.");
    await gateway.SubmitToolResultAsync(session, call, "{\"accepted\":true,\"status\":\"candidate_generation_only\"}", timeout.Token);
    var routingMs = timer.ElapsedMilliseconds;
    // Invoke the real capability prompt without making its internal policy a public API.
    var speechPolicy = System.Reflection.Assembly.Load("VirtualCompany.Infrastructure.Sales")
        .GetType("VirtualCompany.Infrastructure.Sales.SalesRoomDialoguePolicy")!;
    var instructions = (string)speechPolicy.GetMethod("SpeechInstructions")!
        .Invoke(null, [checking ? "checking_sources" : "respond_social"])!;
    var candidate = await gateway.GenerateBufferedSpeechAsync(session, new(company, user, agent, turn,
        heard, instructions), timeout.Token);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { path = "buffered_realtime_candidate_only", model = options.Model,
        routingMs, totalMs = timer.ElapsedMilliseconds, audioMilliseconds = candidate.Pcm.Length / 48,
        candidate.Text, candidate.InputTokens, candidate.OutputTokens, published = false,
        note = "Provider compatibility only; independent content release and browser audibility are not exercised." }));
    await gateway.FinishBufferedSpeechAsync(candidate, 0, false, timeout.Token);
    if (args.Contains("--playback"))
    {
        foreach (var probe in new[] { ("Please start the presentation from the beginning", "start_presentation", "paused"),
            ("Please pause the presentation", "pause_presentation", "agent"),
            ("Please continue from where we stopped", "resume_presentation", "paused"),
            ("How does your company onboard new companies?", "ask_grounded_question", "paused"),
            ("Thank you for explaining that", "respond_social", "paused") })
        {
            var input = Guid.NewGuid();
            await gateway.AddConfirmedTurnAsync(session, input, probe.Item1, timeout.Token);
            await gateway.RequestResponseAsync(session, new(input, false, 512, KeepProviderContext: true, AutomaticToolChoice: true,
                AvailableTools: (probe.Item3 == "agent" ? new[] { "pause_presentation" } : new[] { "start_presentation", "resume_presentation" })
                    .Concat(new[] { "respond_social", "ask_grounded_question", "explain_general", "clarify_input", "wait_for_reply" }).ToArray(),
                PlaybackContext: System.Text.Json.JsonSerializer.Serialize(new { state = probe.Item3, slide = 1, point = 2, savedNarrationOffsetMilliseconds = 1250 })), timeout.Token);
            string? selected = null;
            await foreach (var output in gateway.ReceiveOutputAsync(session, timeout.Token))
            {
                if (output.ProviderEventJson is null) continue;
                RealtimeAgentEvent normalized;
                try { normalized = await gateway.NormalizeEventAsync(new(session, output.ProviderEventId!, output.Sequence, output.ProviderEventJson), timeout.Token); }
                catch (RealtimeAgentEventException ex) when (ex.Code == "unsupported_event") { continue; }
                if (normalized.Type == RealtimeAgentEventTypes.ToolInvocation && output.TurnId == input)
                {
                    selected = normalized.ToolName;
                    await gateway.SubmitToolResultAsync(session, normalized.ToolCallId!, "{\"accepted\":false,\"status\":\"synthetic_probe_no_execution\"}", timeout.Token);
                }
                if (normalized.Type == RealtimeAgentEventTypes.UsageUpdated && output.TurnId == input) break;
            }
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { path = "synthetic_playback_tool", selected, expected = probe.Item2, executed = false }));
            if (selected != probe.Item2) throw new InvalidOperationException("Unexpected playback tool routing.");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { status = "failed", type = ex.GetType().Name,
        code = ex is RealtimeAgentEventException e ? e.Code : ex is RealtimeAgentUnavailableException u ? u.Code : "probe_failed" }));
    Environment.ExitCode = 1;
}
finally { if (session is not null) await gateway.TerminatePcmSessionAsync(session, CancellationToken.None); }

sealed class Factory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
sealed class ProbeLogger : ILogger<OpenAiRealtimeAgentSessionGateway>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
    {
        // Whitelist only categorical provider failure diagnostics, never payloads or credentials.
        if (state is IEnumerable<KeyValuePair<string, object?>> fields)
            foreach (var field in fields.Where(x => x.Key is "Status" or "Reason" or "Code"))
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { diagnostic = field.Key, value = field.Value }));
    }
}
