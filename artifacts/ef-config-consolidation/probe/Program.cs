using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using VirtualCompany.Infrastructure.Persistence;

var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
foreach (var provider in new[] { "sqlserver", "sqlite" })
{
    var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>();
    if (provider == "sqlserver") options.UseSqlServer("Server=localhost;Database=MetadataOnly;Integrated Security=true;TrustServerCertificate=true");
    else options.UseSqlite("Data Source=:memory:");
    using var db = new VirtualCompanyDbContext(options.Options);
    var model = db.GetService<IDesignTimeModel>().Model;
    var metadata = model.ToDebugString(MetadataDebugStringOptions.LongDefault);
    var ddl = db.Database.GenerateCreateScript();
    File.WriteAllText(Path.Combine(output, provider + "-model.txt"), metadata);
    File.WriteAllText(Path.Combine(output, provider + "-schema.sql"), ddl);
    Console.WriteLine($"{provider}: {model.GetEntityTypes().Count()} entities, model {Hash(metadata)}, schema {Hash(ddl)}");
}

static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
