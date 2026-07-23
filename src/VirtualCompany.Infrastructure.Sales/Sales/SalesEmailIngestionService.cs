using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Events;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesEmailIngestionService : ISalesEmailIngestionService
{
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private const decimal MinimumLeadConfidence = 0.65m;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IFieldEncryptionService _fieldEncryption;
    private readonly ICompanyOutboxEnqueuer _outbox;
    private readonly ISalesEmailIntentExtractionService _intentExtraction;
    private readonly IReplySignalDetectionPipeline _replySignalDetection;
    private readonly TimeProvider _timeProvider;
    private readonly ISalesSourceService _sources;

    public SalesEmailIngestionService(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService fieldEncryption,
        ICompanyOutboxEnqueuer outbox,
        ISalesEmailIntentExtractionService intentExtraction,
        IReplySignalDetectionPipeline replySignalDetection,
        TimeProvider timeProvider,
        ISalesSourceService sources)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _fieldEncryption = fieldEncryption;
        _outbox = outbox;
        _intentExtraction = intentExtraction;
        _replySignalDetection = replySignalDetection;
        _timeProvider = timeProvider;
        _sources = sources;
    }

    public async Task<SalesEmailIngestionResult> ProcessMessageAsync(
        ProcessSalesEmailMessageCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.UserId, command.MailboxConnectionId, command.ProviderMessageId, nameof(command.ProviderMessageId));
        var connection = await LoadConnectionAsync(command.CompanyId, command.UserId, command.MailboxConnectionId, cancellationToken);
        var existing = await FindMessageLinkAsync(command.CompanyId, connection, command.ProviderMessageId, cancellationToken);
        if (existing is not null)
        {
            return ToAlreadyProcessedResult(existing, connection);
        }

        var accessToken = DecryptAccessToken(connection);
        var message = await _providerRegistry.Resolve(connection.Provider).GetMessageAsync(
            accessToken,
            new MailboxMessageFetchRequest(command.ProviderMessageId),
            cancellationToken);

        return await ProcessMessagesAsync(command.CompanyId, connection, [message], command.ProviderMessageId, message.ProviderThreadId, cancellationToken);
    }

    public async Task<SalesEmailIngestionResult> ProcessThreadAsync(
        ProcessSalesEmailThreadCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.UserId, command.MailboxConnectionId, command.ProviderThreadId, nameof(command.ProviderThreadId));
        var connection = await LoadConnectionAsync(command.CompanyId, command.UserId, command.MailboxConnectionId, cancellationToken);
        var existingThreadLink = await FindThreadLinkAsync(command.CompanyId, connection, command.ProviderThreadId, cancellationToken);
        if (existingThreadLink is not null)
        {
            return ToAlreadyProcessedResult(existingThreadLink, connection);
        }

        var accessToken = DecryptAccessToken(connection);
        var thread = await _providerRegistry.Resolve(connection.Provider).GetThreadAsync(
            accessToken,
            new MailboxThreadFetchRequest(command.ProviderThreadId),
            cancellationToken);

        if (thread.Messages.Count == 0)
        {
            var ignored = CreateIgnoredLink(command.CompanyId, connection, command.ProviderThreadId, null, null, command.ProviderThreadId, SalesEmailIgnoreReasons.NoSalesIntent, "No inbound messages were returned for this thread.", SalesEmailLinkKinds.Thread);
            _dbContext.SalesEmailLinks.Add(ignored);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ToIgnoredResult(ignored, connection);
        }

        return await ProcessMessagesAsync(command.CompanyId, connection, thread.Messages, thread.Messages.Last().ProviderMessageId, thread.ProviderThreadId, cancellationToken);
    }

    private async Task<SalesEmailIngestionResult> ProcessMessagesAsync(
        Guid companyId,
        MailboxConnection connection,
        IReadOnlyList<MailboxInboundMessage> messages,
        string providerMessageId,
        string? providerThreadId,
        CancellationToken cancellationToken)
    {
        var threadIdentity = ResolveThreadIdentity(connection, messages, providerThreadId);
        var existingLeadLink = await FindLeadThreadLinkAsync(companyId, connection, threadIdentity, cancellationToken);
        var primaryMessage = messages.Last();
        var detection = await DetectSalesSignalAsync(companyId, connection, messages, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (detection.IgnoreReason is not null)
        {
            var ignored = CreateIgnoredLink(companyId, connection, providerMessageId, primaryMessage.InternetMessageId, primaryMessage.ProviderThreadId, threadIdentity, detection.IgnoreReason, detection.Rationale, SalesEmailLinkKinds.Message);
            _dbContext.SalesEmailLinks.Add(ignored);
            if (!string.IsNullOrWhiteSpace(threadIdentity) &&
                !await HasThreadLinkAsync(companyId, connection, threadIdentity, cancellationToken))
            {
                _dbContext.SalesEmailLinks.Add(CreateIgnoredLink(companyId, connection, threadIdentity, null, null, threadIdentity, detection.IgnoreReason, detection.Rationale, SalesEmailLinkKinds.Thread));
            }

            EnqueueEmailReceived(companyId, connection, primaryMessage, null, null, detection, threadIdentity);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToIgnoredResult(ignored, connection);
        }

        var signal = detection.Signal ?? throw new InvalidOperationException("Sales detection did not return a signal.");
        var customerCompany = await UpsertCustomerCompanyAsync(companyId, signal, cancellationToken);
        var contact = await UpsertContactAsync(companyId, signal, customerCompany?.Id, cancellationToken);
        var lead = existingLeadLink?.LeadId is { } existingLeadId
            ? await _dbContext.Leads.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId && x.Id == existingLeadId && !x.IsDeleted, cancellationToken)
            : await FindExistingLeadForSignalAsync(companyId, contact?.Id, customerCompany?.Id, signal, cancellationToken)
                ?? await CreateLeadAsync(companyId, signal, contact?.Id, customerCompany?.Id, cancellationToken);

        lead.ApplyEmailSignal(BuildLeadTitle(signal), contact?.Id, customerCompany?.Id, signal.Confidence, "sales email");
        await _sources.StageAsync(companyId, new RecordSalesSourceTouchRequest("lead", lead.Id,
            SalesSourceCategories.Email, connection.Provider.ToStorageValue(), "email", "inquiry",
            providerMessageId, _timeProvider.GetUtcNow().UtcDateTime, "visitor", signal.SenderEmail,
            Evidence: $"Inbound sales email classified as {signal.Intent} with confidence {signal.Confidence:0.00}.",
            MetadataJson: System.Text.Json.JsonSerializer.Serialize(new { signal.Intent, signal.Confidence, threadIdentity }),
            IsConversion: true), cancellationToken);

        var existingActivityId = await FindDetectionActivityIdAsync(companyId, lead.Id, providerMessageId, cancellationToken);
        Guid? activityId = existingActivityId;

        if (existingActivityId is null)
        {
            activityId = Guid.NewGuid();
            _dbContext.SalesActivities.Add(new SalesActivity(
                activityId.Value,
                companyId,
                "email",
                $"Inbound sales email {providerMessageId} from {signal.SenderEmail}: {signal.Intent}",
                primaryMessage.ReceivedUtc ?? _timeProvider.GetUtcNow().UtcDateTime,
                lead.Id,
                contactId: contact?.Id,
                customerCompanyId: customerCompany?.Id));
        }

        if (!await HasMessageLinkAsync(companyId, connection, providerMessageId, cancellationToken))
        {
            _dbContext.SalesEmailLinks.Add(new SalesEmailLink(
                Guid.NewGuid(),
                companyId,
                providerMessageId,
                lead.Id,
                contactId: contact?.Id,
                customerCompanyId: customerCompany?.Id,
                provider: connection.Provider.ToStorageValue(),
                mailboxConnectionId: connection.Id,
                externalThreadId: threadIdentity,
                internetMessageId: primaryMessage.InternetMessageId,
                linkKind: SalesEmailLinkKinds.Message,
                rationale: detection.Rationale,
                detectedIntent: signal.Intent,
                productOrServiceInterest: signal.ProductOrServiceInterest,
                confidence: signal.Confidence));
        }

        if (!string.IsNullOrWhiteSpace(threadIdentity) &&
            !await HasThreadLinkAsync(companyId, connection, threadIdentity, cancellationToken))
        {
            _dbContext.SalesEmailLinks.Add(new SalesEmailLink(
                Guid.NewGuid(),
                companyId,
                threadIdentity,
                lead.Id,
                contactId: contact?.Id,
                customerCompanyId: customerCompany?.Id,
                provider: connection.Provider.ToStorageValue(),
                mailboxConnectionId: connection.Id,
                externalThreadId: threadIdentity,
                linkKind: SalesEmailLinkKinds.Thread,
                rationale: detection.Rationale,
                detectedIntent: signal.Intent,
                productOrServiceInterest: signal.ProductOrServiceInterest,
                confidence: signal.Confidence));
        }

        EnqueueEmailReceived(companyId, connection, primaryMessage, lead.Id, contact?.Id, detection, threadIdentity);
        EnqueueLeadDetected(companyId, connection, primaryMessage, lead.Id, signal, threadIdentity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _replySignalDetection.AnalyzeInboundReplyAsync(
            new AnalyzeInboundReplySignalsCommand(
                companyId,
                connection.Provider.ToStorageValue(),
                providerMessageId,
                threadIdentity,
                primaryMessage.InternetMessageId,
                primaryMessage.Subject,
                FirstNonEmpty(primaryMessage.PlainTextBody, primaryMessage.HtmlBody),
                signal.SenderEmail,
                primaryMessage.ReceivedUtc),
            cancellationToken);

        return new SalesEmailIngestionResult(
            SalesEmailIngestionStatuses.Processed,
            false,
            lead.Id,
            contact?.Id,
            activityId,
            customerCompany?.Id,
            connection.Id,
            connection.Provider.ToStorageValue(),
            providerMessageId,
            threadIdentity,
            primaryMessage.InternetMessageId,
            null,
            detection.Rationale,
            signal);
    }

    private async Task<MailboxConnection> LoadConnectionAsync(Guid companyId, Guid userId, Guid mailboxConnectionId, CancellationToken cancellationToken)
    {
        var connection = await _dbContext.MailboxConnections
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                    x.UserId == userId &&
                    x.Purpose == MailboxPurpose.Sales &&
                    x.Id == mailboxConnectionId,
                cancellationToken);

        if (connection is null)
        {
            throw new InvalidOperationException("Mailbox connection was not found for this company.");
        }

        if (connection.Status != MailboxConnectionStatus.Active)
        {
            throw new InvalidOperationException("Mailbox connection is not active.");
        }

        return connection;
    }

    private string DecryptAccessToken(MailboxConnection connection) =>
        connection.Provider == MailboxProvider.StandardEmail
            ? StandardMailboxSessionCodec.Create(connection, _fieldEncryption)
            : _fieldEncryption.Decrypt(
                connection.CompanyId,
                MailboxConnectionDefaults.TokenPurpose(connection.Provider, "access_token"),
                connection.EncryptedAccessToken ?? throw new InvalidOperationException("Mailbox access token is missing."));

    private async Task<DetectionOutcome> DetectSalesSignalAsync(
        Guid companyId,
        MailboxConnection connection,
        IReadOnlyList<MailboxInboundMessage> messages,
        CancellationToken cancellationToken)
    {
        var extracted = await _intentExtraction.ExtractAsync(
            new SalesEmailIntentExtractionRequest(companyId, connection.Provider.ToStorageValue(), connection.Id, messages),
            cancellationToken);

        var detection = extracted is null
            ? DetectFallbackSalesSignal(messages)
            : DetectionOutcome.FromExtraction(extracted);

        if (detection.Signal is { Confidence: < MinimumLeadConfidence } signal)
        {
            return DetectionOutcome.Ignored(
                SalesEmailIgnoreReasons.InsufficientSignal,
                $"The message had possible sales intent but confidence was too low to create a lead ({signal.Confidence:0.00}).");
        }

        return detection;
    }

    private static DetectionOutcome DetectFallbackSalesSignal(IReadOnlyList<MailboxInboundMessage> messages)
    {
        var latest = messages.Last();
        var body = NormalizeBody(string.Join("\n\n", messages.Select(x => $"{x.Subject}\n{x.PlainTextBody ?? StripHtml(x.HtmlBody)}")));
        var senderEmail = latest.Sender.Email;

        if (string.IsNullOrWhiteSpace(senderEmail))
        {
            return DetectionOutcome.Ignored(SalesEmailIgnoreReasons.InsufficientSignal, "The message has no sender address to create a lead from.");
        }

        if (ContainsAny(body, "unsubscribe", "newsletter", "weekly digest", "view in browser"))
        {
            return DetectionOutcome.Ignored(SalesEmailIgnoreReasons.Newsletter, "The message looks like a newsletter or automated campaign.");
        }

        if (ContainsAny(body, "receipt", "payment received", "order confirmation", "your purchase"))
        {
            return DetectionOutcome.Ignored(SalesEmailIgnoreReasons.Receipt, "The message looks like a receipt or order confirmation.");
        }

        if (ContainsAny(body, "invoice", "amount due", "payment due", "faktura"))
        {
            return DetectionOutcome.Ignored(SalesEmailIgnoreReasons.Invoice, "The message looks like an invoice or supplier payment request.");
        }

        var hasUpsellIntent = ContainsAny(body, "upgrade", "add seats", "more licenses", "expand", "pricing");
        if (ContainsAny(body, "support ticket", "case number", "bug", "not working", "refund") && !hasUpsellIntent)
        {
            return DetectionOutcome.Ignored(SalesEmailIgnoreReasons.SupportTicket, "The message is a support request without buying or expansion intent.");
        }

        var positive = ContainsAny(body, "demo", "pricing", "quote", "proposal", "interested", "buy", "purchase", "evaluate", "trial", "book a call", "upgrade", "add seats", "license", "contract");
        if (!positive)
        {
            return DetectionOutcome.Ignored(SalesEmailIgnoreReasons.InsufficientSignal, "No clear sales buying signal was found.");
        }

        var urgency = ContainsAny(body, "urgent", "as soon as possible", "this week", "today", "tomorrow") ? "high" :
            ContainsAny(body, "next month", "later", "no rush") ? "low" : "medium";
        var intent = ContainsAny(body, "demo", "book a call") ? "demo request" :
            ContainsAny(body, "pricing", "quote", "proposal") ? "pricing request" :
            hasUpsellIntent ? "expansion interest" : "buying interest";
        var companyName = GuessCompanyName(senderEmail);
        var confidence = positive && hasUpsellIntent ? 0.86m : positive ? 0.72m : 0.50m;

        return DetectionOutcome.Detected(
            new SalesEmailSignalResult(
                senderEmail.ToLowerInvariant(),
                latest.Sender.DisplayName,
                companyName,
                intent,
                ExtractProductInterest(body),
                urgency,
                confidence),
            $"Detected {intent} from {senderEmail} with {urgency} urgency.");
    }

    private async Task<CustomerCompany?> UpsertCustomerCompanyAsync(Guid companyId, SalesEmailSignalResult signal, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(signal.CompanyName))
        {
            return null;
        }

        var existing = await _dbContext.CustomerCompanies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && !x.IsDeleted && x.Name == signal.CompanyName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var customer = new CustomerCompany(Guid.NewGuid(), companyId, signal.CompanyName);
        _dbContext.CustomerCompanies.Add(customer);
        return customer;
    }

    private async Task<Contact?> UpsertContactAsync(Guid companyId, SalesEmailSignalResult signal, Guid? customerCompanyId, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Contacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && !x.IsDeleted && x.Email == signal.SenderEmail, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var name = string.IsNullOrWhiteSpace(signal.ContactName) ? signal.SenderEmail : signal.ContactName;
        var contact = new Contact(Guid.NewGuid(), companyId, name!, signal.SenderEmail, customerCompanyId);
        _dbContext.Contacts.Add(contact);
        return contact;
    }

    private async Task<Lead?> FindExistingLeadForSignalAsync(
        Guid companyId,
        Guid? contactId,
        Guid? customerCompanyId,
        SalesEmailSignalResult signal,
        CancellationToken cancellationToken)
    {
        if (contactId is not null)
        {
            var contactLead = await _dbContext.Leads.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.PrimaryContactId == contactId)
                .OrderByDescending(x => x.UpdatedUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (contactLead is not null)
            {
                return contactLead;
            }
        }

        return customerCompanyId is null ? null : await _dbContext.Leads.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && !x.IsDeleted && x.CustomerCompanyId == customerCompanyId && x.Source == "sales email").OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Lead> CreateLeadAsync(Guid companyId, SalesEmailSignalResult signal, Guid? contactId, Guid? customerCompanyId, CancellationToken cancellationToken)
    {
        var stageExists = await _dbContext.SalesPipelineStages
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == SalesPipelineStage.NewStageId && !x.IsDeleted && x.IsActive, cancellationToken);
        if (!stageExists)
        {
            throw new InvalidOperationException("The default sales pipeline stage is not available.");
        }

        var lead = new Lead(Guid.NewGuid(), companyId, BuildLeadTitle(signal), SalesPipelineStage.NewStageId, primaryContactId: contactId, customerCompanyId: customerCompanyId, source: "sales email");
        _dbContext.Leads.Add(lead);
        return lead;
    }

    private void EnqueueEmailReceived(Guid companyId, MailboxConnection connection, MailboxInboundMessage message, Guid? leadId, Guid? contactId, DetectionOutcome detection, string? threadIdentity) =>
        EnqueuePlatformEvent(companyId, SalesEmailDomainEvents.EmailReceived, "sales_email", message.ProviderMessageId, connection, message, leadId, contactId, detection.Signal, threadIdentity, detection.IgnoreReason);

    private void EnqueueLeadDetected(Guid companyId, MailboxConnection connection, MailboxInboundMessage message, Guid leadId, SalesEmailSignalResult signal, string? threadIdentity) =>
        EnqueuePlatformEvent(companyId, SalesEmailDomainEvents.LeadDetected, "lead", leadId.ToString("N"), connection, message, leadId, null, signal, threadIdentity, null);

    private void EnqueuePlatformEvent(Guid companyId, string eventType, string sourceType, string sourceId, MailboxConnection connection, MailboxInboundMessage message, Guid? leadId, Guid? contactId, SalesEmailSignalResult? signal, string? threadIdentity, string? ignoreReason)
    {
        var eventId = $"{eventType}:{companyId:N}:{connection.Provider.ToStorageValue()}:{connection.Id:N}:{message.ProviderMessageId}";
        var correlationId = eventId;
        _outbox.Enqueue(
            companyId,
            eventType,
            new PlatformEventEnvelope(
                eventId,
                eventType,
                _timeProvider.GetUtcNow().UtcDateTime,
                companyId,
                correlationId,
                sourceType,
                sourceId,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(companyId),
                    ["mailboxConnectionId"] = JsonValue.Create(connection.Id),
                    ["provider"] = JsonValue.Create(connection.Provider.ToStorageValue()),
                    ["providerMessageId"] = JsonValue.Create(message.ProviderMessageId),
                    ["providerThreadId"] = JsonValue.Create(threadIdentity),
                    ["internetMessageId"] = JsonValue.Create(message.InternetMessageId),
                    ["leadId"] = JsonValue.Create(leadId),
                    ["contactId"] = JsonValue.Create(contactId),
                    ["senderEmail"] = JsonValue.Create(signal?.SenderEmail ?? message.Sender.Email),
                    ["intent"] = JsonValue.Create(signal?.Intent),
                    ["confidence"] = JsonValue.Create(signal?.Confidence),
                    ["ignoreReason"] = JsonValue.Create(ignoreReason)
                }),
            correlationId,
            idempotencyKey: $"platform-event:{companyId:N}:{eventId}",
            causationId: message.ProviderMessageId);
    }

    private async Task<SalesEmailLink?> FindMessageLinkAsync(Guid companyId, MailboxConnection connection, string messageId, CancellationToken cancellationToken) =>
        await _dbContext.SalesEmailLinks.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && !x.IsDeleted && x.Provider == connection.Provider.ToStorageValue() && x.MailboxConnectionId == connection.Id && x.ExternalMessageId == messageId && x.LinkKind == SalesEmailLinkKinds.Message, cancellationToken);

    private async Task<SalesEmailLink?> FindThreadLinkAsync(Guid companyId, MailboxConnection connection, string threadId, CancellationToken cancellationToken) =>
        await _dbContext.SalesEmailLinks.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && !x.IsDeleted && x.Provider == connection.Provider.ToStorageValue() && x.MailboxConnectionId == connection.Id && x.ExternalMessageId == threadId && x.LinkKind == SalesEmailLinkKinds.Thread, cancellationToken);

    private async Task<SalesEmailLink?> FindLeadThreadLinkAsync(Guid companyId, MailboxConnection connection, string threadIdentity, CancellationToken cancellationToken) =>
        await _dbContext.SalesEmailLinks.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && !x.IsDeleted && x.Provider == connection.Provider.ToStorageValue() && x.MailboxConnectionId == connection.Id && x.ExternalThreadId == threadIdentity && x.LeadId != null, cancellationToken);

    private async Task<bool> HasMessageLinkAsync(Guid companyId, MailboxConnection connection, string messageId, CancellationToken cancellationToken) =>
        await FindMessageLinkAsync(companyId, connection, messageId, cancellationToken) is not null;

    private async Task<bool> HasThreadLinkAsync(Guid companyId, MailboxConnection connection, string threadId, CancellationToken cancellationToken) =>
        await FindThreadLinkAsync(companyId, connection, threadId, cancellationToken) is not null;

    private async Task<Guid?> FindDetectionActivityIdAsync(Guid companyId, Guid leadId, string messageId, CancellationToken cancellationToken) =>
        await _dbContext.SalesActivities.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.LeadId == leadId && x.ActivityType == "email" && x.Summary.Contains(messageId))
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static SalesEmailLink CreateIgnoredLink(Guid companyId, MailboxConnection connection, string externalMessageId, string? internetMessageId, string? messageThreadId, string? threadIdentity, string ignoreReason, string rationale, string linkKind) =>
        new(Guid.NewGuid(), companyId, externalMessageId, status: SalesStatuses.Ignored, provider: connection.Provider.ToStorageValue(), mailboxConnectionId: connection.Id, externalThreadId: threadIdentity ?? messageThreadId, internetMessageId: internetMessageId, linkKind: linkKind, ignoreReason: ignoreReason, rationale: rationale);

    private static SalesEmailIngestionResult ToAlreadyProcessedResult(SalesEmailLink link, MailboxConnection connection) =>
        new(SalesEmailIngestionStatuses.AlreadyProcessed, true, link.LeadId, link.ContactId, null, link.CustomerCompanyId, connection.Id, connection.Provider.ToStorageValue(), link.LinkKind == SalesEmailLinkKinds.Message ? link.ExternalMessageId : null, link.ExternalThreadId ?? (link.LinkKind == SalesEmailLinkKinds.Thread ? link.ExternalMessageId : null), link.InternetMessageId, link.IgnoreReason, link.Rationale, null);

    private static SalesEmailIngestionResult ToIgnoredResult(SalesEmailLink link, MailboxConnection connection) =>
        new(SalesEmailIngestionStatuses.Ignored, false, null, null, null, null, connection.Id, connection.Provider.ToStorageValue(), link.LinkKind == SalesEmailLinkKinds.Message ? link.ExternalMessageId : null, link.ExternalThreadId, link.InternetMessageId, link.IgnoreReason, link.Rationale, null);

    private static string ResolveThreadIdentity(MailboxConnection connection, IReadOnlyList<MailboxInboundMessage> messages, string? providerThreadId) =>
        providerThreadId ?? messages.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.ProviderThreadId))?.ProviderThreadId ?? messages.First().InternetMessageId ?? $"{connection.Provider.ToStorageValue()}:{messages.First().ProviderMessageId}";

    private static string BuildLeadTitle(SalesEmailSignalResult signal) =>
        string.IsNullOrWhiteSpace(signal.CompanyName) ? $"{signal.Intent} from {signal.SenderEmail}" : $"{signal.Intent} from {signal.CompanyName}";

    private static string? GuessCompanyName(string email)
    {
        var domain = email.Split('@').LastOrDefault();
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var root = domain.Split('.').FirstOrDefault();
        if (string.IsNullOrWhiteSpace(root) || root is "gmail" or "outlook" or "hotmail" or "yahoo" or "icloud")
        {
            return null;
        }

        return char.ToUpperInvariant(root[0]) + root[1..];
    }

    private static string? ExtractProductInterest(string body)
    {
        var match = Regex.Match(body, "(?:interested in|pricing for|quote for|demo of|evaluate) (?<interest>.{3,80})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["interest"].Value.Trim(' ', '.', ',', '?') : null;
    }

    private static string NormalizeBody(string value) => WhitespaceRegex.Replace(value, " ").Trim();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? StripHtml(string? html) =>
        string.IsNullOrWhiteSpace(html) ? null : Regex.Replace(html, "<.*?>", " ");

    private static bool ContainsAny(string value, params string[] keywords) =>
        keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static void EnsureCommand(Guid companyId, Guid userId, Guid mailboxConnectionId, string externalId, string externalIdName)
    {
        _ = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        _ = userId == Guid.Empty ? throw new ArgumentException("UserId is required.", nameof(userId)) : userId;
        _ = mailboxConnectionId == Guid.Empty ? throw new ArgumentException("MailboxConnectionId is required.", nameof(mailboxConnectionId)) : mailboxConnectionId;
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("Provider identifier is required.", externalIdName);
        }
    }

    private sealed record DetectionOutcome(
        SalesEmailSignalResult? Signal,
        string? IgnoreReason,
        string Rationale)
    {
        public static DetectionOutcome Detected(SalesEmailSignalResult signal, string rationale) => new(signal, null, rationale);
        public static DetectionOutcome Ignored(string reason, string rationale) => new(null, reason, rationale);
        public static DetectionOutcome FromExtraction(SalesEmailIntentExtractionResult extraction)
        {
            if (string.Equals(extraction.Classification, SalesEmailIntentClassifications.SalesLead, StringComparison.OrdinalIgnoreCase) &&
                extraction.Signal is not null)
            {
                return Detected(extraction.Signal, extraction.Rationale);
            }

            return Ignored(
                string.IsNullOrWhiteSpace(extraction.IgnoreReason)
                    ? SalesEmailIgnoreReasons.InsufficientSignal
                    : extraction.IgnoreReason,
                extraction.Rationale);
        }
    }
}
