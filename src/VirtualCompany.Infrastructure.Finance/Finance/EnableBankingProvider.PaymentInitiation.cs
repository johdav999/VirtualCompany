using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class EnableBankingProvider
{
    PaymentInitiationProviderDescriptor IPaymentInitiationProvider.Descriptor => new(
        ProviderKeyValue, "Enable Banking", IsConfigured() && _options.PaymentInitiationEnabled,
        _options.PaymentInitiationEnabled ? [PaymentRails.SepaCreditTransfer] : []);

    public async Task<PaymentProviderSubmissionResult> SubmitAsync(PaymentProviderSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        EnsurePaymentInitiationConfigured();
        if (request.Instructions.Count == 0)
            throw Permanent(PaymentExecutionReasonCodes.InvalidLifecycle, "The approved batch has no payment instructions.");
        if (request.Instructions.Any(x => x.Rail != PaymentRails.SepaCreditTransfer))
            throw Permanent(PaymentExecutionReasonCodes.RailUnsupported,
                "Enable Banking payment initiation currently supports approved SEPA credit-transfer instructions only.");
        var currencies = request.Instructions.Select(x => x.Currency).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (currencies.Length != 1)
            throw Permanent(PaymentExecutionReasonCodes.RailUnsupported,
                "One Enable Banking payment request cannot mix instruction currencies.");

        var (country, institution) = ParseInstitutionId(request.InstitutionId);
        var paymentType = request.Instructions.Count == 1 ? _options.SingleSepaPaymentType : _options.BulkSepaPaymentType;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            aspsp = new { country, name = institution },
            payment_type = paymentType,
            payment_request = new
            {
                credit_transfer_transaction = request.Instructions.Select(instruction => new
                {
                    beneficiary = new
                    {
                        creditor = new { name = instruction.BeneficiaryName },
                        creditor_account = new { identification = instruction.Destination, scheme_name = "IBAN" }
                    },
                    instructed_amount = new { amount = instruction.Amount.ToString("0.00", CultureInfo.InvariantCulture), currency = instruction.Currency },
                    payment_id = new { instruction_id = instruction.InstructionId.ToString("D"), end_to_end_id = instruction.InstructionId.ToString("N") },
                    requested_execution_date = instruction.ExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reference_number = instruction.PaymentReference,
                    remittance_information = new[] { instruction.PaymentReference }
                }).ToArray()
            },
            state = request.ExecutionId.ToString("N"),
            redirect_url = request.RedirectUri.ToString(),
            webhook_url = request.WebhookUri.ToString(),
            psu_type = _options.PsuType,
            psu_id = request.CompanyId.ToString("N"),
            language = "sv"
        });

        try
        {
            using var response = await SendAsync(HttpMethod.Post, "payments", payload, cancellationToken);
            string? paymentId = null;
            try
            {
                using var document = JsonDocument.Parse(response.Payload);
                var root = document.RootElement;
                paymentId = RequiredString(root, "payment_id");
                var status = RequiredString(root, "status").ToUpperInvariant();
                var isFinal = root.TryGetProperty("final_status", out var final) && final.ValueKind == JsonValueKind.True;
                var url = OptionalString(root, "url");
                var hasUsableAuthorizationUri = Uri.TryCreate(url, UriKind.Absolute, out var authorizationUri) &&
                    authorizationUri.Scheme == Uri.UriSchemeHttps;
                var reason = root.TryGetProperty("status_reason_information", out var info) && info.ValueKind == JsonValueKind.Object
                    ? info : default;
                var reasonCode = reason.ValueKind == JsonValueKind.Object ? OptionalString(reason, "status_reason_code") : null;
                var reasonSummary = reason.ValueKind == JsonValueKind.Object ? OptionalString(reason, "status_reason_description") : null;
                if (!isFinal && !hasUsableAuthorizationUri)
                {
                    reasonCode ??= PaymentExecutionReasonCodes.StatusReconciliationRequired;
                    reasonSummary ??= "The provider created a payment reference but did not return a usable HTTPS bank-authorization address.";
                }
                return new(paymentId, hasUsableAuthorizationUri ? authorizationUri : null, status,
                    isFinal, !isFinal, false, response.RequestId, reasonCode, reasonSummary,
                    request.Instructions.Select(x => new PaymentProviderInstructionStatus(x.InstructionId,
                        null, status, reasonCode, reasonSummary, isFinal)).ToArray());
            }
            catch (Exception exception) when (exception is JsonException or BankProviderSafeException)
            {
                throw new PaymentProviderOperationException(PaymentExecutionReasonCodes.SubmissionAmbiguous,
                    "The provider accepted the request but returned unsupported payment evidence. Do not submit again; reconcile the retained provider reference or request ID.",
                    false, true, response.RequestId, paymentId, exception);
            }
        }
        catch (PaymentProviderOperationException) { throw; }
        catch (BankProviderSafeException exception)
        {
            if (exception.ReasonCode == BankFeedReasonCodes.RateLimited)
                throw new PaymentProviderOperationException(exception.ReasonCode,
                    "The payment provider rate limit was reached. Submission will retry with the same approved business identity.", true,
                    innerException: exception);
            if (exception.IsTransient)
                throw new PaymentProviderOperationException(PaymentExecutionReasonCodes.SubmissionAmbiguous,
                    "The payment provider outcome is unknown. Do not submit again until an operator reconciles the provider reference.", false, true,
                    innerException: exception);
            throw new PaymentProviderOperationException(exception.ReasonCode, exception.SafeMessage, false,
                innerException: exception);
        }
    }

    public async Task<PaymentProviderStatusResult> GetStatusAsync(Guid companyId, string providerPaymentId,
        CancellationToken cancellationToken)
    {
        EnsurePaymentInitiationConfigured();
        try
        {
            using var response = await SendAsync(HttpMethod.Get,
                $"payments/{Uri.EscapeDataString(providerPaymentId)}", null, cancellationToken);
            using var document = JsonDocument.Parse(response.Payload);
            return ParseStatus(document.RootElement, response.RequestId);
        }
        catch (BankProviderSafeException exception)
        {
            throw new PaymentProviderOperationException(exception.ReasonCode, exception.SafeMessage,
                exception.IsTransient, false, innerException: exception);
        }
        catch (JsonException exception)
        {
            throw new PaymentProviderOperationException(PaymentExecutionReasonCodes.StatusReconciliationRequired,
                "The provider returned malformed payment-status evidence. Reconcile the retained payment reference before further action.",
                false, true, providerPaymentId: providerPaymentId, innerException: exception);
        }
    }

    public Task<PaymentProviderCancelResult> CancelAsync(Guid companyId, string providerPaymentId,
        CancellationToken cancellationToken) => throw Permanent(PaymentExecutionReasonCodes.CancellationUnsafe,
            "Enable Banking does not expose a safe cancellation operation after payment authorization has started. Review the payment at the bank.");

    public async Task<PaymentProviderWebhookEvent> ValidateWebhookAsync(string authorizationHeader,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        EnsurePaymentInitiationConfigured();
        var token = authorizationHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authorizationHeader[7..].Trim()
            : throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook authorization header is missing.");
        var parts = token.Split('.');
        if (parts.Length != 3) throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook token is malformed.");
        JsonElement header; JsonElement claims;
        try
        {
            using var headerDocument = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            using var claimsDocument = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            header = headerDocument.RootElement.Clone(); claims = claimsDocument.RootElement.Clone();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        { throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook token is malformed.", exception); }

        if (!string.Equals(OptionalString(header, "alg"), "RS256", StringComparison.Ordinal) ||
            !string.Equals(OptionalString(header, "typ"), "JWT", StringComparison.OrdinalIgnoreCase))
            throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook token algorithm is not supported.");
        var x5uText = RequiredString(header, "x5u");
        if (!Uri.TryCreate(x5uText, UriKind.Absolute, out var x5u) || x5u.Scheme != Uri.UriSchemeHttps ||
            !(x5u.Host.Equals("enablebanking.com", StringComparison.OrdinalIgnoreCase) || x5u.Host.EndsWith(".enablebanking.com", StringComparison.OrdinalIgnoreCase)))
            throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook signing-key origin is not trusted.");
        var certificateBytes = await _httpClientFactory.CreateClient(HttpClientName).GetByteArrayAsync(x5u, cancellationToken);
        using var certificate = LoadCertificate(certificateBytes);
        using var rsa = certificate.GetRSAPublicKey() ?? throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid,
            "The payment webhook signing certificate does not contain an RSA public key.");
        var signedInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        try
        {
            if (!rsa.VerifyData(signedInput, Base64UrlDecode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook signature is invalid.");
        }
        catch (FormatException exception)
        { throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook signature is malformed.", exception); }
        if (!string.Equals(RequiredString(claims, "sub"), _options.ApplicationId, StringComparison.Ordinal))
            throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook was issued for a different application.");
        if (!string.Equals(RequiredString(claims, "environment"), _options.Environment, StringComparison.OrdinalIgnoreCase))
            throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook environment does not match this installation.");
        var expectedDigest = "sha256-" + Convert.ToBase64String(SHA256.HashData(payload.Span));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(RequiredString(claims, "msgi")), Encoding.ASCII.GetBytes(expectedDigest)))
            throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook payload digest is invalid.");

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var triggered = OptionalDateTime(root, "webhook_triggered") ?? _clock.GetUtcNow().UtcDateTime;
            return new(RequiredString(root, "payment_id"), RequiredString(root, "webhook_id"),
                RequiredString(root, "payment_status").ToUpperInvariant(),
                root.TryGetProperty("payment_updates_expected", out var updates) && updates.ValueKind == JsonValueKind.True,
                OptionalString(root, "payment_auth_status"), triggered,
                Convert.ToHexString(SHA256.HashData(payload.Span)).ToLowerInvariant());
        }
        catch (Exception exception) when (exception is JsonException or BankProviderSafeException)
        { throw Permanent(PaymentExecutionReasonCodes.WebhookInvalid, "The payment webhook payload is malformed.", exception); }
    }

    private PaymentProviderStatusResult ParseStatus(JsonElement root, string? requestId)
    {
        var providerPaymentId = RequiredString(root, "payment_id");
        var status = RequiredString(root, "status").ToUpperInvariant();
        var isFinal = root.TryGetProperty("final_status", out var final) && final.ValueKind == JsonValueKind.True;
        var reason = root.TryGetProperty("status_reason_information", out var info) && info.ValueKind == JsonValueKind.Object ? info : default;
        var reasonCode = reason.ValueKind == JsonValueKind.Object ? OptionalString(reason, "status_reason_code") : null;
        var reasonSummary = reason.ValueKind == JsonValueKind.Object ? OptionalString(reason, "status_reason_description") : null;
        var details = root.TryGetProperty("payment_details", out var paymentDetails) && paymentDetails.ValueKind == JsonValueKind.Object ? paymentDetails : default;
        var debtor = details.ValueKind == JsonValueKind.Object && details.TryGetProperty("debtor_account", out var debtorAccount) && debtorAccount.ValueKind == JsonValueKind.Object
            ? OptionalString(debtorAccount, "identification") : null;
        var instructionStatuses = new List<PaymentProviderInstructionStatus>();
        if (details.ValueKind == JsonValueKind.Object && details.TryGetProperty("credit_transfer_transaction", out var transactions) && transactions.ValueKind == JsonValueKind.Array)
        {
            foreach (var transaction in transactions.EnumerateArray())
            {
                if (!transaction.TryGetProperty("payment_id", out var paymentIdentity) || paymentIdentity.ValueKind != JsonValueKind.Object ||
                    !Guid.TryParse(OptionalString(paymentIdentity, "instruction_id"), out var instructionId)) continue;
                var transactionStatus = OptionalString(transaction, "transaction_status")?.ToUpperInvariant() ?? status;
                instructionStatuses.Add(new(instructionId, OptionalString(transaction, "transaction_id"), transactionStatus,
                    reasonCode, reasonSummary, isFinal));
            }
        }
        return new(providerPaymentId, status, isFinal, !isFinal, false,
            reasonCode, reasonSummary, string.IsNullOrWhiteSpace(debtor) ? null : Mask(debtor), requestId, instructionStatuses);
    }

    private void EnsurePaymentInitiationConfigured()
    {
        EnsureConfigured();
        if (!_options.PaymentInitiationEnabled)
            throw Permanent(PaymentExecutionReasonCodes.ProviderNotConfigured,
                "Enable Banking payment initiation is disabled. Configure licensed production or sandbox PIS access first.");
    }

    private static PaymentProviderOperationException Permanent(string code, string message, Exception? inner = null) =>
        new(code, message, false, false, innerException: inner);

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(normalized);
    }

    private static X509Certificate2 LoadCertificate(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(text)
            : X509CertificateLoader.LoadCertificate(bytes);
    }
}
