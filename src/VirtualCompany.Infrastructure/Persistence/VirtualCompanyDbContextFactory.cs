using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VirtualCompany.Infrastructure.Persistence;

public sealed class VirtualCompanyDbContextFactory : IDesignTimeDbContextFactory<VirtualCompanyDbContext>
{
    public VirtualCompanyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VirtualCompanyDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost,1433;Database=VirtualCompanyDb;User Id=sa;Password=YourStrongPassword123!;TrustServerCertificate=True");

        return new VirtualCompanyDbContext(optionsBuilder.Options);
    }
}