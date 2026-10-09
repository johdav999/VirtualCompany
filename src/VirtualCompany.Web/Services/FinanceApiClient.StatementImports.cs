using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<StatementImportWorkspaceResponse> GetStatementImportWorkspaceAsync(Guid companyId,
        CancellationToken cancellationToken = default) => GetAsync<StatementImportWorkspaceResponse>(companyId,
            $"api/companies/{companyId:D}/finance/statement-imports", false, cancellationToken)!;

    public Task<StatementImportJobResponse?> GetStatementImportJobAsync(Guid companyId, Guid jobId,
        CancellationToken cancellationToken = default) => GetAsync<StatementImportJobResponse>(companyId,
            $"api/companies/{companyId:D}/finance/statement-imports/{jobId:D}", true, cancellationToken);

    public async Task<StatementImportJobResponse> PreviewStatementImportAsync(Guid companyId, Guid bankAccountId,
        string fileName, string? contentType, long contentLength, Stream content, Guid? csvProfileId,
        int? csvProfileVersion, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(bankAccountId.ToString("D")), "BankAccountId");
        if (csvProfileId.HasValue) multipart.Add(new StringContent(csvProfileId.Value.ToString("D")), "CsvMappingProfileId");
        if (csvProfileVersion.HasValue) multipart.Add(new StringContent(csvProfileVersion.Value.ToString()), "CsvMappingProfileVersion");
        var file = new StreamContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        file.Headers.ContentLength = contentLength;
        multipart.Add(file, "File", fileName);
        using var response = await _transport.SendAsync(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/finance/statement-imports/preview", multipart, cancellationToken);
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<StatementImportJobResponse>(SerializerOptions, cancellationToken)
            ?? throw new FinanceApiException("The statement preview returned an empty response.");
    }

    public Task<StatementImportJobResponse> CommitStatementImportAsync(Guid companyId, Guid jobId,
        long expectedVersion, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<CommitStatementImportApiRequest, StatementImportJobResponse>(companyId,
        HttpMethod.Post, $"api/companies/{companyId:D}/finance/statement-imports/{jobId:D}/commit",
        new(expectedVersion), cancellationToken); }

    public Task<StatementImportJobResponse> SkipStatementImportRowAsync(Guid companyId, Guid jobId, Guid rowId,
        long expectedVersion, string reason, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<DecideStatementImportRowApiRequest, StatementImportJobResponse>(companyId,
        HttpMethod.Post, $"api/companies/{companyId:D}/finance/statement-imports/{jobId:D}/rows/{rowId:D}/decision",
        new(expectedVersion, "skip", reason), cancellationToken); }

    public Task<StatementCsvProfileResponse> CreateStatementCsvProfileAsync(Guid companyId,
        CreateStatementCsvProfileApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<CreateStatementCsvProfileApiRequest, StatementCsvProfileResponse>(companyId,
        HttpMethod.Post, $"api/companies/{companyId:D}/finance/statement-imports/csv-profiles", request, cancellationToken); }

    public Task<StatementCsvProfileResponse> CreateStatementCsvProfileVersionAsync(Guid companyId, Guid profileId,
        CreateStatementCsvProfileVersionApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<CreateStatementCsvProfileVersionApiRequest, StatementCsvProfileResponse>(companyId,
        HttpMethod.Post, $"api/companies/{companyId:D}/finance/statement-imports/csv-profiles/{profileId:D}/versions", request, cancellationToken); }
}

public sealed record CommitStatementImportApiRequest(long ExpectedVersion);
public sealed record DecideStatementImportRowApiRequest(long ExpectedVersion, string Decision, string Reason);
public sealed record CreateStatementCsvProfileApiRequest(string Name, string Delimiter, string CultureName,
    string DateFormat, bool HasHeader, string BookingDateColumn, string? ValueDateColumn, string? AmountColumn,
    string? DebitColumn, string? CreditColumn, string? CurrencyColumn, string ReferenceColumn,
    string? CounterpartyColumn, string? ExternalReferenceColumn, string? AccountIdentifierColumn,
    string? DefaultCurrency);
public sealed record CreateStatementCsvProfileVersionApiRequest(int ExpectedCurrentVersion, string Delimiter,
    string CultureName, string DateFormat, bool HasHeader, string BookingDateColumn, string? ValueDateColumn,
    string? AmountColumn, string? DebitColumn, string? CreditColumn, string? CurrencyColumn,
    string ReferenceColumn, string? CounterpartyColumn, string? ExternalReferenceColumn,
    string? AccountIdentifierColumn, string? DefaultCurrency);
