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
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class DeterministicSupportReplySafetyPolicy : ISupportReplySafetyPolicy
{
    public const string Version = SupportReplySafetyRules.PolicyVersion;
    private readonly VirtualCompanyDbContext _dbContext;

    public DeterministicSupportReplySafetyPolicy(VirtualCompanyDbContext dbContext) => _dbContext = dbContext;

    public async Task<SupportReplySafetyDecision> EvaluateAsync(Guid companyId, Guid supportCaseId, string draftBody, string? sourceReferencesJson, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken)
            ?? throw new InvalidOperationException("Support case was not found for safety evaluation.");
        return SupportReplySafetyRules.Evaluate(supportCase.Category, draftBody, sourceReferencesJson);
    }
}

