using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSandboxAdminPageTests
{
    [Fact]
    public void Sandbox_admin_page_denies_non_admin_finance_users_without_loading_admin_sections()
    {
        var companyId = Guid.Parse("db404fe8-df37-4698-b36c-57db6ecb217a");
        var sandboxService = new StubFinanceSandboxAdminService();

        using var harness = CreateHarness(companyId, "manager", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Simulation Lab access required", cut.Markup);
            Assert.Contains("limited to company admins and testers", cut.Markup);
        });

        Assert.Equal(0, sandboxService.TotalCalls);
    }

    [Fact]
    public void Sandbox_admin_page_allows_tester_memberships_to_load_admin_sections()
    {
        var companyId = Guid.Parse("6dbd9307-4ab8-43d6-9458-6db64f927f68");
        var sandboxService = new StubFinanceSandboxAdminService();

        using var harness = CreateHarness(companyId, "tester", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Simulation Lab", cut.Markup);
            Assert.Contains("Dataset generation is not configured", cut.Markup);
        });

        Assert.Equal(4, sandboxService.TotalCalls);
    }

    [Fact]
    public void Sandbox_admin_page_exposes_tenant_aware_company_selection_for_seed_generation()
    {
        var primaryCompanyId = Guid.Parse("1b40e8b2-465a-4c35-bb52-eb41523c5df6");
        var secondaryCompanyId = Guid.Parse("5e73f3c4-f39c-4254-861a-8065ea57f031");
        var sandboxService = new StubFinanceSandboxAdminService();
        var currentUser = CreateCurrentUserContext(
            primaryCompanyId,
            "admin",
            [
                new CompanyMembershipViewModel
                {
                    MembershipId = Guid.NewGuid(),
                    CompanyId = primaryCompanyId,
                    CompanyName = "Contoso Finance",
                    MembershipRole = "admin",
                    Status = "active"
                },
                new CompanyMembershipViewModel
                {
                    MembershipId = Guid.NewGuid(),
                    CompanyId = secondaryCompanyId,
                    CompanyName = "Fabrikam Treasury",
                    MembershipRole = "tester",
                    Status = "active"
                }
            ]);

        using var harness = CreateHarness(currentUser, sandboxService);
        var cut = RenderSandboxAdmin(harness, primaryCompanyId);
        var companySelect = cut.Find("#seed-company");

        Assert.Contains("Contoso Finance", companySelect.InnerHtml);
        Assert.Contains("Fabrikam Treasury", companySelect.InnerHtml);

        companySelect.Change(secondaryCompanyId.ToString());

        Assert.Contains($"companyId={secondaryCompanyId:D}", harness.Navigation.Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sandbox_admin_page_sections_load_independently_and_support_loading_empty_error_and_content_states()
    {
        var companyId = Guid.Parse("711eefc3-ac7f-4a43-a713-36f7710d53b3");
        var datasetSource = new TaskCompletionSource<FinanceSandboxDatasetGenerationViewModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var anomalySource = new TaskCompletionSource<FinanceSandboxAnomalyInjectionViewModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var simulationSource = new TaskCompletionSource<FinanceSandboxSimulationControlsViewModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manifestSource = new TaskCompletionSource<FinanceTransparencyToolManifestListViewModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionSource = new TaskCompletionSource<FinanceTransparencyExecutionHistoryViewModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventsSource = new TaskCompletionSource<FinanceTransparencyEventStreamViewModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGetDatasetGenerationAsync = (_, _) => datasetSource.Task,
            OnGetAnomalyInjectionAsync = (_, _) => anomalySource.Task,
            OnGetSimulationControlsAsync = (_, _) => simulationSource.Task,
            OnGetSimulationDiagnosticsAsync = (_, _) => Task.FromResult<FinanceSandboxSimulationDiagnosticsViewModel?>(new FinanceSandboxSimulationDiagnosticsViewModel()),
            OnGetTransparencyToolManifestsAsync = (_, _) => manifestSource.Task,
            OnGetTransparencyToolExecutionsAsync = (_, _) => executionSource.Task,
            OnGetTransparencyEventsAsync = (_, _) => eventsSource.Task
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Loading dataset generation", cut.Markup);
            Assert.Contains("Loading anomaly scenarios", cut.Markup);
            Assert.Contains("Loading progression controls", cut.Markup);
            Assert.Contains("Loading simulation status", cut.Markup);
        });

        datasetSource.SetResult(new FinanceSandboxDatasetGenerationViewModel
        {
            ProfileName = "Seed dataset refresh",
            LastGeneratedUtc = new DateTime(2026, 4, 16, 10, 30, 0, DateTimeKind.Utc),
            CoverageSummary = "90-day replay coverage",
            AvailableProfiles = ["Baseline 90-day ledger"]
        });
        anomalySource.SetResult(null);
        manifestSource.SetResult(null);
        executionSource.SetException(new FinanceApiException("Finance execution history is unavailable."));
        eventsSource.SetResult(null);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Seed dataset refresh", cut.Markup);
            Assert.Contains("No anomaly scenarios are configured.", cut.Markup);
            Assert.Contains("Loading progression controls", cut.Markup);
        });

        Assert.Equal(1, sandboxService.DatasetGenerationCalls);
        Assert.Equal(1, sandboxService.AnomalyInjectionCalls);
        Assert.Equal(1, sandboxService.SimulationControlsCalls);
        Assert.Equal(1, sandboxService.SimulationDiagnosticsCalls);
        Assert.Equal(0, sandboxService.TransparencyToolManifestCalls);
        Assert.Equal(0, sandboxService.TransparencyToolExecutionCalls);
        Assert.Equal(0, sandboxService.TransparencyEventCalls);
    }

    [Fact]
    public void Sandbox_admin_page_renders_simulation_diagnostics_with_error_state_and_day_logs()
    {
        var companyId = Guid.Parse("f344eddd-a997-4620-b80f-7a75d9bba132");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGetSimulationDiagnosticsAsync = (_, _) => Task.FromResult<FinanceSandboxSimulationDiagnosticsViewModel?>(new FinanceSandboxSimulationDiagnosticsViewModel
            {
                CompanyId = companyId,
                Status = FinanceCompanySimulationStatusValues.Stopped,
                UiVisible = true,
                BackendExecutionEnabled = false,
                BackgroundJobsEnabled = false,
                DisabledReason = "Simulation backend execution is disabled for the sandbox.",
                RecentHistory =
                [
                    new FinanceSandboxSimulationRunHistoryViewModel
                    {
                        SessionId = Guid.Parse("7b7cf4be-2f66-4923-84d8-ed71908d0b98"),
                        Status = FinanceCompanySimulationStatusValues.Stopped,
                        StartedUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        CompletedUtc = new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedUtc = new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
                        GenerationEnabled = true,
                        Seed = 42,
                        StartSimulatedDateTime = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        CurrentSimulatedDateTime = new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
                        InjectedAnomalies = ["duplicate_vendor_charge"],
                        Warnings = ["Manual review recommended."],
                        Errors = ["Safe generation failure summary."],
                        StatusTransitions =
                        [
                            new FinanceSandboxSimulationStatusTransitionViewModel
                            {
                                Status = FinanceCompanySimulationStatusValues.Running,
                                TransitionedUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                                Message = "Simulation started."
                            },
                            new FinanceSandboxSimulationStatusTransitionViewModel
                            {
                                Status = FinanceCompanySimulationStatusValues.Stopped,
                                TransitionedUtc = new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
                                Message = "Simulation stopped."
                            }
                        ],
                        DayLogs =
                        [
                            new FinanceSandboxSimulationDayLogViewModel
                            {
                                SimulatedDateUtc = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                                TransactionsGenerated = 2,
                                InvoicesGenerated = 1,
                                BillsGenerated = 1,
                                RecurringExpenseInstancesGenerated = 1,
                                AlertsGenerated = 1,
                                InjectedAnomalies = ["duplicate_vendor_charge"],
                                Warnings = ["Manual review recommended."],
                                Errors = ["Safe generation failure summary."]
                            }
                        ]
                    }
                ]
            })
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        var cut = RenderSandboxAdmin(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Simulation status and history", cut.Markup);
            Assert.Contains("Current or last issue state", cut.Markup);
            Assert.Contains("Simulation backend execution is disabled for the sandbox.", cut.Markup);
            Assert.Contains("Duplicate vendor charge", cut.Markup);
            Assert.Contains("Manual review recommended.", cut.Markup);
            Assert.Contains("Safe generation failure summary.", cut.Markup);
            Assert.Contains("Simulation started.", cut.Markup);
            Assert.Contains(">5<", cut.Markup);
        });
    }

    [Fact]
    public void Sandbox_admin_page_hides_simulation_sections_when_ui_visibility_is_disabled()
    {
        var companyId = Guid.Parse("2de08c4c-3c0d-4ac9-b7b6-bc3db5f6a4df");
        var sandboxService = new StubFinanceSandboxAdminService();

        using var harness = CreateHarness(companyId, "admin", sandboxService, simulationUiVisible: false);
        var cut = RenderSandboxAdmin(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Simulation status and history", cut.Markup);
            Assert.DoesNotContain("Progression controls", cut.Markup);
            Assert.DoesNotContain("Advance simulation time", cut.Markup);
        });

        Assert.Equal(0, sandboxService.SimulationDiagnosticsCalls);
        Assert.Equal(0, sandboxService.SimulationControlsCalls);
    }

    [Fact]
    public void Sandbox_admin_page_blocks_seed_generation_submit_when_form_is_invalid()
    {
        var companyId = Guid.Parse("4bfb1eb0-cbe8-4ee8-a56b-32a56326707a");
        var sandboxService = new StubFinanceSandboxAdminService();

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.Find("#seed-value").Change("0");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Enter a positive reproducibility value.", cut.Markup);
            Assert.DoesNotContain("Regenerating data...", cut.Markup);
        });

        Assert.Equal(0, sandboxService.GenerateSeedDatasetCalls);
    }

    [Fact]
    public void Sandbox_admin_page_submits_seed_generation_once_and_disables_duplicate_submissions_while_pending()
    {
        var companyId = Guid.Parse("2eecf389-2c65-4976-a636-4259fa8fd22b");
        var generationSource = new TaskCompletionSource<FinanceSandboxSeedGenerationViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        FinanceSandboxSeedGenerationCommand? capturedCommand = null;
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGenerateSeedDatasetAsync = (command, _) =>
            {
                capturedCommand = command;
                return generationSource.Task;
            }
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.Find("#seed-value").Change("913");
        cut.Find("#anchor-date").Change("2026-04-15");
        cut.Find("#generation-mode").Change(FinanceSandboxSeedGenerationModes.RefreshWithAnomalies);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("#seed-generate-submit").HasAttribute("disabled"));
            Assert.Contains("Regenerating data...", cut.Markup);
        });

        cut.Find("form").Submit();

        Assert.Equal(1, sandboxService.GenerateSeedDatasetCalls);
        Assert.NotNull(capturedCommand);
        Assert.Equal(companyId, capturedCommand!.CompanyId);
        Assert.Equal(913, capturedCommand.SeedValue);
        Assert.Equal(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), capturedCommand.AnchorDateUtc);
        Assert.Equal(FinanceSandboxSeedGenerationModes.RefreshWithAnomalies, capturedCommand.GenerationMode);
    }

    [Fact]
    public void Sandbox_admin_page_renders_seed_generation_summary_after_successful_submission()
    {
        var companyId = Guid.Parse("22d5a332-f4dc-4f56-bf95-0019901e1621");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGenerateSeedDatasetAsync = (_, _) => Task.FromResult(new FinanceSandboxSeedGenerationViewModel
            {
                Succeeded = true,
                CreatedCount = 87,
                UpdatedCount = 0,
                Message = "Seed dataset generated successfully.",
                Errors = [],
                Warnings = [],
                ReferentialIntegrityErrors = []
            })
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Seed dataset generated successfully.", cut.Markup);
            Assert.Contains("Generation summary", cut.Markup);
            Assert.Contains("Validation results", cut.Markup);
            Assert.Contains(">302<", cut.Markup);
            Assert.Contains("Replace existing dataset", cut.Markup);
            Assert.Contains(">87<", cut.Markup);
            Assert.Contains(">0<", cut.Markup);
            Assert.Contains("Validation completed without errors or warnings.", cut.Markup);
        });
    }

    [Fact]
    public void Sandbox_admin_page_renders_validation_results_inline_when_generation_returns_issues()
    {
        var companyId = Guid.Parse("f93040a4-8457-4dfe-a792-0f71d4524c6f");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGenerateSeedDatasetAsync = (_, _) => Task.FromResult(new FinanceSandboxSeedGenerationViewModel
            {
                Succeeded = false,
                CreatedCount = 0,
                UpdatedCount = 0,
                Message = "Seed dataset generation returned validation issues. Resolve the reported problems and retry the request.",
                Errors = [new FinanceSandboxSeedGenerationIssueViewModel { Code = "transactions.window", Message = "Transactions must be inside the seeded history window." }],
                Warnings = [new FinanceSandboxSeedGenerationIssueViewModel { Code = "anomaly.duplicate_vendor_charge", Message = "Injected validation scenario 'duplicate vendor charge' affecting 2 record(s)." }],
                ReferentialIntegrityErrors = [new FinanceSandboxSeedGenerationIssueViewModel { Code = "transactions.account", Message = "Transactions must reference valid accounts." }]
            })
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Referential integrity errors", cut.Markup);
            Assert.Contains("Validation results", cut.Markup);
            Assert.Contains("Transactions must reference valid accounts.", cut.Markup);
            Assert.Contains("Validation errors", cut.Markup);
            Assert.Contains("Transactions must be inside the seeded history window.", cut.Markup);
            Assert.Contains("Warnings", cut.Markup);
            Assert.Contains("Injected validation scenario 'duplicate vendor charge' affecting 2 record(s).", cut.Markup);
        });
    }

    [Fact]
    public void Sandbox_admin_page_shows_backend_validation_errors_inline_when_seed_generation_api_rejects_request()
    {
        var companyId = Guid.Parse("8d7169ca-2b36-48cf-8aa2-d30530d2ce47");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGenerateSeedDatasetAsync = (_, _) => throw new FinanceApiValidationException(
                "Update the highlighted fields and retry the request.",
                new Dictionary<string, string[]>
                {
                    [nameof(FinanceSandboxSeedGenerationRequest.SeedValue)] = ["The backend requires a seed value greater than zero."],
                    [nameof(FinanceSandboxSeedGenerationRequest.GenerationMode)] = ["Select a supported generation mode."]
                })
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Update the highlighted fields and retry the request.", cut.Markup);
            Assert.Contains("The backend requires a seed value greater than zero.", cut.Markup);
            Assert.Contains("Select a supported generation mode.", cut.Markup);
            Assert.False(cut.Find("#seed-generate-submit").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Sandbox_admin_page_shows_inline_error_when_seed_generation_api_fails()
    {
        var companyId = Guid.Parse("2f212236-4943-4f9d-a323-dbb9f697020a");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGenerateSeedDatasetAsync = (_, _) => throw new FinanceApiException("The web app could not reach the finance backend. Start the API project and retry the request.")
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("The web app could not reach the finance backend. Start the API project and retry the request.", cut.Markup);
            Assert.False(cut.Find("#seed-generate-submit").HasAttribute("disabled"));
            Assert.DoesNotContain("Regenerating data...", cut.Markup);
        });
    }

    [Fact]
    public void Sandbox_admin_page_requires_a_scenario_profile_before_injecting_an_anomaly()
    {
        var companyId = Guid.Parse("d14589ee-0a75-4f2b-a4bc-4d6dd3661f98");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGetAnomalyInjectionAsync = (_, _) => Task.FromResult<FinanceSandboxAnomalyInjectionViewModel?>(new FinanceSandboxAnomalyInjectionViewModel
            {
                Mode = "Seed anomaly scenarios",
                LastInjectedUtc = new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc),
                Observation = "Ready",
                AvailableScenarioProfiles =
                [
                    new FinanceSandboxAnomalyScenarioProfileViewModel
                    {
                        Code = "duplicate_vendor_charge",
                        Name = "Duplicate vendor charge",
                        Description = "Inject a duplicate vendor charge scenario."
                    }
                ],
                RegistryEntries = []
            })
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() => Assert.Contains("Scenario", cut.Markup));
        cut.Find("#anomaly-injection-form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Select a scenario profile.", cut.Markup);
            Assert.Equal(0, sandboxService.InjectAnomalyCalls);
        });
    }

    [Fact]
    public void Sandbox_admin_page_renders_progression_output_counts_and_backend_messages()
    {
        var companyId = Guid.Parse("909f4e3d-71d4-4fd0-a1f7-d7e6a0eb03d3");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGetSimulationControlsAsync = (_, _) => Task.FromResult<FinanceSandboxSimulationControlsViewModel?>(new FinanceSandboxSimulationControlsViewModel
            {
                ClockMode = "Simulated clock enabled",
                ReferenceUtc = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
                CheckpointLabel = "Ready",
                Observation = "Awaiting run"
            }),
            OnStartProgressionRunAsync = (_, _) => Task.FromResult(new FinanceSandboxProgressionRunViewModel
            {
                RunType = "progression_run",
                Status = "completed",
                StartedUtc = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
                CompletedUtc = new DateTime(2026, 4, 16, 11, 0, 0, DateTimeKind.Utc),
                AdvancedHours = 24,
                ExecutionStepHours = 24,
                TransactionsGenerated = 4,
                InvoicesGenerated = 2,
                BillsGenerated = 1,
                RecurringExpenseInstancesGenerated = 1,
                EventsEmitted = 1,
                Messages = [new FinanceSandboxBackendMessageViewModel { Severity = "warning", Code = "sandbox.progression.completed", Message = "The progression run completed and generated 8 finance record(s)." }],
                Steps = [new FinanceSandboxProgressionRunStepViewModel { WindowStartUtc = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc), WindowEndUtc = new DateTime(2026, 4, 16, 11, 0, 0, DateTimeKind.Utc), TransactionsGenerated = 4, InvoicesGenerated = 2, BillsGenerated = 1, RecurringExpenseInstancesGenerated = 1, EventsEmitted = 1 }]
            })
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        var cut = RenderSandboxAdmin(harness, companyId);
        cut.WaitForAssertion(() => Assert.Contains("Start progression run", cut.Markup));
        cut.Find("#progression-run-submit").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("generated 8 finance record(s)", cut.Markup);
            Assert.Contains(">8<", cut.Markup);
            Assert.Contains(">1<", cut.Markup);
        });
    }

    [Fact]
    public void Sandbox_admin_page_polls_simulation_controls_until_progression_run_reaches_a_terminal_state()
    {
        var companyId = Guid.Parse("d728d91e-c53a-46c3-a198-542a5e7db59d");
        var runningRun = new FinanceSandboxProgressionRunViewModel
        {
            RunType = "progression_run",
            Status = "running",
            StartedUtc = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
            AdvancedHours = 24,
            ExecutionStepHours = 24,
            Messages = [new FinanceSandboxBackendMessageViewModel { Severity = "warning", Code = "sandbox.progression.running", Message = "The progression run is still processing backend steps." }]
        };
        var completedRun = new FinanceSandboxProgressionRunViewModel
        {
            RunType = "progression_run",
            Status = "completed",
            StartedUtc = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
            CompletedUtc = new DateTime(2026, 4, 16, 11, 0, 0, DateTimeKind.Utc),
            AdvancedHours = 24,
            ExecutionStepHours = 24,
            TransactionsGenerated = 6,
            InvoicesGenerated = 3,
            BillsGenerated = 2,
            RecurringExpenseInstancesGenerated = 1,
            EventsEmitted = 2,
            Messages = [new FinanceSandboxBackendMessageViewModel { Severity = "warning", Code = "sandbox.progression.completed", Message = "The progression run completed and generated 12 finance record(s)." }],
            Steps = [new FinanceSandboxProgressionRunStepViewModel { WindowStartUtc = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc), WindowEndUtc = new DateTime(2026, 4, 16, 11, 0, 0, DateTimeKind.Utc), TransactionsGenerated = 6, InvoicesGenerated = 3, BillsGenerated = 2, RecurringExpenseInstancesGenerated = 1, EventsEmitted = 2 }]
        };
        var simulationResponseIndex = 0;
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGetSimulationControlsAsync = (_, _) =>
            {
                simulationResponseIndex++;

                return Task.FromResult<FinanceSandboxSimulationControlsViewModel?>(simulationResponseIndex switch
                {
                    1 => new FinanceSandboxSimulationControlsViewModel
                    {
                        ClockMode = "Simulated clock enabled",
                        ReferenceUtc = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
                        CheckpointLabel = "Ready",
                        Observation = "Awaiting run"
                    },
                    2 => new FinanceSandboxSimulationControlsViewModel
                    {
                        ClockMode = "Simulated clock enabled",
                        ReferenceUtc = new DateTime(2026, 4, 16, 10, 30, 0, DateTimeKind.Utc),
                        CheckpointLabel = "Progression run is in progress.",
                        Observation = "Backend status is still refreshing.",
                        CurrentRun = runningRun,
                        RunHistory = []
                    },
                    _ => new FinanceSandboxSimulationControlsViewModel
                    {
                        ClockMode = "Simulated clock enabled",
                        ReferenceUtc = new DateTime(2026, 4, 16, 11, 0, 0, DateTimeKind.Utc),
                        CheckpointLabel = "Latest run completed 24h with 1 step.",
                        Observation = "The progression run completed and generated 12 finance record(s).",
                        CurrentRun = completedRun,
                        RunHistory = [completedRun]
                    }
                });
            },
            OnStartProgressionRunAsync = (_, _) => Task.FromResult(runningRun)
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.SimulationPollingIntervalMilliseconds, 50));

        cut.WaitForAssertion(() => Assert.Contains("Start progression run", cut.Markup));
        cut.Find("#progression-run-submit").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Progression run started for 24 hour(s). Status updates will refresh automatically.", cut.Markup);
            Assert.Contains("Status refresh is active while the latest progression run is still in progress.", cut.Markup);
            Assert.Contains("Sandbox progression running", cut.Markup);
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Sandbox progression completed", cut.Markup);
            Assert.Contains("generated 12 finance record(s)", cut.Markup);
            Assert.Contains(">12<", cut.Markup);
            Assert.DoesNotContain("Status refresh is active while the latest progression run is still in progress.", cut.Markup);
        });

        Assert.Equal(1, sandboxService.StartProgressionRunCalls);
        Assert.True(sandboxService.SimulationControlsCalls >= 3);
    }

    [Fact]
    public void Sandbox_admin_page_renders_backend_run_history_from_simulation_controls()
    {
        var companyId = Guid.Parse("483f9850-f887-4b31-a448-d1c4f94832ef");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGetSimulationControlsAsync = (_, _) => Task.FromResult<FinanceSandboxSimulationControlsViewModel?>(new FinanceSandboxSimulationControlsViewModel
            {
                ClockMode = "Simulated clock enabled",
                ReferenceUtc = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
                CheckpointLabel = "Latest run completed 24h with 1 step.",
                Observation = "The simulation run completed and generated 8 finance record(s).",
                CurrentRun = new FinanceSandboxProgressionRunViewModel
                {
                    RunType = "progression_run",
                    Status = "completed",
                    CompletedUtc = new DateTime(2026, 4, 16, 11, 0, 0, DateTimeKind.Utc),
                    TransactionsGenerated = 4,
                    InvoicesGenerated = 2,
                    BillsGenerated = 1,
                    RecurringExpenseInstancesGenerated = 1
                },
                RunHistory = [new FinanceSandboxProgressionRunViewModel { RunType = "progression_run", Status = "completed", CompletedUtc = new DateTime(2026, 4, 16, 11, 0, 0, DateTimeKind.Utc), TransactionsGenerated = 4, InvoicesGenerated = 2, BillsGenerated = 1, RecurringExpenseInstancesGenerated = 1 }]
            })
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        var cut = RenderSandboxAdmin(harness, companyId);
        cut.WaitForAssertion(() => Assert.Contains("Run history", cut.Markup));
        Assert.Contains("generated 8 finance record(s)", cut.Markup);
    }

    [Fact]
    public void Transparency_events_page_renders_event_detail_and_empty_trace_state()
    {
        var companyId = Guid.Parse("3be0dc16-7e7a-4f59-bd5e-bf1a55a0f120");
        var eventId = Guid.Parse("71caeea7-c57c-4822-bec1-0b3636af93ff");
        var invoiceId = Guid.Parse("b611895a-a7e2-47a8-8cd4-4f0f533f46b2");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGetTransparencyEventsAsync = (_, _) => Task.FromResult<FinanceTransparencyEventStreamViewModel?>(new FinanceTransparencyEventStreamViewModel
            {
                Summary = "Recent finance events.",
                Items =
                [
                    new FinanceTransparencyEventListItemViewModel
                    {
                        Id = eventId,
                        EventType = "finance.invoice.approval.requested",
                        OccurredAtUtc = new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc),
                        CorrelationId = "corr-events-1",
                        AffectedEntityType = "finance_invoice",
                        AffectedEntityId = invoiceId.ToString("D"),
                        EntityReference = "Finance invoice INV-1001",
                        PayloadSummary = "Invoice approval requested.",
                        HasTriggerTrace = false
                    }
                ]
            }),
            OnGetTransparencyEventDetailAsync = (_, _, _) => Task.FromResult<FinanceTransparencyEventDetailViewModel?>(new FinanceTransparencyEventDetailViewModel
            {
                Id = eventId,
                EventType = "finance.invoice.approval.requested",
                OccurredAtUtc = new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc),
                CorrelationId = "corr-events-1",
                EntityType = "finance_invoice",
                EntityId = invoiceId.ToString("D"),
                EntityReference = "Finance invoice INV-1001",
                PayloadSummary = "Invoice approval requested.",
                TriggerConsumptionTrace = []
            })
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/system/admin/transparency-events/{eventId:D}?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<TransparencyEventsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.EventId, eventId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Finance domain event stream", cut.Markup);
            Assert.Contains("Invoice approval requested.", cut.Markup);
            Assert.Contains("No trigger trace available for this event.", cut.Markup);
            Assert.Contains("Open affected finance record", cut.Markup);
        });
    }

    [Fact]
    public void Transparency_tool_registry_page_renders_schema_and_provider_metadata()
    {
        var companyId = Guid.Parse("8a0e4f28-19e7-49c9-97e2-2693d4e659f4");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGetTransparencyToolManifestsAsync = (_, _) => Task.FromResult<FinanceTransparencyToolManifestListViewModel?>(new FinanceTransparencyToolManifestListViewModel
            {
                Summary = "Runtime registry finance manifests.",
                Items =
                [
                    new FinanceTransparencyToolManifestItemViewModel
                    {
                        VersionMetadata = "Manifest version 1.0.0 from the active runtime registry.",
                        ToolName = "approve_invoice",
                        Version = "1.0.0",
                        ContractSummary = "Input requires invoiceId; output declares approval status.",
                        SchemaSummary = "Input requires invoiceId; output declares approval status.",
                        ManifestSource = "runtime_registry",
                        ProviderAdapterId = "provider:finance.internal",
                        ProviderAdapterName = "Internal finance provider",
                        ProviderAdapterIdentity = "InternalFinanceToolProvider"
                    }
                ]
            })
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/system/admin/tool-registry?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<TransparencyToolRegistryPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Finance tool manifest registry", cut.Markup);
            Assert.Contains("runtime registry", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Internal finance provider", cut.Markup);
            Assert.Contains("provider:finance.internal", cut.Markup);
        });
    }

    [Fact]
    public void Transparency_tool_executions_page_renders_related_approval_and_origin_links()
    {
        var companyId = Guid.Parse("7aa95707-e513-4916-a4eb-cd1634b3d483");
        var executionId = Guid.Parse("f67cc8fd-b8dc-4c4f-8521-664db3e1ff0d");
        var sandboxService = new StubFinanceSandboxAdminService
        {
            OnGetTransparencyToolExecutionsAsync = (_, _) => Task.FromResult<FinanceTransparencyExecutionHistoryViewModel?>(new FinanceTransparencyExecutionHistoryViewModel
            {
                Summary = "Recent executions.",
                Items = [new FinanceTransparencyToolExecutionListItemViewModel { ExecutionId = executionId, ToolName = "approve_invoice", ToolVersion = "1.0.0", LifecycleState = "awaiting_approval", RequestSummary = "invoiceId=1001", ResponseSummary = "Awaiting approval.", ExecutionTimestampUtc = new DateTime(2026, 4, 16, 11, 0, 0, DateTimeKind.Utc), CorrelationId = "corr-exec-1" }]
            }),
            OnGetTransparencyToolExecutionDetailAsync = (_, _, _) => Task.FromResult<FinanceTransparencyToolExecutionDetailViewModel?>(new FinanceTransparencyToolExecutionDetailViewModel
            {
                ExecutionId = executionId,
                ToolName = "approve_invoice",
                ToolVersion = "1.0.0",
                LifecycleState = "awaiting_approval",
                RequestSummary = "invoiceId=1001",
                ResponseSummary = "Awaiting approval.",
                ExecutionTimestampUtc = new DateTime(2026, 4, 16, 11, 0, 0, DateTimeKind.Utc),
                CorrelationId = "corr-exec-1",
                ApprovalRequestDisplay = "Approval request 0a3150a7-cbc1-44ba-90bd-ae98202d1f12",
                ApprovalRequestId = Guid.Parse("0a3150a7-cbc1-44ba-90bd-ae98202d1f12"),
                OriginatingEntityType = "finance_invoice",
                OriginatingFinanceActionDisplay = "Finance invoice INV-1001",
                OriginatingEntityId = Guid.Parse("6c436d88-e7b0-4147-91d0-d5871129c84a"),
                OriginatingEntityReference = "Finance invoice INV-1001",
                TaskId = Guid.Parse("b56fdd08-f82d-4bca-bca7-b847f6285e5e"),
                WorkflowInstanceId = Guid.Parse("6e167bcf-2d3a-4879-b8e6-d6928d4bded6")
            })
        };

        using var harness = CreateHarness(companyId, "admin", sandboxService);
        harness.Navigation.NavigateTo($"http://localhost/system/admin/tool-executions/{executionId:D}?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<TransparencyToolExecutionsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.ExecutionId, executionId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Open approval request", cut.Markup);
            Assert.Contains("Approval request 0a3150a7-cbc1-44ba-90bd-ae98202d1f12", cut.Markup);
            Assert.Contains("Finance invoice INV-1001", cut.Markup);
            Assert.Contains("Open originating finance action", cut.Markup);
            Assert.Contains("Open workflow instance", cut.Markup);
            Assert.Contains("Open related task", cut.Markup);
        });
    }

    private static IRenderedComponent<SandboxAdminPage> RenderSandboxAdmin(SandboxAdminHarness harness, Guid companyId)
    {
        harness.Navigation.NavigateTo($"http://localhost/simulation-lab?companyId={companyId:D}");

        return harness.Context.RenderComponent<SandboxAdminPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));
    }

    private static SandboxAdminHarness CreateHarness(
        CurrentUserContextViewModel currentUserContext,
        StubFinanceSandboxAdminService sandboxService,
        bool simulationUiVisible = true)
    {
        var context = new TestContext();

        context.Services.AddOptions();
        context.Services.Configure<FinanceSimulationControlPanelOptions>(options => options.UiVisible = simulationUiVisible);
        context.Services.AddSingleton(new FinanceAccessResolver());
        context.Services.AddSingleton<IFinanceSandboxAdminService>(sandboxService);
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(currentUserContext),
                _ => CreateNotFoundResponse()
            });
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        return new SandboxAdminHarness(context, context.Services.GetRequiredService<FakeNavigationManager>());
    }

    private static SandboxAdminHarness CreateHarness(Guid companyId, string membershipRole, StubFinanceSandboxAdminService sandboxService, bool simulationUiVisible = true) =>
        CreateHarness(CreateCurrentUserContext(companyId, membershipRole), sandboxService, simulationUiVisible);

    private static CurrentUserContextViewModel CreateCurrentUserContext(
        Guid companyId,
        string membershipRole,
        IReadOnlyList<CompanyMembershipViewModel>? memberships = null)
    {
        var resolvedMemberships = memberships?.ToList()
            ?? [
                new CompanyMembershipViewModel
                {
                    MembershipId = Guid.NewGuid(),
                    CompanyId = companyId,
                    CompanyName = "Contoso Finance",
                    MembershipRole = membershipRole,
                    Status = "active"
                }
            ];
        var activeMembership = resolvedMemberships.First(membership => membership.CompanyId == companyId);

        return new CurrentUserContextViewModel
        {
            Memberships = resolvedMemberships,
            ActiveCompany = new ResolvedCompanyContextViewModel
            {
                MembershipId = activeMembership.MembershipId,
                CompanyId = activeMembership.CompanyId,
                CompanyName = activeMembership.CompanyName,
                MembershipRole = activeMembership.MembershipRole,
                Status = activeMembership.Status
            }
        };
    }

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

    private sealed class SandboxAdminHarness : IDisposable
    {
        public SandboxAdminHarness(TestContext context, FakeNavigationManager navigation)
        {
            Context = context;
            Navigation = navigation;
        }

        public TestContext Context { get; }
        public FakeNavigationManager Navigation { get; }

        public void Dispose() => Context.Dispose();
    }

    private sealed class StubFinanceSandboxAdminService : IFinanceSandboxAdminService
    {
        public Func<Guid, CancellationToken, Task<FinanceSandboxDatasetGenerationViewModel?>>? OnGetDatasetGenerationAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceSandboxAnomalyInjectionViewModel?>>? OnGetAnomalyInjectionAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceSandboxSimulationControlsViewModel?>>? OnGetSimulationControlsAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceSandboxSimulationDiagnosticsViewModel?>>? OnGetSimulationDiagnosticsAsync { get; init; }
        public Func<Guid, Guid, CancellationToken, Task<FinanceSandboxAnomalyDetailViewModel?>>? OnGetAnomalyDetailAsync { get; init; }
        public Func<FinanceSandboxAnomalyInjectionCommand, CancellationToken, Task<FinanceSandboxAnomalyDetailViewModel>>? OnInjectAnomalyAsync { get; init; }
        public Func<FinanceSandboxSeedGenerationCommand, CancellationToken, Task<FinanceSandboxSeedGenerationViewModel>>? OnGenerateSeedDatasetAsync { get; init; }
        public Func<FinanceSandboxSimulationAdvanceCommand, CancellationToken, Task<FinanceSandboxProgressionRunViewModel>>? OnAdvanceSimulationAsync { get; init; }
        public Func<FinanceSandboxSimulationAdvanceCommand, CancellationToken, Task<FinanceSandboxProgressionRunViewModel>>? OnStartProgressionRunAsync { get; init; }
        public Func<FinanceSandboxCompanySimulationStartCommand, CancellationToken, Task<FinanceSandboxSimulationDiagnosticsViewModel>>? OnStartCompanySimulationAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceSandboxSimulationDiagnosticsViewModel>>? OnPauseCompanySimulationAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceSandboxSimulationDiagnosticsViewModel>>? OnResumeCompanySimulationAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceSandboxSimulationDiagnosticsViewModel>>? OnStepForwardCompanySimulationAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceSandboxSimulationDiagnosticsViewModel>>? OnStopCompanySimulationAsync { get; init; }
        public Func<Guid, bool, CancellationToken, Task<FinanceSandboxSimulationDiagnosticsViewModel>>? OnUpdateCompanySimulationGenerationAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceTransparencyToolManifestListViewModel?>>? OnGetTransparencyToolManifestsAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceTransparencyExecutionHistoryViewModel?>>? OnGetTransparencyToolExecutionsAsync { get; init; }
        public Func<Guid, Guid, CancellationToken, Task<FinanceTransparencyToolExecutionDetailViewModel?>>? OnGetTransparencyToolExecutionDetailAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceTransparencyEventStreamViewModel?>>? OnGetTransparencyEventsAsync { get; init; }
        public Func<Guid, Guid, CancellationToken, Task<FinanceTransparencyEventDetailViewModel?>>? OnGetTransparencyEventDetailAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceSandboxToolExecutionVisibilityViewModel?>>? OnGetToolExecutionVisibilityAsync { get; init; }
        public Func<Guid, CancellationToken, Task<FinanceSandboxDomainEventsViewModel?>>? OnGetDomainEventsAsync { get; init; }

        public int DatasetGenerationCalls { get; private set; }
        public int AnomalyInjectionCalls { get; private set; }
        public int SimulationControlsCalls { get; private set; }
        public int SimulationDiagnosticsCalls { get; private set; }
        public int ToolExecutionVisibilityCalls { get; private set; }
        public int DomainEventsCalls { get; private set; }

        public int TransparencyToolManifestCalls { get; private set; }
        public int TransparencyToolExecutionCalls { get; private set; }
        public int TransparencyEventCalls { get; private set; }
        public int InjectAnomalyCalls { get; private set; }
        public int GenerateSeedDatasetCalls { get; private set; }
        public int StartProgressionRunCalls { get; private set; }
        public int AdvanceSimulationCalls { get; private set; }
        public int CompanySimulationMutationCalls { get; private set; }
        public int TotalCalls =>
            DatasetGenerationCalls +
            AnomalyInjectionCalls +
            SimulationDiagnosticsCalls +
            SimulationControlsCalls +
            TransparencyToolManifestCalls +
            TransparencyToolExecutionCalls +
            TransparencyEventCalls +
            ToolExecutionVisibilityCalls +
            DomainEventsCalls;

        public Task<FinanceSandboxDatasetGenerationViewModel?> GetDatasetGenerationAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            DatasetGenerationCalls++;
            return OnGetDatasetGenerationAsync is not null
                ? OnGetDatasetGenerationAsync(companyId, cancellationToken)
                : Task.FromResult<FinanceSandboxDatasetGenerationViewModel?>(null);
        }

        public Task<FinanceSandboxAnomalyInjectionViewModel?> GetAnomalyInjectionAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            AnomalyInjectionCalls++;
            return OnGetAnomalyInjectionAsync is not null
                ? OnGetAnomalyInjectionAsync(companyId, cancellationToken)
                : Task.FromResult<FinanceSandboxAnomalyInjectionViewModel?>(null);
        }

        public Task<FinanceSandboxSimulationDiagnosticsViewModel?> GetSimulationDiagnosticsAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            SimulationDiagnosticsCalls++;
            return OnGetSimulationDiagnosticsAsync is not null
                ? OnGetSimulationDiagnosticsAsync(companyId, cancellationToken)
                : Task.FromResult<FinanceSandboxSimulationDiagnosticsViewModel?>(null);
        }

        public Task<FinanceSandboxSimulationControlsViewModel?> GetSimulationControlsAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            SimulationControlsCalls++;
            return OnGetSimulationControlsAsync is not null
                ? OnGetSimulationControlsAsync(companyId, cancellationToken)
                : Task.FromResult<FinanceSandboxSimulationControlsViewModel?>(null);
        }

        public Task<FinanceSandboxAnomalyDetailViewModel?> GetAnomalyDetailAsync(Guid companyId, Guid anomalyId, CancellationToken cancellationToken = default) =>
            OnGetAnomalyDetailAsync is not null
                ? OnGetAnomalyDetailAsync(companyId, anomalyId, cancellationToken)
                : Task.FromResult<FinanceSandboxAnomalyDetailViewModel?>(null);

        public Task<FinanceSandboxAnomalyDetailViewModel> InjectAnomalyAsync(FinanceSandboxAnomalyInjectionCommand command, CancellationToken cancellationToken = default)
        {
            InjectAnomalyCalls++;
            return OnInjectAnomalyAsync is not null
                ? OnInjectAnomalyAsync(command, cancellationToken)
                : Task.FromResult(new FinanceSandboxAnomalyDetailViewModel());
        }

        public Task<FinanceSandboxProgressionRunViewModel> AdvanceSimulationAsync(FinanceSandboxSimulationAdvanceCommand command, CancellationToken cancellationToken = default)
        {
            AdvanceSimulationCalls++;
            return OnAdvanceSimulationAsync is not null
                ? OnAdvanceSimulationAsync(command, cancellationToken)
                : Task.FromResult(new FinanceSandboxProgressionRunViewModel());
        }

        public Task<FinanceSandboxProgressionRunViewModel> StartProgressionRunAsync(FinanceSandboxSimulationAdvanceCommand command, CancellationToken cancellationToken = default)
        {
            StartProgressionRunCalls++;
            return OnStartProgressionRunAsync is not null
                ? OnStartProgressionRunAsync(command, cancellationToken)
                : Task.FromResult(new FinanceSandboxProgressionRunViewModel());
        }

        public Task<FinanceSandboxSimulationDiagnosticsViewModel> StartCompanySimulationAsync(FinanceSandboxCompanySimulationStartCommand command, CancellationToken cancellationToken = default)
        {
            CompanySimulationMutationCalls++;
            return OnStartCompanySimulationAsync is not null
                ? OnStartCompanySimulationAsync(command, cancellationToken)
                : Task.FromResult(new FinanceSandboxSimulationDiagnosticsViewModel());
        }

        public Task<FinanceSandboxSimulationDiagnosticsViewModel> PauseCompanySimulationAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            CompanySimulationMutationCalls++;
            return OnPauseCompanySimulationAsync is not null
                ? OnPauseCompanySimulationAsync(companyId, cancellationToken)
                : Task.FromResult(new FinanceSandboxSimulationDiagnosticsViewModel());
        }

        public Task<FinanceSandboxSimulationDiagnosticsViewModel> ResumeCompanySimulationAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            CompanySimulationMutationCalls++;
            return OnResumeCompanySimulationAsync is not null
                ? OnResumeCompanySimulationAsync(companyId, cancellationToken)
                : Task.FromResult(new FinanceSandboxSimulationDiagnosticsViewModel());
        }

        public Task<FinanceSandboxSimulationDiagnosticsViewModel> StepForwardCompanySimulationAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            CompanySimulationMutationCalls++;
            return OnStepForwardCompanySimulationAsync is not null
                ? OnStepForwardCompanySimulationAsync(companyId, cancellationToken)
                : Task.FromResult(new FinanceSandboxSimulationDiagnosticsViewModel());
        }

        public Task<FinanceSandboxSimulationDiagnosticsViewModel> StopCompanySimulationAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            CompanySimulationMutationCalls++;
            return OnStopCompanySimulationAsync is not null
                ? OnStopCompanySimulationAsync(companyId, cancellationToken)
                : Task.FromResult(new FinanceSandboxSimulationDiagnosticsViewModel());
        }

        public Task<FinanceSandboxSimulationDiagnosticsViewModel> UpdateCompanySimulationGenerationAsync(Guid companyId, bool generationEnabled, CancellationToken cancellationToken = default)
        {
            CompanySimulationMutationCalls++;
            return OnUpdateCompanySimulationGenerationAsync is not null
                ? OnUpdateCompanySimulationGenerationAsync(companyId, generationEnabled, cancellationToken)
                : Task.FromResult(new FinanceSandboxSimulationDiagnosticsViewModel());
        }

        public Task<FinanceTransparencyToolManifestListViewModel?> GetTransparencyToolManifestsAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            TransparencyToolManifestCalls++;
            return OnGetTransparencyToolManifestsAsync is not null
                ? OnGetTransparencyToolManifestsAsync(companyId, cancellationToken)
                : Task.FromResult<FinanceTransparencyToolManifestListViewModel?>(null);
        }

        public Task<FinanceTransparencyExecutionHistoryViewModel?> GetTransparencyToolExecutionsAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            TransparencyToolExecutionCalls++;
            return OnGetTransparencyToolExecutionsAsync is not null
                ? OnGetTransparencyToolExecutionsAsync(companyId, cancellationToken)
                : Task.FromResult<FinanceTransparencyExecutionHistoryViewModel?>(null);
        }

        public Task<FinanceTransparencyToolExecutionDetailViewModel?> GetTransparencyToolExecutionDetailAsync(Guid companyId, Guid executionId, CancellationToken cancellationToken = default) =>
            OnGetTransparencyToolExecutionDetailAsync is not null
                ? OnGetTransparencyToolExecutionDetailAsync(companyId, executionId, cancellationToken)
                : Task.FromResult<FinanceTransparencyToolExecutionDetailViewModel?>(null);

        public Task<FinanceTransparencyEventStreamViewModel?> GetTransparencyEventsAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            TransparencyEventCalls++;
            return OnGetTransparencyEventsAsync is not null
                ? OnGetTransparencyEventsAsync(companyId, cancellationToken)
                : Task.FromResult<FinanceTransparencyEventStreamViewModel?>(null);
        }

        public Task<FinanceTransparencyEventDetailViewModel?> GetTransparencyEventDetailAsync(Guid companyId, Guid eventId, CancellationToken cancellationToken = default) =>
            OnGetTransparencyEventDetailAsync is not null
                ? OnGetTransparencyEventDetailAsync(companyId, eventId, cancellationToken)
                : Task.FromResult<FinanceTransparencyEventDetailViewModel?>(null);

        public Task<FinanceSandboxToolExecutionVisibilityViewModel?> GetToolExecutionVisibilityAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            ToolExecutionVisibilityCalls++;
            return OnGetToolExecutionVisibilityAsync is not null
                ? OnGetToolExecutionVisibilityAsync(companyId, cancellationToken)
                : Task.FromResult<FinanceSandboxToolExecutionVisibilityViewModel?>(null);
        }

        public Task<FinanceSandboxDomainEventsViewModel?> GetDomainEventsAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            DomainEventsCalls++;
            return OnGetDomainEventsAsync is not null
                ? OnGetDomainEventsAsync(companyId, cancellationToken)
                : Task.FromResult<FinanceSandboxDomainEventsViewModel?>(null);
        }

        public Task<FinanceSandboxSeedGenerationViewModel> GenerateSeedDatasetAsync(FinanceSandboxSeedGenerationCommand command, CancellationToken cancellationToken = default)
        {
            GenerateSeedDatasetCalls++;
            return OnGenerateSeedDatasetAsync is not null
                ? OnGenerateSeedDatasetAsync(command, cancellationToken)
                : Task.FromResult(new FinanceSandboxSeedGenerationViewModel());
        }
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
