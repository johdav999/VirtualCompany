using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class TreasuryMovementServiceTests
{
    private static readonly TreasuryEvidenceInputDto ProviderEvidence = new(
        TreasuryEvidenceTypes.ProviderSettlement,
        "provider-settlement.json",
        new string('a', 64),
        "Signed provider settlement export");

    [Fact]
    public async Task One_transfer_leg_stays_in_transit_and_both_legs_post_only_between_cash_accounts()
    {
        await using var fixture = await Fixture.CreateAsync();

        var inTransit = await fixture.Service.CreateTransferAsync(new(
            fixture.CompanyId, "transfer-delayed-1", fixture.FromBankAccountId, fixture.ToBankAccountId,
            100m, 0m, "SEK", null, 0m, null, fixture.OutboundTransferId, null, [],
            fixture.UserId), CancellationToken.None);

        Assert.Equal(TreasuryMovementStatuses.InTransit, inTransit.Summary.Status);
        Assert.Equal(TreasuryMovementReasonCodes.TransferLegMissing, inTransit.Summary.ReasonCode);
        Assert.False(inTransit.AllowedActions.CanPost);
        var blocked = await Assert.ThrowsAsync<TreasuryMovementException>(() => fixture.Service.PostAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, inTransit.Summary.Id, fixture.FiscalPeriodId,
            fixture.PostingDate, inTransit.Summary.Version, fixture.UserId), CancellationToken.None));
        Assert.Equal(TreasuryMovementReasonCodes.TransferLegMissing, blocked.ReasonCode);
        Assert.Empty(fixture.Posting.Posts);

        var ready = await fixture.Service.LinkBankEvidenceAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, inTransit.Summary.Id,
            fixture.InboundTransferId, TreasuryTransferLegRoles.Inbound, inTransit.Summary.Version,
            fixture.UserId), CancellationToken.None);
        Assert.Equal(TreasuryMovementStatuses.ReadyToPost, ready.Summary.Status);

        var preview = await fixture.Service.PreviewAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, ready.Summary.Id,
            fixture.FiscalPeriodId, fixture.PostingDate, fixture.UserId), CancellationToken.None);
        Assert.True(preview.CanPost);
        Assert.Equal(100m, preview.Lines.Sum(x => x.DebitAmount));
        Assert.Equal(100m, preview.Lines.Sum(x => x.CreditAmount));
        Assert.All(preview.Lines, line => Assert.Contains(line.FinanceAccountId,
            new[] { fixture.FromFinanceAccountId, fixture.ToFinanceAccountId }));

        var posted = await fixture.Service.PostAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, ready.Summary.Id, fixture.FiscalPeriodId,
            fixture.PostingDate, ready.Summary.Version, fixture.UserId), CancellationToken.None);
        Assert.Equal(TreasuryMovementStatuses.Posted, posted.Summary.Status);
        Assert.Single(fixture.Posting.Posts);
        Assert.Equal(2, fixture.Posting.Posts[0].Entry.Lines.Count);

        var replay = await fixture.Service.PostAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, ready.Summary.Id, fixture.FiscalPeriodId,
            fixture.PostingDate, ready.Summary.Version, fixture.UserId), CancellationToken.None);
        Assert.Equal(TreasuryMovementStatuses.Posted, replay.Summary.Status);
        Assert.Single(fixture.Posting.Posts);
    }

    [Fact]
    public async Task Net_card_settlement_balances_gross_receivable_fee_cash_and_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();

        var ready = await fixture.Service.CreateCardSettlementAsync(new(
            fixture.CompanyId, "card-batch-1", "CARD-20260828-01", fixture.FromBankAccountId,
            fixture.ControlAccountId, 1000m, 30m, 970m, "SEK", 0m, null,
            fixture.CardPayoutId, [ProviderEvidence], fixture.UserId), CancellationToken.None);

        Assert.Equal(TreasuryMovementStatuses.ReadyToPost, ready.Summary.Status);
        Assert.Contains(ready.Evidence, x => x.EvidenceType == TreasuryEvidenceTypes.ProviderSettlement);
        Assert.Contains(ready.Evidence, x => x.EvidenceType == TreasuryEvidenceTypes.BankTransaction);

        var preview = await fixture.Service.PreviewAsync(new(
            fixture.CompanyId, TreasurySourceTypes.CardSettlement, ready.Summary.Id,
            fixture.FiscalPeriodId, fixture.PostingDate, fixture.UserId), CancellationToken.None);
        Assert.True(preview.CanPost);
        Assert.Collection(preview.Lines.OrderBy(x => x.AccountCode),
            control => { Assert.Equal("1580", control.AccountCode); Assert.Equal(1000m, control.CreditAmount); },
            cash => { Assert.Equal("1930", cash.AccountCode); Assert.Equal(970m, cash.DebitAmount); },
            fee => { Assert.Equal("6570", fee.AccountCode); Assert.Equal(30m, fee.DebitAmount); });
        Assert.Equal(preview.Lines.Sum(x => x.DebitAmount), preview.Lines.Sum(x => x.CreditAmount));

        var posted = await fixture.Service.PostAsync(new(
            fixture.CompanyId, TreasurySourceTypes.CardSettlement, ready.Summary.Id, fixture.FiscalPeriodId,
            fixture.PostingDate, ready.Summary.Version, fixture.UserId), CancellationToken.None);
        Assert.Equal(TreasuryMovementStatuses.Posted, posted.Summary.Status);
        Assert.Equal(3, fixture.Posting.Posts.Single().Entry.Lines.Count);
    }

    [Fact]
    public async Task Ambiguous_payout_remains_visible_and_never_invents_a_counterpart()
    {
        await using var fixture = await Fixture.CreateAsync();

        var review = await fixture.Service.CreatePayoutSettlementAsync(new(
            fixture.CompanyId, "payout-batch-ambiguous", "PAYOUT-20260828-01", fixture.FromBankAccountId,
            fixture.ControlAccountId, 1000m, 30m, 970m, "SEK", 0m, null,
            fixture.AmbiguousPayoutId, [ProviderEvidence], fixture.UserId), CancellationToken.None);

        Assert.Equal(TreasuryMovementStatuses.NeedsReview, review.Summary.Status);
        Assert.Equal(TreasuryMovementReasonCodes.BankAmountMismatch, review.Summary.ReasonCode);
        Assert.Single(review.BankEvidence);
        var listed = await fixture.Service.ListAsync(new(fixture.CompanyId, BankTransactionId: fixture.AmbiguousPayoutId), CancellationToken.None);
        Assert.Contains(listed.Items, x => x.Id == review.Summary.Id);
        await Assert.ThrowsAsync<TreasuryMovementException>(() => fixture.Service.PostAsync(new(
            fixture.CompanyId, TreasurySourceTypes.PayoutSettlement, review.Summary.Id, fixture.FiscalPeriodId,
            fixture.PostingDate, review.Summary.Version, fixture.UserId), CancellationToken.None));
        Assert.Empty(fixture.Posting.Posts);
    }

    [Fact]
    public async Task Bank_fee_and_interest_use_explicit_expense_and_income_counterparts()
    {
        await using var fixture = await Fixture.CreateAsync();

        var fee = await fixture.Service.CreateBankAdjustmentAsync(new(
            fixture.CompanyId, "bank-fee-1", BankAdjustmentKinds.BankFee, fixture.FromBankAccountId,
            fixture.BankFeeTransactionId, fixture.FeeAccountId, 30m, "SEK", "Monthly bank charge",
            0m, null, [], fixture.UserId), CancellationToken.None);
        var feePreview = await fixture.Service.PreviewAsync(new(
            fixture.CompanyId, TreasurySourceTypes.BankAdjustment, fee.Summary.Id, fixture.FiscalPeriodId,
            fixture.PostingDate, fixture.UserId), CancellationToken.None);
        Assert.Contains(feePreview.Lines, x => x.FinanceAccountId == fixture.FeeAccountId && x.DebitAmount == 30m);
        Assert.Contains(feePreview.Lines, x => x.FinanceAccountId == fixture.FromFinanceAccountId && x.CreditAmount == 30m);

        var interest = await fixture.Service.CreateBankAdjustmentAsync(new(
            fixture.CompanyId, "bank-interest-1", BankAdjustmentKinds.InterestIncome, fixture.ToBankAccountId,
            fixture.InterestTransactionId, fixture.IncomeAccountId, 20m, "SEK", "Deposit interest",
            0m, null, [], fixture.UserId), CancellationToken.None);
        var interestPreview = await fixture.Service.PreviewAsync(new(
            fixture.CompanyId, TreasurySourceTypes.BankAdjustment, interest.Summary.Id, fixture.FiscalPeriodId,
            fixture.PostingDate, fixture.UserId), CancellationToken.None);
        Assert.Contains(interestPreview.Lines, x => x.FinanceAccountId == fixture.ToFinanceAccountId && x.DebitAmount == 20m);
        Assert.Contains(interestPreview.Lines, x => x.FinanceAccountId == fixture.IncomeAccountId && x.CreditAmount == 20m);
    }

    [Fact]
    public async Task Cross_currency_transfer_and_cross_company_access_are_blocked_explicitly()
    {
        await using var fixture = await Fixture.CreateAsync();

        var crossCurrency = await Assert.ThrowsAsync<TreasuryMovementException>(() => fixture.Service.CreateTransferAsync(new(
            fixture.CompanyId, "cross-currency-1", fixture.FromBankAccountId, fixture.NokBankAccountId,
            100m, 0m, "SEK", null, 0m, null, null, null, [], fixture.UserId), CancellationToken.None));
        Assert.Equal(TreasuryMovementReasonCodes.CrossCurrencyTransferBlocked, crossCurrency.ReasonCode);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ListAsync(
            new(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Stale_bank_link_version_is_rejected_before_evidence_changes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = await fixture.Service.CreateTransferAsync(new(
            fixture.CompanyId, "transfer-version-1", fixture.FromBankAccountId, fixture.ToBankAccountId,
            100m, 0m, "SEK", null, 0m, null, null, null, [], fixture.UserId), CancellationToken.None);
        var oneLeg = await fixture.Service.LinkBankEvidenceAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, source.Summary.Id, fixture.OutboundTransferId,
            TreasuryTransferLegRoles.Outbound, source.Summary.Version, fixture.UserId), CancellationToken.None);

        var stale = await Assert.ThrowsAsync<TreasuryMovementException>(() => fixture.Service.LinkBankEvidenceAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, source.Summary.Id, fixture.InboundTransferId,
            TreasuryTransferLegRoles.Inbound, source.Summary.Version, fixture.UserId), CancellationToken.None));
        Assert.Equal(TreasuryMovementReasonCodes.SourceVersionConflict, stale.ReasonCode);
        var current = await fixture.Service.GetAsync(new(fixture.CompanyId, TreasurySourceTypes.AccountTransfer, source.Summary.Id), CancellationToken.None);
        Assert.Equal(oneLeg.Summary.Version, current!.Summary.Version);
        Assert.Single(current.BankEvidence);
    }

    [Fact]
    public async Task Posted_source_reverses_through_the_accounting_boundary_and_keeps_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        var ready = await fixture.Service.CreateTransferAsync(new(
            fixture.CompanyId, "transfer-reversal-1", fixture.FromBankAccountId, fixture.ToBankAccountId,
            100m, 0m, "SEK", null, 0m, null, fixture.OutboundTransferId, fixture.InboundTransferId,
            [], fixture.UserId), CancellationToken.None);
        var posted = await fixture.Service.PostAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, ready.Summary.Id, fixture.FiscalPeriodId,
            fixture.PostingDate, ready.Summary.Version, fixture.UserId), CancellationToken.None);

        var reversed = await fixture.Service.ReverseAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, ready.Summary.Id, fixture.FiscalPeriodId,
            fixture.PostingDate, posted.Summary.Version, "Bank evidence was corrected", fixture.UserId), CancellationToken.None);

        Assert.Equal(TreasuryMovementStatuses.Reversed, reversed.Summary.Status);
        Assert.Single(fixture.Posting.Reversals);
        Assert.Contains(reversed.History, x => x.Action == "posted");
        Assert.Contains(reversed.History, x => x.Action == "reversed");
    }

    [Fact]
    public async Task Material_source_accepts_only_an_approved_treasury_request_for_its_resulting_version()
    {
        await using var fixture = await Fixture.CreateAsync();
        var material = await fixture.Service.CreateTransferAsync(new(
            fixture.CompanyId, "transfer-material-1", fixture.FromBankAccountId, fixture.ToBankAccountId,
            100m, 0m, "SEK", null, 50m, null, fixture.OutboundTransferId, fixture.InboundTransferId,
            [], fixture.UserId), CancellationToken.None);
        Assert.Equal(TreasuryMovementStatuses.AwaitingApproval, material.Summary.Status);

        var wrongApproval = ApprovedRequest(fixture, material.Summary.Id, material.Summary.Version + 1,
            ApprovalTargetEntityType.Task);
        fixture.Db.ApprovalRequests.Add(wrongApproval); await fixture.Db.SaveChangesAsync();
        var wrong = await Assert.ThrowsAsync<TreasuryMovementException>(() => fixture.Service.BindApprovalAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, material.Summary.Id, wrongApproval.Id,
            material.Summary.Version, fixture.UserId), CancellationToken.None));
        Assert.Equal(TreasuryMovementReasonCodes.ApprovalRequired, wrong.ReasonCode);

        var validApproval = ApprovedRequest(fixture, material.Summary.Id, material.Summary.Version + 1,
            ApprovalTargetEntityType.TreasurySource);
        fixture.Db.ApprovalRequests.Add(validApproval); await fixture.Db.SaveChangesAsync();
        var ready = await fixture.Service.BindApprovalAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, material.Summary.Id, validApproval.Id,
            material.Summary.Version, fixture.UserId), CancellationToken.None);
        Assert.Equal(TreasuryMovementStatuses.ReadyToPost, ready.Summary.Status);
        Assert.Equal(material.Summary.Version + 1, ready.Summary.Version);
        var evidenceChange = await Assert.ThrowsAsync<TreasuryMovementException>(() => fixture.Service.LinkBankEvidenceAsync(new(
            fixture.CompanyId, TreasurySourceTypes.AccountTransfer, material.Summary.Id, fixture.CardPayoutId,
            TreasuryTransferLegRoles.Outbound, ready.Summary.Version, fixture.UserId), CancellationToken.None));
        Assert.Equal(TreasuryMovementReasonCodes.InvalidLifecycleState, evidenceChange.ReasonCode);

        await fixture.Service.PostAsync(new(fixture.CompanyId, TreasurySourceTypes.AccountTransfer,
            material.Summary.Id, fixture.FiscalPeriodId, fixture.PostingDate, ready.Summary.Version,
            fixture.UserId), CancellationToken.None);
        Assert.Equal(ready.Summary.Version.ToString(), fixture.Posting.Posts.Single().Entry.SourceVersion);
        Assert.Equal(validApproval.Id, fixture.Posting.Posts.Single().Entry.ApprovalRequestId);
    }

    private static ApprovalRequest ApprovedRequest(Fixture fixture, Guid sourceId, long approvedVersion,
        ApprovalTargetEntityType targetType)
    {
        var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), fixture.CompanyId, targetType, sourceId,
            "user", fixture.UserId, "treasury_post",
            new Dictionary<string, JsonNode?>
            {
                ["sourceVersion"] = JsonValue.Create(approvedVersion.ToString()),
                ["sourceType"] = JsonValue.Create(TreasurySourceTypes.AccountTransfer)
            },
            null, fixture.UserId, []);
        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, fixture.UserId, "Reviewed treasury evidence");
        return approval;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext db, TreasuryMovementService service,
            RecordingPostingService posting, Guid companyId, Guid userId, Guid fiscalPeriodId, DateOnly postingDate,
            Guid fromFinanceAccountId, Guid toFinanceAccountId, Guid controlAccountId, Guid feeAccountId,
            Guid incomeAccountId, Guid fromBankAccountId, Guid toBankAccountId, Guid nokBankAccountId,
            Guid outboundTransferId, Guid inboundTransferId, Guid cardPayoutId, Guid ambiguousPayoutId,
            Guid bankFeeTransactionId, Guid interestTransactionId)
        {
            _connection = connection; Db = db; Service = service; Posting = posting; CompanyId = companyId;
            UserId = userId; FiscalPeriodId = fiscalPeriodId; PostingDate = postingDate;
            FromFinanceAccountId = fromFinanceAccountId; ToFinanceAccountId = toFinanceAccountId;
            ControlAccountId = controlAccountId; FeeAccountId = feeAccountId; IncomeAccountId = incomeAccountId;
            FromBankAccountId = fromBankAccountId; ToBankAccountId = toBankAccountId; NokBankAccountId = nokBankAccountId;
            OutboundTransferId = outboundTransferId; InboundTransferId = inboundTransferId;
            CardPayoutId = cardPayoutId; AmbiguousPayoutId = ambiguousPayoutId;
            BankFeeTransactionId = bankFeeTransactionId; InterestTransactionId = interestTransactionId;
        }

        public VirtualCompanyDbContext Db { get; }
        public TreasuryMovementService Service { get; }
        public RecordingPostingService Posting { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid FiscalPeriodId { get; }
        public DateOnly PostingDate { get; }
        public Guid FromFinanceAccountId { get; }
        public Guid ToFinanceAccountId { get; }
        public Guid ControlAccountId { get; }
        public Guid FeeAccountId { get; }
        public Guid IncomeAccountId { get; }
        public Guid FromBankAccountId { get; }
        public Guid ToBankAccountId { get; }
        public Guid NokBankAccountId { get; }
        public Guid OutboundTransferId { get; }
        public Guid InboundTransferId { get; }
        public Guid CardPayoutId { get; }
        public Guid AmbiguousPayoutId { get; }
        public Guid BankFeeTransactionId { get; }
        public Guid InterestTransactionId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var now = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
            var postingDate = new DateOnly(2026, 8, 28);
            var company = Guid.NewGuid(); var user = Guid.NewGuid(); var fiscalPeriod = Guid.NewGuid();
            db.Companies.Add(new Company(company, "Treasury policy company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, user,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            FinanceAccount Account(string code, string name, string accountClass, string normal, string currency = "SEK") =>
                new(Guid.NewGuid(), company, code, name, accountClass, currency, 0m, now,
                    accountClass: accountClass, normalBalance: normal, effectiveFrom: postingDate,
                    isPostingEnabled: true);
            var fromFinance = Account("1930", "Operating cash", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit);
            var toFinance = Account("1940", "Reserve cash", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit);
            var nokFinance = Account("1950", "NOK cash", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit, "NOK");
            var control = Account("1580", "Card receivable", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit);
            var fee = Account("6570", "Bank fees", FinanceAccountClassValues.Expense, FinanceNormalBalanceValues.Debit);
            var income = Account("8310", "Interest income", FinanceAccountClassValues.Income, FinanceNormalBalanceValues.Credit);
            var fromBank = new CompanyBankAccount(Guid.NewGuid(), company, fromFinance.Id, "Operating", "Testbank", "•••• 1000", "SEK");
            var toBank = new CompanyBankAccount(Guid.NewGuid(), company, toFinance.Id, "Reserve", "Testbank", "•••• 2000", "SEK");
            var nokBank = new CompanyBankAccount(Guid.NewGuid(), company, nokFinance.Id, "NOK", "Testbank", "•••• 3000", "NOK");
            BankTransaction Transaction(Guid bank, decimal amount, string reference) => new(Guid.NewGuid(), company,
                bank, now, now, amount, "SEK", reference, "Treasury counterparty", importSource: "test");
            var outbound = Transaction(fromBank.Id, -100m, "TRANSFER-OUT");
            var inbound = Transaction(toBank.Id, 100m, "TRANSFER-IN");
            var card = Transaction(fromBank.Id, 970m, "CARD-PAYOUT");
            var ambiguous = Transaction(fromBank.Id, 960m, "AMBIGUOUS-PAYOUT");
            var bankFee = Transaction(fromBank.Id, -30m, "BANK-FEE");
            var interest = Transaction(toBank.Id, 20m, "INTEREST");
            db.AddRange(fromFinance, toFinance, nokFinance, control, fee, income, fromBank, toBank, nokBank,
                outbound, inbound, card, ambiguous, bankFee, interest);
            await db.SaveChangesAsync();

            var posting = new RecordingPostingService();
            var roles = new RoleResolver(fee.Id);
            var service = new TreasuryMovementService(db, posting, roles, new AuditStub(),
                new Context(company, user), new TreasuryMovementTelemetry(), new FixedTimeProvider(now));
            return new(connection, db, service, posting, company, user, fiscalPeriod, postingDate,
                fromFinance.Id, toFinance.Id, control.Id, fee.Id, income.Id, fromBank.Id, toBank.Id, nokBank.Id,
                outbound.Id, inbound.Id, card.Id, ambiguous.Id, bankFee.Id, interest.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RecordingPostingService : IAccountingPostingService
    {
        public List<PostAccountingEntryCommand> Posts { get; } = [];
        public List<ReverseAccountingEntryCommand> Reversals { get; } = [];

        public Task<AccountingPostingPreview> PreviewAsync(PreviewAccountingEntryCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Preview(command.Entry));
        public Task<AccountingPostingPreview> PreviewNonAuthoritativeCandidateAsync(
            PreviewNonAuthoritativeAccountingCandidateCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Preview(command.Entry));
        public Task<PostedAccountingJournal> PostAsync(PostAccountingEntryCommand command, CancellationToken cancellationToken)
        {
            Posts.Add(command);
            return Task.FromResult(new PostedAccountingJournal(Journal(command.Entry), false));
        }
        public Task<PostedAccountingJournal> ReverseAsync(ReverseAccountingEntryCommand command, CancellationToken cancellationToken)
        {
            Reversals.Add(command);
            var entry = Posts.Single().Entry;
            return Task.FromResult(new PostedAccountingJournal(Journal(entry, Guid.NewGuid(), "B-REV-1"), false));
        }
        public Task<PostedAccountingJournal> MaterializeProviderSwitchJournalAsync(
            MaterializeAccountingProviderSwitchJournalCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static AccountingPostingPreview Preview(ProposedAccountingEntry entry)
        {
            var debit = entry.Lines.Sum(x => x.DebitAmount); var credit = entry.Lines.Sum(x => x.CreditAmount);
            return new(debit == credit, debit, credit, debit - credit, entry.Lines[0].Currency, 2, []);
        }
        private static AccountingJournalDto Journal(ProposedAccountingEntry entry, Guid? id = null, string number = "B-1") =>
            new(id ?? Guid.NewGuid(), entry.CompanyId, entry.FiscalPeriodId, number, "posted", entry.VoucherSeriesCode,
                1, entry.PostingDate.Year, entry.DocumentDate, entry.PostingDate, entry.Lines[0].Currency,
                entry.PostingType, entry.Description, entry.SourceType, entry.SourceId, entry.SourceVersion,
                "se-default", "1", entry.ActorUserId, entry.ApprovalRequestId, entry.OriginalLedgerEntryId,
                entry.CorrectionReason, DateTime.UtcNow, entry.Lines.Sum(x => x.DebitAmount),
                entry.Lines.Sum(x => x.CreditAmount), []);
    }

    private sealed class RoleResolver(Guid feeAccountId) : IAccountingAccountRoleResolver
    {
        public Task<AccountingAccountRoleResolutionDto> ResolveRequiredAsync(Guid companyId, string roleKey,
            CancellationToken cancellationToken) => Task.FromResult(new AccountingAccountRoleResolutionDto(
                AccountingAccountRoleKeys.BankFee, feeAccountId, "6570", "Bank fees"));
        public Task<AccountingAccountRoleResolutionDto?> ResolveOptionalAsync(Guid companyId, string roleKey,
            CancellationToken cancellationToken) => Task.FromResult<AccountingAccountRoleResolutionDto?>(new(
                AccountingAccountRoleKeys.BankFee, feeAccountId, "6570", "Bank fees"));
    }

    private sealed class AuditStub : IAuditEventWriter
    {
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Context(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; } = userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
