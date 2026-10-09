namespace VirtualCompany.Shared.Contracts.Auth;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record SelectCompanyRequest(Guid CompanyId)
{
    public Guid CompanyId { get; set; } = CompanyId;

    public SelectCompanyRequest() : this(default(Guid))
    {
    }
}
