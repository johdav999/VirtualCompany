using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Shared;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePageTests
{
    [Fact]
    public void Finance_page_shows_generate_action_without_auto_request_when_seed_data_is_absent()
    {
        var companyId = Guid.Parse("7b791dba-0d66-4717-a832-8f562cdf07ce");
        var entryResponses = new ConcurrentQueue<FinanceEntryInitializationResponse>(new[]
        {
            new FinanceEntryInitializationResponse
            {
                CompanyId = companyId,
                InitializationStatus = FinanceEntryInitializationContractValues.Initializing,
                ProgressState = FinanceEntryProgressStateContractValues.NotSeeded,
                SeedingState = FinanceSeedingStateContractValues.NotSeeded,
                SeedJobActive = false,
                Message = "Finance data has not been initialized yet."
            }
        });
        var requestedPaths = new List<string>();

        using var harness = CreateHarness(companyId, CreateCurrentUser(companyId, "owner"), entryResponses, requestResponse: null, retryResponse: null, manualSeedResponse: null, requestedPaths);
        harness.Navigation.NavigateTo($"http://localhost/finance?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<FinancePage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Generate finance data", cut.Markup);
            Assert.DoesNotContain("Initializing finance data", cut.Markup);
        });

        Assert.Contains(requestedPaths, path => path == $"/internal/companies/{companyId:D}/finance/entry-state");
        Assert.DoesNotContain(requestedPaths, path => path.EndsWith("/entry-state/request", StringComparison.Ordinal));
        Assert.DoesNotContain(requestedPaths, path => path.EndsWith("/manual-seed", StringComparison.Ordinal));
    }

    [Fact]
    public void Finance_page_requires_confirmation_before_replace_regenerate_request_is_sent()
    {
        var companyId = Guid.Parse("d339f54e-4219-4e5f-baf7-a3bfd5e92903");
        var entryResponses = new ConcurrentQueue<FinanceEntryInitializationResponse>(new[]
        {
            new FinanceEntryInitializationResponse
            {
                CompanyId = companyId,
                InitializationStatus = FinanceEntryInitializationContractValues.Ready,
                ProgressState = FinanceEntryProgressStateContractValues.Seeded,
                SeedingState = FinanceSeedingStateContractValues.FullySeeded,
                Message = "Finance data is ready."
            }
        });
        var manualSeedResponse = new FinanceEntryInitializationResponse
        {
            CompanyId = companyId,
            InitializationStatus = FinanceEntryInitializationContractValues.Initializing,
            ProgressState = FinanceEntryProgressStateContractValues.SeedingRequested,
            SeedingState = FinanceSeedingStateContractValues.Seeding,
            SeedJobEnqueued = true,
            SeedJobActive = true,
            Message = "Finance seed regeneration was requested in the background."
        };
        var requestedPaths = new List<string>();
        FinanceManualSeedRequest? capturedManualSeedRequest = null;

        using var harness = CreateHarness(companyId, CreateCurrentUser(companyId, "owner"), entryResponses, requestResponse: null, retryResponse: null, manualSeedResponse, requestedPaths, request => capturedManualSeedRequest = request);
        harness.Navigation.NavigateTo($"http://localhost/finance?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<FinancePage>(parameters => parameters.Add(x => x.CompanyId, companyId));
        cut.WaitForAssertion(() => Assert.Contains("Regenerate finance data", cut.Markup));

        cut.Find("#finance-seed-action").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Replace existing finance data", cut.Markup);
            Assert.DoesNotContain(requestedPaths, path => path.EndsWith("/manual-seed", StringComparison.Ordinal));
        });

        cut.Find("#finance-seed-confirm").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Initializing finance data", cut.Markup);
            Assert.Contains(requestedPaths, path => path.EndsWith("/manual-seed", StringComparison.Ordinal));
            Assert.NotNull(capturedManualSeedRequest);
            Assert.Equal(FinanceManualSeedModes.Replace, capturedManualSeedRequest!.Mode);
            Assert.True(capturedManualSeedRequest.ConfirmReplace);
        });
    }

    [Fact]
    public void Finance_page_shows_retry_guidance_for_failed_state_and_retries_through_same_entry_endpoint()
    {
        var companyId = Guid.Parse("44f7a342-08c8-4b33-ab40-39180c5c97f0");
        var entryResponses = new ConcurrentQueue<FinanceEntryInitializationResponse>(new[]
        {
            new FinanceEntryInitializationResponse
            {
                CompanyId = companyId,
                InitializationStatus = FinanceEntryInitializationContractValues.Failed,
                ProgressState = FinanceEntryProgressStateContractValues.Failed,
                SeedingState = FinanceSeedingStateContractValues.NotSeeded,
                CanRetry = true,
                Message = "Finance setup failed: worker timeout."
            }
        });

        var retryResponse = new FinanceEntryInitializationResponse
        {
            CompanyId = companyId,
            InitializationStatus = FinanceEntryInitializationContractValues.Ready,
            ProgressState = FinanceEntryProgressStateContractValues.Seeded,
            SeedingState = FinanceSeedingStateContractValues.FullySeeded,
            Message = "Finance data is ready."
        };
        var requestResponse = new FinanceEntryInitializationResponse
        {
            CompanyId = companyId,
            InitializationStatus = FinanceEntryInitializationContractValues.Initializing,
            ProgressState = FinanceEntryProgressStateContractValues.SeedingRequested,
            SeedingState = FinanceSeedingStateContractValues.NotSeeded,
            Message = "Finance setup was requested in the background."
        };

        using var harness = CreateHarness(companyId, CreateCurrentUser(companyId, "owner"), entryResponses, requestResponse, retryResponse);
        harness.Navigation.NavigateTo($"http://localhost/finance?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<FinancePage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Finance setup needs attention", cut.Markup);
            Assert.Contains("Retrying is safe", cut.Markup);
            Assert.Contains("worker timeout", cut.Markup);
        });

        cut.Find("button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Cash position", cut.Markup);
            Assert.Contains("Transactions", cut.Markup);
        });
    }

    [Fact]
    public void Finance_page_renders_live_simulation_controls_when_finance_is_ready()
    {
        var companyId = Guid.Parse("c99702c3-ecda-49ac-b782-5ebcfdbf1471");
        var entryResponses = new ConcurrentQueue<FinanceEntryInitializationResponse>(new[]
        {
            new FinanceEntryInitializationResponse
            {
                CompanyId = companyId,
                InitializationStatus = FinanceEntryInitializationContractValues.Ready,
                ProgressState = FinanceEntryProgressStateContractValues.Seeded,
                SeedingState = FinanceSeedingStateContractValues.FullySeeded,
                Message = "Finance data is ready."
            }
        });

        using var harness = CreateHarness(
            companyId,
            CreateCurrentUser(companyId, "owner"),
            entryResponses,
            requestResponse: null,
            retryResponse: null,
            manualSeedResponse: null,
            simulationStateResponse: FinanceCompanySimulationStateResponse.NotStarted(companyId));
        harness.Navigation.NavigateTo($"http://localhost/finance?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<FinancePage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Finance progression controls", cut.Markup);
            Assert.Contains("Generate financial data during simulation", cut.Markup);
            Assert.Contains("Refresh", cut.Markup);
        });
    }

    [Fact]
    public void Finance_page_updates_status_optimistically_when_start_is_clicked()
    {
        var companyId = Guid.Parse("e897fbb5-f5a3-4fe3-8906-7f7f1d5d67f9");
        var entryResponses = new ConcurrentQueue<FinanceEntryInitializationResponse>(new[]
        {
            new FinanceEntryInitializationResponse
            {
                CompanyId = companyId,
                InitializationStatus = FinanceEntryInitializationContractValues.Ready,
                ProgressState = FinanceEntryProgressStateContractValues.Seeded,
                SeedingState = FinanceSeedingStateContractValues.FullySeeded,
                Message = "Finance data is ready."
            }
        });
        var simulationResponses = new ConcurrentQueue<FinanceCompanySimulationStateResponse>(new[]
        {
            FinanceCompanySimulationStateResponse.NotStarted(companyId)
        });
        var startResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var harness = CreateHarness(
            companyId,
            CreateCurrentUser(companyId, "owner"),
            entryResponses,
            requestResponse: null,
            retryResponse: null,
            manualSeedResponse: null,
            simulationStateResponse: null,
            simulationStateResponses: simulationResponses,
            financeOverrideHandler: (request, _) =>
            {
                if (request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/simulation/start" &&
                    request.Method == HttpMethod.Post)
                {
                    return startResponse.Task;
                }

                return Task.FromResult<HttpResponseMessage?>(null);
            });
        harness.Navigation.NavigateTo($"http://localhost/finance?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<FinancePage>(parameters => parameters.Add(x => x.CompanyId, companyId));
        cut.WaitForAssertion(() => Assert.Contains("Stopped", cut.Markup));

        cut.FindAll("button").Single(button => button.TextContent.Contains("Start", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Running", cut.Markup);
            Assert.Contains("Generate financial data during simulation", cut.Markup);
        });

        startResponse.SetResult(CreateJsonResponse(new FinanceCompanySimulationStateResponse
        {
            CompanyId = companyId,
            Status = FinanceCompanySimulationStatusValues.Running,
            CurrentSimulatedDateTime = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            LastProgressionTimestamp = new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc),
            GenerationEnabled = true,
            CanPause = true,
            CanStop = true,
            SupportsRefresh = true,
            SupportsStepForwardOneDay = true
        }));

        cut.WaitForAssertion(() => Assert.Contains("Simulation started.", cut.Markup));
    }

    [Fact]
    public void Finance_page_hides_simulation_panel_when_ui_visibility_is_disabled_by_state()
    {
        var companyId = Guid.Parse("69deecbb-5d56-4fd6-8eaf-b04b870c9732");
        var entryResponses = new ConcurrentQueue<FinanceEntryInitializationResponse>(new[]
        {
            new FinanceEntryInitializationResponse
            {
                CompanyId = companyId,
                InitializationStatus = FinanceEntryInitializationContractValues.Ready,
                ProgressState = FinanceEntryProgressStateContractValues.Seeded,
                SeedingState = FinanceSeedingStateContractValues.FullySeeded,
                Message = "Finance data is ready."
            }
        });

        using var harness = CreateHarness(
            companyId,
            CreateCurrentUser(companyId, "owner"),
            entryResponses,
            requestResponse: null,
            retryResponse: null,
            manualSeedResponse: null,
            simulationStateResponse: new FinanceCompanySimulationStateResponse
            {
                CompanyId = companyId,
                Status = FinanceCompanySimulationStatusValues.NotStarted,
                UiVisible = false,
                BackendExecutionEnabled = true,
                BackgroundJobsEnabled = true
            });
        harness.Navigation.NavigateTo($"http://localhost/finance?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<FinancePage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Finance progression controls", cut.Markup);
            Assert.DoesNotContain("Generate financial data during simulation", cut.Markup);
        });
    }

    [Fact]
    public void Finance_page_renders_recent_simulation_history_with_day_level_logs()
    {
        var companyId = Guid.Parse("bfa52e67-bf7d-4619-b024-c4f8b180184b");
        var entryResponses = new ConcurrentQueue<FinanceEntryInitializationResponse>(new[]
        {
            new FinanceEntryInitializationResponse
            {
                CompanyId = companyId,
                InitializationStatus = FinanceEntryInitializationContractValues.Ready,
                ProgressState = FinanceEntryProgressStateContractValues.Seeded,
                SeedingState = FinanceSeedingStateContractValues.FullySeeded,
                Message = "Finance data is ready."
            }
        });

        using var harness = CreateHarness(
            companyId,
            CreateCurrentUser(companyId, "owner"),
            entryResponses,
            requestResponse: null,
            retryResponse: null,
            manualSeedResponse: null,
            simulationStateResponse: new FinanceCompanySimulationStateResponse
            {
                CompanyId = companyId,
                Status = FinanceCompanySimulationStatusValues.Stopped,
                UiVisible = true,
                BackendExecutionEnabled = true,
                BackgroundJobsEnabled = true,
                GenerationEnabled = true,
                RecentHistory =
                [
                    new FinanceCompanySimulationRunHistoryResponse
                    {
                        SessionId = Guid.Parse("8d4efba1-d099-4b02-8916-f51b21965d5b"),
                        Status = FinanceCompanySimulationStatusValues.Stopped,
                        StartedUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        CompletedUtc = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedUtc = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                        Seed = 42,
                        GenerationEnabled = true,
                        StartSimulatedDateTime = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        CurrentSimulatedDateTime = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                        InjectedAnomalies = ["duplicate_vendor_charge"],
                        Warnings = ["Manual review recommended."],
                        Errors = ["Safe generation failure summary."],
                        DayLogs =
                        [
                            new FinanceCompanySimulationDayLogResponse
                            {
                                SimulatedDateUtc = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                                TransactionsGenerated = 2,
                                InvoicesGenerated = 1,
                                BillsGenerated = 1,
                                RecurringExpenseInstancesGenerated = 1,
                                InjectedAnomalies = ["duplicate_vendor_charge"],
                                Warnings = ["Manual review recommended."],
                                Errors = ["Safe generation failure summary."]
                            }
                        ]
                    }
                ]
            });
        harness.Navigation.NavigateTo($"http://localhost/finance?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<FinancePage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Recent simulation history", cut.Markup);
            Assert.Contains("duplicate_vendor_charge", cut.Markup);
            Assert.Contains("Manual review recommended.", cut.Markup);
            Assert.Contains("2026-04-02", cut.Markup);
            Assert.Contains(">5<", cut.Markup);
        });
    }

    private static FinancePageHarness CreateHarness(
        Guid companyId,
        CurrentUserContextViewModel currentUser,
        ConcurrentQueue<FinanceEntryInitializationResponse> entryResponses,
        FinanceEntryInitializationResponse? requestResponse,
        FinanceEntryInitializationResponse? retryResponse,
        FinanceEntryInitializationResponse? manualSeedResponse,
        FinanceCompanySimulationStateResponse? simulationStateResponse = null,
        ConcurrentQueue<FinanceCompanySimulationStateResponse>? simulationStateResponses = null,
        List<string>? requestedPaths = null,
        Action<FinanceManualSeedRequest>? onManualSeedRequest = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage?>>? financeOverrideHandler = null)
    {
        var context = new TestContext();
        context.Services.AddOptions();
        context.Services.AddSingleton(new FinanceAccessResolver());
        context.Services.Configure<FinanceSimulationControlPanelOptions>(options => options.Enabled = true);
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>

        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(currentUser),
                _ => CreateNotFoundResponse()
            });
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            requestedPaths?.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            if (financeOverrideHandler is not null)
            {
                var overridden = await financeOverrideHandler(request, _);
                if (overridden is not null)
                {
                    return overridden;
                }
            }
            if (request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/finance/entry-state" &&
                request.Method == HttpMethod.Get)
            {
                if (!entryResponses.TryDequeue(out var nextResponse))
                {
                    nextResponse = retryResponse ?? new FinanceEntryInitializationResponse
                    {
                        CompanyId = companyId,
                        InitializationStatus = FinanceEntryInitializationContractValues.Ready,
                        SeedingState = FinanceSeedingStateContractValues.FullySeeded,
                        Message = "Finance data is ready."
                    };
                }

                return Task.FromResult(CreateJsonResponse(nextResponse));
            }

            if (request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/finance/entry-state/request" &&
                request.Method == HttpMethod.Post)
            {
                return Task.FromResult(CreateJsonResponse(requestResponse!));
            }

            if (request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/finance/entry-state/retry" &&
                request.Method == HttpMethod.Post)
            {
                return Task.FromResult(CreateJsonResponse(requestResponse ?? retryResponse!));
            }

            if (request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/finance/manual-seed" &&
                request.Method == HttpMethod.Post)
            {
                var payload = await request.Content!.ReadFromJsonAsync<FinanceManualSeedRequest>(cancellationToken: _);
                onManualSeedRequest?.Invoke(payload!);
                return Task.FromResult(CreateJsonResponse(manualSeedResponse!));
            }

            if (request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/simulation" &&
                request.Method == HttpMethod.Get)
            {
                FinanceCompanySimulationStateResponse response;
                if (simulationStateResponses is not null && simulationStateResponses.TryDequeue(out var queuedResponse))
                {
                    response = queuedResponse;
                }
                else
                {
                    response = simulationStateResponse ?? FinanceCompanySimulationStateResponse.NotStarted(companyId);
                }

                return Task.FromResult(CreateJsonResponse(response));
            }

            if (request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/finance/entry-state" &&
                request.Method == HttpMethod.Get &&
                retryResponse is not null)
            {
                return Task.FromResult(CreateJsonResponse(retryResponse));
            }

            return Task.FromResult(CreateNotFoundResponse());
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        return new FinancePageHarness(context, context.Services.GetRequiredService<FakeNavigationManager>());
    }

    private static CurrentUserContextViewModel CreateCurrentUser(Guid companyId, string membershipRole) =>
        new()
        {
            Memberships =
            [
                new CompanyMembershipViewModel
                {
                    MembershipId = Guid.NewGuid(),
                    CompanyId = companyId,
                    CompanyName = "Contoso Finance",
                    MembershipRole = membershipRole,
                    Status = "active"
                }
            ],
            ActiveCompany = new ResolvedCompanyContextViewModel
            {
                MembershipId = Guid.NewGuid(),
                CompanyId = companyId,
                CompanyName = "Contoso Finance",
                MembershipRole = membershipRole,
                Status = "active"
            }
        };

    private static HttpResponseMessage CreateJsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private static HttpResponseMessage CreateNotFoundResponse() =>
        new(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { title = "Not found", detail = "Not found." })
        };

    private static HttpResponseMessage CreateProblemResponse(HttpStatusCode statusCode, string title, string detail) =>
        new(statusCode)
        {
            Content = JsonContent.Create(new { title, detail })
        };

    private sealed record FinancePageHarness(TestContext Context, FakeNavigationManager Navigation) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }

    private sealed class AsyncStubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public AsyncStubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}