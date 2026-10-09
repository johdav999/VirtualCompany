using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Api.Controllers;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record UpdateFinanceEmailProviderSettingsRequest(string? ClientId, string? ClientSecret)
{
    public string? ClientId { get; set; } = ClientId;
    public string? ClientSecret { get; set; } = ClientSecret;

    public UpdateFinanceEmailProviderSettingsRequest() : this(default !, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record UpdateFinanceEmailSettingsRequest(UpdateFinanceEmailProviderSettingsRequest? Gmail, UpdateFinanceEmailProviderSettingsRequest? Microsoft365)
{
    public UpdateFinanceEmailProviderSettingsRequest? Gmail { get; set; } = Gmail;
    public UpdateFinanceEmailProviderSettingsRequest? Microsoft365 { get; set; } = Microsoft365;

    public UpdateFinanceEmailSettingsRequest() : this(new(), new())
    {
    }
}

public sealed record FinanceEmailSettingsDto(bool IsWritable, bool RequiresRestart, FinanceEmailProviderSettingsDto Gmail, FinanceEmailProviderSettingsDto Microsoft365)
{
    public bool IsWritable { get; set; } = IsWritable;
    public bool RequiresRestart { get; set; } = RequiresRestart;
    public FinanceEmailProviderSettingsDto Gmail { get; set; } = Gmail;
    public FinanceEmailProviderSettingsDto Microsoft365 { get; set; } = Microsoft365;

    public FinanceEmailSettingsDto() : this(default !, default !, new(), new())
    {
    }
}

public sealed record FinanceEmailProviderSettingsDto(string ClientId, bool IsClientIdConfigured, bool IsClientSecretConfigured)
{
    public string ClientId { get; set; } = ClientId;
    public bool IsClientIdConfigured { get; set; } = IsClientIdConfigured;
    public bool IsClientSecretConfigured { get; set; } = IsClientSecretConfigured;

    public FinanceEmailProviderSettingsDto() : this(string.Empty, default !, default !)
    {
    }
}
