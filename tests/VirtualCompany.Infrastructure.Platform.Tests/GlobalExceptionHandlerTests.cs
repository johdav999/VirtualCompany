using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Infrastructure.Observability;
using Xunit;

namespace VirtualCompany.Infrastructure.Platform.Tests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Aborted_request_is_handled_without_writing_a_problem_response()
    {
        using var requestCancellation = new CancellationTokenSource();
        requestCancellation.Cancel();

        var context = new DefaultHttpContext
        {
            RequestAborted = requestCancellation.Token
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/internal/companies/company-id/finance/bills";
        context.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            Options.Create(new ObservabilityOptions()));

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("The provider surfaced a cancelled database command."),
            requestCancellation.Token);

        Assert.True(handled);
        Assert.Equal(0, context.Response.Body.Length);
    }
}
