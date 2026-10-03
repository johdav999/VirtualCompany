namespace VirtualCompany.Application.Support;

public interface ISupportReplySourceAccess
{
    Task<string?> FilterAsync(Guid companyId, string? sourceReferencesJson, CancellationToken cancellationToken);
}
