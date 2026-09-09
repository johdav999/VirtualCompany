using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MicrosoftGraphTranscriptWebhookControllerTests
{
    [Fact]
    public async Task Validation_token_is_returned_verbatim_as_plain_text_without_dispatch()
    {
        var webhooks = new RecordingWebhooks();
        var controller = Controller(webhooks);

        var result = Assert.IsType<ContentResult>(await controller.ReceiveAsync("token%value", CancellationToken.None));

        Assert.Equal("token%value", result.Content);
        Assert.StartsWith("text/plain", result.ContentType, StringComparison.Ordinal);
        Assert.Equal(0, webhooks.ReceiveCount);
    }

    [Fact]
    public async Task Oversized_notification_is_rejected_before_dispatch()
    {
        var webhooks = new RecordingWebhooks();
        var controller = Controller(webhooks, new string('x', 2048));
        controller.Request.ContentLength = 2048;

        var result = Assert.IsType<StatusCodeResult>(await controller.ReceiveAsync(null, CancellationToken.None));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
        Assert.Equal(0, webhooks.ReceiveCount);
    }

    private static MicrosoftGraphMeetingTranscriptWebhookController Controller(RecordingWebhooks webhooks,
        string body = "{}")
    {
        var controller = new MicrosoftGraphMeetingTranscriptWebhookController(webhooks,
            Options.Create(new SalesMeetingTranscriptOptions { MaximumWebhookBytes = 1024 }));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return controller;
    }

    private sealed class RecordingWebhooks : IMicrosoftGraphTranscriptWebhookService
    {
        public int ReceiveCount { get; private set; }
        public Task<MicrosoftGraphWebhookReceipt> ReceiveAsync(string payload, string? correlationId, CancellationToken cancellationToken)
        {
            ReceiveCount++;
            return Task.FromResult(new MicrosoftGraphWebhookReceipt(0, 0, 0));
        }
        public Task<MicrosoftGraphWebhookReceipt> ReceiveLifecycleAsync(string payload, CancellationToken cancellationToken) =>
            Task.FromResult(new MicrosoftGraphWebhookReceipt(0, 0, 0));
    }
}
