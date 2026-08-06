using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Shared;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Localization.Formatting;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceBillsPageTests
{
    [Fact]
    public void Bills_page_renders_list_selected_detail_and_agent_insights()
    {
        var companyId = Guid.Parse("d53590ef-f7ff-4b98-a372-a9f3133e0f6c");
        var billId = Guid.Parse("8dbde10a-cbd7-4fb3-aab8-459f0f55ed1f");
        var bills = new List<FinanceBillResponse>
        {
            new()
            {
                Id = billId,
                CounterpartyId = Guid.NewGuid(),
                CounterpartyName = "Northwind Supplies",
                BillNumber = "BILL-24018",
                ReceivedUtc = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                DueUtc = new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc),
                Amount = 845.30m,
                Currency = "USD",
                Status = "approved",
                PostingStatus = "booked",
                SettlementStatus = "partially_paid",
                DueStatus = "not_due",
                DocumentKind = "supplier_invoice",
                ProviderStatus = "booked=true;cancelled=false;fullyPaid=false;credit=false;balance=400"
            }
        };

        var billDetail = new FinanceBillDetailResponse
        {
            Id = billId,
            CounterpartyId = bills[0].CounterpartyId,
            CounterpartyName = bills[0].CounterpartyName,
            BillNumber = bills[0].BillNumber,
            ReceivedUtc = bills[0].ReceivedUtc,
            DueUtc = bills[0].DueUtc,
            Amount = bills[0].Amount,
            Currency = bills[0].Currency,
            Status = bills[0].Status,
            PostingStatus = bills[0].PostingStatus,
            SettlementStatus = bills[0].SettlementStatus,
            DueStatus = bills[0].DueStatus,
            DocumentKind = bills[0].DocumentKind,
            ProviderStatus = bills[0].ProviderStatus,
            LinkedDocument = new FinanceLinkedDocumentAccessResponse
            {
                Availability = "missing",
                Message = "No linked document."
            },
            AgentInsights =
            [
                new NormalizedFinanceInsightResponse
                {
                    Id = Guid.NewGuid(),
                    Severity = "critical",
                    Status = "active",
                    CheckName = "Payables pressure",
                    Message = "This bill is overdue and needs attention.",
                    Recommendation = "Prioritize payment timing with treasury.",
                    UpdatedAt = new DateTime(2026, 4, 22, 8, 30, 0, DateTimeKind.Utc)
                }
            ]
        };

        using var harness = CreateHarness(companyId, bills, billDetail);
        harness.Navigation.NavigateTo($"http://localhost/finance/bills/{billId:D}?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<BillsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.BillId, billId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Bill list", cut.Markup);
            Assert.Contains("BILL-24018", cut.Markup);
            Assert.Contains("Northwind Supplies", cut.Markup);
            Assert.Contains("USD 845.30", cut.Markup);
            Assert.Contains("Partially paid", cut.Markup);
            Assert.Contains("Supplier invoice", cut.Markup);
            Assert.Contains("USD 445.30 paid; USD 400.00 remaining", cut.Markup);
            Assert.Contains("Agent insights", cut.Markup);
            Assert.Contains("This bill is overdue and needs attention.", cut.Markup);
            Assert.Contains("Prioritize payment timing with treasury.", cut.Markup);
        });
    }

    [Fact]
    public void Bills_page_embeds_payment_approval_and_records_the_decision()
    {
        var companyId = Guid.Parse("391d7881-f475-43ce-ac86-7e610e72946a");
        var billId = Guid.Parse("fc9df8a8-bcd9-4978-806c-a845a696aa43");
        var approvalId = Guid.Parse("6f66e311-4a3a-448b-ae23-d7223683d3b7");
        var stepId = Guid.Parse("809e6439-f5ae-477e-93ad-ece676622003");
        var proposal = new SupplierInvoicePaymentProposalResponse
        {
            Id = Guid.NewGuid(),
            BillId = billId,
            SupplierId = Guid.NewGuid(),
            SupplierName = "Prosa Test Services AB",
            Amount = 10_000m,
            Currency = "SEK",
            DueUtc = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
            PaymentReference = "9",
            Status = "awaiting_approval",
            ApprovalRequestId = approvalId
        };
        var bill = new FinanceBillResponse
        {
            Id = billId,
            CounterpartyId = proposal.SupplierId,
            CounterpartyName = proposal.SupplierName,
            BillNumber = "9",
            ReceivedUtc = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc),
            DueUtc = proposal.DueUtc,
            Amount = proposal.Amount,
            Currency = proposal.Currency,
            Status = "draft",
            PostingStatus = "draft",
            SettlementStatus = "unpaid",
            DueStatus = "not_due",
            DocumentKind = "supplier_invoice",
            PaymentProposal = proposal
        };
        var detail = new FinanceBillDetailResponse
        {
            Id = bill.Id,
            CounterpartyId = bill.CounterpartyId,
            CounterpartyName = bill.CounterpartyName,
            BillNumber = bill.BillNumber,
            ReceivedUtc = bill.ReceivedUtc,
            DueUtc = bill.DueUtc,
            Amount = bill.Amount,
            Currency = bill.Currency,
            Status = bill.Status,
            PostingStatus = bill.PostingStatus,
            SettlementStatus = bill.SettlementStatus,
            DueStatus = bill.DueStatus,
            DocumentKind = bill.DocumentKind,
            PaymentProposal = proposal,
            LinkedDocument = new FinanceLinkedDocumentAccessResponse { Availability = "missing", Message = "No linked document." }
        };
        var approval = new ApprovalRequestViewModel
        {
            Id = approvalId,
            CompanyId = companyId,
            Status = "pending",
            RationaleSummary = "This payment exceeds the approval threshold.",
            AffectedDataSummary = "Task: Approve payment proposal for Prosa Test Services AB | Amount: 10000 SEK",
            CurrentStep = new ApprovalStepViewModel { Id = stepId, SequenceNo = 1, Status = "pending", ApproverType = "role", ApproverRef = "owner" },
            Steps = [new ApprovalStepViewModel { Id = stepId, SequenceNo = 1, Status = "pending", ApproverType = "role", ApproverRef = "owner" }]
        };
        ApprovalDecisionRequest? recordedDecision = null;

        using var harness = CreateHarness(companyId, [bill], detail, approval, decision => recordedDecision = decision);
        harness.Navigation.NavigateTo($"http://localhost/finance/supplier-bills/{billId:D}?companyId={companyId:D}");
        var cut = harness.Context.RenderComponent<BillsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.BillId, billId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Bill progress", cut.Markup);
            Assert.Contains("Payment approved", cut.Markup);
            Assert.Contains("Action needed", cut.Markup);
            Assert.Contains("Approve payment", cut.Markup);
        });
        var approveButton = cut.FindAll("button").Single(button => button.TextContent.Contains("Approve payment", StringComparison.Ordinal));
        approveButton.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(recordedDecision);
            Assert.Equal(approvalId, recordedDecision!.ApprovalId);
            Assert.Equal(stepId, recordedDecision.StepId);
            Assert.Equal("approve", recordedDecision.Decision);
            Assert.Contains("Payment approved", cut.Markup);
        });
    }

    [Fact]
    public void Bills_page_allows_fortnox_registration_after_manual_payment_file_was_selected()
    {
        var companyId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        var proposal = new SupplierInvoicePaymentProposalResponse
        {
            Id = Guid.NewGuid(),
            BillId = billId,
            SupplierId = Guid.NewGuid(),
            SupplierName = "Nordic Test Services AB",
            Amount = 15_000m,
            Currency = "SEK",
            DueUtc = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            PaymentReference = "TEST20260801001",
            Status = "ready_for_payment",
            ExportMode = "prepare_payment_file",
            ExportStatus = "export_requested",
            ExportProviderKey = "fortnox",
            ExportResponseSummary = "Manual payment file required. No bank payment was initiated automatically."
        };
        var bill = new FinanceBillResponse
        {
            Id = billId,
            CounterpartyId = proposal.SupplierId,
            CounterpartyName = proposal.SupplierName,
            BillNumber = "10",
            ReceivedUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            DueUtc = proposal.DueUtc,
            Amount = proposal.Amount,
            Currency = proposal.Currency,
            Status = "approved",
            PostingStatus = "booked",
            SettlementStatus = "unpaid",
            DueStatus = "not_due",
            DocumentKind = "supplier_invoice",
            PaymentProposal = proposal
        };
        var detail = new FinanceBillDetailResponse
        {
            Id = bill.Id,
            CounterpartyId = bill.CounterpartyId,
            CounterpartyName = bill.CounterpartyName,
            BillNumber = bill.BillNumber,
            ReceivedUtc = bill.ReceivedUtc,
            DueUtc = bill.DueUtc,
            Amount = bill.Amount,
            Currency = bill.Currency,
            Status = bill.Status,
            PostingStatus = bill.PostingStatus,
            SettlementStatus = bill.SettlementStatus,
            DueStatus = bill.DueStatus,
            DocumentKind = bill.DocumentKind,
            PaymentProposal = proposal,
            LinkedDocument = new FinanceLinkedDocumentAccessResponse { Availability = "missing", Message = "No linked document." }
        };

        using var harness = CreateHarness(companyId, [bill], detail);
        harness.Navigation.NavigateTo($"http://localhost/finance/supplier-bills/{billId:D}?companyId={companyId:D}");
        var cut = harness.Context.RenderComponent<BillsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.BillId, billId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Invoice received", cut.Markup);
            Assert.Contains("Details checked", cut.Markup);
            Assert.Contains("Supplier confirmed", cut.Markup);
            Assert.Contains("Bill approved", cut.Markup);
            Assert.Contains("Sent to Fortnox", cut.Markup);
            Assert.Contains("Payment approved", cut.Markup);
            Assert.Contains("Payment registered", cut.Markup);
            Assert.Contains("Paid and reconciled", cut.Markup);
            Assert.Single(cut.FindAll("button").Where(button =>
                button.TextContent.Contains("Register payment in Fortnox", StringComparison.Ordinal)));
            Assert.Empty(cut.FindAll("button").Where(button =>
                button.TextContent.Contains("Prepare payment file", StringComparison.Ordinal)));
        });
    }

    private static BillsPageHarness CreateHarness(
        Guid companyId,
        List<FinanceBillResponse> bills,
        FinanceBillDetailResponse billDetail,
        ApprovalRequestViewModel? approval = null,
        Action<ApprovalDecisionRequest>? decisionObserver = null)
    {
        var context = new TestContext();
        context.Services.AddOptions();
        context.Services.AddLogging();
        context.Services.AddLocalization();
        var presentationContext = new CompanyPresentationContext();
        presentationContext.SetFormattingCulture("en-US");
        context.Services.AddSingleton<ICompanyPresentationContext>(presentationContext);
        context.Services.AddSingleton<ILocalDateTimeFormatter, LocalDateTimeFormatter>();
        context.Services.AddSingleton<INumberFormatter, NumberFormatter>();
        context.Services.AddSingleton<IMoneyFormatter, MoneyFormatter>();
        context.Services.AddSingleton(new FinanceAccessResolver());

        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(CreateCurrentUser(companyId, "owner")),
                _ => CreateNotFoundResponse()
            });
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path == $"/internal/companies/{companyId:D}/finance/bills" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateJsonResponse(bills));
            }

            if (path == $"/internal/companies/{companyId:D}/finance/bills/{billDetail.Id:D}" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateJsonResponse(billDetail));
            }

            return Task.FromResult(CreateNotFoundResponse());
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        context.Services.AddSingleton(new ApprovalApiClient(new HttpClient(new AsyncStubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (approval is null)
            {
                return CreateNotFoundResponse();
            }

            var approvalPath = $"/api/companies/{companyId:D}/approvals/{approval.Id:D}";
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == approvalPath)
            {
                return CreateJsonResponse(approval);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == $"{approvalPath}/decisions")
            {
                var decision = await request.Content!.ReadFromJsonAsync<ApprovalDecisionRequest>(cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException("Expected an approval decision payload.");
                decisionObserver?.Invoke(decision);
                approval.Status = decision.Decision.StartsWith("reject", StringComparison.OrdinalIgnoreCase) ? "rejected" : "approved";
                approval.CurrentStep!.Status = approval.Status;
                approval.CurrentStep.Comment = decision.Comment;
                return CreateJsonResponse(new ApprovalDecisionResultViewModel
                {
                    Approval = approval,
                    DecidedStep = approval.CurrentStep,
                    IsFinalized = true
                });
            }

            return CreateNotFoundResponse();
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        return new BillsPageHarness(context, context.Services.GetRequiredService<FakeNavigationManager>());
    }

    private static CurrentUserContextViewModel CreateCurrentUser(Guid companyId, string membershipRole) =>
        new()
        {
            Memberships =
            [
                new CompanyMembershipViewModel
                {
                    MembershipId = Guid.NewGuid(),
                    CompanyId = companyId,
                    CompanyName = "Contoso Finance",
                    MembershipRole = membershipRole,
                    Status = "active"
                }
            ],
            ActiveCompany = new ResolvedCompanyContextViewModel
            {
                MembershipId = Guid.NewGuid(),
                CompanyId = companyId,
                CompanyName = "Contoso Finance",
                MembershipRole = membershipRole,
                Status = "active"
            }
        };

    private static HttpResponseMessage CreateJsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private static HttpResponseMessage CreateNotFoundResponse() =>
        new(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { title = "Not found", detail = "Not found." })
        };

    private sealed record BillsPageHarness(TestContext Context, FakeNavigationManager Navigation) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }

    private sealed class AsyncStubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public AsyncStubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
