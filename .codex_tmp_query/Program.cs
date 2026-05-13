using Microsoft.Data.SqlClient;

var companyId = args.Length > 0
    ? args[0]
    : "4e030f2f-789a-49f0-8385-8e1c67f499c8";
var outputPath = args.Length > 1 ? args[1] : null;

var connectionString = "Server=localhost,1433;Database=virtualcompany;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True";

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

await using var command = connection.CreateCommand();
command.CommandText = """
SELECT code, name, account_type, currency, opening_balance
FROM dbo.finance_accounts
WHERE company_id = @companyId
ORDER BY TRY_CONVERT(int, code), code;
""";
command.Parameters.AddWithValue("@companyId", Guid.Parse(companyId));

await using var reader = await command.ExecuteReaderAsync();
TextWriter writer = outputPath is null ? Console.Out : new StreamWriter(outputPath, false, System.Text.Encoding.UTF8);
await using var disposableWriter = writer as IAsyncDisposable;
writer.WriteLine("code,name,account_type,currency,opening_balance");
while (await reader.ReadAsync())
{
    writer.WriteLine(string.Join(',',
        Csv(reader.GetString(0)),
        Csv(reader.GetString(1)),
        Csv(reader.GetString(2)),
        Csv(reader.GetString(3)),
        reader.GetDecimal(4).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
}

if (writer != Console.Out)
{
    await writer.FlushAsync();
}

static string Csv(string value)
{
    var escaped = value.Replace("\"", "\"\"");
    return escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n')
        ? $"\"{escaped}\""
        : escaped;
}
