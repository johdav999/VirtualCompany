namespace VirtualCompany.Web.Services;

public interface ICompanyApiTransport
{
    Uri? BaseAddress { get; }

    Task<HttpResponseMessage> SendAsync(
        Guid companyId,
        HttpMethod method,
        string uri,
        HttpContent? content,
        CancellationToken cancellationToken);
}

public sealed class CompanyApiTransport(HttpClient httpClient) : ICompanyApiTransport
{
    private const string CompanyContextHeaderName = "X-Company-Id";
    private const string CorrelationHeaderName = "X-Correlation-Id";

    public Uri? BaseAddress => httpClient.BaseAddress;

    public Task<HttpResponseMessage> SendAsync(
        Guid companyId,
        HttpMethod method,
        string uri,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company context is required for this request.", nameof(companyId));
        }

        var request = new HttpRequestMessage(method, uri)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation(CompanyContextHeaderName, companyId.ToString());
        request.Headers.TryAddWithoutValidation(CorrelationHeaderName, Guid.NewGuid().ToString("N"));

        return SendAndDisposeRequestAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAndDisposeRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
    }
}
