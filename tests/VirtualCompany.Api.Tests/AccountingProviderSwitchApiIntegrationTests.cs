using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Laura_briefing_is_company_scoped_plain_English_and_grounded_in_current_switch_version()
    {
        var seed = await SeedAsync();
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        using var created = await owner.PostAsJsonAsync(Route(seed.CompanyId), CreateRequest(seed));
        created.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var switchId = json.RootElement.GetProperty("id").GetGuid();
        var version = json.RootElement.GetProperty("version").GetInt64();

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(seed.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IAccountingProviderSwitchAgentService>();
        var briefing = await service.GetBriefingAsync(new(seed.CompanyId, switchId), CancellationToken.None);

        Assert.Equal(switchId, briefing.SwitchId);
        Assert.Equal(version, briefing.SwitchVersion);
        Assert.Equal("Draft", briefing.CurrentStep);
        Assert.Contains("source", briefing.Evidence[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opening_balances_and_open_items", string.Join(' ', briefing.Evidence), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", string.Join(' ', briefing.Evidence), StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(briefing.DataSources);

        var crossCompany = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            service.GetBriefingAsync(new(seed.UnownedCompanyId, switchId), CancellationToken.None));
        Assert.Equal(AccountingProviderSwitchReasonCodes.NotFound, crossCompany.ReasonCode);
    }

    [Fact]
    public async Task Owner_and_accounting_admin_can_manage_versioned_draft_and_authority_does_not_change()
    {
        var seed = await SeedAsync();
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        using var admin = CreateClient(seed.AdminSubject, seed.AdminEmail);
        using var created = await owner.PostAsJsonAsync(Route(seed.CompanyId), CreateRequest(seed));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var switchId = createdJson.RootElement.GetProperty("id").GetGuid();
        var version = createdJson.RootElement.GetProperty("version").GetInt64();
        Assert.Equal("draft", createdJson.RootElement.GetProperty("status").GetString());
        Assert.Equal("outbound", createdJson.RootElement.GetProperty("direction").GetString());

        using var listed = await admin.GetAsync(Route(seed.CompanyId));
        using var fetched = await admin.GetAsync($"{Route(seed.CompanyId)}/{switchId:D}");
        using var allowed = await admin.GetAsync($"{Route(seed.CompanyId)}/{switchId:D}/allowed-actions");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var listJson = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        using var allowedJson = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());
        Assert.Single(listJson.RootElement.EnumerateArray());
        Assert.True(allowedJson.RootElement.GetProperty("canUpdatePlan").GetBoolean());
        Assert.True(allowedJson.RootElement.GetProperty("canCancel").GetBoolean());

        using var stale = await owner.PutAsJsonAsync($"{Route(seed.CompanyId)}/{switchId:D}/plan", new
        {
            sourceKind = "internal",
            sourceProviderKey = (string?)null,
            targetKind = "external",
            targetProviderKey = "fortnox",
            effectiveFiscalPeriodId = seed.FiscalPeriodId,
            migrationStrategy = AccountingProviderSwitchStrategies.FullHistory,
            reason = "This stale plan must not be applied.",
            responsibleUserId = seed.OwnerId,
            responsibleAgentId = (Guid?)null,
            expectedVersion = version - 1
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var staleJson = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        Assert.Equal(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
            staleJson.RootElement.GetProperty("code").GetString());

        using var cancelled = await admin.PostAsJsonAsync($"{Route(seed.CompanyId)}/{switchId:D}/cancel", new
        {
            reason = "The company decided to keep its current accounting system.",
            expectedVersion = version
        });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        using var cancelledJson = JsonDocument.Parse(await cancelled.Content.ReadAsStringAsync());
        Assert.Equal("cancelled", cancelledJson.RootElement.GetProperty("status").GetString());

        var state = await _factory.ExecuteDbContextAsync(async db => new
        {
            Authority = await db.AccountingAuthorityPeriods.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == seed.CompanyId),
            RejectedAudits = await db.AuditEvents.IgnoreQueryFilters().CountAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.Action == AuditEventActions.AccountingProviderSwitchMutationRejected),
            CancellationAudits = await db.AuditEvents.IgnoreQueryFilters().CountAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.Action == AuditEventActions.AccountingProviderSwitchCancelled)
        });
        Assert.Equal(AccountingAuthorityValues.InternalLedger, state.Authority.Authority);
        Assert.Null(state.Authority.EffectiveTo);
        Assert.Equal(1, state.RejectedAudits);
        Assert.Equal(1, state.CancellationAudits);
    }

    [Fact]
    public async Task Employee_and_cross_company_requests_are_denied_by_backend_authorization()
    {
        var seed = await SeedAsync();
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var employeeCreate = await employee.PostAsJsonAsync(Route(seed.CompanyId), CreateRequest(seed));
        using var employeeList = await employee.GetAsync(Route(seed.CompanyId));
        using var crossCompanyCreate = await owner.PostAsJsonAsync(Route(seed.UnownedCompanyId), CreateRequest(seed));
        using var crossCompanyList = await owner.GetAsync(Route(seed.UnownedCompanyId));

        Assert.Equal(HttpStatusCode.Forbidden, employeeCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyList.StatusCode);
        Assert.Equal(0, await _factory.ExecuteDbContextAsync(db =>
            db.AccountingProviderSwitches.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task Rehearsal_evidence_and_plan_routes_enforce_accounting_roles_and_company_membership()
    {
        var seed = await SeedAsync();
        var switchId = Guid.NewGuid();
        var rehearsalId = Guid.NewGuid();
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var employeeStart = await employee.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/{switchId:D}/rehearsals", new
            {
                expectedSwitchVersion = 1,
                idempotencyKey = "employee-rehearsal-denied"
            });
        using var employeeEvidence = await employee.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/{switchId:D}/rehearsals/{rehearsalId:D}/checks/{Guid.NewGuid():D}/evidence", new
            {
                explanation = "Must not be accepted.",
                evidenceReference = "document:denied",
                expiresUtc = (DateTime?)null
            });
        using var crossCompanyRead = await owner.GetAsync(
            $"{Route(seed.UnownedCompanyId)}/{switchId:D}/rehearsals/latest");
        using var crossCompanyPlan = await owner.PostAsJsonAsync(
            $"{Route(seed.UnownedCompanyId)}/{switchId:D}/cutover-plans", new
            {
                rehearsalId,
                expectedSwitchVersion = 1,
                freezeStartsUtc = DateTime.UtcNow.AddHours(1),
                freezeEndsUtc = DateTime.UtcNow.AddHours(2),
                recoveryBoundary = "No target authority.",
                participantUserIds = new[] { seed.OwnerId }
            });

        Assert.Equal(HttpStatusCode.Forbidden, employeeStart.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeEvidence.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyPlan.StatusCode);
        Assert.Equal(0, await _factory.ExecuteDbContextAsync(db =>
            db.AccountingProviderSwitchRehearsals.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task Assessment_start_and_results_enforce_accounting_roles_company_scope_and_switch_version()
    {
        var seed = await SeedAsync();
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        using var admin = CreateClient(seed.AdminSubject, seed.AdminEmail);
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);
        using var created = await owner.PostAsJsonAsync(Route(seed.CompanyId), CreateRequest(seed));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var switchId = createdJson.RootElement.GetProperty("id").GetGuid();
        var version = createdJson.RootElement.GetProperty("version").GetInt64();
        var assessmentRoute = $"{Route(seed.CompanyId)}/{switchId:D}/assessments";

        using var started = await admin.PostAsJsonAsync(assessmentRoute, new
        {
            expectedSwitchVersion = version,
            idempotencyKey = "api-assessment-start"
        });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var startedJson = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var assessmentId = startedJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(switchId, startedJson.RootElement.GetProperty("switchId").GetGuid());

        using var latest = await owner.GetAsync($"{assessmentRoute}/latest");
        using var detail = await owner.GetAsync($"{assessmentRoute}/{assessmentId:D}");
        using var capabilities = await owner.GetAsync($"{assessmentRoute}/{assessmentId:D}/capabilities");
        using var datasets = await owner.GetAsync($"{assessmentRoute}/{assessmentId:D}/datasets");
        using var gaps = await owner.GetAsync($"{assessmentRoute}/{assessmentId:D}/gaps");
        using var progress = await owner.GetAsync($"{assessmentRoute}/{assessmentId:D}/progress");
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode);
        Assert.Equal(HttpStatusCode.OK, datasets.StatusCode);
        Assert.Equal(HttpStatusCode.OK, gaps.StatusCode);
        Assert.Equal(HttpStatusCode.OK, progress.StatusCode);

        using var stale = await owner.PostAsJsonAsync(assessmentRoute, new
        {
            expectedSwitchVersion = version,
            idempotencyKey = "api-assessment-stale"
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var employeeStart = await employee.PostAsJsonAsync(assessmentRoute, new
        {
            expectedSwitchVersion = version + 1,
            idempotencyKey = "employee-denied"
        });
        using var employeeRead = await employee.GetAsync($"{assessmentRoute}/{assessmentId:D}");
        using var crossCompanyRead = await owner.GetAsync(
            $"{Route(seed.UnownedCompanyId)}/{switchId:D}/assessments/{assessmentId:D}");
        Assert.Equal(HttpStatusCode.Forbidden, employeeStart.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyRead.StatusCode);
    }

    [Fact]
    public async Task Staging_mapping_approval_and_completeness_are_tenant_safe_and_version_bound()
    {
        var seed = await SeedAsync();
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        using var admin = CreateClient(seed.AdminSubject, seed.AdminEmail);
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);
        using var created = await owner.PostAsJsonAsync(Route(seed.CompanyId), CreateRequest(seed));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var switchId = createdJson.RootElement.GetProperty("id").GetGuid();
        var assessmentId = await _factory.ExecuteDbContextAsync(async db =>
        {
            var providerSwitch = await db.AccountingProviderSwitches.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == seed.CompanyId && x.Id == switchId);
            providerSwitch.TransitionTo(AccountingProviderSwitchStatuses.Assessing, seed.OwnerId,
                "api-staging-assessing", DateTime.UtcNow);
            providerSwitch.TransitionTo(AccountingProviderSwitchStatuses.ReadyForPlanning, seed.OwnerId,
                "api-staging-ready", DateTime.UtcNow);
            var assessment = new AccountingProviderSwitchAssessment(Guid.NewGuid(), seed.CompanyId, switchId,
                seed.OwnerId, "api-staging-assessment", "api-staging-assessment", 1, DateTime.UtcNow);
            assessment.Complete(DateTime.UtcNow);
            db.AccountingProviderSwitchAssessments.Add(assessment);
            var dataset = new AccountingProviderSwitchDataset(seed.CompanyId, switchId, assessment.Id,
                AccountingProviderSwitchEndpointRoles.Source, AccountingProviderSwitchDatasetKeys.Currencies,
                DateTime.UtcNow);
            dataset.Record(AccountingProviderSwitchDatasetAvailability.Available,
                AccountingProviderSwitchCapabilityLevels.Supported, 1, 125m, "SEK", null, "v1",
                new string('d', 64), "{}", null, null, DateTime.UtcNow);
            db.AccountingProviderSwitchDatasets.Add(dataset);
            await db.SaveChangesAsync();
            return assessment.Id;
        });
        var stagingRoute = $"{Route(seed.CompanyId)}/{switchId:D}/staging";

        using var staged = await admin.PostAsJsonAsync(stagingRoute, new
        {
            extractionBatchId = assessmentId,
            dataset = AccountingProviderSwitchStagingDatasets.Currencies,
            sourceIdentity = "SEK",
            sourceVersion = "v1",
            providerModifiedUtc = DateTime.UtcNow,
            sourceHash = new string('a', 64),
            normalizedDataJson = "{\"code\":\"SEK\"}",
            evidenceJson = "{}",
            financialAmount = 125m,
            currency = "SEK",
            initialDisposition = AccountingProviderSwitchDispositions.Ready
        });
        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        using var stagedJson = JsonDocument.Parse(await staged.Content.ReadAsStringAsync());
        var stagedRecordId = stagedJson.RootElement.GetProperty("id").GetGuid();
        var stagedVersion = stagedJson.RootElement.GetProperty("version").GetInt64();

        using var employeeRead = await employee.GetAsync(stagingRoute);
        using var crossCompanyRead = await owner.GetAsync(
            $"{Route(seed.UnownedCompanyId)}/{switchId:D}/staging");
        Assert.Equal(HttpStatusCode.Forbidden, employeeRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyRead.StatusCode);

        using var preview = await owner.PostAsJsonAsync($"{Route(seed.CompanyId)}/{switchId:D}/mappings/preview", new
        {
            mappingType = AccountingProviderSwitchMappingTypes.Currency,
            sourceKey = "SEK",
            proposedTargetKey = "SEK",
            sourceSemantic = (string?)null,
            affectedStagedRecordIds = new[] { stagedRecordId },
            isMaterial = true
        });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        var decisionId = previewJson.RootElement.GetProperty("id").GetGuid();
        var decisionVersion = previewJson.RootElement.GetProperty("version").GetInt64();
        Assert.Equal(AccountingProviderSwitchMappingStatuses.Suggested,
            previewJson.RootElement.GetProperty("status").GetString());

        using var approvalRequested = await owner.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/{switchId:D}/mappings/{decisionId:D}/approval",
            new { expectedVersion = decisionVersion });
        Assert.Equal(HttpStatusCode.OK, approvalRequested.StatusCode);
        using var approvalRequestedJson = JsonDocument.Parse(await approvalRequested.Content.ReadAsStringAsync());
        var approvalId = approvalRequestedJson.RootElement.GetProperty("approvalRequestId").GetGuid();
        var approvalBoundDecisionVersion = approvalRequestedJson.RootElement.GetProperty("version").GetInt64();

        using var approvalDetail = await admin.GetAsync($"/api/companies/{seed.CompanyId:D}/approvals/{approvalId:D}");
        using var approvalJson = JsonDocument.Parse(await approvalDetail.Content.ReadAsStringAsync());
        var context = approvalJson.RootElement.GetProperty("thresholdContext");
        Assert.Equal(decisionId.ToString("D"), context.GetProperty("mappingDecisionId").GetString());
        Assert.Equal(1, context.GetProperty("affectedRecordCount").GetInt64());
        Assert.Equal(64, context.GetProperty("bindingHash").GetString()!.Length);

        using var approved = await admin.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId:D}/approvals/{approvalId:D}/decisions",
            new { approvalId = Guid.Empty, decision = "approve", comment = "Reviewed source and target currency evidence." });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        using var refreshed = await owner.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/{switchId:D}/mappings/{decisionId:D}/approval",
            new { expectedVersion = approvalBoundDecisionVersion });
        using var refreshedJson = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
        Assert.Equal(AccountingProviderSwitchMappingStatuses.Approved,
            refreshedJson.RootElement.GetProperty("status").GetString());
        Assert.True(refreshedJson.RootElement.GetProperty("isApprovalCurrent").GetBoolean());

        using var resolved = await admin.PutAsJsonAsync($"{stagingRoute}/{stagedRecordId:D}/disposition", new
        {
            disposition = AccountingProviderSwitchDispositions.Mapped,
            reason = "Approved material currency mapping.",
            mappingDecisionId = decisionId,
            duplicateOfStagedRecordId = (Guid?)null,
            expectedVersion = stagedVersion
        });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        using var completeness = await owner.GetAsync($"{stagingRoute}/completeness");
        using var completenessJson = JsonDocument.Parse(await completeness.Content.ReadAsStringAsync());
        Assert.True(completenessJson.RootElement.GetProperty("isComplete").GetBoolean());

        using var changedReplay = await admin.PostAsJsonAsync(stagingRoute, new
        {
            extractionBatchId = assessmentId,
            dataset = AccountingProviderSwitchStagingDatasets.Currencies,
            sourceIdentity = "SEK",
            sourceVersion = "v1",
            providerModifiedUtc = DateTime.UtcNow,
            sourceHash = new string('b', 64),
            normalizedDataJson = "{\"code\":\"SEK\",\"changed\":true}",
            evidenceJson = "{}",
            financialAmount = 125m,
            currency = "SEK",
            initialDisposition = AccountingProviderSwitchDispositions.Ready
        });
        using var changedJson = JsonDocument.Parse(await changedReplay.Content.ReadAsStringAsync());
        Assert.Equal(AccountingProviderSwitchDispositions.AwaitingEvidence,
            changedJson.RootElement.GetProperty("disposition").GetString());
        using var staleApproval = await owner.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/{switchId:D}/mappings/{decisionId:D}/approval",
            new { expectedVersion = approvalBoundDecisionVersion + 1 });
        Assert.Equal(HttpStatusCode.Conflict, staleApproval.StatusCode);
    }

    [Fact]
    public async Task Preparation_routes_enforce_accounting_roles_and_company_membership()
    {
        var seed = await SeedAsync();
        var switchId = Guid.NewGuid();
        var preparationId = Guid.NewGuid();
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var employeeStart = await employee.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/{switchId:D}/preparations", new
            {
                planId = Guid.NewGuid(),
                expectedSwitchVersion = 1,
                idempotencyKey = "employee-preparation-denied"
            });
        using var employeeRead = await employee.GetAsync(
            $"{Route(seed.CompanyId)}/{switchId:D}/preparations/latest");
        using var crossCompanyReadiness = await owner.GetAsync(
            $"{Route(seed.UnownedCompanyId)}/{switchId:D}/preparation/readiness");
        using var crossCompanyReplay = await owner.PostAsync(
            $"{Route(seed.UnownedCompanyId)}/{switchId:D}/preparations/{preparationId:D}/replay", null);

        Assert.Equal(HttpStatusCode.Forbidden, employeeStart.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyReadiness.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyReplay.StatusCode);
        Assert.Equal(0, await _factory.ExecuteDbContextAsync(db =>
            db.AccountingProviderSwitchPreparations.IgnoreQueryFilters().CountAsync()));
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var unownedCompanyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        const string ownerSubject = "provider-switch-owner";
        const string ownerEmail = "provider-switch-owner@example.com";
        const string adminSubject = "provider-switch-admin";
        const string adminEmail = "provider-switch-admin@example.com";
        const string employeeSubject = "provider-switch-employee";
        const string employeeEmail = "provider-switch-employee@example.com";

        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(
                new User(ownerId, ownerEmail, "Accounting Owner", "dev-header", ownerSubject),
                new User(adminId, adminEmail, "Accounting Admin", "dev-header", adminSubject),
                new User(employeeId, employeeEmail, "Accounting Employee", "dev-header", employeeSubject));
            db.Companies.AddRange(
                new Company(companyId, "Provider switch API company"),
                new Company(unownedCompanyId, "Unowned provider switch company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, adminId, CompanyMembershipRole.Admin, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.FiscalPeriods.Add(new FiscalPeriod(
                fiscalPeriodId, companyId, "Future monthly boundary",
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
            db.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(
                Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), null,
                AccountingAuthorityValues.InternalLedger, null, ownerId,
                "Virtual Company remains authoritative.", DateTime.UtcNow));
            return Task.CompletedTask;
        });

        return new Seed(companyId, unownedCompanyId, ownerId, fiscalPeriodId,
            ownerSubject, ownerEmail, adminSubject, adminEmail, employeeSubject, employeeEmail);
    }

    private static object CreateRequest(Seed seed) => new
    {
        sourceKind = "internal",
        sourceProviderKey = (string?)null,
        targetKind = "external",
        targetProviderKey = "fortnox",
        effectiveFiscalPeriodId = seed.FiscalPeriodId,
        migrationStrategy = AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems,
        reason = "Move accounting at the future monthly boundary.",
        responsibleUserId = seed.OwnerId,
        responsibleAgentId = (Guid?)null
    };

    private static string Route(Guid companyId) =>
        $"/internal/companies/{companyId:D}/finance/accounting/provider-switches";

    private HttpClient CreateClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed record Seed(
        Guid CompanyId,
        Guid UnownedCompanyId,
        Guid OwnerId,
        Guid FiscalPeriodId,
        string OwnerSubject,
        string OwnerEmail,
        string AdminSubject,
        string AdminEmail,
        string EmployeeSubject,
        string EmployeeEmail);
}
