namespace VirtualCompany.Application.Companies;

public interface ICoreCompanyAgentSeeder
{
    Task SeedAsync(Guid companyId, CancellationToken cancellationToken);

    Task BackfillAllCompaniesAsync(CancellationToken cancellationToken);
}
