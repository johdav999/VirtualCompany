using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Approvals;
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
    private readonly FinanceBillFortnoxRegistrationCompletionService _fortnoxRegistrationCompletionService;
    private readonly IMailboxProviderRegistry? _mailboxProviderRegistry;
    private readonly IFieldEncryptionService? _fieldEncryption;
    private readonly IReadOnlyList<IDocumentTextExtractor> _documentTextExtractors;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyFinanceBillInboxService> _logger;
    private readonly IApprovalAutomationService _approvalAutomationService;
    private readonly IAccountingAuthorityPolicy _accountingAuthorityPolicy;
    private readonly SupplierApprovalAutomationOptions _supplierApprovalAutomationOptions;

    public CompanyFinanceBillInboxService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter auditEventWriter,
        IFinanceIntegrationWriteCommandService financeWriteCommands,
        IFortnoxOutboundActionExecutor fortnoxOutboundActionExecutor,
        IFortnoxApiClient fortnoxApiClient,
        FinanceBillFortnoxRegistrationCompletionService fortnoxRegistrationCompletionService,
        TimeProvider timeProvider,
        ILogger<CompanyFinanceBillInboxService> logger,
        IApprovalAutomationService approvalAutomationService,
        IAccountingAuthorityPolicy accountingAuthorityPolicy,
        IOptions<SupplierApprovalAutomationOptions> supplierApprovalAutomationOptions,
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
        _fortnoxRegistrationCompletionService = fortnoxRegistrationCompletionService;
        _mailboxProviderRegistry = mailboxProviderRegistry;
        _fieldEncryption = fieldEncryption;
        _documentTextExtractors = documentTextExtractors?.ToArray() ?? [];
        _timeProvider = timeProvider;
        _logger = logger;
        _approvalAutomationService = approvalAutomationService;
        _accountingAuthorityPolicy = accountingAuthorityPolicy;
        _supplierApprovalAutomationOptions = supplierApprovalAutomationOptions.Value;
        _companyContextAccessor = companyContextAccessor;
    }
}
