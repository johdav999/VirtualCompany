using Microsoft.AspNetCore.Http;
using VirtualCompany.Api.ProblemHandling;

namespace VirtualCompany.Api.Tests;

public sealed class StableProblemDetailsTests
{
    [Fact]
    public void Create_ProducesInvariantCodeArgumentsAndTraceMetadata()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-123" };
        context.Request.Path = "/api/test";

        var problem = StableProblemDetails.Create(
            context,
            StatusCodes.Status409Conflict,
            "finance.approval.conflict",
            "Conflict",
            "The approval changed.",
            new Dictionary<string, object?> { ["version"] = 2 });

        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("finance.approval.conflict", problem.Extensions["code"]);
        Assert.Equal("trace-123", problem.Extensions["traceId"]);
        Assert.Equal("/api/test", problem.Instance);
    }
}
