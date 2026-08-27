using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/accounting/customer-invoices")]
[Authorize(Policy = CompanyPolicies.AccountingView)]
[RequireCompanyContext]
public sealed class CustomerInvoiceDeliveryController(ICustomerInvoiceDeliveryService delivery) : ControllerBase
{
    [HttpPost("{invoiceId:guid}/render")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public Task<ActionResult<CustomerInvoiceArtifactDto>> Render(Guid companyId, Guid invoiceId, [FromBody] InvoiceRenderRequest request, CancellationToken ct) => Run(() => delivery.RequestRenderAsync(new(companyId, invoiceId, request.Locale, request.TemplateVersion, Actor(), Correlation()), ct));

    [HttpGet("artifacts/{artifactId:guid}")]
    public Task<ActionResult<CustomerInvoiceArtifactDto>> Artifact(Guid companyId, Guid artifactId, CancellationToken ct) => Run(() => delivery.GetArtifactAsync(new(companyId, artifactId), ct));

    [HttpGet("artifacts/{artifactId:guid}/download")]
    public async Task<IActionResult> Download(Guid companyId, Guid artifactId, CancellationToken ct)
    {
        try { var result = await delivery.OpenArtifactAsync(companyId, artifactId, ct); return File(result.Content, "application/pdf", result.FileName, enableRangeProcessing: true); }
        catch (CustomerInvoiceDeliveryException e) { return Problem(statusCode: e.Conflict ? 409 : 404, title: "Invoice PDF unavailable", detail: e.Message, extensions: new Dictionary<string, object?> { ["reasonCode"] = e.ReasonCode }); }
    }

    [HttpPost("{invoiceId:guid}/email-deliveries")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public Task<ActionResult<CustomerInvoiceEmailDeliveryDto>> Email(Guid companyId, Guid invoiceId, [FromBody] InvoiceEmailDeliveryRequest request, CancellationToken ct) => Run(() => delivery.RequestEmailAsync(new(companyId, invoiceId, request.ArtifactId, request.RecipientEmail, request.Reason, request.IdempotencyKey, Actor(), Correlation()), ct));

    [HttpPost("{invoiceId:guid}/preferred-delivery")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public Task<ActionResult<CustomerInvoicePreferredDeliveryDto>> PreferredDelivery(Guid companyId, Guid invoiceId, [FromBody] InvoicePreferredDeliveryRequest request, CancellationToken ct) => Run(() => delivery.RequestPreferredDeliveryAsync(new(companyId, invoiceId, request.ArtifactId, request.RecipientEmail, request.AllowEmailFallback, request.Reason, request.IdempotencyKey, Actor(), Correlation()), ct));

    [HttpGet("email-deliveries/{deliveryId:guid}")]
    public Task<ActionResult<CustomerInvoiceEmailDeliveryDto>> GetDelivery(Guid companyId, Guid deliveryId, CancellationToken ct) => Run(() => delivery.GetDeliveryAsync(new(companyId, deliveryId), ct));

    [HttpPost("email-deliveries/{deliveryId:guid}/resend")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public Task<ActionResult<CustomerInvoiceEmailDeliveryDto>> Resend(Guid companyId, Guid deliveryId, [FromBody] InvoiceResendRequest request, CancellationToken ct) => Run(() => delivery.ResendAsync(new(companyId, deliveryId, request.Reason, request.IdempotencyKey, Actor(), Correlation()), ct));

    [HttpGet("electronic-provider")]
    public Task<ActionResult<CustomerInvoiceElectronicProviderCapabilityDto>> ElectronicProvider(Guid companyId, CancellationToken ct) =>
        Run(() => delivery.GetElectronicProviderCapabilityAsync(companyId, ct));

    [HttpPost("{invoiceId:guid}/electronic-deliveries")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public Task<ActionResult<CustomerInvoiceElectronicDeliveryDto>> Electronic(Guid companyId, Guid invoiceId,
        [FromBody] InvoiceElectronicDeliveryRequest request, CancellationToken ct) =>
        Run(() => delivery.RequestElectronicAsync(new(companyId, invoiceId, request.ArtifactId,
            request.AllowEmailFallback, request.RecipientEmail, request.Reason, request.IdempotencyKey,
            Actor(), Correlation()), ct));

    [HttpGet("electronic-deliveries/{deliveryId:guid}")]
    public Task<ActionResult<CustomerInvoiceElectronicDeliveryDto>> GetElectronic(Guid companyId, Guid deliveryId,
        CancellationToken ct) => Run(() => delivery.GetElectronicDeliveryAsync(new(companyId, deliveryId), ct));

    [HttpPost("electronic-deliveries/{deliveryId:guid}/retry")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public Task<ActionResult<CustomerInvoiceElectronicDeliveryDto>> RetryElectronic(Guid companyId, Guid deliveryId,
        [FromBody] InvoiceElectronicOperatorRequest request, CancellationToken ct) =>
        Run(() => delivery.RetryElectronicAsync(new(companyId, deliveryId, request.Reason, Actor(), Correlation()), ct));

    [HttpPost("electronic-deliveries/{deliveryId:guid}/reconcile")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public Task<ActionResult<CustomerInvoiceElectronicDeliveryDto>> ReconcileElectronic(Guid companyId, Guid deliveryId,
        [FromBody] InvoiceElectronicOperatorRequest request, CancellationToken ct) =>
        Run(() => delivery.ReconcileElectronicAsync(new(companyId, deliveryId, request.Reason, Actor(), Correlation()), ct));

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (CustomerInvoiceDeliveryException e) { return Problem(statusCode: e.Conflict ? 409 : 400, title: "Invoice delivery request could not be completed", detail: e.Message, extensions: new Dictionary<string, object?> { ["reasonCode"] = e.ReasonCode }); }
        catch (ArgumentException e) { return Problem(statusCode: 400, title: "Invoice delivery request is invalid", detail: e.Message); }
    }
    private Guid Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A signed-in user is required.");
    private string? Correlation() => HttpContext.TraceIdentifier;
}

public sealed record InvoiceRenderRequest(string Locale = "en-US", string TemplateVersion = "native-invoice-pdf-2026.1");
public sealed record InvoiceEmailDeliveryRequest(Guid ArtifactId, string? RecipientEmail, string Reason, string IdempotencyKey);
public sealed record InvoicePreferredDeliveryRequest(Guid ArtifactId, string? RecipientEmail, bool AllowEmailFallback, string Reason, string IdempotencyKey);
public sealed record InvoiceResendRequest(string Reason, string IdempotencyKey);
public sealed record InvoiceElectronicDeliveryRequest(Guid ArtifactId, bool AllowEmailFallback, string? RecipientEmail,
    string Reason, string IdempotencyKey);
public sealed record InvoiceElectronicOperatorRequest(string Reason);

[ApiController]
[Route("api/integrations/b2brouter/webhooks")]
[AllowAnonymous]
public sealed class B2BRouterWebhookController(IEnumerable<ICustomerInvoiceElectronicDeliveryProvider> providers)
    : ControllerBase
{
    [HttpPost("invoice-state")]
    [RequestSizeLimit(256_000)]
    public async Task<IActionResult> InvoiceState(CancellationToken cancellationToken)
    {
        var provider = providers.SingleOrDefault(x => string.Equals(x.ProviderKey, "b2brouter",
            StringComparison.OrdinalIgnoreCase));
        if (provider is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (!Request.Headers.TryGetValue("X-B2Brouter-Signature", out var signature) ||
            string.IsNullOrWhiteSpace(signature)) return Unauthorized();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, false, 8192, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(rawBody) > 256_000) return StatusCode(StatusCodes.Status413PayloadTooLarge);
        var result = await provider.ProcessWebhookAsync(new(signature.ToString(), rawBody, DateTime.UtcNow),
            cancellationToken);
        if (result.Accepted) return Ok(new { result.Duplicate });
        return result.SafeMessage.Contains("not known", StringComparison.OrdinalIgnoreCase) ? NotFound()
            : Unauthorized();
    }
}
