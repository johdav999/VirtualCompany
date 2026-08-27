using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.BackgroundJobs;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceDeliveryService(
    VirtualCompanyDbContext db, ICompanyOutboxEnqueuer outbox, ICompanyDocumentStorage storage,
    IMailboxTransportRegistry transports, IFieldEncryptionService encryption, IAuditEventWriter audit,
    IEnumerable<ICustomerInvoiceElectronicDeliveryProvider> electronicProviders,
    ILogger<CustomerInvoiceDeliveryService> logger) : ICustomerInvoiceDeliveryService, ICustomerInvoiceDeliveryDispatcher
{
    private const string DefaultTemplate = "native-invoice-pdf-2026.1";

    public async Task<CustomerInvoiceArtifactDto> RequestRenderAsync(RequestCustomerInvoiceRenderCommand command, CancellationToken ct)
    {
        var invoice = await NativeInvoiceAsync(command.CompanyId, command.InvoiceId, ct);
        var issued = await IssuedAsync(command.CompanyId, invoice.Id, ct);
        var locale = NormalizeLocale(command.Locale); var template = string.IsNullOrWhiteSpace(command.TemplateVersion) ? DefaultTemplate : command.TemplateVersion.Trim();
        var artifact = await db.CustomerInvoiceRenderedArtifacts.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.InvoiceId == invoice.Id && x.SnapshotHash == issued.SnapshotHash && x.TemplateVersion == template && x.Locale == locale, ct);
        if (artifact is null)
        {
            artifact = new(Guid.NewGuid(), command.CompanyId, invoice.Id, issued.Id, issued.SnapshotHash, template, locale, $"{SafeFileName(invoice.InvoiceNumber)}.pdf", DateTime.UtcNow);
            db.CustomerInvoiceRenderedArtifacts.Add(artifact);
            outbox.Enqueue(command.CompanyId, CompanyOutboxTopics.CustomerInvoiceRenderRequested, new CustomerInvoiceRenderRequestedMessage(command.CompanyId, artifact.Id, command.CorrelationId), command.CorrelationId, idempotencyKey: $"invoice-render:{command.CompanyId:N}:{invoice.Id:N}:{issued.SnapshotHash}:{template}:{locale}");
            await audit.WriteAsync(new(command.CompanyId, AuditActorTypes.User, command.ActorUserId, "finance.customer_invoice.render_requested", "finance_invoice", invoice.Id.ToString("N"), AuditEventOutcomes.Succeeded, "Invoice PDF rendering was queued.", ["finance", "invoice", "render"], new Dictionary<string, string?> { ["artifactId"] = artifact.Id.ToString("N"), ["snapshotHash"] = issued.SnapshotHash, ["templateVersion"] = template }, command.CorrelationId), ct);
            await db.SaveChangesAsync(ct);
        }
        return Map(artifact);
    }

    public async Task<CustomerInvoiceEmailDeliveryDto> RequestEmailAsync(RequestCustomerInvoiceEmailDeliveryCommand command, CancellationToken ct)
        => await RequestEmailCoreAsync(command, CustomerInvoiceEmailRequestSources.Direct, null, null, ct);

    public async Task<CustomerInvoicePreferredDeliveryDto> RequestPreferredDeliveryAsync(RequestCustomerInvoicePreferredDeliveryCommand command, CancellationToken ct)
    {
        _ = await NativeInvoiceAsync(command.CompanyId, command.InvoiceId, ct);
        var artifact = await ArtifactAsync(command.CompanyId, command.ArtifactId, ct);
        if (artifact.InvoiceId != command.InvoiceId) throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ArtifactNotFound, "The rendered invoice does not belong to this invoice.");
        if (artifact.Status != CustomerInvoiceRenderStatuses.Rendered || artifact.ContentHash is null) throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ArtifactNotReady, "The invoice PDF is not ready for delivery.");
        var issued = await IssuedAsync(command.CompanyId, command.InvoiceId, ct);
        var recipient = string.IsNullOrWhiteSpace(command.RecipientEmail) ? ReadRecipient(issued.SnapshotJson) : NormalizeEmail(command.RecipientEmail);
        var key = NormalizePreferredIdempotencyKey(command.IdempotencyKey);
        var providers = electronicProviders.OrderBy(x => x.ProviderKey, StringComparer.Ordinal).ToArray();
        CustomerInvoiceElectronicDeliveryResult electronic;
        if (providers.Length == 0)
        {
            electronic = new(CustomerInvoiceElectronicDeliveryOutcomes.Unavailable,
                CustomerInvoiceDeliveryReasonCodes.PeppolProviderUnavailable,
                "No production Peppol provider is configured for this company.", true);
        }
        else if (providers.Length > 1)
        {
            electronic = new(CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired,
                CustomerInvoiceDeliveryReasonCodes.PeppolOutcomePending,
                "More than one Peppol provider is registered and no single delivery route can be selected safely.", false);
        }
        else
        {
            try
            {
                electronic = await providers[0].TryQueueAsync(new(command.CompanyId, command.InvoiceId,
                    command.ArtifactId, command.AllowEmailFallback, recipient, command.Reason,
                    key, command.ActorUserId, command.CorrelationId), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Peppol delivery routing returned an uncertain outcome for invoice {InvoiceId} in company {CompanyId}.",
                    command.InvoiceId, command.CompanyId);
                electronic = new(CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired,
                    CustomerInvoiceDeliveryReasonCodes.PeppolOutcomePending,
                    "The Peppol request outcome is uncertain and must be reconciled before using email fallback.",
                    false, providers[0].ProviderKey);
            }
        }

        var decision = CustomerInvoiceDeliveryFallbackPolicy.Decide(electronic,
            command.AllowEmailFallback, recipient is not null);
        if (!decision.QueueEmail)
        {
            return new(CustomerInvoiceDeliveryChannels.Peppol, decision.SelectedChannel,
                decision.Status, decision.ReasonCode, false, electronic.ProviderKey,
                electronic.Profile, electronic.DeliveryId, null);
        }

        var delivery = await RequestEmailCoreAsync(new(command.CompanyId, command.InvoiceId,
                command.ArtifactId, recipient, command.Reason, $"preferred:{key}:email",
                command.ActorUserId, command.CorrelationId),
            CustomerInvoiceEmailRequestSources.PeppolFallback, decision.ReasonCode,
            electronic.ProviderKey, ct);
        return new(CustomerInvoiceDeliveryChannels.Peppol, CustomerInvoiceDeliveryChannels.Email,
            delivery.Status, decision.ReasonCode, true, electronic.ProviderKey,
            electronic.Profile, electronic.DeliveryId, delivery);
    }

    public async Task<CustomerInvoiceElectronicDeliveryDto> RequestElectronicAsync(
        RequestCustomerInvoiceElectronicDeliveryCommand command, CancellationToken ct)
    {
        _ = await NativeInvoiceAsync(command.CompanyId, command.InvoiceId, ct);
        var artifact = await ArtifactAsync(command.CompanyId, command.ArtifactId, ct);
        if (artifact.InvoiceId != command.InvoiceId)
            throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ArtifactNotFound,
                "The rendered invoice does not belong to this invoice.");
        if (artifact.Status != CustomerInvoiceRenderStatuses.Rendered || artifact.ContentHash is null)
            throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ArtifactNotReady,
                "The invoice PDF is not ready for electronic delivery.");
        var provider = ResolveElectronicProvider();
        var result = await provider.TryQueueAsync(new(command.CompanyId, command.InvoiceId, command.ArtifactId,
            command.AllowEmailFallback, NormalizeEmail(command.RecipientEmail), command.Reason,
            NormalizePreferredIdempotencyKey(command.IdempotencyKey), command.ActorUserId,
            command.CorrelationId), ct);
        if (result.ReasonCode == CustomerInvoiceDeliveryReasonCodes.IdempotencyConflict)
            throw new CustomerInvoiceDeliveryException(result.ReasonCode, result.SafeExplanation,
                true);
        if (Guid.TryParse(result.DeliveryId, out var deliveryId))
            return Map(await ElectronicDeliveryAsync(command.CompanyId, deliveryId, ct));
        throw new CustomerInvoiceDeliveryException(result.ReasonCode, result.SafeExplanation,
            result.Outcome == CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired);
    }

    public async Task<CustomerInvoiceElectronicDeliveryDto> RetryElectronicAsync(
        RetryCustomerInvoiceElectronicDeliveryCommand command, CancellationToken ct)
    {
        var reason = RequiredOperatorReason(command.Reason);
        var delivery = await ElectronicDeliveryAsync(command.CompanyId, command.DeliveryId, ct);
        try { delivery.RequestRetry(DateTime.UtcNow); }
        catch (InvalidOperationException)
        {
            throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.PeppolRetryNotAllowed,
                "This Peppol delivery cannot be retried until the provider proves that no prior submission exists.", true);
        }
        outbox.Enqueue(command.CompanyId, CompanyOutboxTopics.CustomerInvoiceElectronicDeliveryRequested,
            new CustomerInvoiceElectronicDeliveryRequestedMessage(command.CompanyId, delivery.Id,
                delivery.ProviderKey, command.CorrelationId), command.CorrelationId,
            idempotencyKey: $"invoice-peppol-retry:{command.CompanyId:N}:{delivery.Id:N}:{delivery.SubmissionAttempts}");
        await audit.WriteAsync(new(command.CompanyId, AuditActorTypes.User, command.ActorUserId,
            "finance.customer_invoice.peppol_retry_requested", "finance_invoice", delivery.InvoiceId.ToString("N"),
            AuditEventOutcomes.Succeeded, "A safe Peppol retry was queued.", ["finance", "invoice", "peppol", "outbox"],
            new Dictionary<string, string?> { ["deliveryId"] = delivery.Id.ToString("N"), ["reason"] = reason }, command.CorrelationId), ct);
        await db.SaveChangesAsync(ct);
        return Map(delivery);
    }

    public async Task<CustomerInvoiceElectronicDeliveryDto> ReconcileElectronicAsync(
        ReconcileCustomerInvoiceElectronicDeliveryCommand command, CancellationToken ct)
    {
        var reason = RequiredOperatorReason(command.Reason);
        var delivery = await ElectronicDeliveryAsync(command.CompanyId, command.DeliveryId, ct);
        if (delivery.Status is not CustomerInvoiceElectronicDeliveryStatuses.Delivered and
            not CustomerInvoiceElectronicDeliveryStatuses.Rejected)
        {
            outbox.Enqueue(command.CompanyId, CompanyOutboxTopics.CustomerInvoiceElectronicReconciliationRequested,
                new CustomerInvoiceElectronicReconciliationRequestedMessage(command.CompanyId, delivery.Id,
                    delivery.ProviderKey, command.CorrelationId), command.CorrelationId,
                idempotencyKey: $"invoice-peppol-manual-reconcile:{command.CompanyId:N}:{delivery.Id:N}:{delivery.ReconciliationAttempts}");
            await audit.WriteAsync(new(command.CompanyId, AuditActorTypes.User, command.ActorUserId,
                "finance.customer_invoice.peppol_reconciliation_requested", "finance_invoice", delivery.InvoiceId.ToString("N"),
                AuditEventOutcomes.Succeeded, "Peppol reconciliation was queued.", ["finance", "invoice", "peppol", "outbox"],
                new Dictionary<string, string?> { ["deliveryId"] = delivery.Id.ToString("N"), ["reason"] = reason }, command.CorrelationId), ct);
            await db.SaveChangesAsync(ct);
        }
        return Map(delivery);
    }

    public async Task<CustomerInvoiceElectronicDeliveryDto> GetElectronicDeliveryAsync(
        GetCustomerInvoiceElectronicDeliveryQuery query, CancellationToken ct) =>
        Map(await ElectronicDeliveryAsync(query.CompanyId, query.DeliveryId, ct));

    public Task<CustomerInvoiceElectronicProviderCapabilityDto> GetElectronicProviderCapabilityAsync(Guid companyId, CancellationToken ct) =>
        ResolveElectronicProvider().GetCapabilityAsync(companyId, ct);

    private async Task<CustomerInvoiceEmailDeliveryDto> RequestEmailCoreAsync(
        RequestCustomerInvoiceEmailDeliveryCommand command,
        string requestSource,
        string? fallbackReasonCode,
        string? fallbackProviderKey,
        CancellationToken ct)
    {
        var artifact = await ArtifactAsync(command.CompanyId, command.ArtifactId, ct);
        if (artifact.InvoiceId != command.InvoiceId) throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ArtifactNotFound, "The rendered invoice does not belong to this invoice.");
        if (artifact.Status != CustomerInvoiceRenderStatuses.Rendered || artifact.ContentHash is null) throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ArtifactNotReady, "The invoice PDF is not ready for delivery.");
        var issued = await IssuedAsync(command.CompanyId, command.InvoiceId, ct);
        var recipient = string.IsNullOrWhiteSpace(command.RecipientEmail) ? ReadRecipient(issued.SnapshotJson) : NormalizeEmail(command.RecipientEmail);
        if (recipient is null) throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.DeliveryAddressMissing, "The issued invoice does not contain an email delivery address.");
        var existing = await db.CustomerInvoiceEmailDeliveries.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == command.IdempotencyKey.Trim(), ct);
        if (existing is not null)
        {
            if (existing.InvoiceId != command.InvoiceId || existing.ArtifactId != command.ArtifactId) throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.IdempotencyConflict, "This delivery key was already used for a different invoice delivery.", true);
            return Map(existing);
        }
        var delivery = new CustomerInvoiceEmailDelivery(Guid.NewGuid(), command.CompanyId, command.InvoiceId, artifact.Id, artifact.ContentHash, recipient, issued.SnapshotHash, $"Invoice {ReadDocumentNumber(issued.SnapshotJson)}", command.Reason, command.IdempotencyKey, command.ActorUserId, DateTime.UtcNow, requestSource, fallbackReasonCode, fallbackProviderKey);
        db.CustomerInvoiceEmailDeliveries.Add(delivery);
        QueueDelivery(delivery, command.CorrelationId);
        await audit.WriteAsync(new(command.CompanyId, AuditActorTypes.User, command.ActorUserId, "finance.customer_invoice.email_requested", "finance_invoice", command.InvoiceId.ToString("N"), AuditEventOutcomes.Succeeded, "Invoice email delivery was queued.", ["finance", "invoice", "mailbox", "outbox"], new Dictionary<string, string?> { ["deliveryId"] = delivery.Id.ToString("N"), ["artifactHash"] = artifact.ContentHash, ["requestSource"] = requestSource, ["fallbackReasonCode"] = fallbackReasonCode, ["fallbackProviderKey"] = fallbackProviderKey }, command.CorrelationId), ct);
        await db.SaveChangesAsync(ct); return Map(delivery);
    }

    public async Task<CustomerInvoiceEmailDeliveryDto> ResendAsync(ResendCustomerInvoiceEmailCommand command, CancellationToken ct)
    {
        var prior = await DeliveryAsync(command.CompanyId, command.DeliveryId, ct);
        if (prior.Status == CustomerInvoiceDeliveryStatuses.ReconciliationRequired) throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ReconciliationRequired, "Reconcile the prior mailbox outcome before resending this invoice.");
        return await RequestEmailAsync(new(command.CompanyId, prior.InvoiceId, prior.ArtifactId, prior.RecipientEmail, command.Reason, command.IdempotencyKey, command.ActorUserId, command.CorrelationId), ct);
    }

    public async Task<CustomerInvoiceArtifactDto> GetArtifactAsync(GetCustomerInvoiceArtifactQuery query, CancellationToken ct) => Map(await ArtifactAsync(query.CompanyId, query.ArtifactId, ct));
    public async Task<CustomerInvoiceEmailDeliveryDto> GetDeliveryAsync(GetCustomerInvoiceDeliveryQuery query, CancellationToken ct) => Map(await DeliveryAsync(query.CompanyId, query.DeliveryId, ct));
    public async Task<(Stream Content, string FileName)> OpenArtifactAsync(Guid companyId, Guid artifactId, CancellationToken ct)
    { var a = await ArtifactAsync(companyId, artifactId, ct); if (a.Status != CustomerInvoiceRenderStatuses.Rendered || a.ObjectKey is null) throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ArtifactNotReady, "The invoice PDF is not ready for download."); return (await storage.OpenReadAsync(a.ObjectKey, ct), a.FileName); }

    public async Task RenderAsync(Guid companyId, Guid artifactId, CancellationToken ct)
    {
        var artifact = await ArtifactAsync(companyId, artifactId, ct); if (artifact.Status == CustomerInvoiceRenderStatuses.Rendered) return;
        var issued = await db.IssuedStatutoryDocuments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == artifact.IssuedDocumentId && x.SnapshotHash == artifact.SnapshotHash, ct) ?? throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.InvoiceNotFound, "The immutable issued invoice snapshot is unavailable.");
        artifact.Start(DateTime.UtcNow); await db.SaveChangesAsync(ct);
        try
        {
            var content = DeterministicInvoicePdf.Render(issued.SnapshotJson, artifact.Locale, artifact.TemplateVersion);
            var hash = Hash(content); var key = $"companies/{companyId:N}/finance/invoices/{artifact.InvoiceId:N}/{artifact.SnapshotHash}/{artifact.TemplateVersion}/{artifact.Locale}/{artifact.FileName}";
            await using var stream = new MemoryStream(content, writable: false);
            await storage.WriteAsync(new(companyId, artifact.Id, key, artifact.FileName, "application/pdf", stream), ct);
            artifact.Complete(key, hash, content.LongLength, DateTime.UtcNow); await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            artifact.Fail("render_failed", "The invoice PDF could not be rendered or stored.", DateTime.UtcNow); await db.SaveChangesAsync(ct);
            logger.LogWarning(ex, "Invoice PDF rendering failed for artifact {ArtifactId} in company {CompanyId}.", artifactId, companyId); throw;
        }
    }

    public async Task DeliverAsync(Guid companyId, Guid deliveryId, CancellationToken ct)
    {
        var delivery = await DeliveryAsync(companyId, deliveryId, ct); if (delivery.Status is CustomerInvoiceDeliveryStatuses.Accepted or CustomerInvoiceDeliveryStatuses.Delivered or CustomerInvoiceDeliveryStatuses.ReconciliationRequired) return;
        var artifact = await ArtifactAsync(companyId, delivery.ArtifactId, ct);
        if (artifact.Status != CustomerInvoiceRenderStatuses.Rendered || artifact.ContentHash != delivery.ArtifactHash || artifact.ObjectKey is null) throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ArtifactNotReady, "The invoice PDF changed or is unavailable.");
        delivery.Start(DateTime.UtcNow); await db.SaveChangesAsync(ct);
        try
        {
            var connection = await db.MailboxConnections.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.Purpose == MailboxPurpose.Finance && x.Status == MailboxConnectionStatus.Active && x.CapabilityFlags.HasFlag(MailboxCapability.SendMessages)).OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(ct) ?? throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.DeliveryAddressMissing, "Connect a finance mailbox before sending invoice email.");
            if (connection.Provider != MailboxProvider.StandardEmail) throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.DeliveryAddressMissing, "The selected mailbox cannot send invoice attachments. Connect a standard SMTP finance mailbox.");
            var context = StandardMailboxSessionCodec.Decode(StandardMailboxSessionCodec.Create(connection, encryption));
            await using var pdf = await storage.OpenReadAsync(artifact.ObjectKey, ct); using var memory = new MemoryStream(); await pdf.CopyToAsync(memory, ct);
            var message = new MailboxOutboundMessage($"<invoice-{delivery.Id:N}@virtualcompany.local>", connection.EmailAddress, [delivery.RecipientEmail], [], [], delivery.Subject, "Please find your invoice attached.", null, null, [], [new MailboxOutboundAttachment(artifact.FileName, artifact.MediaType, memory.ToArray())]);
            var result = await transports.Resolve("mailkit").SendAsync(context, message, ct);
            if (result.Outcome == MailboxSubmissionOutcome.Accepted) { delivery.Accepted(result.ProviderReference, DateTime.UtcNow); await audit.WriteAsync(new(companyId, AuditActorTypes.System, null, "finance.customer_invoice.email_accepted", "finance_invoice", delivery.InvoiceId.ToString("N"), AuditEventOutcomes.Succeeded, "The mailbox provider accepted the invoice email. Recipient delivery is not asserted.", ["finance", "mailbox", "outbox"], new Dictionary<string, string?> { ["deliveryId"] = delivery.Id.ToString("N"), ["providerReference"] = result.ProviderReference }), ct); await db.SaveChangesAsync(ct); return; }
            if (result.Outcome == MailboxSubmissionOutcome.Ambiguous) { delivery.Reconcile(result.SafeFailureCode ?? "delivery_ambiguous", "Delivery could not be confirmed. Reconcile the mailbox Sent folder before resending.", DateTime.UtcNow); await audit.WriteAsync(new(companyId, AuditActorTypes.System, null, "finance.customer_invoice.email_reconciliation_required", "finance_invoice", delivery.InvoiceId.ToString("N"), AuditEventOutcomes.Failed, "The mailbox outcome is ambiguous and requires reconciliation before resend.", ["finance", "mailbox", "outbox"], new Dictionary<string, string?> { ["deliveryId"] = delivery.Id.ToString("N"), ["failureCode"] = result.SafeFailureCode }), ct); await db.SaveChangesAsync(ct); return; }
            delivery.Fail(result.SafeFailureCode ?? "delivery_failed", result.SafeFailureMessage ?? "Invoice email could not be sent.", DateTime.UtcNow); await db.SaveChangesAsync(ct);
            if (result.Outcome is MailboxSubmissionOutcome.PermanentFailure or MailboxSubmissionOutcome.AuthenticationRequired) throw new PermanentBackgroundJobException("Invoice email delivery failed permanently. Reconnect the finance mailbox or correct the recipient.");
            throw new InvalidOperationException("Invoice email delivery failed and will be retried.");
        }
        catch (CustomerInvoiceDeliveryException ex)
        {
            delivery.Fail(ex.ReasonCode, ex.Message, DateTime.UtcNow); await db.SaveChangesAsync(ct); throw new PermanentBackgroundJobException(ex.Message);
        }
    }

    public Task DeliverElectronicAsync(Guid companyId, Guid deliveryId, string providerKey, CancellationToken ct) =>
        ResolveElectronicProvider(providerKey).ProcessAsync(companyId, deliveryId, ct);

    public Task ReconcileElectronicAsync(Guid companyId, Guid deliveryId, string providerKey, CancellationToken ct) =>
        ResolveElectronicProvider(providerKey).ReconcileAsync(companyId, deliveryId, ct);

    private void QueueDelivery(CustomerInvoiceEmailDelivery d, string? correlationId) => outbox.Enqueue(d.CompanyId, CompanyOutboxTopics.CustomerInvoiceEmailDeliveryRequested, new CustomerInvoiceEmailDeliveryRequestedMessage(d.CompanyId, d.Id, correlationId), correlationId, idempotencyKey: $"invoice-email:{d.CompanyId:N}:{d.IdempotencyKey}");
    private async Task<FinanceInvoice> NativeInvoiceAsync(Guid companyId, Guid invoiceId, CancellationToken ct) => await db.FinanceInvoices.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invoiceId && x.Authority == "native", ct) ?? throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.InvoiceNotFound, "The issued native invoice could not be found.");
    private async Task<IssuedStatutoryDocument> IssuedAsync(Guid companyId, Guid invoiceId, CancellationToken ct) => await db.IssuedStatutoryDocuments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SourceRecordId == invoiceId && x.Authority == StatutoryDocumentAuthorities.Native, ct) ?? throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.InvoiceNotFound, "The immutable issued invoice snapshot could not be found.");
    private async Task<CustomerInvoiceRenderedArtifact> ArtifactAsync(Guid companyId, Guid id, CancellationToken ct) => await db.CustomerInvoiceRenderedArtifacts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct) ?? throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ArtifactNotFound, "The invoice PDF could not be found.");
    private async Task<CustomerInvoiceEmailDelivery> DeliveryAsync(Guid companyId, Guid id, CancellationToken ct) => await db.CustomerInvoiceEmailDeliveries.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct) ?? throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.ArtifactNotFound, "The invoice email delivery could not be found.");
    private async Task<CustomerInvoiceElectronicDelivery> ElectronicDeliveryAsync(Guid companyId, Guid id, CancellationToken ct) => await db.CustomerInvoiceElectronicDeliveries.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct) ?? throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.PeppolDeliveryNotFound, "The Peppol delivery could not be found.");
    private static CustomerInvoiceArtifactDto Map(CustomerInvoiceRenderedArtifact x) => new(x.Id, x.InvoiceId, x.SnapshotHash, x.TemplateVersion, x.Locale, x.MediaType, x.FileName, x.Status, x.ContentHash, x.ContentLength, x.GenerationAttempts, x.FailureCode, x.FailureSummary, x.CreatedUtc, x.UpdatedUtc, x.RenderedUtc);
    private static CustomerInvoiceEmailDeliveryDto Map(CustomerInvoiceEmailDelivery x) => new(x.Id, x.InvoiceId, x.ArtifactId, x.Status, x.Attempts, x.ProviderReference, x.FailureCode, x.FailureSummary, x.RequestSource, x.FallbackReasonCode, x.FallbackProviderKey, x.CreatedUtc, x.UpdatedUtc, x.AcceptedUtc);
    private static CustomerInvoiceElectronicDeliveryDto Map(CustomerInvoiceElectronicDelivery x) => new(x.Id,
        x.InvoiceId, x.ArtifactId, x.ProviderKey, x.Profile, x.ProfileVersion, x.ParticipantScheme,
        x.ParticipantIdentifier, x.DocumentType, x.Status, x.Outcome, x.SubmissionAttempts,
        x.ReconciliationAttempts, x.ProviderReference, x.ProviderState, x.FailureCode, x.FailureSummary,
        x.AllowEmailFallback, x.FallbackEmailDeliveryId, x.CreatedUtc, x.UpdatedUtc, x.SubmittedUtc,
        x.DeliveredUtc, x.NextReconcileUtc);
    private ICustomerInvoiceElectronicDeliveryProvider ResolveElectronicProvider(string? providerKey = null)
    {
        var providers = electronicProviders.OrderBy(x => x.ProviderKey, StringComparer.Ordinal).ToArray();
        if (providers.Length == 0)
            throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.PeppolProviderUnavailable,
                "No production Peppol provider is configured for this company.");
        if (providerKey is not null)
            return providers.SingleOrDefault(x => string.Equals(x.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new CustomerInvoiceDeliveryException(CustomerInvoiceDeliveryReasonCodes.PeppolProviderUnavailable,
                    "The Peppol provider recorded for this delivery is not available.");
        return providers.Length == 1 ? providers[0] : throw new CustomerInvoiceDeliveryException(
            CustomerInvoiceDeliveryReasonCodes.PeppolOutcomePending,
            "More than one Peppol provider is registered and no single route can be selected safely.", true);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string NormalizeLocale(string value) => value?.Trim().ToLowerInvariant() switch { "sv" or "sv-se" => "sv-SE", null or "" or "en" or "en-us" => "en-US", _ => throw new ArgumentException("Only English and Swedish invoice locales are supported.") };
    private static string? NormalizeEmail(string? value) => string.IsNullOrWhiteSpace(value) || !value.Trim().Contains('@') ? null : value.Trim().ToLowerInvariant();
    private static string NormalizePreferredIdempotencyKey(string value)
    {
        var key = value?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 150)
            throw new ArgumentException("A preferred-delivery idempotency key between 1 and 150 characters is required.");
        return key;
    }
    private static string RequiredOperatorReason(string value)
    {
        var reason = value?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw new ArgumentException("An operator reason between 1 and 500 characters is required.");
        return reason;
    }
    private static string? ReadRecipient(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("buyer", out var b) && b.TryGetProperty("invoiceDeliveryEmail", out var e) ? NormalizeEmail(e.GetString()) : null; }
    private static string ReadDocumentNumber(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("documentNumber", out var n) ? n.GetString() ?? "Invoice" : "Invoice"; }
    private static string SafeFileName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

internal static class DeterministicInvoicePdf
{
    // Self-contained PDF 1.7 text renderer: no third-party runtime, Helvetica/WinAnsi encoding, deterministic object order. PDF/UA tagging remains limited to document metadata and marked content.
    public static byte[] Render(string snapshotJson, string locale, string templateVersion)
    {
        using var doc = JsonDocument.Parse(snapshotJson); var r = doc.RootElement; var lines = new List<string>();
        string Get(JsonElement e, string key) => e.TryGetProperty(key, out var x) ? x.ToString() : "";
        var title = Get(r, "documentNumber"); lines.Add(locale == "sv-SE" ? $"FAKTURA {title}" : $"INVOICE {title}"); lines.Add($"Issue date: {Get(r.GetProperty("draft"), "issueDate")}    Due date: {Get(r.GetProperty("draft"), "dueDate")}");
        var seller = r.GetProperty("seller"); var buyer = r.GetProperty("buyer"); lines.Add($"Seller: {Get(seller, "legalName")}"); lines.Add($"Buyer: {Get(buyer, "legalName")}"); lines.Add($"{Get(buyer, "billingAddressLine1")}, {Get(buyer, "billingPostalCode")} {Get(buyer, "billingCity")}"); lines.Add(""); lines.Add("Description                                      Qty       Net        VAT      Gross");
        foreach (var l in r.GetProperty("lines").EnumerateArray()) lines.Add($"{Get(l, "description")} | {Get(l, "quantity")} | {Get(l, "netAmount")} | {Get(l, "taxAmount")} | {Get(l, "grossAmount")}");
        var draft = r.GetProperty("draft"); lines.Add(""); lines.Add($"Net: {Get(draft, "netTotal")}   VAT: {Get(draft, "taxTotal")}   Total: {Get(draft, "grossTotal")} {Get(draft, "currency")}"); lines.Add($"Template: {templateVersion}");
        return Build(lines, title, locale);
    }
    private static byte[] Build(IReadOnlyList<string> lines, string title, string locale)
    {
        var perPage = 38; var pages = lines.Chunk(perPage).ToArray(); var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /Lang (" + Esc(locale) + ") >>", "<< /Type /Pages /Kids [" + string.Join(" ", Enumerable.Range(0, pages.Length).Select(i => $"{3 + i * 2} 0 R")) + $"] /Count {pages.Length} >>" };
        for (var p = 0; p < pages.Length; p++) { var content = new StringBuilder("BT /F1 10 Tf 48 790 Td "); foreach (var line in pages[p]) content.Append('(').Append(Esc(line)).Append(") Tj 0 -18 Td "); content.Append($"0 -18 Td (Page {p + 1} of {pages.Length}) Tj ET"); var contentText = content.ToString(); var pageId = 3 + p * 2; var contentId = pageId + 1; objects.Add($"<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 {3 + pages.Length * 2} 0 R >> >> /MediaBox [0 0 595 842] /Contents {contentId} 0 R >>"); objects.Add($"<< /Length {Encoding.Latin1.GetByteCount(contentText)} >>\nstream\n{contentText}\nendstream"); }
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"); objects.Add("<< /Title (" + Esc(title) + ") /Producer (Virtual Company deterministic invoice renderer) >>");
        var sb = new StringBuilder("%PDF-1.7\n%âãÏÓ\n"); var offsets = new List<int> { 0 }; for (var i = 0; i < objects.Count; i++) { offsets.Add(Encoding.Latin1.GetByteCount(sb.ToString())); sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n"); } var xref = Encoding.Latin1.GetByteCount(sb.ToString()); sb.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n"); foreach (var o in offsets.Skip(1)) sb.Append(o.ToString("D10")).Append(" 00000 n \n"); sb.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R /Info ").Append(objects.Count).Append(" 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF"); return Encoding.Latin1.GetBytes(sb.ToString());
    }
    private static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace('\r', ' ').Replace('\n', ' ');
}
