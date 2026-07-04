using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class GmailMailboxProviderClientTests
{
    [Fact]
    public async Task List_messages_reads_pdf_attachments_from_full_gmail_payload()
    {
        var handler = new CapturingHandler(
            JsonResponse("""
                {
                  "messages": [
                    { "id": "19e5a185c1c0a8b3", "threadId": "thread-1" }
                  ]
                }
                """),
            JsonResponse("""
                {
                  "id": "19e5a185c1c0a8b3",
                  "threadId": "thread-1",
                  "labelIds": [ "UNREAD", "CATEGORY_PERSONAL", "INBOX" ],
                  "internalDate": "1779637604000",
                  "snippet": "Invoice attached",
                  "payload": {
                    "headers": [
                      { "name": "Subject", "value": "Invoice IT Services" },
                      { "name": "From", "value": "Johan Davidsson <johandavidsson@hotmail.se>" }
                    ],
                    "parts": [
                      {
                        "mimeType": "multipart/mixed",
                        "parts": [
                          {
                            "mimeType": "text/plain",
                            "body": { "size": 16, "data": "SW52b2ljZSBhdHRhY2hlZA" }
                          },
                          {
                            "filename": "test-supplier-invoice.pdf",
                            "mimeType": "application/pdf",
                            "body": {
                              "attachmentId": "ANGjdJ8-pdf-attachment",
                              "size": 42177
                            }
                          }
                        ]
                      }
                    ]
                  }
                }
                """));
        var client = new GmailMailboxProviderClient(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new StaticOptionsMonitor<MailboxIntegrationOptions>(new MailboxIntegrationOptions()),
            NullLogger<GmailMailboxProviderClient>.Instance);

        var messages = await client.ListMessagesAsync(
            "access-token",
            new MailboxMessageQuery(
                new DateTime(2026, 5, 24, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc),
                [new MailboxFolderSelection("INBOX", "Inbox")]),
            CancellationToken.None);

        var message = Assert.Single(messages);
        var attachment = Assert.Single(message.AttachmentSummaries);
        Assert.Equal("test-supplier-invoice.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MimeType);
        Assert.Equal(42177, attachment.SizeBytes);
        Assert.Equal("ANGjdJ8-pdf-attachment", attachment.ExternalAttachmentId);
        Assert.True(attachment.IsTextExtractable);
        Assert.Contains(handler.Requests, request => request.RequestUri?.Query.Contains("format=full", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task List_messages_follows_gmail_pagination()
    {
        var handler = new CapturingHandler(
            JsonResponse("""
                {
                  "messages": [
                    { "id": "first-page-message", "threadId": "thread-1" }
                  ],
                  "nextPageToken": "next-page"
                }
                """),
            JsonResponse("""
                {
                  "id": "first-page-message",
                  "threadId": "thread-1",
                  "labelIds": [ "INBOX" ],
                  "internalDate": "1779637604000",
                  "snippet": "Older message",
                  "payload": {
                    "headers": [
                      { "name": "Subject", "value": "Older message" },
                      { "name": "From", "value": "alerts@example.com" }
                    ]
                  }
                }
                """),
            JsonResponse("""
                {
                  "messages": [
                    { "id": "second-page-invoice", "threadId": "thread-2" }
                  ]
                }
                """),
            JsonResponse("""
                {
                  "id": "second-page-invoice",
                  "threadId": "thread-2",
                  "labelIds": [ "INBOX", "CATEGORY_PERSONAL" ],
                  "internalDate": "1779650000000",
                  "snippet": "Invoice attached",
                  "payload": {
                    "headers": [
                      { "name": "Subject", "value": "Invoice" },
                      { "name": "From", "value": "Johan Davidsson <johandavidsson@hotmail.se>" }
                    ],
                    "parts": [
                      {
                        "filename": "scanned-supplier-invoice.pdf",
                        "mimeType": "application/pdf",
                        "body": {
                          "attachmentId": "pdf-on-page-two",
                          "size": 275384
                        }
                      }
                    ]
                  }
                }
                """));
        var client = new GmailMailboxProviderClient(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new StaticOptionsMonitor<MailboxIntegrationOptions>(new MailboxIntegrationOptions()),
            NullLogger<GmailMailboxProviderClient>.Instance);

        var messages = await client.ListMessagesAsync(
            "access-token",
            new MailboxMessageQuery(
                new DateTime(2026, 5, 24, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc),
                [new MailboxFolderSelection("INBOX", "Inbox")]),
            CancellationToken.None);

        Assert.Equal(2, messages.Count);
        var invoice = Assert.Single(messages, message => message.ProviderMessageId == "second-page-invoice");
        var attachment = Assert.Single(invoice.AttachmentSummaries);
        Assert.Equal("scanned-supplier-invoice.pdf", attachment.FileName);
        Assert.Contains(handler.Requests, request => request.RequestUri?.Query.Contains("pageToken=next-page", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task List_messages_runs_attachment_search_for_pdf_messages_missed_by_folder_query()
    {
        var handler = new CapturingHandler(
            JsonResponse("""{ "messages": [] }"""),
            JsonResponse("""
                {
                  "messages": [
                    { "id": "scanned-pdf-message", "threadId": "thread-1" }
                  ]
                }
                """),
            JsonResponse("""
                {
                  "id": "scanned-pdf-message",
                  "threadId": "thread-1",
                  "labelIds": [ "INBOX", "CATEGORY_PERSONAL" ],
                  "internalDate": "1779650000000",
                  "snippet": "Invoice",
                  "payload": {
                    "headers": [
                      { "name": "Subject", "value": "Invoice" },
                      { "name": "From", "value": "Johan Davidsson <johandavidsson@hotmail.se>" }
                    ],
                    "parts": [
                      {
                        "filename": "scanned-supplier-invoice-nordic-cloud.pdf",
                        "mimeType": "application/pdf",
                        "body": {
                          "attachmentId": "scanned-pdf-attachment",
                          "size": 275384
                        }
                      }
                    ]
                  }
                }
                """),
            JsonResponse("""{ "messages": [] }"""));
        var client = new GmailMailboxProviderClient(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new StaticOptionsMonitor<MailboxIntegrationOptions>(new MailboxIntegrationOptions()),
            NullLogger<GmailMailboxProviderClient>.Instance);

        var messages = await client.ListMessagesAsync(
            "access-token",
            new MailboxMessageQuery(
                new DateTime(2026, 5, 24, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc),
                [new MailboxFolderSelection("INBOX", "Inbox")]),
            CancellationToken.None);

        var message = Assert.Single(messages);
        Assert.Equal("scanned-pdf-message", message.ProviderMessageId);
        Assert.Equal("scanned-supplier-invoice-nordic-cloud.pdf", Assert.Single(message.AttachmentSummaries).FileName);
        Assert.Contains(handler.Requests, request => request.RequestUri?.Query.Contains("filename%3Apdf", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task List_messages_runs_attachment_search_for_image_messages_missed_by_folder_query()
    {
        var handler = new CapturingHandler(
            JsonResponse("""{ "messages": [] }"""),
            JsonResponse("""{ "messages": [] }"""),
            JsonResponse("""{ "messages": [] }"""),
            JsonResponse("""
                {
                  "messages": [
                    { "id": "scanned-image-message", "threadId": "thread-1" }
                  ]
                }
                """),
            JsonResponse("""
                {
                  "id": "scanned-image-message",
                  "threadId": "thread-1",
                  "labelIds": [ "INBOX", "CATEGORY_PERSONAL" ],
                  "internalDate": "1779650000000",
                  "snippet": "Invoice",
                  "payload": {
                    "headers": [
                      { "name": "Subject", "value": "Invoice" },
                      { "name": "From", "value": "Johan Davidsson <johandavidsson@hotmail.se>" }
                    ],
                    "parts": [
                      {
                        "filename": "scanned-supplier-invoice-nordic-cloud.png",
                        "mimeType": "image/png",
                        "body": {
                          "attachmentId": "scanned-png-attachment",
                          "size": 275384
                        }
                      }
                    ]
                  }
                }
                """));
        var client = new GmailMailboxProviderClient(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new StaticOptionsMonitor<MailboxIntegrationOptions>(new MailboxIntegrationOptions()),
            NullLogger<GmailMailboxProviderClient>.Instance);

        var messages = await client.ListMessagesAsync(
            "access-token",
            new MailboxMessageQuery(
                new DateTime(2026, 5, 24, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc),
                [new MailboxFolderSelection("INBOX", "Inbox")]),
            CancellationToken.None);

        var message = Assert.Single(messages);
        var attachment = Assert.Single(message.AttachmentSummaries);
        Assert.Equal("scanned-image-message", message.ProviderMessageId);
        Assert.Equal("scanned-supplier-invoice-nordic-cloud.png", attachment.FileName);
        Assert.True(attachment.IsTextExtractable);
        Assert.Contains(handler.Requests, request => request.RequestUri?.Query.Contains("filename%3Apng", StringComparison.Ordinal) == true);
    }

    private static HttpResponseMessage JsonResponse(string value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(value, Encoding.UTF8, "application/json")
        };

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public CapturingHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : JsonResponse("""{ "messages": [] }"""));
        }
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
