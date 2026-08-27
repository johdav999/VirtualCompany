using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceScheduleService : ICustomerInvoiceScheduleService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly VirtualCompanyDbContext _db;
    private readonly ICustomerInvoiceScheduleOccurrencePolicy _occurrencePolicy;
    private readonly IAuditEventWriter _audit;
    private readonly CustomerInvoiceScheduleTelemetry _telemetry;
    private readonly TimeProvider _clock;

    public CustomerInvoiceScheduleService(VirtualCompanyDbContext db,
        ICustomerInvoiceScheduleOccurrencePolicy occurrencePolicy, IAuditEventWriter audit,
        CustomerInvoiceScheduleTelemetry telemetry, TimeProvider clock)
    {
        _db = db;
        _occurrencePolicy = occurrencePolicy;
        _audit = audit;
        _telemetry = telemetry;
        _clock = clock;
    }

    public async Task<CustomerInvoiceScheduleDto> CreateAsync(CreateCustomerInvoiceScheduleCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.ActorUserId, command.IdempotencyKey, command.Schedule);
        var templateHash = Hash(command.Schedule);
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            EnsureReplay(replay, templateHash);
            _telemetry.RecordOperation("create", true);
            return await GetAsync(new(command.CompanyId, replay.ScheduleId), cancellationToken);
        }

        await ValidateReferencesAsync(command.CompanyId, command.Schedule, cancellationToken);
        var now = Now();
        CustomerInvoiceSchedule schedule;
        try
        {
            var input = command.Schedule;
            schedule = new CustomerInvoiceSchedule(Guid.NewGuid(), command.CompanyId, input.CustomerId,
                input.Name, input.StartDate, input.EndDate, input.Cadence, input.BillingDay,
                input.TimeZoneId, input.BusinessDayConvention, input.ProrationRule,
                input.DueDateOffsetDays, input.DocumentType, input.Currency, input.PaymentTermKind,
                input.PaymentTermDays, input.BuyerReference, input.SellerReference, input.Notes,
                input.DeliveryIntent, input.AutoIssueEnabled, templateHash, command.ActorUserId, now);
        }
        catch (ArgumentException exception)
        {
            throw InvalidTemplate(exception.Message);
        }

        ApplyTemplate(schedule, command.Schedule);
        _db.CustomerInvoiceSchedules.Add(schedule);
        _db.CustomerInvoiceScheduleOperations.Add(new(Guid.NewGuid(), command.CompanyId, schedule.Id,
            "create", command.IdempotencyKey, templateHash, schedule.Version, now));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            "accounting.customer_invoice_schedule.created", schedule.Id,
            "A recurring native invoice schedule was created as a draft.", command.CorrelationId,
            now, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
            if (replay is null) throw;
            EnsureReplay(replay, templateHash);
            _telemetry.RecordOperation("create", true);
            return await GetAsync(new(command.CompanyId, replay.ScheduleId), cancellationToken);
        }

        _telemetry.RecordOperation("create");
        return await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken);
    }

    public async Task<CustomerInvoiceScheduleDto> UpdateAsync(UpdateCustomerInvoiceScheduleCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.ActorUserId, command.IdempotencyKey, command.Schedule);
        var templateHash = Hash(command.Schedule);
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            EnsureReplay(replay, templateHash);
            _telemetry.RecordOperation("update", true);
            return await GetAsync(new(command.CompanyId, replay.ScheduleId), cancellationToken);
        }

        await ValidateReferencesAsync(command.CompanyId, command.Schedule, cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = await _db.CustomerInvoiceSchedules.Include(x => x.ApprovalRequest)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ScheduleId,
                cancellationToken) ?? throw NotFound();
        EnsureVersion(schedule, command.ExpectedVersion);
        if (schedule.ApprovalRequest is { Status: ApprovalRequestStatus.Pending })
            schedule.ApprovalRequest.MarkCancelled("The recurring invoice template changed and requires new approval.");

        var input = command.Schedule;
        var now = Now();
        try
        {
            schedule.Update(input.CustomerId, input.Name, input.StartDate, input.EndDate, input.Cadence,
                input.BillingDay, input.TimeZoneId, input.BusinessDayConvention, input.ProrationRule,
                input.DueDateOffsetDays, input.DocumentType, input.Currency, input.PaymentTermKind,
                input.PaymentTermDays, input.BuyerReference, input.SellerReference, input.Notes,
                input.DeliveryIntent, input.AutoIssueEnabled, templateHash, command.ActorUserId, now);
        }
        catch (ArgumentException exception)
        {
            throw InvalidTemplate(exception.Message);
        }

        await _db.CustomerInvoiceScheduleLines.Where(x => x.CompanyId == command.CompanyId &&
            x.ScheduleId == schedule.Id).ExecuteDeleteAsync(cancellationToken);
        await _db.CustomerInvoiceScheduleEvidenceLinks.Where(x => x.CompanyId == command.CompanyId &&
            x.ScheduleId == schedule.Id).ExecuteDeleteAsync(cancellationToken);
        ApplyTemplate(schedule, input);
        _db.CustomerInvoiceScheduleOperations.Add(new(Guid.NewGuid(), command.CompanyId, schedule.Id,
            "update", command.IdempotencyKey, templateHash, schedule.Version, now));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            "accounting.customer_invoice_schedule.updated", schedule.Id,
            "The recurring invoice template changed and must be approved before activation.",
            command.CorrelationId, now, cancellationToken);
        await SaveWithConcurrencyAsync(schedule, transaction, cancellationToken);
        _telemetry.RecordOperation("update");
        return await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken);
    }

    public async Task<CustomerInvoiceScheduleSubmissionResult> SubmitAsync(
        SubmitCustomerInvoiceScheduleForApprovalCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.ActorUserId, command.IdempotencyKey, null);
        var schedule = await ReadQuery(true).SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
            x.Id == command.ScheduleId, cancellationToken) ?? throw NotFound();
        EnsureVersion(schedule, command.ExpectedVersion);
        if (schedule.Status == CustomerInvoiceScheduleStatuses.Ended)
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.NotEditable,
                "An ended invoice schedule cannot be approved.");

        var operationHash = Hash($"{schedule.Id:N}:{schedule.TemplateVersion}:{schedule.TemplateHash}:submit");
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            EnsureReplay(replay, operationHash);
            var replaySchedule = await GetAsync(new(command.CompanyId, replay.ScheduleId), cancellationToken);
            var approvalId = replaySchedule.Approval?.Id ?? throw new InvalidOperationException(
                "The recurring invoice schedule approval replay is incomplete.");
            _telemetry.RecordOperation("submit", true);
            return new(replaySchedule, approvalId, true);
        }

        var nextDecision = await _occurrencePolicy.EvaluateAsync(command.CompanyId,
            BuildDraftInput(schedule, schedule.NextOccurrenceDate), cancellationToken);
        if (!nextDecision.IsAllowed)
            throw new CustomerInvoiceScheduleException(nextDecision.ReasonCode, nextDecision.Explanation);

        if (IsCurrentApproval(schedule) && schedule.ApprovalRequest is { Status: ApprovalRequestStatus.Pending or ApprovalRequestStatus.Approved })
        {
            _db.CustomerInvoiceScheduleOperations.Add(new(Guid.NewGuid(), command.CompanyId, schedule.Id,
                "submit", command.IdempotencyKey, operationHash, schedule.Version, Now()));
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
                if (replay is null) throw;
                EnsureReplay(replay, operationHash);
                var replaySchedule = await GetAsync(new(command.CompanyId, replay.ScheduleId), cancellationToken);
                return new(replaySchedule, replaySchedule.Approval!.Id, true);
            }
            _telemetry.RecordOperation("submit", true);
            return new(Map(schedule), schedule.ApprovalRequest.Id, true);
        }

        if (schedule.ApprovalRequest is { Status: ApprovalRequestStatus.Pending })
            schedule.ApprovalRequest.MarkCancelled("Superseded by a new recurring invoice schedule approval request.");
        var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), command.CompanyId,
            ApprovalTargetEntityType.CustomerInvoiceSchedule, schedule.Id, AuditActorTypes.User,
            command.ActorUserId, "customer_invoice_schedule_activation",
            new Dictionary<string, JsonNode?>
            {
                ["templateVersion"] = JsonValue.Create(schedule.TemplateVersion),
                ["templateHash"] = JsonValue.Create(schedule.TemplateHash),
                ["customerId"] = JsonValue.Create(schedule.CustomerId),
                ["autoIssueEnabled"] = JsonValue.Create(schedule.AutoIssueEnabled)
            }, null, null, [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "finance_approver")]);
        _db.ApprovalRequests.Add(approval);
        var now = Now();
        schedule.BindApproval(approval.Id, command.ActorUserId, now);
        _db.CustomerInvoiceScheduleOperations.Add(new(Guid.NewGuid(), command.CompanyId, schedule.Id,
            "submit", command.IdempotencyKey, operationHash, schedule.Version, now));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            "accounting.customer_invoice_schedule.approval_requested", schedule.Id,
            "Approval was requested for the exact recurring invoice template version.",
            command.CorrelationId, now, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.VersionConflict,
                "This invoice schedule changed elsewhere. Reload it before continuing.", true,
                schedule.Version);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
            if (replay is null) throw;
            EnsureReplay(replay, operationHash);
            var replaySchedule = await GetAsync(new(command.CompanyId, replay.ScheduleId), cancellationToken);
            return new(replaySchedule, replaySchedule.Approval!.Id, true);
        }
        _telemetry.RecordOperation("submit");
        return new(await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken), approval.Id, false);
    }

    public Task<CustomerInvoiceScheduleDto> ActivateAsync(CustomerInvoiceScheduleActionCommand command,
        CancellationToken cancellationToken) => ChangeStateAsync(command, "activate", async schedule =>
    {
        EnsureCurrentApproved(schedule);
        var decision = await _occurrencePolicy.EvaluateAsync(command.CompanyId,
            BuildDraftInput(schedule, schedule.NextOccurrenceDate), cancellationToken);
        if (!decision.IsAllowed)
            throw new CustomerInvoiceScheduleException(decision.ReasonCode, decision.Explanation);
        schedule.Activate(command.ActorUserId, Now(), LocalDate(schedule));
    }, cancellationToken);

    public Task<CustomerInvoiceScheduleDto> PauseAsync(CustomerInvoiceScheduleActionCommand command,
        CancellationToken cancellationToken) => ChangeStateAsync(command, "pause", schedule =>
    {
        schedule.Pause(command.ActorUserId, Now());
        return Task.CompletedTask;
    }, cancellationToken);

    public Task<CustomerInvoiceScheduleDto> ResumeAsync(CustomerInvoiceScheduleActionCommand command,
        CancellationToken cancellationToken) => ChangeStateAsync(command, "resume", schedule =>
    {
        EnsureCurrentApproved(schedule);
        schedule.Resume(command.ActorUserId, Now(), LocalDate(schedule), command.AllowBackdatedGeneration);
        var blocked = schedule.Occurrences.SingleOrDefault(x =>
            x.OccurrenceDate == schedule.NextOccurrenceDate &&
            x.Status is CustomerInvoiceScheduleOccurrenceStatuses.Blocked or CustomerInvoiceScheduleOccurrenceStatuses.Failed);
        if (blocked is not null && !command.RetryBlockedOccurrence)
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.OccurrenceBlocked,
                "This occurrence is blocked. Resume it with an explicit retry after correcting the underlying facts.");
        blocked?.ResetBlockedForRetry(schedule.Version, schedule.TemplateVersion, schedule.TemplateHash,
            schedule.NextOccurrenceDate, schedule.DueDateFor(schedule.NextOccurrenceDate), Now());
        return Task.CompletedTask;
    }, cancellationToken);

    public Task<CustomerInvoiceScheduleDto> EndAsync(CustomerInvoiceScheduleActionCommand command,
        CancellationToken cancellationToken) => ChangeStateAsync(command, "end", schedule =>
    {
        schedule.End(command.ActorUserId, Now());
        return Task.CompletedTask;
    }, cancellationToken);

    public async Task<CustomerInvoiceScheduleDto> GetAsync(GetCustomerInvoiceScheduleQuery query,
        CancellationToken cancellationToken)
    {
        var schedule = await ReadQuery().SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId &&
            x.Id == query.ScheduleId, cancellationToken) ?? throw NotFound();
        return Map(schedule);
    }

    public async Task<CustomerInvoiceScheduleListResult> ListAsync(ListCustomerInvoiceSchedulesQuery query,
        CancellationToken cancellationToken)
    {
        var source = ReadQuery().Where(x => x.CompanyId == query.CompanyId);
        if (!string.IsNullOrWhiteSpace(query.Status))
            source = source.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());
        if (query.CustomerId.HasValue) source = source.Where(x => x.CustomerId == query.CustomerId.Value);
        var total = await source.CountAsync(cancellationToken);
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 200);
        var items = await source.OrderBy(x => x.NextOccurrenceDate).ThenBy(x => x.Name)
            .Skip(skip).Take(take).ToListAsync(cancellationToken);
        return new(items.Select(Map).ToArray(), total, skip, take);
    }

    public async Task<CustomerInvoiceSchedulePreviewDto> PreviewAsync(PreviewCustomerInvoiceScheduleQuery query,
        CancellationToken cancellationToken)
    {
        var schedule = await ReadQuery().SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId &&
            x.Id == query.ScheduleId, cancellationToken) ?? throw NotFound();
        var count = Math.Clamp(query.Count, 1, 24);
        var occurrences = new List<CustomerInvoiceSchedulePreviewOccurrenceDto>(count);
        var occurrenceDate = schedule.NextOccurrenceDate;
        for (var index = 0; index < count && (!schedule.EndDate.HasValue || occurrenceDate <= schedule.EndDate.Value); index++)
        {
            var input = BuildDraftInput(schedule, occurrenceDate);
            var decision = await _occurrencePolicy.EvaluateAsync(query.CompanyId, input, cancellationToken);
            var factor = schedule.ProrationFactorFor(occurrenceDate);
            occurrences.Add(new(occurrenceDate, input.IssueDate, input.DueDate, input.SupplyDate,
                RuleExplanation(schedule, factor), decision.NetTotal, decision.TaxTotal, decision.GrossTotal,
                decision.Currency, decision.Warnings, decision.Blockers));
            occurrenceDate = schedule.NextOccurrenceAfter(occurrenceDate);
        }
        _telemetry.RecordOperation("preview");
        return new(schedule.Id, schedule.Version, schedule.TemplateVersion, schedule.TemplateHash, occurrences);
    }

    private async Task<CustomerInvoiceScheduleDto> ChangeStateAsync(CustomerInvoiceScheduleActionCommand command,
        string action, Func<CustomerInvoiceSchedule, Task> transition, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.ActorUserId, command.IdempotencyKey, null);
        var operationHash = Hash($"{command.ScheduleId:N}:{command.ExpectedVersion}:{action}:" +
            $"{command.AllowBackdatedGeneration}:{command.RetryBlockedOccurrence}");
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            EnsureReplay(replay, operationHash);
            _telemetry.RecordOperation(action, true);
            return await GetAsync(new(command.CompanyId, replay.ScheduleId), cancellationToken);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = await WriteQuery().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
            x.Id == command.ScheduleId, cancellationToken) ?? throw NotFound();
        EnsureVersion(schedule, command.ExpectedVersion);
        await transition(schedule);
        _db.CustomerInvoiceScheduleOperations.Add(new(Guid.NewGuid(), command.CompanyId, schedule.Id,
            action, command.IdempotencyKey, operationHash, schedule.Version, Now()));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            $"accounting.customer_invoice_schedule.{action}", schedule.Id,
            $"The recurring native invoice schedule completed the {action} action.",
            command.CorrelationId, Now(), cancellationToken);
        await SaveWithConcurrencyAsync(schedule, transaction, cancellationToken);
        _telemetry.RecordOperation(action);
        return await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken);
    }

    internal static CustomerInvoiceDraftInput BuildDraftInput(CustomerInvoiceSchedule schedule,
        DateOnly occurrenceDate)
    {
        var factor = schedule.ProrationFactorFor(occurrenceDate);
        return new(schedule.CustomerId, schedule.DocumentType, occurrenceDate, occurrenceDate,
            schedule.DueDateFor(occurrenceDate), schedule.Currency, schedule.PaymentTermKind,
            schedule.PaymentTermDays, schedule.BuyerReference, schedule.SellerReference,
            schedule.Notes, schedule.DeliveryIntent, CustomerInvoiceDraftSourceKinds.RecurringSchedule,
            $"{schedule.Id:N}:{occurrenceDate:yyyyMMdd}:{schedule.TemplateVersion}:{schedule.TemplateHash}",
            schedule.Lines.OrderBy(x => x.Sequence).Select(x => new CustomerInvoiceDraftLineInput(
                x.Sequence, x.Description, x.Quantity, x.Unit,
                decimal.Round(x.UnitPrice * factor, 6, MidpointRounding.AwayFromZero),
                x.DiscountPercent, x.TaxRuleKey, x.TaxClassification,
                Deserialize<IReadOnlyList<CustomerInvoiceDraftTaxEvidenceInput>>(x.TaxEvidenceJson) ?? [],
                Deserialize<IReadOnlyDictionary<string, string>>(x.DimensionFactsJson),
                x.RevenueAccountRoleKey, x.SourceReference, x.OrderReference)).ToArray(),
            schedule.EvidenceLinks.Select(x => x.DocumentId).OrderBy(x => x).ToArray());
    }

    private IQueryable<CustomerInvoiceSchedule> ReadQuery(bool tracking = false)
    {
        var query = _db.CustomerInvoiceSchedules.Include(x => x.Customer).Include(x => x.Lines)
            .Include(x => x.EvidenceLinks)
            .Include(x => x.Occurrences.OrderByDescending(o => o.OccurrenceDate).Take(24))
            .Include(x => x.ApprovalRequest);
        return tracking ? query : query.AsNoTracking();
    }

    private IQueryable<CustomerInvoiceSchedule> WriteQuery() => _db.CustomerInvoiceSchedules
        .Include(x => x.Lines).Include(x => x.EvidenceLinks).Include(x => x.Occurrences)
        .Include(x => x.ApprovalRequest);

    private static CustomerInvoiceScheduleDto Map(CustomerInvoiceSchedule schedule)
    {
        var approval = schedule.ApprovalRequest is null ? null : new CustomerInvoiceScheduleApprovalDto(
            schedule.ApprovalRequest.Id, schedule.ApprovalRequest.Status.ToStorageValue(),
            schedule.ApprovalTemplateVersion ?? 0, schedule.ApprovalTemplateHash ?? string.Empty,
            schedule.ApprovalRequest.DecisionSummary, schedule.ApprovalRequest.CreatedUtc,
            schedule.ApprovalRequest.DecidedUtc, IsCurrentApproval(schedule));
        return new(schedule.Id, schedule.CompanyId, schedule.CustomerId, schedule.Customer.Name,
            schedule.Name, schedule.Status, schedule.StartDate, schedule.EndDate, schedule.Cadence,
            schedule.BillingDay, schedule.TimeZoneId, schedule.BusinessDayConvention,
            schedule.ProrationRule, schedule.DueDateOffsetDays, schedule.DocumentType,
            schedule.Currency, schedule.PaymentTermKind, schedule.PaymentTermDays,
            schedule.BuyerReference, schedule.SellerReference, schedule.Notes,
            schedule.DeliveryIntent, schedule.AutoIssueEnabled, schedule.TemplateHash,
            schedule.TemplateVersion, schedule.Version, schedule.NextOccurrenceDate,
            schedule.CreatedUtc, schedule.UpdatedUtc,
            schedule.Lines.OrderBy(x => x.Sequence).Select(x => new CustomerInvoiceScheduleLineDto(
                x.Sequence, x.Description, x.Quantity, x.Unit, x.UnitPrice, x.DiscountPercent,
                x.TaxRuleKey, x.TaxClassification,
                Deserialize<IReadOnlyList<CustomerInvoiceDraftTaxEvidenceInput>>(x.TaxEvidenceJson) ?? [],
                Deserialize<IReadOnlyDictionary<string, string>>(x.DimensionFactsJson) ??
                    new Dictionary<string, string>(), x.RevenueAccountRoleKey, x.SourceReference,
                x.OrderReference)).ToArray(),
            schedule.EvidenceLinks.Select(x => x.DocumentId).OrderBy(x => x).ToArray(),
            schedule.Occurrences.OrderByDescending(x => x.OccurrenceDate).Take(24).Select(x =>
                new CustomerInvoiceScheduleOccurrenceDto(x.Id, x.OccurrenceDate, x.IssueDate,
                    x.DueDate, x.ScheduleVersion, x.TemplateVersion, x.TemplateHash, x.Version,
                    x.Status, x.DraftId, x.TaskId, x.AttemptCount, x.FailureCode,
                    x.FailureSummary, x.LeaseExpiresUtc, x.NextAttemptUtc, x.CreatedUtc,
                    x.UpdatedUtc)).ToArray(), approval);
    }

    private static bool IsCurrentApproval(CustomerInvoiceSchedule schedule) =>
        schedule.ApprovalRequest is not null &&
        schedule.ApprovalRequest.TargetEntityType == ApprovalTargetEntityType.CustomerInvoiceSchedule.ToStorageValue() &&
        schedule.ApprovalRequest.TargetEntityId == schedule.Id &&
        schedule.ApprovalTemplateVersion == schedule.TemplateVersion &&
        string.Equals(schedule.ApprovalTemplateHash, schedule.TemplateHash, StringComparison.OrdinalIgnoreCase);

    private static void EnsureCurrentApproved(CustomerInvoiceSchedule schedule)
    {
        if (schedule.ApprovalRequest is null)
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.ApprovalRequired,
                "Approve the current recurring invoice template before activation.");
        if (!IsCurrentApproval(schedule))
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.ApprovalStale,
                "The recurring invoice template changed after approval.");
        if (schedule.ApprovalRequest.Status == ApprovalRequestStatus.Pending)
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.ApprovalPending,
                "The recurring invoice template is waiting for approval.");
        if (schedule.ApprovalRequest.Status != ApprovalRequestStatus.Approved)
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.ApprovalRejected,
                "The recurring invoice template does not have current approval.");
    }

    private static void ApplyTemplate(CustomerInvoiceSchedule schedule, CustomerInvoiceScheduleInput input)
    {
        foreach (var line in input.Lines.OrderBy(x => x.Sequence))
            schedule.Lines.Add(new(Guid.NewGuid(), schedule.CompanyId, schedule.Id, line.Sequence,
                line.Description, line.Quantity, line.Unit, line.UnitPrice, line.DiscountPercent,
                line.TaxRuleKey, line.TaxClassification,
                JsonSerializer.Serialize(line.TaxEvidence, JsonOptions),
                JsonSerializer.Serialize(line.DimensionFacts ?? new Dictionary<string, string>(), JsonOptions),
                line.RevenueAccountRoleKey, line.SourceReference, line.OrderReference));
        foreach (var documentId in input.EvidenceDocumentIds.Distinct())
            schedule.EvidenceLinks.Add(new(Guid.NewGuid(), schedule.CompanyId, schedule.Id, documentId));
    }

    private async Task ValidateReferencesAsync(Guid companyId, CustomerInvoiceScheduleInput input,
        CancellationToken cancellationToken)
    {
        if (!await _db.FinanceCounterparties.AnyAsync(x => x.CompanyId == companyId &&
            x.Id == input.CustomerId && x.CounterpartyType == "customer" &&
            x.MergedIntoCounterpartyId == null, cancellationToken))
            throw InvalidTemplate("Select an active customer.");
        var ids = input.EvidenceDocumentIds.Distinct().ToArray();
        if (ids.Length != await _db.CompanyKnowledgeDocuments.Where(x => x.CompanyId == companyId &&
            ids.Contains(x.Id)).CountAsync(cancellationToken))
            throw InvalidTemplate("One or more schedule evidence documents could not be found.");
    }

    private async Task<CustomerInvoiceScheduleOperation?> FindOperationAsync(Guid companyId, string key,
        CancellationToken cancellationToken) => await _db.CustomerInvoiceScheduleOperations.AsNoTracking()
        .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == key.Trim(),
            cancellationToken);

    private async Task SaveWithConcurrencyAsync(CustomerInvoiceSchedule schedule,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.VersionConflict,
                "This invoice schedule changed elsewhere. Reload it before continuing.", true,
                schedule.Version);
        }
    }

    private async Task WriteAuditAsync(Guid companyId, Guid actorId, string action, Guid id,
        string summary, string? correlationId, DateTime now, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new(companyId, AuditActorTypes.User, actorId, action,
            "customer_invoice_schedule", id.ToString("N"), AuditEventOutcomes.Succeeded, summary,
            ["customer_invoice_schedule"], new Dictionary<string, string?>(), correlationId, now),
            cancellationToken);

    private static void Validate(Guid companyId, Guid actorId, string key,
        CustomerInvoiceScheduleInput? input)
    {
        if (companyId == Guid.Empty || actorId == Guid.Empty) throw NotFound();
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 200)
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.IdempotencyConflict,
                "A stable request identity is required.");
        if (input is not null && input.AutoIssueEnabled)
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.AutoIssueNotPermitted,
                "Automatic issue is unavailable until a company policy, low-risk limit, accounting authority, and per-draft approval path are configured.");
        if (input is not null && (input.Lines is null || input.Lines.Count == 0 ||
            input.Lines.Select(x => x.Sequence).Distinct().Count() != input.Lines.Count))
            throw InvalidTemplate("The schedule needs uniquely sequenced invoice lines.");
    }

    private static void EnsureVersion(CustomerInvoiceSchedule schedule, long expected)
    {
        if (schedule.Version != expected)
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.VersionConflict,
                "This invoice schedule changed elsewhere. Reload it before continuing.", true,
                schedule.Version);
    }

    private static void EnsureReplay(CustomerInvoiceScheduleOperation operation, string hash)
    {
        if (!string.Equals(operation.PayloadHash, hash, StringComparison.OrdinalIgnoreCase))
            throw new CustomerInvoiceScheduleException(CustomerInvoiceScheduleReasonCodes.IdempotencyConflict,
                "This request identity was already used with different schedule content.", true);
    }

    private static CustomerInvoiceScheduleException InvalidTemplate(string message) =>
        new(CustomerInvoiceScheduleReasonCodes.InvalidTemplate, message);
    private static CustomerInvoiceScheduleException NotFound() =>
        new(CustomerInvoiceScheduleReasonCodes.NotFound,
            "The customer invoice schedule could not be found.");
    private static string Hash(CustomerInvoiceScheduleInput input) => Hash(JsonSerializer.Serialize(new
    {
        input.CustomerId,
        Name = input.Name?.Trim() ?? string.Empty,
        input.StartDate,
        input.EndDate,
        Cadence = input.Cadence?.Trim().ToLowerInvariant() ?? string.Empty,
        input.BillingDay,
        TimeZoneId = input.TimeZoneId?.Trim() ?? string.Empty,
        BusinessDayConvention = input.BusinessDayConvention?.Trim().ToLowerInvariant() ?? string.Empty,
        ProrationRule = input.ProrationRule?.Trim().ToLowerInvariant() ?? string.Empty,
        input.DueDateOffsetDays,
        DocumentType = input.DocumentType?.Trim().ToLowerInvariant() ?? string.Empty,
        Currency = input.Currency?.Trim().ToUpperInvariant() ?? string.Empty,
        PaymentTermKind = input.PaymentTermKind?.Trim().ToLowerInvariant() ?? string.Empty,
        input.PaymentTermDays,
        BuyerReference = input.BuyerReference?.Trim(),
        SellerReference = input.SellerReference?.Trim(),
        Notes = input.Notes?.Trim(),
        DeliveryIntent = input.DeliveryIntent?.Trim().ToLowerInvariant() ?? string.Empty,
        input.AutoIssueEnabled,
        Lines = input.Lines.OrderBy(x => x.Sequence).Select(x => new
        {
            x.Sequence,
            Description = x.Description?.Trim() ?? string.Empty,
            x.Quantity,
            Unit = x.Unit?.Trim() ?? string.Empty,
            x.UnitPrice,
            x.DiscountPercent,
            TaxRuleKey = x.TaxRuleKey?.Trim() ?? string.Empty,
            TaxClassification = x.TaxClassification?.Trim() ?? string.Empty,
            TaxEvidence = (x.TaxEvidence ?? []).OrderBy(y => y.Classification, StringComparer.Ordinal)
                .ThenBy(y => y.SourceReference, StringComparer.Ordinal).ToArray(),
            DimensionFacts = (x.DimensionFacts ?? new Dictionary<string, string>())
                .OrderBy(y => y.Key, StringComparer.Ordinal).ToArray(),
            x.RevenueAccountRoleKey,
            x.SourceReference,
            x.OrderReference
        }).ToArray(),
        EvidenceDocumentIds = (input.EvidenceDocumentIds ?? []).Distinct().OrderBy(x => x).ToArray()
    }, JsonOptions));
    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, JsonOptions);
    private static string RuleExplanation(CustomerInvoiceSchedule schedule, decimal factor) =>
        $"{schedule.Cadence} on day {schedule.BillingDay}, adjusted {schedule.BusinessDayConvention}; " +
        $"due {schedule.DueDateOffsetDays} days later." +
        (factor == 1m ? string.Empty : $" The first period is prorated daily at {factor:P2}.");
    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private DateOnly LocalDate(CustomerInvoiceSchedule schedule) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(Now(), DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId)).Date);
}
