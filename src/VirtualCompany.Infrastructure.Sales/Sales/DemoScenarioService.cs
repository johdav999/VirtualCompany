using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class DemoScenarioService(
    VirtualCompanyDbContext dbContext,
    IDemoScenarioCatalog catalog,
    ICompanyOnboardingService onboarding,
    IAuditEventWriter audit,
    IOptions<DemoScenarioOptions> options) : IDemoScenarioService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string QualifyLead = "demo.sales.qualify_lead";
    private const string ConvertLead = "demo.sales.convert_lead";
    private const string MoveDeal = "demo.sales.move_deal_to_proposal";

    public async Task<ProvisionDemoScenarioResult> ProvisionAsync(
        Guid userId,
        ProvisionDemoScenarioRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled(options.Value.ProvisioningEnabled, "Demo tenant provisioning is disabled in this environment.");
        EnsureUser(userId);
        if (!request.ConfirmSyntheticDataOnly)
            throw new DemoScenarioException(DemoScenarioProblemCodes.InvalidSpecification, "Confirm that only synthetic data will be provisioned.");

        var definition = catalog.Get(request.ScenarioKey, request.ScenarioVersion);
        using var activity = DemoScenarioTelemetry.ActivitySource.StartActivity("demo-scenario.provision");
        activity?.SetTag("demo.scenario", definition.ScenarioKey);
        activity?.SetTag("demo.version", definition.ScenarioVersion);

        var created = await onboarding.CreateCompanyAsync(
            new CreateCompanyCommand(
                definition.CompanyName,
                "Software",
                "Synthetic product demonstration",
                null,
                new CompanySettingsDto
                {
                    FeatureFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["demoTenant"] = true,
                        ["externalIntegrations"] = false
                    }
                },
                "Europe/Stockholm",
                definition.InitialState.Currency,
                "en",
                "SE",
                null,
                ExplicitNewCompany: true),
            cancellationToken);

        var company = await dbContext.Companies.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == created.CompanyId, cancellationToken);
        company.MarkAsDemoTenant(definition.ScenarioKey, definition.ScenarioVersion);
        var now = DateTime.UtcNow;
        var run = new DemoScenarioRun(
            DeterministicId(company.Id, definition.ScenarioKey, definition.ScenarioVersion, "run"),
            company.Id,
            definition.ScenarioKey,
            definition.ScenarioVersion,
            userId,
            now);
        dbContext.DemoScenarioRuns.Add(run);
        SeedStartingState(company.Id, definition);
        await WriteAuditAsync(company.Id, userId, AuditEventActions.DemoScenarioProvisioned, run.Id,
            "Provisioned an isolated synthetic demo tenant with external integrations blocked.",
            new Dictionary<string, string?>
            {
                ["scenarioKey"] = definition.ScenarioKey,
                ["scenarioVersion"] = definition.ScenarioVersion.ToString(),
                ["companyName"] = company.Name,
                ["syntheticDataOnly"] = "true",
                ["externalIntegrationsBlocked"] = "true"
            }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        DemoScenarioTelemetry.Provisions.Add(1);

        return new ProvisionDemoScenarioResult(
            company.Id, company.Name, run.Id, run.ScenarioKey, run.ScenarioVersion,
            StatusValue(run.Status), true, true);
    }

    public async Task<DemoScenarioStatusDto?> GetStatusAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        await EnsurePermissionAsync(companyId, userId, cancellationToken);
        var pair = await FindAsync(companyId, cancellationToken);
        return pair is null ? null : await BuildStatusAsync(pair.Value.Company, pair.Value.Run, cancellationToken);
    }

    public async Task<DemoScenarioStatusDto> LinkMeetingAsync(
        Guid companyId,
        Guid userId,
        LinkDemoScenarioMeetingRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled(options.Value.Enabled, "Demo scenarios are disabled in this environment.");
        await EnsurePermissionAsync(companyId, userId, cancellationToken);
        var (company, run, definition) = await RequireAsync(companyId, request.ScenarioKey, request.ScenarioVersion, cancellationToken);
        var meetingExists = await dbContext.SalesMeetingSessions.IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == request.MeetingSessionId, cancellationToken);
        if (!meetingExists)
            throw new DemoScenarioException(DemoScenarioProblemCodes.MeetingMismatch, "The sales meeting does not belong to the exact demo company.");

        run.LinkMeeting(request.MeetingSessionId, userId, DateTime.UtcNow);
        await WriteAuditAsync(companyId, userId, AuditEventActions.DemoScenarioMeetingLinked, run.Id,
            "Linked the approved demo scenario to a company-scoped sales meeting.",
            new Dictionary<string, string?> { ["meetingSessionId"] = request.MeetingSessionId.ToString("D") }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await BuildStatusAsync(company, run, cancellationToken, definition);
    }

    public async Task<DemoScenarioStatusDto> StartAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        EnsureEnabled(options.Value.Enabled, "Demo scenarios are disabled in this environment.");
        await EnsurePermissionAsync(companyId, userId, cancellationToken);
        var (company, run, definition) = await RequireAsync(companyId, cancellationToken: cancellationToken);
        run.Start(DateTime.UtcNow);
        await WriteAuditAsync(companyId, userId, AuditEventActions.DemoScenarioStarted, run.Id,
            "Started the controlled demo workflow.", null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await BuildStatusAsync(company, run, cancellationToken, definition);
    }

    public async Task<DemoScenarioResetPreviewDto> PreviewResetAsync(
        Guid companyId,
        Guid userId,
        string scenarioKey,
        int scenarioVersion,
        CancellationToken cancellationToken)
    {
        EnsureEnabled(options.Value.Enabled, "Demo scenarios are disabled in this environment.");
        await EnsurePermissionAsync(companyId, userId, cancellationToken);
        var (company, run, definition) = await RequireAsync(companyId, scenarioKey, scenarioVersion, cancellationToken);
        var affected = await CountAffectedAsync(companyId, definition, cancellationToken);
        var validations = await ValidateAsync(company, run, definition, cancellationToken);
        var canReset = validations.All(x => x.Passed);
        var token = BuildPreviewToken(company, run, affected);

        await WriteAuditAsync(companyId, userId, AuditEventActions.DemoScenarioResetPreviewed, run.Id,
            "Previewed the exact records and invariants for a demo reset without changing data.",
            new Dictionary<string, string?>
            {
                ["scenarioKey"] = definition.ScenarioKey,
                ["scenarioVersion"] = definition.ScenarioVersion.ToString(),
                ["canReset"] = canReset.ToString(),
                ["affectedCounts"] = string.Join(",", affected.Select(x => $"{x.RecordClass}:{x.ExistingCount}"))
            }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DemoScenarioResetPreviewDto(
            company.Id, company.Name, run.Id, definition.ScenarioKey, definition.ScenarioVersion,
            run.ResetGeneration, affected, definition.DisabledIntegrations, validations,
            [
                "Exactly one synthetic customer company exists.",
                "Exactly one synthetic contact exists and uses the reserved .invalid domain.",
                "Exactly one open lead exists at the New stage.",
                "No demo deal or demo sales activity exists.",
                "External integration delivery remains blocked."
            ],
            true, canReset, token);
    }

    public async Task<DemoScenarioStatusDto> ResetAsync(
        Guid companyId,
        Guid userId,
        ResetDemoScenarioRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled(options.Value.ResetEnabled, "Demo reset is disabled in this environment.");
        await EnsurePermissionAsync(companyId, userId, cancellationToken);
        var (company, run, definition) = await RequireAsync(companyId, request.ScenarioKey, request.ScenarioVersion, cancellationToken);
        if (!string.Equals(company.Name, request.ExpectedCompanyName?.Trim(), StringComparison.Ordinal))
            throw new DemoScenarioException(DemoScenarioProblemCodes.ScenarioMismatch, "The confirmed company name does not match the exact demo tenant.");

        var affected = await CountAffectedAsync(companyId, definition, cancellationToken);
        var validations = await ValidateAsync(company, run, definition, cancellationToken);
        if (validations.Any(x => !x.Passed))
            throw new DemoScenarioException(DemoScenarioProblemCodes.NotDemoTenant, "Demo reset validation failed before mutation.");
        if (!FixedTimeEquals(BuildPreviewToken(company, run, affected), request.PreviewToken))
            throw new DemoScenarioException(DemoScenarioProblemCodes.ResetPreviewStale, "The reset preview is stale. Preview the exact target again.");

        using var activity = DemoScenarioTelemetry.ActivitySource.StartActivity("demo-scenario.reset");
        activity?.SetTag("company.id", companyId);
        activity?.SetTag("demo.scenario", definition.ScenarioKey);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await RestoreScenarioRecordsAsync(companyId, definition, cancellationToken);
        run.Reset(DateTime.UtcNow);
        await WriteAuditAsync(companyId, userId, AuditEventActions.DemoScenarioReset, run.Id,
            "Reset only the verified synthetic demo tenant to its deterministic starting state.",
            new Dictionary<string, string?>
            {
                ["scenarioKey"] = definition.ScenarioKey,
                ["scenarioVersion"] = definition.ScenarioVersion.ToString(),
                ["resetGeneration"] = run.ResetGeneration.ToString(),
                ["auditHistoryPreserved"] = "true",
                ["externalIntegrationsBlocked"] = "true"
            }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        DemoScenarioTelemetry.Resets.Add(1);
        return await BuildStatusAsync(company, run, cancellationToken, definition);
    }

    public async Task<DemoScenarioCommandResultDto> ExecuteAsync(
        Guid companyId,
        Guid userId,
        ExecuteDemoScenarioCommandRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled(options.Value.Enabled, "Demo scenarios are disabled in this environment.");
        await EnsurePermissionAsync(companyId, userId, cancellationToken);
        var (company, run, definition) = await RequireAsync(companyId, cancellationToken: cancellationToken);
        var commandName = Normalize(request.CommandName, nameof(request.CommandName), 100);
        var idempotencyKey = Normalize(request.IdempotencyKey, nameof(request.IdempotencyKey), 160, lower: false);

        var prior = await dbContext.DemoScenarioCommandExecutions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.RunId == run.Id &&
                x.ResetGeneration == run.ResetGeneration && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (prior is not null)
            return JsonSerializer.Deserialize<DemoScenarioCommandResultDto>(prior.ResultJson, JsonOptions)
                ?? throw new InvalidOperationException("Stored demo command result is invalid.");

        var action = definition.Actions.SingleOrDefault(x => x.CommandName == commandName);
        if (action is null)
        {
            DemoScenarioTelemetry.Rejections.Add(1, new KeyValuePair<string, object?>("reason", "not_allowlisted"));
            await WriteAuditAsync(companyId, userId, AuditEventActions.DemoScenarioCommandRejected, run.Id,
                "Rejected a command that is not present in the scenario allowlist.",
                new Dictionary<string, string?> { ["commandName"] = commandName, ["reasonCode"] = DemoScenarioProblemCodes.CommandNotAllowed }, cancellationToken,
                AuditEventOutcomes.Denied);
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new DemoScenarioException(DemoScenarioProblemCodes.CommandNotAllowed, "Only typed commands in the approved scenario are allowed.");
        }

        if (run.Status != DemoScenarioRunStatus.Running || action.StepNumber != run.CurrentStep + 1)
        {
            DemoScenarioTelemetry.Rejections.Add(1, new KeyValuePair<string, object?>("reason", "out_of_order"));
            throw new DemoScenarioException(DemoScenarioProblemCodes.CommandOutOfOrder, "Run only the current ordered demo step after starting the demo.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var entity = await ExecuteTypedCommandAsync(companyId, userId, definition, action, cancellationToken);
        run.CompleteStep(run.CurrentStep, definition.Actions.Count, DateTime.UtcNow);
        var status = await BuildStatusAsync(company, run, cancellationToken, definition);
        var result = new DemoScenarioCommandResultDto(
            "executed", null, $"{action.DisplayName} completed.", commandName, action.StepNumber,
            action.ExpectedVisibleOutcome, entity.EntityType, entity.EntityId, status);
        var execution = new DemoScenarioCommandExecution(
            DeterministicId(companyId, definition.ScenarioKey, definition.ScenarioVersion, $"execution:{run.ResetGeneration}:{action.StepNumber}"),
            companyId, run.Id, run.ResetGeneration, action.StepNumber, commandName, idempotencyKey,
            userId, "executed", JsonSerializer.Serialize(result, JsonOptions), DateTime.UtcNow);
        dbContext.DemoScenarioCommandExecutions.Add(execution);
        await WriteAuditAsync(companyId, userId, AuditEventActions.DemoScenarioCommandExecuted, entity.EntityId,
            action.ExpectedVisibleOutcome,
            new Dictionary<string, string?>
            {
                ["runId"] = run.Id.ToString("D"),
                ["commandName"] = commandName,
                ["stepNumber"] = action.StepNumber.ToString(),
                ["resetGeneration"] = run.ResetGeneration.ToString(),
                ["idempotencyKey"] = idempotencyKey,
                ["entityType"] = entity.EntityType
            }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        DemoScenarioTelemetry.Commands.Add(1, new KeyValuePair<string, object?>("command", commandName));
        return result;
    }

    private async Task<(string EntityType, Guid EntityId)> ExecuteTypedCommandAsync(
        Guid companyId,
        Guid userId,
        DemoScenarioDefinitionDto definition,
        DemoScenarioActionDto action,
        CancellationToken cancellationToken)
    {
        var leadId = Id(companyId, definition, "lead");
        var dealId = Id(companyId, definition, "deal");
        var contactId = Id(companyId, definition, "contact");
        var customerId = Id(companyId, definition, "customer");
        var lead = await dbContext.Leads.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == leadId && !x.IsDeleted, cancellationToken)
            ?? throw new DemoScenarioException(DemoScenarioProblemCodes.ScenarioMismatch, "The deterministic demo lead is missing. Reset the demo.");

        switch (action.CommandName)
        {
            case QualifyLead:
                lead.Qualify("Strong fit", "hot", "high", "Prepare the operating-platform proposal", userId);
                AddActivity(companyId, definition, action.StepNumber, "qualification", "Demo lead qualified through an allowlisted typed command.", leadId, null, contactId, customerId);
                return ("lead", leadId);
            case ConvertLead:
                if (await dbContext.Deals.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == dealId, cancellationToken))
                    throw new DemoScenarioException(DemoScenarioProblemCodes.CommandOutOfOrder, "The deterministic demo deal already exists.");
                var deal = new Deal(
                    dealId, companyId, definition.InitialState.LeadTitle, SalesPipelineStage.QualifiedStageId,
                    definition.InitialState.EstimatedValue, definition.InitialState.Currency,
                    sourceLeadId: leadId, primaryContactId: contactId, customerCompanyId: customerId,
                    expectedCloseUtc: definition.InitialState.SeedTimestampUtc.AddDays(45),
                    createdUtc: definition.InitialState.SeedTimestampUtc, updatedUtc: definition.InitialState.SeedTimestampUtc);
                lead.ConvertToDeal(dealId);
                dbContext.Deals.Add(deal);
                AddActivity(companyId, definition, action.StepNumber, "conversion", "Demo lead converted through an allowlisted typed command.", leadId, dealId, contactId, customerId);
                return ("deal", dealId);
            case MoveDeal:
                var existingDeal = await dbContext.Deals.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == dealId && !x.IsDeleted, cancellationToken)
                    ?? throw new DemoScenarioException(DemoScenarioProblemCodes.ScenarioMismatch, "The deterministic demo deal is missing.");
                existingDeal.ChangeStage(SalesPipelineStage.ProposalStageId);
                AddActivity(companyId, definition, action.StepNumber, "stage change", "Demo deal moved to Proposal through an allowlisted typed command.", leadId, dealId, contactId, customerId);
                return ("deal", dealId);
            default:
                throw new DemoScenarioException(DemoScenarioProblemCodes.CommandNotAllowed, "The command is not implemented by the typed demo command handler.");
        }
    }

    private void SeedStartingState(Guid companyId, DemoScenarioDefinitionDto definition)
    {
        var state = definition.InitialState;
        var customerId = Id(companyId, definition, "customer");
        var contactId = Id(companyId, definition, "contact");
        dbContext.CustomerCompanies.Add(new CustomerCompany(
            customerId, companyId, state.CustomerCompanyName, website: "https://example.invalid/aurora-kitchens",
            industry: state.CustomerIndustry, createdUtc: state.SeedTimestampUtc, updatedUtc: state.SeedTimestampUtc));
        dbContext.Contacts.Add(new Contact(
            contactId, companyId, state.ContactName, state.ContactEmail, customerId,
            title: state.ContactTitle, createdUtc: state.SeedTimestampUtc, updatedUtc: state.SeedTimestampUtc));
        dbContext.Leads.Add(new Lead(
            Id(companyId, definition, "lead"), companyId, state.LeadTitle, SalesPipelineStage.NewStageId,
            primaryContactId: contactId, customerCompanyId: customerId, estimatedValue: state.EstimatedValue,
            currency: state.Currency, source: "synthetic demo scenario",
            createdUtc: state.SeedTimestampUtc, updatedUtc: state.SeedTimestampUtc));
    }

    private void AddActivity(Guid companyId, DemoScenarioDefinitionDto definition, int step, string type, string summary,
        Guid? leadId, Guid? dealId, Guid? contactId, Guid? customerId) =>
        dbContext.SalesActivities.Add(new SalesActivity(
            Id(companyId, definition, $"activity:{step}"), companyId, type, summary,
            definition.InitialState.SeedTimestampUtc.AddMinutes(step), leadId, dealId, contactId, customerId,
            createdUtc: definition.InitialState.SeedTimestampUtc.AddMinutes(step),
            updatedUtc: definition.InitialState.SeedTimestampUtc.AddMinutes(step)));

    private async Task RestoreScenarioRecordsAsync(Guid companyId, DemoScenarioDefinitionDto definition, CancellationToken cancellationToken)
    {
        var leadId = Id(companyId, definition, "lead");
        var dealId = Id(companyId, definition, "deal");
        var contactId = Id(companyId, definition, "contact");
        var customerId = Id(companyId, definition, "customer");
        var activityIds = definition.Actions.Select(x => Id(companyId, definition, $"activity:{x.StepNumber}")).ToArray();

        var activities = await dbContext.SalesActivities.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && activityIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var deals = await dbContext.Deals.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Id == dealId).ToListAsync(cancellationToken);
        var lead = await dbContext.Leads.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == leadId, cancellationToken);
        var contact = await dbContext.Contacts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == contactId, cancellationToken);
        var customer = await dbContext.CustomerCompanies.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == customerId, cancellationToken);
        EnsureOwned(companyId, activities.Cast<ICompanyOwnedEntity>()
            .Concat(deals)
            .Concat(lead is null ? [] : [lead])
            .Concat(contact is null ? [] : [contact])
            .Concat(customer is null ? [] : [customer]));

        var state = definition.InitialState;
        dbContext.SalesActivities.RemoveRange(activities);
        if (lead is not null)
        {
            lead.RestoreOpenState(state.LeadTitle, SalesPipelineStage.NewStageId, contactId, customerId,
                state.EstimatedValue, state.Currency, "synthetic demo scenario", state.SeedTimestampUtc);
        }
        if (contact is not null)
        {
            contact.RestoreActiveState(state.ContactName, state.ContactEmail, customerId, state.ContactTitle,
                phone: null, preferredLanguage: null, state.SeedTimestampUtc);
        }
        if (customer is not null)
        {
            customer.RestoreActiveState(state.CustomerCompanyName, "https://example.invalid/aurora-kitchens",
                state.CustomerIndustry, state.SeedTimestampUtc);
        }

        // Clear the lead-to-deal relationship before deleting the generated deal. Meeting sessions
        // retain their required references to the restored base records throughout the reset.
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.Deals.RemoveRange(deals);

        if (customer is null)
        {
            dbContext.CustomerCompanies.Add(new CustomerCompany(
                customerId, companyId, state.CustomerCompanyName, website: "https://example.invalid/aurora-kitchens",
                industry: state.CustomerIndustry, createdUtc: state.SeedTimestampUtc, updatedUtc: state.SeedTimestampUtc));
        }
        if (contact is null)
        {
            dbContext.Contacts.Add(new Contact(
                contactId, companyId, state.ContactName, state.ContactEmail, customerId,
                title: state.ContactTitle, createdUtc: state.SeedTimestampUtc, updatedUtc: state.SeedTimestampUtc));
        }
        if (lead is null)
        {
            dbContext.Leads.Add(new Lead(
                leadId, companyId, state.LeadTitle, SalesPipelineStage.NewStageId,
                primaryContactId: contactId, customerCompanyId: customerId, estimatedValue: state.EstimatedValue,
                currency: state.Currency, source: "synthetic demo scenario",
                createdUtc: state.SeedTimestampUtc, updatedUtc: state.SeedTimestampUtc));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DemoScenarioAffectedRecordDto>> CountAffectedAsync(
        Guid companyId, DemoScenarioDefinitionDto definition, CancellationToken cancellationToken)
    {
        var leadId = Id(companyId, definition, "lead");
        var dealId = Id(companyId, definition, "deal");
        var contactId = Id(companyId, definition, "contact");
        var customerId = Id(companyId, definition, "customer");
        var activityIds = definition.Actions.Select(x => Id(companyId, definition, $"activity:{x.StepNumber}")).ToArray();
        return
        [
            new("customer_company", await dbContext.CustomerCompanies.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && x.Id == customerId, cancellationToken), 1),
            new("contact", await dbContext.Contacts.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && x.Id == contactId, cancellationToken), 1),
            new("lead", await dbContext.Leads.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && x.Id == leadId, cancellationToken), 1),
            new("deal", await dbContext.Deals.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && x.Id == dealId, cancellationToken), 0),
            new("sales_activity", await dbContext.SalesActivities.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && activityIds.Contains(x.Id), cancellationToken), 0)
        ];
    }

    private async Task<IReadOnlyList<DemoScenarioValidationDto>> ValidateAsync(
        Company company, DemoScenarioRun run, DemoScenarioDefinitionDto definition, CancellationToken cancellationToken)
    {
        var markerMatches = company.IsDemoTenant &&
            string.Equals(company.DemoScenarioKey, definition.ScenarioKey, StringComparison.Ordinal) &&
            company.DemoScenarioVersion == definition.ScenarioVersion &&
            run.CompanyId == company.Id && run.ScenarioKey == definition.ScenarioKey && run.ScenarioVersion == definition.ScenarioVersion;
        var ids = new[]
        {
            Id(company.Id, definition, "customer"), Id(company.Id, definition, "contact"),
            Id(company.Id, definition, "lead"), Id(company.Id, definition, "deal")
        }.Concat(definition.Actions.Select(x => Id(company.Id, definition, $"activity:{x.StepNumber}"))).ToArray();
        var foreignRecord = await dbContext.CustomerCompanies.IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.Id) && x.CompanyId != company.Id, cancellationToken) ||
            await dbContext.Contacts.IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.Id) && x.CompanyId != company.Id, cancellationToken) ||
            await dbContext.Leads.IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.Id) && x.CompanyId != company.Id, cancellationToken) ||
            await dbContext.Deals.IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.Id) && x.CompanyId != company.Id, cancellationToken) ||
            await dbContext.SalesActivities.IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.Id) && x.CompanyId != company.Id, cancellationToken);
        var meetingMatches = !run.MeetingSessionId.HasValue || await dbContext.SalesMeetingSessions.IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == company.Id && x.Id == run.MeetingSessionId, cancellationToken);
        return
        [
            new("verified_demo_tenant", markerMatches, markerMatches ? "The exact company and scenario version are verified." : "The company demo marker does not match the scenario."),
            new("deterministic_record_scope", !foreignRecord, !foreignRecord ? "All deterministic record identifiers are isolated to this company." : "A deterministic record identifier belongs to another company."),
            new("meeting_scope", meetingMatches, meetingMatches ? "The linked meeting is company-scoped." : "The linked meeting is missing or belongs to another company."),
            new("external_integrations_blocked", true, "All outbox-backed external actions are blocked for demo tenants.")
        ];
    }

    private async Task<DemoScenarioStatusDto> BuildStatusAsync(
        Company company, DemoScenarioRun run, CancellationToken cancellationToken,
        DemoScenarioDefinitionDto? definition = null)
    {
        definition ??= catalog.Get(run.ScenarioKey, run.ScenarioVersion);
        var validations = await ValidateAsync(company, run, definition, cancellationToken);
        var next = run.CurrentStep < definition.Actions.Count ? definition.Actions[run.CurrentStep] : null;
        return new DemoScenarioStatusDto(
            company.Id, company.Name, run.Id, run.ScenarioKey, run.ScenarioVersion, run.MeetingSessionId,
            StatusValue(run.Status), run.CurrentStep, definition.Actions.Count, run.ResetGeneration,
            true, next, definition.Actions, validations, run.UpdatedUtc);
    }

    private async Task<(Company Company, DemoScenarioRun Run, DemoScenarioDefinitionDto Definition)> RequireAsync(
        Guid companyId, string? scenarioKey = null, int? scenarioVersion = null,
        CancellationToken cancellationToken = default)
    {
        EnsureCompany(companyId);
        var pair = await FindAsync(companyId, cancellationToken)
            ?? throw new DemoScenarioException(DemoScenarioProblemCodes.NotDemoTenant, "The exact company is not a registered demo tenant.");
        if (!pair.Company.IsDemoTenant)
            throw new DemoScenarioException(DemoScenarioProblemCodes.NotDemoTenant, "Reset and demo commands require a verified demo tenant.");
        var key = scenarioKey is null ? pair.Run.ScenarioKey : Normalize(scenarioKey, nameof(scenarioKey), 100);
        var version = scenarioVersion ?? pair.Run.ScenarioVersion;
        if (!string.Equals(pair.Company.DemoScenarioKey, key, StringComparison.Ordinal) ||
            pair.Company.DemoScenarioVersion != version ||
            pair.Run.ScenarioKey != key || pair.Run.ScenarioVersion != version)
            throw new DemoScenarioException(DemoScenarioProblemCodes.ScenarioMismatch, "The company, run, scenario key, and version must match exactly.");
        return (pair.Company, pair.Run, catalog.Get(key, version));
    }

    private async Task<(Company Company, DemoScenarioRun Run)?> FindAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);
        if (company is null) return null;
        var run = await dbContext.DemoScenarioRuns.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        return run is null ? null : (company, run);
    }

    private async Task EnsurePermissionAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var allowed = await dbContext.CompanyMemberships.IgnoreQueryFilters().AnyAsync(x =>
            x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active &&
            (x.Role == CompanyMembershipRole.Owner || x.Role == CompanyMembershipRole.Admin || x.Role == CompanyMembershipRole.Manager),
            cancellationToken);
        if (!allowed)
            throw new DemoScenarioException(DemoScenarioProblemCodes.PermissionDenied, "Demo controls require an active owner, admin, or manager membership.");
    }

    private Task WriteAuditAsync(Guid companyId, Guid userId, string action, Guid targetId, string rationale,
        IReadOnlyDictionary<string, string?>? metadata, CancellationToken cancellationToken,
        string outcome = AuditEventOutcomes.Succeeded) => audit.WriteAsync(new AuditEventWriteRequest(
            companyId, "user", userId, action, "demo_scenario_run", targetId.ToString("D"), outcome,
            rationale, ["demo_scenario_specification", "company_scope", "typed_command_policy"], metadata), cancellationToken);

    private static string BuildPreviewToken(Company company, DemoScenarioRun run, IReadOnlyList<DemoScenarioAffectedRecordDto> affected)
    {
        var material = $"{company.Id:N}|{company.Name}|{run.Id:N}|{run.ScenarioKey}|{run.ScenarioVersion}|{run.ResetGeneration}|" +
            string.Join("|", affected.OrderBy(x => x.RecordClass).Select(x => $"{x.RecordClass}:{x.ExistingCount}:{x.StartingCount}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(actual)) return false;
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(actual.Trim().ToLowerInvariant());
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static Guid Id(Guid companyId, DemoScenarioDefinitionDto definition, string logicalKey) =>
        DeterministicId(companyId, definition.ScenarioKey, definition.ScenarioVersion, logicalKey);

    private static Guid DeterministicId(Guid companyId, string scenarioKey, int version, string logicalKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{companyId:N}|{scenarioKey}|{version}|{logicalKey}"));
        Span<byte> guid = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guid);
        return new Guid(guid);
    }

    private static void EnsureOwned(Guid companyId, IEnumerable<ICompanyOwnedEntity> entities)
    {
        if (entities.Any(x => x.CompanyId != companyId))
            throw new DemoScenarioException(DemoScenarioProblemCodes.NotDemoTenant, "A scenario-owned record failed the exact company guard.");
    }

    private static string Normalize(string? value, string field, int maxLength, bool lower = true)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DemoScenarioException(DemoScenarioProblemCodes.InvalidSpecification, $"{field} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new DemoScenarioException(DemoScenarioProblemCodes.InvalidSpecification, $"{field} is too long.");
        return lower ? normalized.ToLowerInvariant() : normalized;
    }

    private static string StatusValue(DemoScenarioRunStatus status) => status switch
    {
        DemoScenarioRunStatus.Ready => "ready",
        DemoScenarioRunStatus.Running => "running",
        DemoScenarioRunStatus.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static void EnsureEnabled(bool enabled, string message)
    {
        if (!enabled) throw new DemoScenarioException(DemoScenarioProblemCodes.Disabled, message);
    }
    private static void EnsureCompany(Guid id) { if (id == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(id)); }
    private static void EnsureUser(Guid id) { if (id == Guid.Empty) throw new ArgumentException("UserId is required.", nameof(id)); }
}
