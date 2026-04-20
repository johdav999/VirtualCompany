using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;
using VirtualCompany.Shared;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSandboxAdminEndpointIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly string[] SandboxAdminPaths =
    [
        "sandbox-admin/dataset-generation",
        "sandbox-admin/anomaly-injection",
        "sandbox-admin/simulation-controls",
        "sandbox-admin/tool-execution-visibility",
        "sandbox-admin/transparency/tool-manifests",
        "sandbox-admin/transparency/tool-executions",
        "sandbox-admin/transparency/events",
        "sandbox-admin/domain-events"
    ];

    private readonly TestWebApplicationFactory _factory;

    public FinanceSandboxAdminEndpointIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("admin")]
    [InlineData("tester")]
    public async Task Sandbox_admin_endpoints_allow_owner_and_admin_memberships(string role)
    {
        var seed = await SeedSandboxAdminCompanyAsync();
        var identity = seed.GetIdentity(role);
        using var client = CreateAuthenticatedClient(identity.Subject, identity.Email, identity.DisplayName);

        foreach (var path in SandboxAdminPaths)
        {
            var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/{path}");
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("finance_approver")]
    [InlineData("employee")]
    public async Task Sandbox_admin_endpoints_reject_non_admin_finance_memberships(string role)
    {
        var seed = await SeedSandboxAdminCompanyAsync();
        var identity = seed.GetIdentity(role);
        using var client = CreateAuthenticatedClient(identity.Subject, identity.Email, identity.DisplayName);

        foreach (var path in SandboxAdminPaths)
        {
            var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/{path}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Dataset_generation_endpoint_returns_tenant_scoped_payload_for_sandbox_admins()
    {
        var seed = await SeedSandboxAdminCompanyAsync();
        var identity = seed.GetIdentity("owner");
        using var client = CreateAuthenticatedClient(identity.Subject, identity.Email, identity.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/dataset-generation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<FinanceSandboxDatasetGenerationResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Tenant sandbox dataset", payload!.ProfileName);
        Assert.Contains("transactions", payload.CoverageSummary, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(payload.AvailableProfiles);
    }

    [Fact]
    public async Task Seed_generation_endpoint_returns_summary_counts_and_warnings_for_sandbox_admins()
    {
        var seed = await SeedSandboxAdminCompanyAsync();
        var identity = seed.GetIdentity("owner");
        using var client = CreateAuthenticatedClient(identity.Subject, identity.Email, identity.DisplayName);

        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/seed-generation",
            new FinanceSandboxSeedGenerationRequest
            {
                CompanyId = seed.CompanyId,
                SeedValue = 1919,
                AnchorDateUtc = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
                GenerationMode = FinanceSandboxSeedGenerationModes.RefreshWithAnomalies
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<FinanceSandboxSeedGenerationResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.Succeeded);
        Assert.True(payload.CreatedCount > 0);
        Assert.Equal(0, payload.UpdatedCount);
        Assert.NotEmpty(payload.Warnings);
    }

    [Fact]
    public async Task Seed_generation_endpoint_rejects_invalid_requests_with_structured_validation_errors()
    {
        var seed = await SeedSandboxAdminCompanyAsync();
        var identity = seed.GetIdentity("owner");
        using var client = CreateAuthenticatedClient(identity.Subject, identity.Email, identity.DisplayName);

        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/seed-generation",
            new FinanceSandboxSeedGenerationRequest
            {
                CompanyId = Guid.NewGuid(),
                SeedValue = 0,
                AnchorDateUtc = default,
                GenerationMode = "unsupported_mode"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(payload);
        Assert.Equal("Finance validation failed", payload!.Title);
        Assert.Equal("Update the seed generation request and try again.", payload.Detail);
        Assert.Contains(nameof(FinanceSandboxSeedGenerationRequest.CompanyId), payload.Errors.Keys);
        Assert.Contains(nameof(FinanceSandboxSeedGenerationRequest.SeedValue), payload.Errors.Keys);
        Assert.Contains(nameof(FinanceSandboxSeedGenerationRequest.AnchorDateUtc), payload.Errors.Keys);
        Assert.Contains(nameof(FinanceSandboxSeedGenerationRequest.GenerationMode), payload.Errors.Keys);
        Assert.Contains("does not match the active company context", payload.Errors[nameof(FinanceSandboxSeedGenerationRequest.CompanyId)][0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("positive seed value", payload.Errors[nameof(FinanceSandboxSeedGenerationRequest.SeedValue)][0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supported generation mode", payload.Errors[nameof(FinanceSandboxSeedGenerationRequest.GenerationMode)][0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Progression_run_endpoint_populates_simulation_status_and_history()
    {
        var seed = await SeedSandboxAdminCompanyAsync();
        var identity = seed.GetIdentity("owner");
        using var client = CreateAuthenticatedClient(identity.Subject, identity.Email, identity.DisplayName);

        var progressionResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/simulation-controls/progression-run",
            new FinanceSandboxSimulationAdvanceRequest
            {
                CompanyId = seed.CompanyId,
                IncrementHours = 24,
                ExecutionStepHours = 24,
                Accelerated = true
            });

        Assert.Equal(HttpStatusCode.OK, progressionResponse.StatusCode);

        var progressionPayload = await progressionResponse.Content.ReadFromJsonAsync<FinanceSandboxProgressionRunSummaryResponse>();
        Assert.NotNull(progressionPayload);
        Assert.Equal("progression_run", progressionPayload!.RunType);
        Assert.NotEmpty(progressionPayload.Steps);

        var controlsResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/simulation-controls");

        Assert.Equal(HttpStatusCode.OK, controlsResponse.StatusCode);

        var controlsPayload = await controlsResponse.Content.ReadFromJsonAsync<FinanceSandboxSimulationControlsResponse>();
        Assert.NotNull(controlsPayload);
        Assert.NotNull(controlsPayload!.CurrentRun);
        Assert.NotEmpty(controlsPayload.RunHistory);
        Assert.Equal(controlsPayload.CurrentRun!.CompletedUtc, controlsPayload.RunHistory[0].CompletedUtc);
        Assert.NotEmpty(controlsPayload.CurrentRun.Messages);
    }

    [Fact]
    public async Task Anomaly_injection_endpoint_returns_detail_metadata_and_related_record_reference()
    {
        var seed = await SeedSandboxAdminCompanyAsync();
        var identity = seed.GetIdentity("owner");
        using var client = CreateAuthenticatedClient(identity.Subject, identity.Email, identity.DisplayName);

        var injectResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/anomaly-injection",
            new FinanceSandboxAnomalyInjectionRequest
            {
                CompanyId = seed.CompanyId,
                ScenarioProfileCode = "duplicate_vendor_charge"
            });

        Assert.Equal(HttpStatusCode.OK, injectResponse.StatusCode);

        var injectedDetail = await injectResponse.Content.ReadFromJsonAsync<FinanceSandboxAnomalyDetailResponse>();
        Assert.NotNull(injectedDetail);
        Assert.Equal("duplicate_vendor_charge", injectedDetail!.ScenarioProfileCode);
        Assert.Equal("Duplicate vendor charge", injectedDetail.ScenarioProfileName);
        Assert.False(string.IsNullOrWhiteSpace(injectedDetail.AffectedRecordReference));
        Assert.Contains("scenarioProfileName", injectedDetail.ExpectedDetectionMetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(injectedDetail.Messages);

        var detailResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/anomaly-injection/{injectedDetail.Id}");

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var fetchedDetail = await detailResponse.Content.ReadFromJsonAsync<FinanceSandboxAnomalyDetailResponse>();
        Assert.NotNull(fetchedDetail);
        Assert.Equal(injectedDetail.Id, fetchedDetail!.Id);
        Assert.Equal(injectedDetail.AffectedRecordId, fetchedDetail.AffectedRecordId);
        Assert.Equal(injectedDetail.AffectedRecordReference, fetchedDetail.AffectedRecordReference);

        var registryResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/anomaly-injection");

        Assert.Equal(HttpStatusCode.OK, registryResponse.StatusCode);

        var registry = await registryResponse.Content.ReadFromJsonAsync<FinanceSandboxAnomalyInjectionResponse>();
        Assert.NotNull(registry);
        Assert.Contains(
            registry!.RegistryEntries,
            entry => entry.Id == injectedDetail.Id && !string.IsNullOrWhiteSpace(entry.AffectedRecordReference));
    }

    [Fact]
    public async Task Transparency_endpoints_return_event_execution_and_manifest_details()
    {
        var seed = await SeedSandboxAdminCompanyAsync();
        var identity = seed.GetIdentity("owner");
        using var client = CreateAuthenticatedClient(identity.Subject, identity.Email, identity.DisplayName);

        var manifestResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/transparency/tool-manifests");
        Assert.Equal(HttpStatusCode.OK, manifestResponse.StatusCode);
        var manifests = await manifestResponse.Content.ReadFromJsonAsync<FinanceTransparencyToolManifestListResponse>();
        Assert.NotNull(manifests);
        Assert.NotEmpty(manifests!.Items);
        Assert.Contains(
            manifests.Items,
            item => item.ToolName == "approve_invoice" &&
                    item.ManifestSource == "runtime_registry" &&
                    !string.IsNullOrWhiteSpace(item.VersionMetadata) &&
                    !string.IsNullOrWhiteSpace(item.SchemaSummary) &&
                    !string.IsNullOrWhiteSpace(item.ProviderAdapterId) &&
                    !string.IsNullOrWhiteSpace(item.ProviderAdapterName));
        Assert.All(manifests.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.ProviderAdapterIdentity)));

        var executionsResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/transparency/tool-executions");
        Assert.Equal(HttpStatusCode.OK, executionsResponse.StatusCode);
        var executions = await executionsResponse.Content.ReadFromJsonAsync<FinanceTransparencyToolExecutionHistoryResponse>();
        Assert.NotNull(executions);
        Assert.Contains(executions!.Items, item => item.ExecutionId == seed.TransparencyExecutionId);

        var executionDetailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/transparency/tool-executions/{seed.TransparencyExecutionId}");
        Assert.Equal(HttpStatusCode.OK, executionDetailResponse.StatusCode);
        var executionDetail = await executionDetailResponse.Content.ReadFromJsonAsync<FinanceTransparencyToolExecutionDetailResponse>();
        Assert.NotNull(executionDetail);
        Assert.Equal(seed.TransparencyExecutionId, executionDetail!.ExecutionId);
        Assert.Equal("approve_invoice", executionDetail.ToolName);
        Assert.Equal("awaitingapproval", executionDetail.LifecycleState.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(seed.TransparencyApprovalRequestId, executionDetail.ApprovalRequestId);
        Assert.Contains(seed.TransparencyApprovalRequestId.ToString("D"), executionDetail.ApprovalRequestDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("finance_invoice", executionDetail.OriginatingEntityType);
        Assert.Contains(
            executionDetail.RelatedRecords,
            item => item.RelationshipType == "approval_request" &&
                    item.TargetType == "approval_request" &&
                    item.TargetId == seed.TransparencyApprovalRequestId.ToString("D"));
        Assert.Contains(
            executionDetail.RelatedRecords,
            item => item.RelationshipType == "event" &&
                    item.TargetType == "audit_event" &&
                    item.TargetId == seed.TransparencyEventId.ToString("D"));
        Assert.True(executionDetail.OriginatingEntityId.HasValue);
        Assert.Contains("invoice", executionDetail.OriginatingFinanceActionDisplay, StringComparison.OrdinalIgnoreCase);

        var eventResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/transparency/events");
        Assert.Equal(HttpStatusCode.OK, eventResponse.StatusCode);
        var events = await eventResponse.Content.ReadFromJsonAsync<FinanceTransparencyEventStreamResponse>();
        Assert.NotNull(events);
        Assert.Contains(
            events!.Items,
            item => item.Id == seed.TransparencyEventId &&
                    item.HasTriggerTrace &&
                    item.AffectedEntityType == "finance_invoice" &&
                    !string.IsNullOrWhiteSpace(item.AffectedEntityId) &&
                    !string.IsNullOrWhiteSpace(item.EntityReference) &&
                    !string.IsNullOrWhiteSpace(item.PayloadSummary));

        var eventDetailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/transparency/events/{seed.TransparencyEventId}");
        Assert.Equal(HttpStatusCode.OK, eventDetailResponse.StatusCode);
        var eventDetail = await eventDetailResponse.Content.ReadFromJsonAsync<FinanceTransparencyEventDetailResponse>();
        Assert.NotNull(eventDetail);
        Assert.Equal(seed.TransparencyEventId, eventDetail!.Id);
        Assert.Equal("finance.invoice.approval.requested", eventDetail.EventType);
        Assert.Equal(seed.TransparencyCorrelationId, eventDetail.CorrelationId);
        Assert.Contains(
            eventDetail.RelatedRecords,
            item => item.RelationshipType == "tool_execution" &&
                    item.TargetType == "tool_execution" &&
                    item.TargetId == seed.TransparencyExecutionId.ToString("D"));
        Assert.Contains(
            eventDetail.RelatedRecords,
            item => item.RelationshipType == "approval_request" &&
                    item.TargetType == "approval_request" &&
                    item.TargetId == seed.TransparencyApprovalRequestId.ToString("D"));
        Assert.Contains("awaiting approval", eventDetail.PayloadSummary, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(eventDetail.TriggerConsumptionTrace);
    }

    [Fact]
    public async Task Simulation_control_endpoints_reject_invalid_hour_increment()
    {
        var seed = await SeedSandboxAdminCompanyAsync();
        var identity = seed.GetIdentity("owner");
        using var client = CreateAuthenticatedClient(identity.Subject, identity.Email, identity.DisplayName);

        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/sandbox-admin/simulation-controls/advance",
            new FinanceSandboxSimulationAdvanceRequest
            {
                CompanyId = seed.CompanyId,
                IncrementHours = 0,
                ExecutionStepHours = 24,
                Accelerated = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(payload);
        Assert.Equal("Finance validation failed", payload!.Title);
        Assert.Contains(nameof(FinanceSandboxSimulationAdvanceRequest.IncrementHours), payload.Errors.Keys);
        Assert.Contains("positive hour increment", payload.Errors[nameof(FinanceSandboxSimulationAdvanceRequest.IncrementHours)][0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Anomaly_injection_endpoint_returns_profiles_even_when_registry_is_empty()
    {
        var companyId = Guid.NewGuid();
        var owner = TestIdentity.Create("Empty Sandbox Owner");
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(owner.UserId, owner.Email, owner.DisplayName, "dev-header", owner.Subject));
            dbContext.Companies.Add(new Company(companyId, "Empty Sandbox Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, owner.UserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(owner.Subject, owner.Email, owner.DisplayName);
        var response = await client.GetAsync($"/internal/companies/{companyId}/finance/sandbox-admin/anomaly-injection");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceSandboxAnomalyInjectionResponse>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.AvailableScenarioProfiles);
        Assert.Empty(payload.RegistryEntries);
    }

    private async Task<SandboxAdminEndpointSeed> SeedSandboxAdminCompanyAsync()
    {
        var companyId = Guid.NewGuid();

        var owner = TestIdentity.Create("Sandbox Owner");
        var admin = TestIdentity.Create("Sandbox Admin");
        var manager = TestIdentity.Create("Sandbox Manager");
        var tester = TestIdentity.Create("Sandbox Tester");
        var approver = TestIdentity.Create("Sandbox Approver");
        var employee = TestIdentity.Create("Sandbox Employee");
        var transparencyEventId = Guid.NewGuid();
        var transparencyExecutionId = Guid.NewGuid();
        var transparencyApprovalRequestId = Guid.NewGuid();
        var transparencyCorrelationId = $"finance-transparency-{Guid.NewGuid():N}";
        var transparencyAgentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(owner.UserId, owner.Email, owner.DisplayName, "dev-header", owner.Subject),
                new User(admin.UserId, admin.Email, admin.DisplayName, "dev-header", admin.Subject),
                new User(manager.UserId, manager.Email, manager.DisplayName, "dev-header", manager.Subject),
                new User(tester.UserId, tester.Email, tester.DisplayName, "dev-header", tester.Subject),
                new User(approver.UserId, approver.Email, approver.DisplayName, "dev-header", approver.Subject),
                new User(employee.UserId, employee.Email, employee.DisplayName, "dev-header", employee.Subject));

            dbContext.Companies.Add(new Company(companyId, "Finance Sandbox Company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, owner.UserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, admin.UserId, CompanyMembershipRole.Admin, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, manager.UserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, tester.UserId, CompanyMembershipRole.Tester, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, approver.UserId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employee.UserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));

            var financeSeed = FinanceSeedData.AddMockFinanceData(dbContext, companyId);
            dbContext.FinanceSeedAnomalies.Add(new FinanceSeedAnomaly(
                Guid.NewGuid(),
                companyId,
                "missing_receipt",
                "sandbox_validation",
                [financeSeed.TransactionIds[0]],
                """{"expectedDetector":"receipt_completeness"}"""));
            dbContext.Agents.Add(new Agent(
                transparencyAgentId,
                companyId,
                "finance-transparency",
                "Finance Transparency Agent",
                "Finance operator",
                "finance",
                null,
                AgentSeniority.Mid));

            var transparencyExecution = new ToolExecutionAttempt(
                transparencyExecutionId,
                companyId,
                transparencyAgentId,
                "approve_invoice",
                ToolActionType.Execute,
                "finance",
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["invoiceId"] = JsonValue.Create(financeSeed.InvoiceIds[0]),
                    ["status"] = JsonValue.Create("approved")
                },
                taskId: Guid.NewGuid(),
                workflowInstanceId: Guid.NewGuid(),
                correlationId: transparencyCorrelationId,
                toolVersion: "1.0.0");
            transparencyExecution.MarkAwaitingApproval(
                transparencyApprovalRequestId,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["outcome"] = JsonValue.Create("require_approval")
                },
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["status"] = JsonValue.Create("awaiting_approval"),
                    ["userSafeSummary"] = JsonValue.Create("Invoice approval request is awaiting approval.")
                },
                new DateTime(2026, 4, 16, 12, 30, 0, DateTimeKind.Utc));
            dbContext.ToolExecutionAttempts.Add(transparencyExecution);

            dbContext.AuditEvents.Add(new AuditEvent(
                Guid.NewGuid(),
                companyId,
                "user",
                owner.UserId,
                "finance.sandbox.dataset.generated",
                "finance_sandbox",
                "dataset_generation",
                "captured"));
            dbContext.AuditEvents.Add(new AuditEvent(
                transparencyEventId,
                companyId,
                "agent",
                transparencyAgentId,
                "finance.invoice.approval.requested",
                "finance_invoice",
                financeSeed.InvoiceIds[0].ToString("D"),
                "captured",
                rationaleSummary: "Invoice approval is awaiting approval after finance policy evaluation.",
                metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["requestedDomain"] = "finance",
                    ["recordReference"] = $"Invoice {financeSeed.InvoiceIds[0]:D}"
                },
                correlationId: transparencyCorrelationId,
                dataSourcesUsed:
                [
                    new AuditDataSourceUsed("tool_execution", transparencyExecutionId.ToString("D"), "Approval decision trace", "Finance tool execution"),
                    new AuditDataSourceUsed("workflow_trigger", "finance.approval.threshold", "Threshold trigger", "Invoice amount exceeded approval threshold")
                ],
                payloadDiffJson: """{"approvalStatus":"awaiting_approval","toolName":"approve_invoice"}"""));

            return Task.CompletedTask;
        });

        return new SandboxAdminEndpointSeed(
            companyId,
            owner,
            admin,
            manager,
            tester,
            approver,
            employee,
            transparencyEventId,
            transparencyExecutionId,
            transparencyApprovalRequestId,
            transparencyCorrelationId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record SandboxAdminEndpointSeed(
        Guid CompanyId,
        TestIdentity Owner,
        TestIdentity Admin,
        TestIdentity Manager,
        TestIdentity Tester,
        TestIdentity Approver,
        TestIdentity Employee,
        Guid TransparencyEventId,
        Guid TransparencyExecutionId,
        Guid TransparencyApprovalRequestId,
        string TransparencyCorrelationId)
    {
        public TestIdentity GetIdentity(string role) =>
            role switch
            {
                "owner" => Owner,
                "admin" => Admin,
                "manager" => Manager,
                "tester" => Tester,
                "finance_approver" => Approver,
                "employee" => Employee,
                _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported sandbox admin test role.")
            };
    }

    private sealed record TestIdentity(
        Guid UserId,
        string Subject,
        string Email,
        string DisplayName)
    {
        public static TestIdentity Create(string displayName)
        {
            var userId = Guid.NewGuid();
            var subject = $"sandbox-{displayName.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}-{Guid.NewGuid():N}";
            return new TestIdentity(
                userId,
                subject,
                $"{subject}@example.com",
                displayName);
        }
    }
}