using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VirtualCompany.Infrastructure.Persistence;

public sealed class VirtualCompanyDbContextFactory : IDesignTimeDbContextFactory<VirtualCompanyDbContext>
{
    public VirtualCompanyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VirtualCompanyDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=virtualcompany;Username=postgres;Password=postgres;Include Error Detail=true",
            npgsqlOptions => npgsqlOptions.EnableRetryOnFailure());

        return new VirtualCompanyDbContext(optionsBuilder.Options);
    }
}
