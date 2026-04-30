using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/settings/email")]
[Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
[RequireCompanyContext]
public sealed class FinanceEmailSettingsController : ControllerBase
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IOptionsMonitor<MailboxIntegrationOptions> _options;

    public FinanceEmailSettingsController(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IOptionsMonitor<MailboxIntegrationOptions> options)
    {
        _configuration = configuration;
        _environment = environment;
        _options = options;
    }

    [HttpGet]
    public ActionResult<FinanceEmailSettingsDto> Get(Guid companyId)
    {
        var options = _options.CurrentValue;
        return Ok(new FinanceEmailSettingsDto(
            IsWritable: _environment.IsDevelopment(),
            RequiresRestart: false,
            Gmail: ToProviderDto(options.Gmail),
            Microsoft365: ToProviderDto(options.Microsoft365)));
    }

    [HttpPut]
    public async Task<ActionResult<FinanceEmailSettingsDto>> Update(
        Guid companyId,
        [FromBody] UpdateFinanceEmailSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Email integration settings are read-only.",
                Detail = "Runtime editing of mailbox OAuth client settings is only enabled in Development."
            });
        }

        var path = Path.Combine(_environment.ContentRootPath, "appsettings.Development.json");
        if (!System.IO.File.Exists(path))
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Development settings file was not found.",
                Detail = $"Could not find {path}."
            });
        }

        JsonObject root;
        await using (var stream = System.IO.File.OpenRead(path))
        {
            root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject ?? new JsonObject();
        }

        var integrations = EnsureObject(root, MailboxIntegrationOptions.SectionName);
        ApplyProviderSettings(EnsureObject(integrations, "Gmail"), request.Gmail);
        ApplyProviderSettings(EnsureObject(integrations, "Microsoft365"), request.Microsoft365);

        await System.IO.File.WriteAllTextAsync(path, root.ToJsonString(WriteOptions), cancellationToken);
        (_configuration as IConfigurationRoot)?.Reload();

        var options = _options.CurrentValue;
        return Ok(new FinanceEmailSettingsDto(
            IsWritable: true,
            RequiresRestart: false,
            Gmail: ToProviderDto(options.Gmail),
            Microsoft365: ToProviderDto(options.Microsoft365)));
    }

    private static FinanceEmailProviderSettingsDto ToProviderDto(MailboxIntegrationOptions.OAuthProviderOptions options) =>
        new(
            ClientId: options.ClientId,
            IsClientIdConfigured: !string.IsNullOrWhiteSpace(options.ClientId),
            IsClientSecretConfigured: !string.IsNullOrWhiteSpace(options.ClientSecret));

    private static JsonObject EnsureObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }

    private static void ApplyProviderSettings(JsonObject target, UpdateFinanceEmailProviderSettingsRequest? request)
    {
        if (request is null)
        {
            return;
        }

        if (request.ClientId is not null)
        {
            target["ClientId"] = request.ClientId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            target["ClientSecret"] = request.ClientSecret.Trim();
        }
    }
}

public sealed record FinanceEmailSettingsDto(
    bool IsWritable,
    bool RequiresRestart,
    FinanceEmailProviderSettingsDto Gmail,
    FinanceEmailProviderSettingsDto Microsoft365);

public sealed record FinanceEmailProviderSettingsDto(
    string ClientId,
    bool IsClientIdConfigured,
    bool IsClientSecretConfigured);

public sealed record UpdateFinanceEmailSettingsRequest(
    UpdateFinanceEmailProviderSettingsRequest? Gmail,
    UpdateFinanceEmailProviderSettingsRequest? Microsoft365);

public sealed record UpdateFinanceEmailProviderSettingsRequest(
    string? ClientId,
    string? ClientSecret);
