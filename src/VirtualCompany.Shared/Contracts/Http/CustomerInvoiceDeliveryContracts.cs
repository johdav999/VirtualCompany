using System.Security.Claims;
using System.Text;
using VirtualCompany.Application.Finance;
namespace VirtualCompany.Api.Controllers;

public sealed record InvoiceRenderRequest(string Locale = "en-US", string TemplateVersion = "native-invoice-pdf-2026.1");

public sealed record InvoiceEmailDeliveryRequest(Guid ArtifactId, string? RecipientEmail, string Reason, string IdempotencyKey);

public sealed record InvoicePreferredDeliveryRequest(Guid ArtifactId, string? RecipientEmail, bool AllowEmailFallback, string Reason, string IdempotencyKey);

public sealed record InvoiceResendRequest(string Reason, string IdempotencyKey);

public sealed record InvoiceElectronicDeliveryRequest(Guid ArtifactId, bool AllowEmailFallback, string? RecipientEmail,
    string Reason, string IdempotencyKey);

public sealed record InvoiceElectronicOperatorRequest(string Reason);
