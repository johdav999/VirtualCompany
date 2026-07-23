using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceBillInboxService : IFinanceBillInboxService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IFinanceIntegrationWriteCommandService _financeWriteCommands;
    private readonly IFortnoxOutboundActionExecutor _fortnoxOutboundActionExecutor;
    private readonly IFortnoxApiClient _fortnoxApiClient;
    private readonly IMailboxProviderRegistry? _mailboxProviderRegistry;
    private readonly IFieldEncryptionService? _fieldEncryption;
    private readonly IReadOnlyList<IDocumentTextExtractor> _documentTextExtractors;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyFinanceBillInboxService> _logger;

    public CompanyFinanceBillInboxService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter auditEventWriter,
        IFinanceIntegrationWriteCommandService financeWriteCommands,
        IFortnoxOutboundActionExecutor fortnoxOutboundActionExecutor,
        IFortnoxApiClient fortnoxApiClient,
        TimeProvider timeProvider,
        ILogger<CompanyFinanceBillInboxService> logger,
        ICompanyContextAccessor? companyContextAccessor = null,
        IMailboxProviderRegistry? mailboxProviderRegistry = null,
        IFieldEncryptionService? fieldEncryption = null,
        IEnumerable<IDocumentTextExtractor>? documentTextExtractors = null)
    {
        _dbContext = dbContext;
        _auditEventWriter = auditEventWriter;
        _financeWriteCommands = financeWriteCommands;
        _fortnoxOutboundActionExecutor = fortnoxOutboundActionExecutor;
        _fortnoxApiClient = fortnoxApiClient;
        _mailboxProviderRegistry = mailboxProviderRegistry;
        _fieldEncryption = fieldEncryption;
        _documentTextExtractors = documentTextExtractors?.ToArray() ?? [];
        _timeProvider = timeProvider;
        _logger = logger;
        _companyContextAccessor = companyContextAccessor;
    }
}
