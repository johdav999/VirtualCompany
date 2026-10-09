using System.Text.Json;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Web.Tests;

public sealed class WireContractSerializationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("google", ExternalAccountProvider.Google)]
    [InlineData("microsoft365", ExternalAccountProvider.Microsoft365)]
    public void Connection_provider_uses_the_same_wire_value_without_client_serializer_configuration(
        string wireValue, ExternalAccountProvider provider)
    {
        Assert.Equal(provider, JsonSerializer.Deserialize<ExternalAccountProvider>($"\"{wireValue}\"", Json));
        Assert.Equal($"\"{wireValue}\"", JsonSerializer.Serialize(provider, Json));
    }

    [Theory]
    [InlineData("active", ExternalConnectionStatus.Active)]
    [InlineData("token_expired", ExternalConnectionStatus.TokenExpired)]
    public void Connection_status_accepts_API_snake_case_and_legacy_numeric_payloads(
        string wireValue, ExternalConnectionStatus status)
    {
        Assert.Equal(status, JsonSerializer.Deserialize<ExternalConnectionStatus>($"\"{wireValue}\"", Json));
        Assert.Equal(status, JsonSerializer.Deserialize<ExternalConnectionStatus>(((int)status).ToString(), Json));
        Assert.Equal($"\"{wireValue}\"", JsonSerializer.Serialize(status, Json));
    }

    [Fact]
    public void Mailbox_capability_flags_roundtrip_and_accept_legacy_numeric_masks()
    {
        var flags = MailboxCapability.ReadMessages | MailboxCapability.SendMessages;
        var payload = JsonSerializer.Serialize(flags, Json);
        Assert.Equal("\"read_messages, send_messages\"", payload);
        Assert.Equal(flags, JsonSerializer.Deserialize<MailboxCapability>(payload, Json));
        Assert.Equal(flags, JsonSerializer.Deserialize<MailboxCapability>(((int)flags).ToString(), Json));
    }

    [Fact]
    public void Calendar_summary_deserializes_real_API_enum_and_capability_fields()
    {
        var connection = JsonSerializer.Deserialize<CalendarConnectionSummaryResponse>("""
            {"provider":"google","status":"active","capabilities":"read_availability, create_events",
             "accountEmail":"host@example.test","calendarId":"primary"}
            """, Json)!;
        Assert.Equal(ExternalAccountProvider.Google, connection.Provider);
        Assert.Equal(ExternalConnectionStatus.Active, connection.Status);
        Assert.Equal(CalendarCapability.ReadAvailability | CalendarCapability.CreateEvents, connection.Capabilities);
    }

    [Fact]
    public void Finance_permissions_and_document_access_preserve_their_existing_JSON_names()
    {
        var permissions = JsonSerializer.Deserialize<FinanceActionPermissionsResponse>("""
            {"canEditTransactionCategory":true,"canManagePolicyConfiguration":true}
            """, Json)!;
        Assert.True(permissions.CanChangeTransactionCategory);
        Assert.True(permissions.CanManagePolicies);
        var document = JsonSerializer.Deserialize<FinanceLinkedDocumentAccessResponse>("""
            {"availability":"available","canNavigate":true,"message":"Ready"}
            """, Json)!;
        Assert.Equal("available", document.AccessState);
        Assert.True(document.CanOpen);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(document, Json));
        Assert.True(serialized.RootElement.GetProperty("canNavigate").GetBoolean());
        Assert.False(serialized.RootElement.TryGetProperty("canOpen", out _));
    }

    [Fact]
    public void Convenience_constructors_do_not_change_original_server_request_defaults()
    {
        var request = JsonSerializer.Deserialize<ReconcileBankTransactionApiRequest>("{}", Json)!;
        Assert.Equal(1, request.ExpectedSourceVersion);
        Assert.Equal("payment", request.HandlingMode);
        Assert.Null(request.Payments);
        Assert.Null(request.Adjustments);
        // Explicitly constructed editors retain their safe empty collections.
        Assert.Empty(new ReconcileBankTransactionApiRequest().Payments);
    }
}
