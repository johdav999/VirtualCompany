using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportContextResolutionService : ISupportContextResolutionService
{
    private readonly VirtualCompanyDbContext _dbContext;

    public SupportContextResolutionService(VirtualCompanyDbContext dbContext) => _dbContext = dbContext;

    public async Task<SupportCaseContextSummary> ResolveAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken)
            ?? throw new InvalidOperationException("Support case was not found.");
        var lastInboundEmail = await _dbContext.SupportMessages.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SupportCaseId == supportCaseId && x.Direction == SupportMessageDirections.Inbound)
            .OrderByDescending(x => x.OccurredUtc)
            .Select(x => x.Sender)
            .FirstOrDefaultAsync(cancellationToken);
        Contact? contact = null;
        CustomerCompany? customer = null;
        if (!string.IsNullOrWhiteSpace(lastInboundEmail))
        {
            contact = await _dbContext.Contacts.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Email == lastInboundEmail.ToLower(), cancellationToken);
            if (contact?.CustomerCompanyId is Guid customerId)
            {
                customer = await _dbContext.CustomerCompanies.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == customerId, cancellationToken);
            }
        }

        if (contact is not null || customer is not null)
        {
            supportCase.LinkContext(contact?.Id, customer?.Id, supportCase.RelatedInvoiceId, supportCase.RelatedPaymentId);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SupportCaseContextSummary(supportCase.Id, customer?.Name, contact?.FullName, contact?.Email, SupportCaseService.BuildReferences(supportCase, contact, customer), contact is null ? 0 : 0.9m, contact is null ? "No customer match found." : "Matched sender email to a known contact.");
    }
}
