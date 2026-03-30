using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VirtualCompany.Infrastructure.Persistence;

public sealed class VirtualCompanyDbContextFactory : IDesignTimeDbContextFactory<VirtualCompanyDbContext>
{
    public VirtualCompanyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VirtualCompanyDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=virtualcompany;Username=postgres;Password=postgres");

        return new VirtualCompanyDbContext(optionsBuilder.Options);
    }
}