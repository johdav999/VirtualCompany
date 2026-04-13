using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Chat;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DirectChatIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DirectChatIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Opening_direct_agent_conversation_creates_tenant_scoped_record()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!.CompanyId);
        Assert.Equal(seed.UserId, payload.CreatedByUserId);
        Assert.Equal(seed.AgentId, payload.AgentId);
        Assert.Equal(ChatChannelTypes.DirectAgent, payload.ChannelType);
        Assert.Equal("Direct chat with Active Agent", payload.Subject);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var conversation = await dbContext.Conversations
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == payload.Id);

        Assert.Equal(seed.CompanyId, conversation.CompanyId);
        Assert.Equal(seed.UserId, conversation.CreatedByUserId);
        Assert.Equal(seed.AgentId, conversation.AgentId);
        Assert.Equal(ChatChannelTypes.DirectAgent, conversation.ChannelType);
    }

    [Fact]
    public async Task Opening_direct_agent_conversation_reuses_existing_user_agent_thread()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var firstResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var secondResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var first = await firstResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        var second = await secondResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var conversationCount = await dbContext.Conversations
            .IgnoreQueryFilters()
            .CountAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.CreatedByUserId == seed.UserId &&
                x.AgentId == seed.AgentId &&
                x.ChannelType == ChatChannelTypes.DirectAgent);

        Assert.Equal(1, conversationCount);
    }

    [Fact]
    public async Task Opening_direct_agent_conversation_rejects_cross_tenant_agent()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.OtherCompanyAgentId}/conversations/direct",
            new { AgentId = seed.OtherCompanyAgentId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Opening_direct_agent_conversation_rejects_archived_agent()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Archived);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var conversationExists = await dbContext.Conversations
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == seed.CompanyId && x.AgentId == seed.AgentId);

        Assert.False(conversationExists);
    }

    [Fact]
    public async Task Sending_direct_agent_message_persists_sender_metadata_and_updates_conversation_timestamp()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var openResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var conversation = await openResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(conversation);

        await Task.Delay(20);
        var sendResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation!.Id}/messages",
            new SendDirectAgentMessageCommand("What should I prioritize today?"));

        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var payload = await sendResponse.Content.ReadFromJsonAsync<SendDirectAgentMessageResponse>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!.HumanMessage.CompanyId);
        Assert.Equal(conversation.Id, payload.HumanMessage.ConversationId);
        Assert.Equal(ChatSenderTypes.User, payload.HumanMessage.SenderType);
        Assert.Equal(seed.UserId, payload.HumanMessage.SenderId);
        Assert.Equal(ChatMessageTypes.Text, payload.HumanMessage.MessageType);
        Assert.Equal("What should I prioritize today?", payload.HumanMessage.Body);
        Assert.NotEqual(default, payload.HumanMessage.CreatedAt);
        Assert.Equal(seed.CompanyId, payload.AgentMessage.CompanyId);
        Assert.Equal(conversation.Id, payload.AgentMessage.ConversationId);
        Assert.Equal(ChatSenderTypes.Agent, payload.AgentMessage.SenderType);
        Assert.Equal(seed.AgentId, payload.AgentMessage.SenderId);
        Assert.NotEqual(default, payload.AgentMessage.CreatedAt);
        Assert.True(payload.Conversation.UpdatedAt > conversation.UpdatedAt);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var persistedConversation = await dbContext.Conversations
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == conversation.Id);
        var messages = await dbContext.Messages
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.CompanyId && x.ConversationId == conversation.Id)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();

        Assert.Equal(ChatChannelTypes.DirectAgent, persistedConversation.ChannelType);
        Assert.NotEqual(default, persistedConversation.CreatedUtc);
        Assert.True(persistedConversation.UpdatedUtc > persistedConversation.CreatedUtc);
        Assert.Equal(2, messages.Count);
        Assert.Equal(seed.CompanyId, messages[0].CompanyId);
        Assert.Equal(conversation.Id, messages[0].ConversationId);
        Assert.Equal(ChatSenderTypes.User, messages[0].SenderType);
        Assert.Equal(seed.UserId, messages[0].SenderId);
        Assert.NotEqual(default, messages[0].CreatedUtc);
        Assert.Equal(ChatSenderTypes.Agent, messages[1].SenderType);
        Assert.Equal(seed.AgentId, messages[1].SenderId);
        Assert.NotEqual(default, messages[1].CreatedUtc);
    }

    [Fact]
    public async Task Sending_direct_agent_message_uses_selected_agent_persona_and_role_brief()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var openResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var conversation = await openResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(conversation);

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation!.Id}/messages",
            new SendDirectAgentMessageCommand("Use the direct chat runtime profile."));

        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var payload = await sendResponse.Content.ReadFromJsonAsync<SendDirectAgentMessageResponse>();
        Assert.NotNull(payload);
        Assert.Contains("Active Agent here (Operations Lead).", payload!.AgentMessage.Body);
        Assert.Contains("Keeps operational triage crisp and tenant-safe.", payload.AgentMessage.Body);
        Assert.Contains("calm systems thinker", payload.AgentMessage.Body);
        Assert.True(payload.AgentMessage.StructuredPayload.ContainsKey("agent_role_brief"));
        Assert.True(payload.AgentMessage.StructuredPayload.ContainsKey("agent_personality_summary"));
        Assert.Equal("single_agent_orchestration", payload.AgentMessage.StructuredPayload["shared_engine"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sending_direct_agent_message_returns_rationale_summary_without_raw_reasoning_fields()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var openResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var conversation = await openResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(conversation);

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation!.Id}/messages",
            new SendDirectAgentMessageCommand("Why should I prioritize this?"));

        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var payload = await sendResponse.Content.ReadFromJsonAsync<SendDirectAgentMessageResponse>();
        Assert.NotNull(payload);
        Assert.Equal(
            "Used the selected agent profile, role brief, scoped context counts, and recent direct-chat history.",
            payload!.AgentMessage.RationaleSummary);
        Assert.Equal(
            payload.AgentMessage.RationaleSummary,
            payload.AgentMessage.StructuredPayload["rationale_summary"]!.GetValue<string>());
        AssertPayloadOmitsUnsafeReasoningFields(payload.AgentMessage.StructuredPayload);
    }

    [Fact]
    public async Task Getting_direct_agent_messages_filters_legacy_raw_reasoning_payload_fields()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var openResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var conversation = await openResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(conversation);

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Messages.Add(new Message(
                Guid.NewGuid(),
                seed.CompanyId,
                conversation!.Id,
                ChatSenderTypes.Agent,
                seed.AgentId,
                ChatMessageTypes.Text,
                "Safe response body.",
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rationale_summary"] = JsonValue.Create("Used the current chat context."),
                    ["reasoning"] = JsonValue.Create("private chain-of-thought"),
                    ["analysis"] = JsonValue.Create("hidden analysis"),
                    ["scratchpad"] = JsonValue.Create("private scratchpad"),
                    ["agent_role_name"] = JsonValue.Create("Operations Lead")
                }));

            return Task.CompletedTask;
        });

        var getResponse = await client.GetAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation!.Id}/messages?take=10");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var page = await getResponse.Content.ReadFromJsonAsync<ChatMessagePageResponse>();
        Assert.NotNull(page);
        var agentMessage = Assert.Single(page!.Items, x => x.Body == "Safe response body.");
        Assert.Equal("Used the current chat context.", agentMessage.RationaleSummary);
        Assert.True(agentMessage.StructuredPayload.ContainsKey("agent_role_name"));
        AssertPayloadOmitsUnsafeReasoningFields(agentMessage.StructuredPayload);
    }

    [Fact]
    public async Task Sending_direct_agent_message_passes_selected_agent_scope_to_retrieval()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var openResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var conversation = await openResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(conversation);

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation!.Id}/messages",
            new SendDirectAgentMessageCommand("Use finance scoped memory."));

        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var retrieval = await dbContext.ContextRetrievals
            .IgnoreQueryFilters()
            .OrderByDescending(x => x.CreatedUtc)
            .FirstAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.AgentId == seed.AgentId &&
                x.ActorUserId == seed.UserId &&
                x.RetrievalPurpose == "direct_agent_chat");

        var sourceIds = await dbContext.ContextRetrievalSources
            .IgnoreQueryFilters()
            .Where(x => x.RetrievalId == retrieval.Id && x.SourceType == GroundedContextSourceTypes.MemoryItem)
            .Select(x => x.SourceEntityId)
            .ToListAsync();

        Assert.Contains(seed.ScopedMemoryId.ToString("N"), sourceIds);
        Assert.DoesNotContain(seed.OutOfScopeMemoryId.ToString("N"), sourceIds);
    }

    [Fact]
    public async Task Sending_direct_agent_message_produces_different_runtime_output_for_different_agents()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var firstOpenResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var firstConversation = await firstOpenResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(firstConversation);

        var secondOpenResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.SecondAgentId}/conversations/direct",
            new { AgentId = seed.SecondAgentId });
        var secondConversation = await secondOpenResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(secondConversation);

        var firstSendResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{firstConversation!.Id}/messages",
            new SendDirectAgentMessageCommand("How should I prepare?"));
        var secondSendResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{secondConversation!.Id}/messages",
            new SendDirectAgentMessageCommand("How should I prepare?"));

        Assert.Equal(HttpStatusCode.OK, firstSendResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondSendResponse.StatusCode);

        var firstPayload = await firstSendResponse.Content.ReadFromJsonAsync<SendDirectAgentMessageResponse>();
        var secondPayload = await secondSendResponse.Content.ReadFromJsonAsync<SendDirectAgentMessageResponse>();
        Assert.NotNull(firstPayload);
        Assert.NotNull(secondPayload);
        Assert.NotEqual(firstPayload!.AgentMessage.Body, secondPayload!.AgentMessage.Body);
        Assert.Contains("Active Agent here (Operations Lead).", firstPayload.AgentMessage.Body);
        Assert.Contains("Strategy Agent here (Strategy Lead).", secondPayload.AgentMessage.Body);
        Assert.Contains("calm systems thinker", firstPayload.AgentMessage.Body);
        Assert.Contains("market-focused strategist", secondPayload.AgentMessage.Body);
    }

    [Fact]
    public async Task Sending_direct_agent_message_rejects_conversation_agent_mismatch()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);
        var mismatchedConversationId = Guid.NewGuid();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Conversations.Add(new Conversation(
                mismatchedConversationId,
                seed.CompanyId,
                ChatChannelTypes.DirectAgent,
                "Mismatched direct chat",
                seed.UserId,
                seed.OtherCompanyAgentId));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var sendResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{mismatchedConversationId}/messages",
            new SendDirectAgentMessageCommand("This should not reach the wrong agent."));

        Assert.Equal(HttpStatusCode.NotFound, sendResponse.StatusCode);
    }

    [Fact]
    public async Task Getting_direct_agent_conversations_returns_tenant_scoped_paginated_list()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var firstOpenResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var firstConversation = await firstOpenResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(firstConversation);

        await Task.Delay(20);
        var secondOpenResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.SecondAgentId}/conversations/direct",
            new { AgentId = seed.SecondAgentId });
        var secondConversation = await secondOpenResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(secondConversation);

        using var otherTenantClient = CreateAuthenticatedClient(seed.OtherSubject, seed.OtherEmail, "Other Chat User");
        var otherOpenResponse = await otherTenantClient.PostAsJsonAsync(
            $"/api/companies/{seed.OtherCompanyId}/agents/{seed.OtherCompanyAgentId}/conversations/direct",
            new { AgentId = seed.OtherCompanyAgentId });
        Assert.Equal(HttpStatusCode.OK, otherOpenResponse.StatusCode);

        var listResponse = await client.GetAsync(
            $"/api/companies/{seed.CompanyId}/conversations/direct?skip=0&take=1");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var page = await listResponse.Content.ReadFromJsonAsync<DirectConversationPageResponse>();
        Assert.NotNull(page);
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(0, page.Skip);
        Assert.Equal(1, page.Take);
        Assert.Single(page.Items);
        Assert.Equal(secondConversation!.Id, page.Items[0].Id);
        Assert.Equal(seed.CompanyId, page.Items[0].CompanyId);
        Assert.DoesNotContain(page.Items, x => x.CompanyId == seed.OtherCompanyId);
    }

    [Fact]
    public async Task Getting_direct_agent_messages_returns_chronological_page()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var openResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var conversation = await openResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(conversation);

        var firstSendResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation!.Id}/messages",
            new SendDirectAgentMessageCommand("First message"));
        Assert.Equal(HttpStatusCode.OK, firstSendResponse.StatusCode);

        await Task.Delay(20);
        var secondSendResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation.Id}/messages",
            new SendDirectAgentMessageCommand("Second message"));
        Assert.Equal(HttpStatusCode.OK, secondSendResponse.StatusCode);

        var getResponse = await client.GetAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation.Id}/messages?take=10");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var page = await getResponse.Content.ReadFromJsonAsync<ChatMessagePageResponse>();
        Assert.NotNull(page);
        Assert.Equal(4, page!.TotalCount);
        Assert.Equal(4, page.Items.Count);
        Assert.Equal("First message", page.Items[0].Body);
        Assert.Equal(ChatSenderTypes.User, page.Items[0].SenderType);
        Assert.Equal("Second message", page.Items[2].Body);
        Assert.Equal(ChatSenderTypes.User, page.Items[2].SenderType);
        Assert.True(page.Items.SequenceEqual(page.Items.OrderBy(x => x.CreatedAt)));
    }

    [Fact]
    public async Task Getting_messages_scopes_conversation_lookup_to_requested_tenant()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var ownerClient = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var openResponse = await ownerClient.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var conversation = await openResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(conversation);

        using var otherTenantClient = CreateAuthenticatedClient(seed.OtherSubject, seed.OtherEmail, "Other Chat User");
        var getResponse = await otherTenantClient.GetAsync(
            $"/api/companies/{seed.OtherCompanyId}/conversations/{conversation!.Id}/messages");

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Getting_direct_agent_messages_paginates_with_stable_tie_breaker()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var openResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var conversation = await openResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(conversation);

        var createdUtc = DateTime.UtcNow.AddMinutes(-5);
        var messageIds = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            Guid.Parse("00000000-0000-0000-0000-000000000004")
        };

        await _factory.SeedAsync(dbContext =>
        {
            foreach (var messageId in messageIds)
            {
                var message = new Message(
                    messageId,
                    seed.CompanyId,
                    conversation!.Id,
                    ChatSenderTypes.System,
                    null,
                    ChatMessageTypes.Text,
                    $"Tied message {messageId}");
                SetCreatedUtc(message, createdUtc);
                dbContext.Messages.Add(message);
            }

            return Task.CompletedTask;
        });

        var firstPageResponse = await client.GetAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation!.Id}/messages?skip=0&take=2");
        var secondPageResponse = await client.GetAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation.Id}/messages?skip=2&take=2");

        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);

        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<ChatMessagePageResponse>();
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<ChatMessagePageResponse>();
        Assert.NotNull(firstPage);
        Assert.NotNull(secondPage);
        Assert.Equal(4, firstPage!.TotalCount);
        Assert.Equal(4, secondPage!.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Equal(new[] { messageIds[2], messageIds[3] }, firstPage.Items.Select(x => x.Id));
        Assert.Equal(new[] { messageIds[0], messageIds[1] }, secondPage.Items.Select(x => x.Id));
        Assert.Empty(firstPage.Items.Select(x => x.Id).Intersect(secondPage.Items.Select(x => x.Id)));
    }

    [Fact]
    public async Task Getting_direct_agent_messages_clamps_page_size()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var openResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var conversation = await openResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(conversation);

        var getResponse = await client.GetAsync(
            $"/api/companies/{seed.CompanyId}/conversations/{conversation!.Id}/messages?take=1000");
        var page = await getResponse.Content.ReadFromJsonAsync<ChatMessagePageResponse>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(200, page!.Take);
    }

    [Fact]
    public async Task System_messages_can_be_persisted_without_sender_id()
    {
        var seed = await SeedDirectChatAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Chat User");
        var openResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/conversations/direct",
            new { seed.AgentId });
        var conversation = await openResponse.Content.ReadFromJsonAsync<DirectConversationResponse>();
        Assert.NotNull(conversation);

        var systemMessage = new Message(
            Guid.NewGuid(),
            seed.CompanyId,
            conversation!.Id,
            ChatSenderTypes.System,
            null,
            ChatMessageTypes.Text,
            "Conversation opened.");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        dbContext.Messages.Add(systemMessage);
        await dbContext.SaveChangesAsync();

        var persisted = await dbContext.Messages
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == systemMessage.Id);

        Assert.Equal(seed.CompanyId, persisted.CompanyId);
        Assert.Equal(conversation.Id, persisted.ConversationId);
        Assert.Equal(ChatSenderTypes.System, persisted.SenderType);
        Assert.Null(persisted.SenderId);
        Assert.NotEqual(default, persisted.CreatedUtc);
    }

    private static void SetCreatedUtc(Message message, DateTime createdUtc) =>
        typeof(Message).GetProperty(nameof(Message.CreatedUtc))!.SetValue(message, createdUtc);

    private static void AssertPayloadOmitsUnsafeReasoningFields(IReadOnlyDictionary<string, JsonNode?> payload)
    {
        Assert.False(payload.ContainsKey("analysis"));
        Assert.False(payload.ContainsKey("chain_of_thought"));
        Assert.False(payload.ContainsKey("chainOfThought"));
        Assert.False(payload.ContainsKey("hidden_reasoning"));
        Assert.False(payload.ContainsKey("hiddenReasoning"));
        Assert.False(payload.ContainsKey("internal_reasoning"));
        Assert.False(payload.ContainsKey("internalReasoning"));
        Assert.False(payload.ContainsKey("raw_reasoning"));
        Assert.False(payload.ContainsKey("rawReasoning"));
        Assert.False(payload.ContainsKey("reasoning"));
        Assert.False(payload.ContainsKey("scratchpad"));
        Assert.False(payload.ContainsKey("thoughts"));
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private async Task<DirectChatSeed> SeedDirectChatAsync(AgentStatus agentStatus)
    {
        var userId = Guid.NewGuid();
        var subject = $"direct-chat-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var otherSubject = $"direct-chat-other-{Guid.NewGuid():N}";
        var otherEmail = $"{otherSubject}@example.com";
        var agentId = Guid.NewGuid();
        var otherCompanyAgentId = Guid.NewGuid();
        var secondAgentId = Guid.NewGuid();
        var scopedMemoryId = Guid.NewGuid();
        var outOfScopeMemoryId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(userId, email, "Chat User", "dev-header", subject),
                new User(otherUserId, otherEmail, "Other Chat User", "dev-header", otherSubject));

            dbContext.Companies.AddRange(
                new Company(companyId, "Direct Chat Company"),
                new Company(otherCompanyId, "Other Direct Chat Company"));

            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(
                    Guid.NewGuid(),
                    companyId,
                    userId,
                    CompanyMembershipRole.Employee,
                    CompanyMembershipStatus.Active),
                new CompanyMembership(
                    Guid.NewGuid(),
                    otherCompanyId,
                    otherUserId,
                    CompanyMembershipRole.Employee,
                    CompanyMembershipStatus.Active));

            dbContext.Agents.AddRange(
                new Agent(
                    agentId,
                    companyId,
                    "operations",
                    agentStatus == AgentStatus.Archived ? "Archived Agent" : "Active Agent",
                    "Operations Lead",
                    "Operations",
                    null,
                    AgentSeniority.Lead,
                    agentStatus,
                    personality: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["summary"] = JsonValue.Create("calm systems thinker")
                    },
                    scopes: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["read"] = new JsonArray(JsonValue.Create("finance"))
                    },
                    roleBrief: "Keeps operational triage crisp and tenant-safe."),
                new Agent(
                    secondAgentId,
                    companyId,
                    "strategy",
                    "Strategy Agent",
                    "Strategy Lead",
                    "Strategy",
                    null,
                    AgentSeniority.Senior,
                    AgentStatus.Active,
                    personality: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["summary"] = JsonValue.Create("market-focused strategist")
                    },
                    scopes: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["read"] = new JsonArray(JsonValue.Create("sales"))
                    },
                    roleBrief: "Frames choices around positioning, timing, and tradeoffs."),
                new Agent(
                    otherCompanyAgentId,
                    otherCompanyId,
                    "operations",
                    "Other Tenant Agent",
                    "Operations Lead",
                    "Operations",
                    null,
                    AgentSeniority.Lead,
                    AgentStatus.Active));

            dbContext.MemoryItems.AddRange(
                new MemoryItem(
                    scopedMemoryId,
                    companyId,
                    agentId,
                    MemoryType.Fact,
                    "Finance scoped memory for direct chat runtime.",
                    null,
                    null,
                    0.95m,
                    DateTime.UtcNow.AddHours(-1),
                    null,
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["scope"] = JsonValue.Create("finance")
                    }),
                new MemoryItem(
                    outOfScopeMemoryId,
                    companyId,
                    agentId,
                    MemoryType.Fact,
                    "HR scoped memory must not be available to the finance-scoped agent.",
                    null,
                    null,
                    0.99m,
                    DateTime.UtcNow.AddHours(-1),
                    null,
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["scope"] = JsonValue.Create("hr")
                    }));

            return Task.CompletedTask;
        });

        return new DirectChatSeed(
            userId,
            subject,
            email,
            companyId,
            otherCompanyId,
            otherSubject,
            otherEmail,
            agentId,
            otherCompanyAgentId,
            secondAgentId,
            scopedMemoryId,
            outOfScopeMemoryId);
    }

    private sealed record DirectChatSeed(
        Guid UserId,
        string Subject,
        string Email,
        Guid CompanyId,
        Guid OtherCompanyId,
        string OtherSubject,
        string OtherEmail,
        Guid AgentId,
        Guid OtherCompanyAgentId,
        Guid SecondAgentId,
        Guid ScopedMemoryId,
        Guid OutOfScopeMemoryId);

    private sealed class DirectConversationResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string ChannelType { get; set; } = string.Empty;
        public Guid CreatedByUserId { get; set; }
        public Guid AgentId { get; set; }
        public string AgentDisplayName { get; set; } = string.Empty;
        public string AgentRoleName { get; set; } = string.Empty;
        public string AgentStatus { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class DirectConversationPageResponse
    {
        public List<DirectConversationResponse> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; }
    }

    private sealed class ChatMessageResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public Guid ConversationId { get; set; }
        public string SenderType { get; set; } = string.Empty;
        public Guid? SenderId { get; set; }
        public string MessageType { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? RationaleSummary { get; set; }
        public Dictionary<string, JsonNode?> StructuredPayload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime CreatedAt { get; set; }
    }

    private sealed class ChatMessagePageResponse
    {
        public List<ChatMessageResponse> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; }
    }

    private sealed class SendDirectAgentMessageResponse
    {
        public DirectConversationResponse Conversation { get; set; } = new();
        public ChatMessageResponse HumanMessage { get; set; } = new();
        public ChatMessageResponse AgentMessage { get; set; } = new();
    }
}
