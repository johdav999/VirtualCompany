using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class B2BRouterCustomerInvoiceElectronicDeliveryProvider(
    VirtualCompanyDbContext db,
    ICompanyOutboxEnqueuer outbox,
    ICompanyDocumentStorage storage,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<B2BRouterOptions> optionsMonitor,
    IAuditEventWriter audit,
    TimeProvider timeProvider,
    B2BRouterTelemetry telemetry,
    ILogger<B2BRouterCustomerInvoiceElectronicDeliveryProvider> logger)
    : ICustomerInvoiceElectronicDeliveryProvider
{
    private const int MaximumSubmissionAttempts = 5;
    public string ProviderKey => B2BRouterOptions.ProviderKey;

    public Task<CustomerInvoiceElectronicProviderCapabilityDto> GetCapabilityAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        var enabled = options.Enabled && HasCredentials(options, companyId);
        return Task.FromResult(new CustomerInvoiceElectronicProviderCapabilityDto(ProviderKey, enabled,
            options.Environment, enabled ? "ready" : options.Enabled ? "credentials_missing" : "disabled",
            enabled ? "B2Brouter Peppol delivery is configured."
                : options.Enabled ? "B2Brouter needs a company account mapping and API key before Peppol delivery can be used."
                : "B2Brouter Peppol delivery is disabled.",
            [B2BRouterOptions.PeppolBisBillingProfile], ["invoice", "credit_note"], true, true, true,
            true, options.WebhooksEnabled && !string.IsNullOrWhiteSpace(options.WebhookSecret), false,
            options.ApiVersion));
    }

    public async Task<CustomerInvoiceElectronicDeliveryResult> TryQueueAsync(
        CustomerInvoiceElectronicDeliveryCommand command, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled || !HasCredentials(options, command.CompanyId))
            return new(CustomerInvoiceElectronicDeliveryOutcomes.Unavailable,
                CustomerInvoiceDeliveryReasonCodes.PeppolCredentialsMissing,
                "B2Brouter credentials are not configured for Peppol delivery.", true, ProviderKey);

        var key = Required(command.IdempotencyKey, 200, "An electronic-delivery idempotency key is required.");
        var existing = await db.CustomerInvoiceElectronicDeliveries.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == key,
                cancellationToken);
        if (existing is not null)
        {
            if (existing.InvoiceId != command.InvoiceId || existing.ArtifactId != command.ArtifactId)
                return new(CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired,
                    CustomerInvoiceDeliveryReasonCodes.IdempotencyConflict,
                    "This electronic-delivery key was already used for a different invoice.", false,
                    ProviderKey, existing.Profile, existing.Id.ToString("D"));
            return new(existing.Outcome, existing.FailureCode ?? CustomerInvoiceDeliveryReasonCodes.PeppolOutcomePending,
                existing.FailureSummary ?? "The existing Peppol delivery was returned.",
                IsSafeToFallback(existing), ProviderKey, existing.Profile, existing.Id.ToString("D"));
        }

        var invoice = await db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.InvoiceId && x.Authority == "native",
                cancellationToken);
        var issued = await db.IssuedStatutoryDocuments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SourceRecordId == command.InvoiceId &&
                                       x.Authority == StatutoryDocumentAuthorities.Native, cancellationToken);
        var artifact = await db.CustomerInvoiceRenderedArtifacts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ArtifactId &&
                                       x.InvoiceId == command.InvoiceId, cancellationToken);
        if (invoice is null || issued is null)
            return new(CustomerInvoiceElectronicDeliveryOutcomes.ValidationFailed,
                CustomerInvoiceDeliveryReasonCodes.InvoiceNotFound,
                "The immutable native invoice could not be found.", true, ProviderKey);
        if (artifact is null || artifact.Status != CustomerInvoiceRenderStatuses.Rendered || artifact.ContentHash is null)
            return new(CustomerInvoiceElectronicDeliveryOutcomes.ValidationFailed,
                CustomerInvoiceDeliveryReasonCodes.ArtifactNotReady,
                "Render the immutable invoice PDF before Peppol delivery.", true, ProviderKey);

        var route = B2BRouterInvoiceSnapshot.ReadRoute(issued.SnapshotJson, issued.DocumentType);
        if (!route.Supported)
            return new(CustomerInvoiceElectronicDeliveryOutcomes.RecipientUnsupported,
                CustomerInvoiceDeliveryReasonCodes.PeppolProfileUnsupported, route.SafeMessage, true,
                ProviderKey, B2BRouterOptions.PeppolBisBillingProfile);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var delivery = new CustomerInvoiceElectronicDelivery(Guid.NewGuid(), command.CompanyId, command.InvoiceId,
            issued.Id, artifact.Id, issued.SnapshotHash, artifact.ContentHash, ProviderKey,
            B2BRouterOptions.PeppolBisBillingProfile, B2BRouterOptions.PeppolBisBillingVersion,
            route.ParticipantScheme!, route.ParticipantIdentifier!, route.DocumentType!, invoice.InvoiceNumber,
            key, command.AllowEmailFallback, command.RecipientEmail, Required(command.Reason, 500,
                "A delivery reason is required."), command.ActorUserId, now);
        db.CustomerInvoiceElectronicDeliveries.Add(delivery);
        AddEvent(delivery, $"queued:{delivery.Id:N}", "application", "queued", null,
            "Peppol delivery was queued.", delivery.SnapshotHash, now);
        outbox.Enqueue(command.CompanyId, CompanyOutboxTopics.CustomerInvoiceElectronicDeliveryRequested,
            new CustomerInvoiceElectronicDeliveryRequestedMessage(command.CompanyId, delivery.Id, ProviderKey,
                command.CorrelationId), command.CorrelationId,
            idempotencyKey: $"invoice-peppol:{command.CompanyId:N}:{key}");
        await audit.WriteAsync(new(command.CompanyId, AuditActorTypes.User, command.ActorUserId,
            "finance.customer_invoice.peppol_requested", "finance_invoice", command.InvoiceId.ToString("N"),
            AuditEventOutcomes.Succeeded, "Peppol delivery was queued for background validation and submission.",
            ["finance", "invoice", "peppol", "outbox"], new Dictionary<string, string?>
            {
                ["deliveryId"] = delivery.Id.ToString("N"), ["profile"] = delivery.Profile,
                ["profileVersion"] = delivery.ProfileVersion, ["participantScheme"] = delivery.ParticipantScheme,
                ["snapshotHash"] = delivery.SnapshotHash
            }, command.CorrelationId), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        telemetry.Queued();
        return new(CustomerInvoiceElectronicDeliveryOutcomes.Queued,
            CustomerInvoiceDeliveryReasonCodes.PeppolOutcomePending,
            "Peppol delivery is queued for participant and document validation.", false, ProviderKey,
            delivery.Profile, delivery.Id.ToString("D"));
    }

    public async Task ProcessAsync(Guid companyId, Guid deliveryId, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        var delivery = await DeliveryAsync(companyId, deliveryId, cancellationToken);
        if (delivery.Status is CustomerInvoiceElectronicDeliveryStatuses.Delivered or
            CustomerInvoiceElectronicDeliveryStatuses.Accepted or CustomerInvoiceElectronicDeliveryStatuses.Rejected ||
            delivery.Status == CustomerInvoiceElectronicDeliveryStatuses.ReconciliationRequired)
            return;
        if (!options.Enabled || !HasCredentials(options, companyId))
        {
            await PermanentlyFailAsync(delivery, CustomerInvoiceDeliveryReasonCodes.PeppolCredentialsMissing,
                "B2Brouter credentials are unavailable. Configure the provider before retrying.", true,
                cancellationToken);
            return;
        }
        if (delivery.SubmissionAttempts >= MaximumSubmissionAttempts)
        {
            await PermanentlyFailAsync(delivery, "peppol_retry_limit_reached",
                "Peppol validation or transport retry limits were reached without external submission.", false,
                cancellationToken);
            return;
        }
        if (delivery.Status == CustomerInvoiceElectronicDeliveryStatuses.Submitting)
        {
            await RequireReconciliationAsync(delivery, "peppol_submission_interrupted",
                "The prior B2Brouter request may have been accepted. Reconciliation is required before any retry.",
                cancellationToken);
            return;
        }

        delivery.StartParticipantVerification(Now());
        await db.SaveChangesAsync(cancellationToken);
        var participant = await ValidateParticipantAsync(delivery, cancellationToken);
        if (participant.Status == "pending" || participant.IsRetryable)
        {
            delivery.Fail(participant.Status == "rate_limited" ? CustomerInvoiceDeliveryReasonCodes.PeppolRateLimited
                : "peppol_participant_lookup_retryable", participant.SafeMessage, true, Now());
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(participant.SafeMessage);
        }
        if (participant.Status != "valid")
        {
            await PermanentlyFailAsync(delivery, CustomerInvoiceDeliveryReasonCodes.PeppolRecipientUnsupported,
                participant.SafeMessage, true, cancellationToken);
            return;
        }

        var issued = await db.IssuedStatutoryDocuments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == delivery.IssuedDocumentId &&
                                       x.SnapshotHash == delivery.SnapshotHash, cancellationToken)
            ?? throw new PermanentBackgroundJobException("The immutable issued invoice snapshot is unavailable.");
        var artifact = await db.CustomerInvoiceRenderedArtifacts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == delivery.ArtifactId &&
                                       x.ContentHash == delivery.ArtifactHash && x.Status == CustomerInvoiceRenderStatuses.Rendered,
                cancellationToken)
            ?? throw new PermanentBackgroundJobException("The immutable invoice PDF is unavailable.");

        delivery.StartDocumentValidation(Now());
        await db.SaveChangesAsync(cancellationToken);
        byte[] pdf;
        if (artifact.ObjectKey is null)
            throw new PermanentBackgroundJobException("The rendered invoice object is unavailable.");
        await using (var source = await storage.OpenReadAsync(artifact.ObjectKey, cancellationToken))
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            pdf = buffer.ToArray();
        }
        if (pdf.Length > options.MaximumAttachmentBytes)
        {
            await PermanentlyFailAsync(delivery, CustomerInvoiceDeliveryReasonCodes.PeppolValidationFailed,
                "The invoice PDF is larger than the configured Peppol attachment limit.", true, cancellationToken);
            return;
        }
        string? originalDocumentNumber = null;
        if (delivery.DocumentType == "credit_note")
        {
            originalDocumentNumber = issued.OriginalIssuedDocumentId.HasValue
                ? await db.IssuedStatutoryDocuments.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.Id == issued.OriginalIssuedDocumentId.Value &&
                                x.DocumentType == StatutoryDocumentTypes.CustomerInvoice)
                    .Select(x => x.DocumentNumber).SingleOrDefaultAsync(cancellationToken)
                : null;
            if (string.IsNullOrWhiteSpace(originalDocumentNumber))
            {
                await PermanentlyFailAsync(delivery, CustomerInvoiceDeliveryReasonCodes.PeppolValidationFailed,
                    "The credit note's immutable original invoice reference is unavailable.", true,
                    cancellationToken);
                return;
            }
        }
        var built = B2BRouterPeppolBisBillingDocument.Build(issued.SnapshotJson, delivery, artifact.FileName, pdf,
            options.PaymentAccountId, options.PaymentAccountName, options.PaymentServiceProviderId,
            originalDocumentNumber);
        if (!built.Validation.IsValid)
        {
            await PermanentlyFailAsync(delivery, CustomerInvoiceDeliveryReasonCodes.PeppolValidationFailed,
                string.Join(" ", built.Validation.SafeMessages), true, cancellationToken);
            return;
        }
        delivery.RecordDocumentHash(built.Validation.DocumentHash, Now());
        await db.SaveChangesAsync(cancellationToken);
        var providerValidation = await ValidateDocumentWithProviderAsync(built.Content, cancellationToken);
        if (!providerValidation.IsValid)
        {
            if (providerValidation.ReasonCodes.Contains("retryable_transport", StringComparer.Ordinal))
            {
                delivery.Fail("peppol_validation_service_retryable", providerValidation.SafeMessages.First(), true, Now());
                await db.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException(providerValidation.SafeMessages.First());
            }
            await PermanentlyFailAsync(delivery, CustomerInvoiceDeliveryReasonCodes.PeppolValidationFailed,
                string.Join(" ", providerValidation.SafeMessages), true, cancellationToken);
            return;
        }

        delivery.StartSubmission(Now());
        await db.SaveChangesAsync(cancellationToken); // durable ambiguity boundary before the provider write
        await SubmitAsync(delivery, built.Content, cancellationToken);
    }

    public async Task ReconcileAsync(Guid companyId, Guid deliveryId, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        var delivery = await DeliveryAsync(companyId, deliveryId, cancellationToken);
        if (delivery.Status is CustomerInvoiceElectronicDeliveryStatuses.Delivered or CustomerInvoiceElectronicDeliveryStatuses.Rejected)
            return;
        if (!options.Enabled || !HasCredentials(options, companyId))
            throw new InvalidOperationException("B2Brouter credentials are required for reconciliation.");
        delivery.StartReconciliation(Now());
        await db.SaveChangesAsync(cancellationToken);
        var response = await QueryStatusAsync(delivery, cancellationToken);
        await ApplyProviderStatusAsync(delivery, response, "poll", cancellationToken);
    }

    public async Task<CustomerInvoiceElectronicWebhookResult> ProcessWebhookAsync(
        CustomerInvoiceElectronicWebhookCommand command, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.WebhooksEnabled || string.IsNullOrWhiteSpace(options.WebhookSecret))
            return new(false, false, "Signed B2Brouter webhooks are not enabled.");
        if (!VerifyWebhookSignature(command.Signature, command.RawBody, command.ReceivedUtc, options, out var evidenceHash))
        {
            telemetry.WebhookRejected();
            return new(false, false, "The B2Brouter webhook signature or timestamp is invalid.");
        }
        using var json = JsonDocument.Parse(command.RawBody);
        var root = json.RootElement;
        if (!TryString(root, "code", out var code) || code != "issued_invoice.state_change" ||
            !root.TryGetProperty("data", out var data))
            return new(false, false, "The webhook event type is not supported.");
        var providerReference = FindScalar(data, "invoice_id", "invoiceId", "id");
        var state = FindScalar(data, "state", "status");
        if (string.IsNullOrWhiteSpace(providerReference) || string.IsNullOrWhiteSpace(state))
            return new(false, false, "The webhook does not contain an invoice reference and state.");
        var delivery = await db.CustomerInvoiceElectronicDeliveries.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProviderKey == ProviderKey && x.ProviderReference == providerReference,
                cancellationToken);
        if (delivery is null)
            return new(false, false, "The webhook invoice reference is not known.");
        var connectionAccount = ResolveAccountId(options, delivery.CompanyId);
        var account = FindScalar(data, "account_id", "accountId", "account");
        if (connectionAccount is null || (!string.IsNullOrWhiteSpace(account) &&
            !string.Equals(account, connectionAccount, StringComparison.Ordinal)))
            return new(false, false, "The webhook account does not match the company B2Brouter connection.");
        var eventKey = FindScalar(root, "id") ?? $"webhook:{evidenceHash}";
        if (await db.CustomerInvoiceElectronicDeliveryEvents.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == delivery.CompanyId && x.ProviderKey == ProviderKey &&
                               x.EventKey == eventKey, cancellationToken))
            return new(true, true, "The webhook event was already applied.");
        var status = MapProviderState(providerReference, state);
        await ApplyProviderStatusAsync(delivery, status, "webhook", cancellationToken, eventKey, evidenceHash);
        telemetry.WebhookAccepted();
        return new(true, false, "The B2Brouter acknowledgement was applied.");
    }

    private async Task<CustomerInvoiceElectronicParticipantValidation> ValidateParticipantAsync(
        CustomerInvoiceElectronicDelivery delivery, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(B2BRouterOptions.HttpClientName);
        using var response = await client.GetAsync($"directory/{Uri.EscapeDataString(delivery.ParticipantScheme)}/{Uri.EscapeDataString(delivery.ParticipantIdentifier)}", cancellationToken);
        var classification = ClassifyParticipantResponse(response.StatusCode, delivery.ParticipantScheme,
            delivery.ParticipantIdentifier);
        if (classification.Status != "valid") return classification;
        var body = await ReadBoundedAsync(response, 256_000, cancellationToken);
        var documentTypes = CollectStringValues(body).Where(x => x.Contains("billing", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("ubl.invoice", StringComparison.OrdinalIgnoreCase) || x.Contains("ubl.credit", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToArray();
        return classification with { SupportedDocumentTypes = documentTypes };
    }

    internal static CustomerInvoiceElectronicParticipantValidation ClassifyParticipantResponse(
        HttpStatusCode statusCode, string participantScheme, string participantIdentifier)
    {
        if (statusCode == HttpStatusCode.Accepted)
            return new("pending", "B2Brouter is still checking the recipient in the Peppol directory.",
                participantScheme, participantIdentifier, [], true);
        if (statusCode == HttpStatusCode.NotFound)
            return new("not_found", "The recipient is not registered for this Peppol participant identifier.",
                participantScheme, participantIdentifier, [], false);
        if (statusCode == HttpStatusCode.UnprocessableEntity)
            return new("invalid", "The recipient Peppol participant identifier is invalid.",
                participantScheme, participantIdentifier, [], false);
        if ((int)statusCode == 424 || statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500)
            return new(statusCode == HttpStatusCode.TooManyRequests ? "rate_limited" : "upstream_unavailable",
                "The Peppol directory is temporarily unavailable. The lookup will be retried.",
                participantScheme, participantIdentifier, [], true);
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new("credentials_invalid", "B2Brouter rejected the configured credentials.",
                participantScheme, participantIdentifier, [], false);
        if (statusCode != HttpStatusCode.OK)
            return new("upstream_unavailable", "The Peppol directory returned an unexpected response. The lookup will be retried.",
                participantScheme, participantIdentifier, [], true);
        return new("valid", "The recipient is registered in the Peppol directory.", participantScheme,
            participantIdentifier, [], false);
    }

    private async Task<CustomerInvoiceElectronicDocumentValidation> ValidateDocumentWithProviderAsync(
        byte[] content, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(B2BRouterOptions.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "documents/validate")
        { Content = new ByteArrayContent(content) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return new(true, B2BRouterOptions.PeppolBisBillingProfile, B2BRouterOptions.PeppolBisBillingVersion,
                Hash(content), [], []);
        if (response.StatusCode == HttpStatusCode.BadRequest)
            return new(false, B2BRouterOptions.PeppolBisBillingProfile, B2BRouterOptions.PeppolBisBillingVersion,
                Hash(content), ["provider_schema_validation_failed"],
                ["B2Brouter rejected the Peppol BIS 3 document during schema or business-rule validation."]);
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            return new(false, B2BRouterOptions.PeppolBisBillingProfile, B2BRouterOptions.PeppolBisBillingVersion,
                Hash(content), ["retryable_transport"],
                ["B2Brouter document validation is temporarily unavailable and will be retried."]);
        return new(false, B2BRouterOptions.PeppolBisBillingProfile, B2BRouterOptions.PeppolBisBillingVersion,
            Hash(content), ["provider_validation_unavailable"],
            ["B2Brouter could not validate the document with the configured credentials."]);
    }

    private async Task SubmitAsync(CustomerInvoiceElectronicDelivery delivery, byte[] content,
        CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        var client = httpClientFactory.CreateClient(B2BRouterOptions.HttpClientName);
        var documentCode = delivery.DocumentType == "credit_note" ? B2BRouterOptions.CreditNoteDocumentTypeCode
            : B2BRouterOptions.InvoiceDocumentTypeCode;
        var accountId = ResolveAccountId(options, delivery.CompanyId)
            ?? throw new InvalidOperationException("The company B2Brouter account mapping is unavailable.");
        var url = $"accounts/{Uri.EscapeDataString(accountId)}/invoices/import?send_after_import=true&transport_type_code_for_contact=peppol&document_type_code_for_contact={Uri.EscapeDataString(documentCode)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(content) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        request.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            { FileNameStar = $"{delivery.DocumentNumber}.xml" };
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", delivery.IdempotencyKey);
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, cancellationToken); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("B2Brouter submission outcome is ambiguous for electronic delivery {DeliveryId} in company {CompanyId}; transport exception type {ExceptionType}.",
                delivery.Id, delivery.CompanyId, ex.GetType().Name);
            await RequireReconciliationAsync(delivery, CustomerInvoiceDeliveryReasonCodes.ReconciliationRequired,
                "B2Brouter may have accepted the invoice before the connection failed. Reconciliation is required and the invoice will not be resubmitted.", cancellationToken);
            telemetry.Ambiguous();
            return;
        }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Created)
            {
                var body = await ReadBoundedAsync(response, 256_000, cancellationToken);
                var reference = FindScalar(body, "id", "invoice_id");
                var state = FindScalar(body, "state", "status") ?? "sending";
                if (string.IsNullOrWhiteSpace(reference))
                {
                    await RequireReconciliationAsync(delivery, CustomerInvoiceDeliveryReasonCodes.ReconciliationRequired,
                        "B2Brouter accepted the import but did not return a usable invoice reference. Reconciliation is required.", cancellationToken);
                    return;
                }
                await ApplyProviderStatusAsync(delivery, MapProviderState(reference, state), "submission",
                    cancellationToken);
                telemetry.Submitted();
                return;
            }
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotAcceptable or HttpStatusCode.UnprocessableEntity)
            {
                await PermanentlyFailAsync(delivery, CustomerInvoiceDeliveryReasonCodes.PeppolValidationFailed,
                    "B2Brouter rejected the structured invoice before transmission. Review the recipient and invoice fields.", true, cancellationToken);
                return;
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                await PermanentlyFailAsync(delivery, CustomerInvoiceDeliveryReasonCodes.PeppolCredentialsMissing,
                    "B2Brouter rejected the configured account or API key.", true, cancellationToken);
                return;
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                delivery.Fail(response.StatusCode == HttpStatusCode.TooManyRequests
                    ? CustomerInvoiceDeliveryReasonCodes.PeppolRateLimited : "peppol_provider_retryable",
                    "B2Brouter did not accept the request. The delivery will be retried with bounded backoff.", true, Now());
                await db.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException("B2Brouter submission is temporarily unavailable.");
            }
            await RequireReconciliationAsync(delivery, CustomerInvoiceDeliveryReasonCodes.ReconciliationRequired,
                "B2Brouter returned an unexpected submission response. Reconcile before any retry.", cancellationToken);
        }
    }

    private async Task<CustomerInvoiceElectronicProviderStatus> QueryStatusAsync(
        CustomerInvoiceElectronicDelivery delivery, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        var client = httpClientFactory.CreateClient(B2BRouterOptions.HttpClientName);
        var accountId = ResolveAccountId(options, delivery.CompanyId)
            ?? throw new InvalidOperationException("The company B2Brouter account mapping is unavailable.");
        var url = delivery.ProviderReference is not null
            ? $"invoices/{Uri.EscapeDataString(delivery.ProviderReference)}"
            : $"accounts/{Uri.EscapeDataString(accountId)}/invoices?number={Uri.EscapeDataString(delivery.DocumentNumber)}&limit=10";
        using var response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new(CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired, delivery.ProviderReference,
                null, "B2Brouter has not exposed a matching invoice yet.", false, false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            return new(CustomerInvoiceElectronicDeliveryOutcomes.RetryableFailure, delivery.ProviderReference,
                null, "B2Brouter status lookup is temporarily unavailable.", false, false);
        if (!response.IsSuccessStatusCode)
            return new(CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired, delivery.ProviderReference,
                null, "B2Brouter status could not be confirmed with the configured connection.", false, false);
        var body = await ReadBoundedAsync(response, 512_000, cancellationToken);
        var reference = delivery.ProviderReference ?? FindInvoiceReferenceByNumber(body, delivery.DocumentNumber);
        var state = reference is null ? null : FindStateForReference(body, reference);
        return reference is null || state is null
            ? new(CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired, null, null,
                "No exact B2Brouter invoice match is currently visible.", false, false)
            : MapProviderState(reference, state);
    }

    private async Task ApplyProviderStatusAsync(CustomerInvoiceElectronicDelivery delivery,
        CustomerInvoiceElectronicProviderStatus status, string source, CancellationToken cancellationToken,
        string? eventKey = null, string? evidenceHash = null)
    {
        var now = Now();
        if (!string.IsNullOrWhiteSpace(status.ProviderReference) && delivery.ProviderReference is null &&
            status.Outcome is not CustomerInvoiceElectronicDeliveryOutcomes.Delivered)
            delivery.Accepted(status.ProviderReference!, status.ProviderState, now, NextPoll(now));
        switch (status.Outcome)
        {
            case CustomerInvoiceElectronicDeliveryOutcomes.Delivered:
                if (delivery.ProviderReference is null && status.ProviderReference is not null)
                    delivery.Accepted(status.ProviderReference, status.ProviderState, now, NextPoll(now));
                delivery.Delivered(status.ProviderState, now);
                telemetry.Delivered();
                break;
            case CustomerInvoiceElectronicDeliveryOutcomes.Rejected:
                delivery.Reject(CustomerInvoiceDeliveryReasonCodes.PeppolRejected, status.SafeMessage,
                    status.ProviderState, now);
                telemetry.Rejected();
                await QueueFallbackIfAllowedAsync(delivery, CustomerInvoiceDeliveryReasonCodes.PeppolRejected,
                    cancellationToken, providerProvedNoDelivery: true);
                break;
            case CustomerInvoiceElectronicDeliveryOutcomes.Accepted:
            case CustomerInvoiceElectronicDeliveryOutcomes.Queued:
                if (delivery.ProviderReference is null && status.ProviderReference is not null)
                    delivery.Accepted(status.ProviderReference, status.ProviderState, now, NextPoll(now));
                else delivery.ScheduleReconciliation(status.ProviderState, now, NextPoll(now));
                QueueReconciliation(delivery, null);
                break;
            case CustomerInvoiceElectronicDeliveryOutcomes.RetryableFailure:
            case CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired:
                if (delivery.ReconciliationAttempts >= optionsMonitor.CurrentValue.MaximumReconciliationAttempts)
                    delivery.RequireReconciliation(CustomerInvoiceDeliveryReasonCodes.ReconciliationRequired,
                        "Automatic B2Brouter reconciliation reached its limit. An operator must review the provider record.",
                        now, NextPoll(now));
                else
                {
                    delivery.RequireReconciliation(CustomerInvoiceDeliveryReasonCodes.ReconciliationRequired,
                        status.SafeMessage, now, NextPoll(now));
                    QueueReconciliation(delivery, null);
                }
                break;
        }
        AddEvent(delivery, eventKey ?? $"{source}:{delivery.ReconciliationAttempts}:{status.ProviderState}:{now.Ticks}",
            source, status.Outcome, status.ProviderState, status.SafeMessage,
            evidenceHash ?? Hash($"{status.ProviderReference}|{status.ProviderState}|{status.Outcome}"), now);
        await audit.WriteAsync(new(delivery.CompanyId, AuditActorTypes.System, null,
            $"finance.customer_invoice.peppol_{status.Outcome}", "finance_invoice", delivery.InvoiceId.ToString("N"),
            status.Outcome is CustomerInvoiceElectronicDeliveryOutcomes.Delivered or CustomerInvoiceElectronicDeliveryOutcomes.Accepted
                ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Failed,
            status.SafeMessage, ["finance", "invoice", "peppol", source], new Dictionary<string, string?>
            {
                ["deliveryId"] = delivery.Id.ToString("N"), ["providerReference"] = delivery.ProviderReference,
                ["providerState"] = status.ProviderState, ["profile"] = delivery.Profile
            }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task PermanentlyFailAsync(CustomerInvoiceElectronicDelivery delivery, string code,
        string summary, bool allowFallback, CancellationToken cancellationToken)
    {
        delivery.Fail(code, summary, false, Now());
        AddEvent(delivery, $"failure:{delivery.SubmissionAttempts}:{code}", "adapter", delivery.Outcome,
            delivery.ProviderState, summary, Hash($"{delivery.Id:N}|{code}|{delivery.SubmissionAttempts}"), Now());
        if (allowFallback) await QueueFallbackIfAllowedAsync(delivery, code, cancellationToken);
        await audit.WriteAsync(new(delivery.CompanyId, AuditActorTypes.System, null,
            "finance.customer_invoice.peppol_failed", "finance_invoice", delivery.InvoiceId.ToString("N"),
            AuditEventOutcomes.Failed, summary, ["finance", "invoice", "peppol"],
            new Dictionary<string, string?> { ["deliveryId"] = delivery.Id.ToString("N"), ["failureCode"] = code }),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        telemetry.Failed();
    }

    private async Task RequireReconciliationAsync(CustomerInvoiceElectronicDelivery delivery, string code,
        string summary, CancellationToken cancellationToken)
    {
        delivery.RequireReconciliation(code, summary, Now(), NextPoll(Now()));
        AddEvent(delivery, $"ambiguous:{delivery.SubmissionAttempts}:{delivery.ReconciliationAttempts}", "adapter",
            CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired, delivery.ProviderState, summary,
            Hash($"{delivery.Id:N}|{delivery.SubmissionAttempts}|{delivery.ReconciliationAttempts}"), Now());
        QueueReconciliation(delivery, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task QueueFallbackIfAllowedAsync(CustomerInvoiceElectronicDelivery delivery, string reasonCode,
        CancellationToken cancellationToken, bool providerProvedNoDelivery = false)
    {
        if (!delivery.AllowEmailFallback || string.IsNullOrWhiteSpace(delivery.FallbackRecipientEmail) ||
            delivery.FallbackEmailDeliveryId.HasValue || (delivery.ExternalSubmissionMayExist && !providerProvedNoDelivery))
            return;
        var artifact = await db.CustomerInvoiceRenderedArtifacts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == delivery.CompanyId && x.Id == delivery.ArtifactId &&
                                       x.ContentHash == delivery.ArtifactHash && x.Status == CustomerInvoiceRenderStatuses.Rendered,
                cancellationToken);
        if (artifact is null) return;
        var email = new CustomerInvoiceEmailDelivery(Guid.NewGuid(), delivery.CompanyId, delivery.InvoiceId,
            delivery.ArtifactId, delivery.ArtifactHash, delivery.FallbackRecipientEmail, delivery.SnapshotHash,
            $"Invoice {delivery.DocumentNumber}", delivery.RequestReason,
            $"peppol-fallback:{delivery.Id:N}", delivery.RequestedByUserId, Now(),
            CustomerInvoiceEmailRequestSources.PeppolFallback, reasonCode, ProviderKey);
        db.CustomerInvoiceEmailDeliveries.Add(email);
        delivery.RecordFallback(email.Id, Now());
        outbox.Enqueue(delivery.CompanyId, CompanyOutboxTopics.CustomerInvoiceEmailDeliveryRequested,
            new CustomerInvoiceEmailDeliveryRequestedMessage(delivery.CompanyId, email.Id, null),
            idempotencyKey: $"invoice-email:{delivery.CompanyId:N}:{email.IdempotencyKey}");
    }

    private void QueueReconciliation(CustomerInvoiceElectronicDelivery delivery, string? correlationId)
    {
        var available = delivery.NextReconcileUtc ?? NextPoll(Now());
        outbox.Enqueue(delivery.CompanyId, CompanyOutboxTopics.CustomerInvoiceElectronicReconciliationRequested,
            new CustomerInvoiceElectronicReconciliationRequestedMessage(delivery.CompanyId, delivery.Id,
                ProviderKey, correlationId), correlationId, available,
            idempotencyKey: $"invoice-peppol-reconcile:{delivery.CompanyId:N}:{delivery.Id:N}:{delivery.ReconciliationAttempts}:{available.Ticks}");
    }

    private void AddEvent(CustomerInvoiceElectronicDelivery delivery, string eventKey, string source,
        string outcome, string? providerState, string safeSummary, string evidenceHash, DateTime occurredUtc)
    {
        if (db.CustomerInvoiceElectronicDeliveryEvents.Local.Any(x => x.CompanyId == delivery.CompanyId &&
            x.ProviderKey == ProviderKey && x.EventKey == eventKey)) return;
        db.CustomerInvoiceElectronicDeliveryEvents.Add(new(Guid.NewGuid(), delivery.CompanyId, delivery.Id,
            ProviderKey, eventKey, source, outcome, providerState, safeSummary, evidenceHash, occurredUtc));
    }

    private async Task<CustomerInvoiceElectronicDelivery> DeliveryAsync(Guid companyId, Guid deliveryId,
        CancellationToken cancellationToken) => await db.CustomerInvoiceElectronicDeliveries.IgnoreQueryFilters()
        .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == deliveryId, cancellationToken)
        ?? throw new PermanentBackgroundJobException("The company-scoped Peppol delivery could not be found.");

    internal static CustomerInvoiceElectronicProviderStatus MapProviderState(string reference, string state)
    {
        var normalized = state.Trim().ToLowerInvariant();
        return normalized switch
        {
            "sent" or "accepted" or "registered" or "paid" or "closed" =>
                new(CustomerInvoiceElectronicDeliveryOutcomes.Delivered, reference, normalized,
                    normalized == "sent" ? "B2Brouter confirms that the invoice was sent successfully to the recipient."
                        : "B2Brouter confirms a recipient acknowledgement for the invoice.", true, false),
            "refused" or "error" or "discarded" =>
                new(CustomerInvoiceElectronicDeliveryOutcomes.Rejected, reference, normalized,
                    "B2Brouter reports that the Peppol invoice was rejected.", true, true),
            "new" or "issued" or "sending" =>
                new(CustomerInvoiceElectronicDeliveryOutcomes.Accepted, reference, normalized,
                    "B2Brouter accepted the invoice for processing; recipient delivery is not yet proven.", false, false),
            _ => new(CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired, reference, normalized,
                "B2Brouter returned an unrecognized invoice state that requires reconciliation.", false, false)
        };
    }

    internal static bool VerifyWebhookSignature(string signature, string rawBody, DateTime receivedUtc,
        B2BRouterOptions options, out string evidenceHash)
    {
        evidenceHash = Hash(rawBody);
        var parts = signature?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var timestampText = parts?.FirstOrDefault(x => x.StartsWith("t=", StringComparison.Ordinal))?[2..];
        var signatureHex = parts?.FirstOrDefault(x => x.StartsWith("s=", StringComparison.Ordinal))?[2..];
        if (!long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp) ||
            string.IsNullOrWhiteSpace(signatureHex)) return false;
        DateTime signedUtc;
        try { signedUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { return false; }
        var received = receivedUtc.Kind == DateTimeKind.Utc ? receivedUtc : receivedUtc.ToUniversalTime();
        if (Math.Abs((received - signedUtc).TotalSeconds) > options.WebhookToleranceSeconds) return false;
        byte[] provided;
        try { provided = Convert.FromHexString(signatureHex); } catch (FormatException) { return false; }
        JsonDocument json;
        try { json = JsonDocument.Parse(rawBody); }
        catch (JsonException) { return false; }
        using (json)
        {
            var data = json.RootElement.TryGetProperty("data", out var value) ? value.GetRawText() : rawBody;
            var candidates = new[] { $"{timestamp}.{data}", $"{timestamp}{data}", $"{timestamp}.{rawBody}" };
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.WebhookSecret));
            return candidates.Select(x => hmac.ComputeHash(Encoding.UTF8.GetBytes(x)))
                .Any(expected => expected.Length == provided.Length && CryptographicOperations.FixedTimeEquals(expected, provided));
        }
    }

    private static string? FindInvoiceReferenceByNumber(string json, string number)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var element in EnumerateObjects(document.RootElement))
        {
            var candidateNumber = FindScalar(element, "number", "invoice_number", "invoiceNumber");
            if (string.Equals(candidateNumber, number, StringComparison.Ordinal))
                return FindScalar(element, "id", "invoice_id", "invoiceId");
        }
        return null;
    }

    private static string? FindStateForReference(string json, string reference)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var element in EnumerateObjects(document.RootElement))
        {
            var id = FindScalar(element, "id", "invoice_id", "invoiceId");
            if (string.Equals(id, reference, StringComparison.Ordinal))
                return FindScalar(element, "state", "status");
        }
        return FindScalar(document.RootElement, "state", "status");
    }

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
                foreach (var nested in EnumerateObjects(property.Value)) yield return nested;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var nested in EnumerateObjects(item)) yield return nested;
    }

    private static IEnumerable<string> CollectStringValues(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Collect(document.RootElement).ToArray();
        static IEnumerable<string> Collect(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String) yield return element.GetString()!;
            else if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                    foreach (var value in Collect(property.Value)) yield return value;
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray())
                    foreach (var value in Collect(item)) yield return value;
        }
    }

    private static string? FindScalar(string json, params string[] names)
    { using var document = JsonDocument.Parse(json); return FindScalar(document.RootElement, names); }
    private static string? FindScalar(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
            if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return property.Value.ToString();
        foreach (var property in element.EnumerateObject())
        {
            var nested = property.Value.ValueKind == JsonValueKind.Object ? FindScalar(property.Value, names) : null;
            if (nested is not null) return nested;
        }
        return null;
    }
    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty : string.Empty;
        return value.Length > 0;
    }
    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            if (memory.Length + read > maxBytes) throw new InvalidOperationException("B2Brouter returned an oversized response.");
            memory.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }
    internal static string? ResolveAccountId(B2BRouterOptions options, Guid companyId)
    {
        var companyKey = companyId.ToString("D");
        if (options.CompanyAccountIds.TryGetValue(companyKey, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            return mapped.Trim();
        mapped = options.CompanyAccountIds.FirstOrDefault(x => Guid.TryParse(x.Key, out var mappedCompanyId) &&
            mappedCompanyId == companyId).Value;
        if (!string.IsNullOrWhiteSpace(mapped)) return mapped.Trim();
        if (options.CompanyAccountIds.Count > 0) return null;
        return string.IsNullOrWhiteSpace(options.AccountId) ? null : options.AccountId.Trim();
    }
    private static bool HasCredentials(B2BRouterOptions options, Guid companyId) =>
        ResolveAccountId(options, companyId) is not null && !string.IsNullOrWhiteSpace(options.ApiKey) &&
        Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out _);
    private static bool IsSafeToFallback(CustomerInvoiceElectronicDelivery delivery) =>
        !delivery.ExternalSubmissionMayExist && delivery.Outcome is "validation_failed" or "recipient_unsupported" or "unavailable";
    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
    private DateTime NextPoll(DateTime now) => now.AddSeconds(optionsMonitor.CurrentValue.ReconciliationPollingSeconds);
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    private static string Required(string? value, int max, string message) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? throw new ArgumentException(message) : value.Trim();
}

public sealed class B2BRouterTelemetry
{
    private readonly Counter<long> _queued;
    private readonly Counter<long> _submitted;
    private readonly Counter<long> _delivered;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _ambiguous;
    private readonly Counter<long> _webhookAccepted;
    private readonly Counter<long> _webhookRejected;
    public B2BRouterTelemetry(IMeterFactory meters)
    {
        var meter = meters.Create("VirtualCompany.Finance.B2BRouter");
        _queued = meter.CreateCounter<long>("finance_peppol_queued");
        _submitted = meter.CreateCounter<long>("finance_peppol_submitted");
        _delivered = meter.CreateCounter<long>("finance_peppol_delivered");
        _rejected = meter.CreateCounter<long>("finance_peppol_rejected");
        _failed = meter.CreateCounter<long>("finance_peppol_failed");
        _ambiguous = meter.CreateCounter<long>("finance_peppol_ambiguous");
        _webhookAccepted = meter.CreateCounter<long>("finance_peppol_webhook_accepted");
        _webhookRejected = meter.CreateCounter<long>("finance_peppol_webhook_rejected");
    }
    public void Queued() => _queued.Add(1); public void Submitted() => _submitted.Add(1);
    public void Delivered() => _delivered.Add(1); public void Rejected() => _rejected.Add(1);
    public void Failed() => _failed.Add(1); public void Ambiguous() => _ambiguous.Add(1);
    public void WebhookAccepted() => _webhookAccepted.Add(1); public void WebhookRejected() => _webhookRejected.Add(1);
}

public sealed class B2BRouterHealthCheck(IOptionsMonitor<B2BRouterOptions> optionsMonitor,
    IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled) return HealthCheckResult.Healthy("B2Brouter Peppol delivery is disabled.");
        if ((string.IsNullOrWhiteSpace(options.AccountId) && options.CompanyAccountIds.Count == 0) ||
            string.IsNullOrWhiteSpace(options.ApiKey))
            return HealthCheckResult.Unhealthy("B2Brouter Peppol credentials are incomplete.");
        try
        {
            using var response = await httpClientFactory.CreateClient(B2BRouterOptions.HttpClientName)
                .GetAsync("invoice_states", cancellationToken);
            return response.IsSuccessStatusCode ? HealthCheckResult.Healthy("B2Brouter API authentication succeeded.")
                : HealthCheckResult.Degraded("B2Brouter API authentication or availability check failed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { return HealthCheckResult.Degraded("B2Brouter API could not be reached."); }
    }
}
