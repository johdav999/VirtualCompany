using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VirtualCompany.Infrastructure.Persistence;

public sealed class VirtualCompanyDbContextFactory : IDesignTimeDbContextFactory<VirtualCompanyDbContext>
{
    public VirtualCompanyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VirtualCompanyDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost,1433;Database=virtualcompany;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True",
            sqlServerOptions => sqlServerOptions.EnableRetryOnFailure());

        return new VirtualCompanyDbContext(optionsBuilder.Options);
    }
}
