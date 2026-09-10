using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

/// <summary>Explicit external harness: synthetic content, at most four requests, no retries.</summary>
public sealed class SalesNarrationLiveTests
{
    [NarrationLiveFact]
    [Trait("Category", "External")]
    public async Task English_and_Swedish_generate_validate_store_and_preview_through_production_services()
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("VC_NARRATION_LIVE_DIRECTORY")!);
        Directory.CreateDirectory(directory);
        var realtimeOptions = Options.Create(new SharedRealtimeAgentOptions { Enabled = true });
        using var gateway = new OpenAiRealtimeAgentSessionGateway(new Clients(), realtimeOptions, NullLogger<OpenAiRealtimeAgentSessionGateway>.Instance);
        var speech = new ApprovedSpeechGateway(gateway, gateway, realtimeOptions);
        var storage = new LocalCompanyDocumentStorage(new EnvironmentInfo(directory),
            Options.Create(new CompanyDocumentOptions { Storage = new() { RootPath = Path.Combine(directory,"objects") } }));
        f.Options.RateVersion = "2026-09-10 Standard USD conservative maximum modality rates; not invoice reconciled";
        f.Service = new(f.Db, speech, storage, f.Clock, Options.Create(f.Options));
        f.Worker = new(f.Db, new SalesNarrationTests.NarrationContext(f.Company,f.Actor), f.Service, speech, storage, Options.Create(f.Options), f.Clock);
        var results = new List<object>();
        foreach (var language in new[] { "en", "sv" })
        {
            var scripts = language == "en"
                ? new[] { new SalesNarrationScript(1,1,"Welcome to this fictional product presentation."), new SalesNarrationScript(2,1,"We will discuss your goals and next steps.") }
                : new[] { new SalesNarrationScript(1,1,"Välkommen till denna fiktiva produktpresentation."), new SalesNarrationScript(2,1,"Vi kommer att diskutera era mål och nästa steg.") };
            var draft = await f.Service.PrepareAsync(f.Company,f.Actor,f.Session,new(language,scripts),default);
            await f.Approve(draft); await f.Generate();
            var revision = (await f.Service.GetAsync(f.Company,f.Actor,f.Session,default)).Revisions.Single(x => x.Id == draft.Id);
            results.Add(new { language, revision.Status, revision.GeneratedMinutes, revision.InputTokens, revision.OutputTokens,
                revision.EstimatedCostUsd, revision.UnresolvedAttempts, segments = revision.Segments.Select(x => new { x.Status,x.DurationMilliseconds,x.Attempts,x.FailureCode }) });
            await File.WriteAllTextAsync(Path.Combine(directory,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions { WriteIndented=true }));
            Assert.Equal("ready",revision.Status);
            var before = await f.Db.SalesNarrationAttempts.CountAsync();
            var audio = await f.Service.PreviewAsync(f.Company,f.Actor,f.Request(revision),default);
            await File.WriteAllBytesAsync(Path.Combine(directory,language+"-preview.wav"),audio.Audio);
            Assert.Equal(before,await f.Db.SalesNarrationAttempts.CountAsync());
            Assert.Equal(revision.Id,(await f.Service.PrepareAsync(f.Company,f.Actor,f.Session,new(language,scripts),default)).Id);
        }
    }
    private sealed class Clients : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
    private sealed class EnvironmentInfo(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "NarrationExternalTest";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
public sealed class NarrationLiveFactAttribute : FactAttribute
{
    public NarrationLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VC_NARRATION_LIVE") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VC_NARRATION_LIVE_DIRECTORY")))
            Skip = "Explicit synthetic narration live run, provider key and output directory are required.";
    }
}

