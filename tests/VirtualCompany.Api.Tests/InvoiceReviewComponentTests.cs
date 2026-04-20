using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class InvoiceReviewComponentTests
{
    [Fact]
    public void Invoice_review_list_component_renders_loading_state()
    {
        using var context = new TestContext();

        var cut = context.RenderComponent<InvoiceReviewListContent>(parameters => parameters
            .Add(x => x.IsLoading, true));

        Assert.Contains("Loading invoice reviews", cut.Markup);
    }

    [Fact]
    public void Invoice_review_list_component_renders_empty_state()
    {
        using var context = new TestContext();

        var cut = context.RenderComponent<InvoiceReviewListContent>();

        Assert.Contains("No invoice reviews matched the active filters", cut.Markup);
    }

    [Fact]
    public void Invoice_review_list_component_renders_error_state()
    {
        using var context = new TestContext();

        var cut = context.RenderComponent<InvoiceReviewListContent>(parameters => parameters
            .Add(x => x.ErrorMessage, "Finance API failed."));

        Assert.Contains("Finance API failed.", cut.Markup);
    }

    [Fact]
    public void Invoice_review_list_component_renders_required_columns_and_navigation()
    {
        using var context = new TestContext();
        var invoiceId = Guid.Parse("89d7fe3e-3f44-43cf-b383-8f9b4f24cf4e");

        var cut = context.RenderComponent<InvoiceReviewListContent>(parameters => parameters
            .Add(x => x.Items, new[]
            {
                new FinanceInvoiceReviewListItemResponse
                {
                    Id = invoiceId,
                    InvoiceNumber = "INV-24051",
                    SupplierName = "Contoso Supplies",
                    Amount = 15420.50m,
                    Currency = "USD",
                    RiskLevel = "high",
                    RecommendationStatus = "awaiting_approval",
                    RecommendationOutcome = "request_human_approval",
                    Confidence = 0.91m,
                    LastUpdatedUtc = new DateTime(2026, 4, 16, 8, 30, 0, DateTimeKind.Utc)
                }
            })
            .Add(x => x.BuildDetailLink, item => $"/finance/reviews/{item.Id:D}?companyId=4c5cfd22-87fd-4214-b579-fc9e7554ab72"));

        Assert.Contains("Invoice number", cut.Markup);
        Assert.Contains("Supplier name", cut.Markup);
        Assert.Contains("Amount", cut.Markup);
        Assert.Contains("Currency", cut.Markup);
        Assert.Contains("Risk level", cut.Markup);
        Assert.Contains("Recommendation status", cut.Markup);
        Assert.Contains("Confidence", cut.Markup);
        Assert.Contains("Last updated", cut.Markup);
        Assert.Contains("INV-24051", cut.Markup);
        Assert.Contains("Contoso Supplies", cut.Markup);
        Assert.Contains("15420.50", cut.Markup);
        Assert.Contains("USD", cut.Markup);
        Assert.Contains("High", cut.Markup);
        Assert.Contains("Awaiting approval", cut.Markup);
        Assert.Contains("91 %".Replace(" ", string.Empty), cut.Markup.Replace(" ", string.Empty));
        Assert.Equal($"/finance/reviews/{invoiceId:D}?companyId=4c5cfd22-87fd-4214-b579-fc9e7554ab72", cut.Find("tbody a").GetAttribute("href"));
    }

    [Fact]
    public void Invoice_review_detail_component_renders_loading_empty_and_error_states()
    {
        using var context = new TestContext();

        var loading = context.RenderComponent<InvoiceReviewDetailContent>(parameters => parameters
            .Add(x => x.IsLoading, true));
        Assert.Contains("Loading invoice review", loading.Markup);

        var empty = context.RenderComponent<InvoiceReviewDetailContent>();
        Assert.Contains("No review result available", empty.Markup);

        var error = context.RenderComponent<InvoiceReviewDetailContent>(parameters => parameters
            .Add(x => x.ErrorMessage, "Review lookup failed."));
        Assert.Contains("Review lookup failed.", error.Markup);
    }

    [Fact]
    public void Invoice_review_detail_component_only_shows_actions_when_permitted_and_actionable()
    {
        using var context = new TestContext();

        var hidden = context.RenderComponent<InvoiceReviewDetailContent>(parameters => parameters
            .Add(x => x.Detail, new FinanceInvoiceReviewDetailResponse
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-1",
                SupplierName = "Fabrikam",
                RecommendationSummary = "No action.",
                RecommendedAction = "no_action",
                Actions = new FinanceInvoiceReviewActionAvailabilityResponse
                {
                    IsActionable = false
                }
            })
            .Add(x => x.BackToListHref, "/finance/reviews?companyId=1")
            .Add(x => x.SourceInvoiceHref, "/finance/invoices/1?companyId=1"));

        Assert.DoesNotContain("Approve", hidden.Markup);
        Assert.DoesNotContain("Reject", hidden.Markup);
        Assert.DoesNotContain("Send for follow-up", hidden.Markup);

        var approved = false;
        var visible = context.RenderComponent<InvoiceReviewDetailContent>(parameters => parameters
            .Add(x => x.Detail, new FinanceInvoiceReviewDetailResponse
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-2",
                SupplierName = "Northwind",
                RecommendationSummary = "Escalate for finance approval.",
                RecommendedAction = "request_human_approval",
                Actions = new FinanceInvoiceReviewActionAvailabilityResponse
                {
                    IsActionable = true,
                    CanApprove = true,
                    CanReject = true,
                    CanSendForFollowUp = true
                }
            })
            .Add(x => x.BackToListHref, "/finance/reviews?companyId=1")
            .Add(x => x.SourceInvoiceHref, "/finance/invoices/2?companyId=1")
            .Add(x => x.OnApprove, EventCallback.Factory.Create(this, () => approved = true)));

        Assert.Contains("Approve", visible.Markup);
        Assert.Contains("Reject", visible.Markup);
        Assert.Contains("Send for follow-up", visible.Markup);

        visible.Find("button.btn.btn-primary").Click();
        Assert.True(approved);
    }

    [Fact]
    public void Invoice_review_detail_page_gates_actions_by_permission_and_actionable_state()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var invoiceId = Guid.Parse("89d7fe3e-3f44-43cf-b383-8f9b4f24cf4e");

        using var actionableHarness = CreateInvoiceReviewDetailPageHarness(companyId, "owner", CreateReviewDetail(invoiceId, isActionable: true));
        actionableHarness.Navigation.NavigateTo($"http://localhost/finance/reviews/{invoiceId:D}?companyId={companyId:D}");
        var actionableCut = actionableHarness.Context.RenderComponent<InvoiceReviewDetailPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.InvoiceId, invoiceId));
        actionableCut.WaitForAssertion(() =>
        {
            Assert.Contains("Approve", actionableCut.Markup);
            Assert.Contains("Reject", actionableCut.Markup);
            Assert.Contains("Send for follow-up", actionableCut.Markup);
        });

        using var nonActionableHarness = CreateInvoiceReviewDetailPageHarness(companyId, "owner", CreateReviewDetail(invoiceId, isActionable: false));
        nonActionableHarness.Navigation.NavigateTo($"http://localhost/finance/reviews/{invoiceId:D}?companyId={companyId:D}");
        var nonActionableCut = nonActionableHarness.Context.RenderComponent<InvoiceReviewDetailPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.InvoiceId, invoiceId));
        nonActionableCut.WaitForAssertion(() => Assert.DoesNotContain("Approve", nonActionableCut.Markup));

        using var forbiddenHarness = CreateInvoiceReviewDetailPageHarness(companyId, "employee", CreateReviewDetail(invoiceId, isActionable: true));
        forbiddenHarness.Navigation.NavigateTo($"http://localhost/finance/reviews/{invoiceId:D}?companyId={companyId:D}");
        var forbiddenCut = forbiddenHarness.Context.RenderComponent<InvoiceReviewDetailPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.InvoiceId, invoiceId));
        forbiddenCut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Approve", forbiddenCut.Markup);
            Assert.Contains("Finance access requires the finance.view permission for the selected company.", forbiddenCut.Markup);
        });
    }

    [Fact]
    public void Invoice_review_detail_component_renders_structured_recommendation_details_and_workflow_history_links()
    {
        using var context = new TestContext();
        var approvalId = Guid.Parse("b1143925-4938-4e74-a421-7b5cb8855ff8");
        var auditId = Guid.Parse("db84057d-2d39-4322-8f57-bf0687cc748e");

        var cut = context.RenderComponent<InvoiceReviewDetailContent>(parameters => parameters
            .Add(x => x.CompanyId, Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72"))
            .Add(x => x.BackToListHref, "/finance/reviews?companyId=1")
            .Add(x => x.Detail, new FinanceInvoiceReviewDetailResponse
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-5",
                SupplierName = "Adventure Works",
                SourceInvoiceId = Guid.NewGuid(),
                RecommendationDetails = new FinanceInvoiceRecommendationDetailsResponse
                {
                    Classification = "overdue_invoice",
                    Risk = "high",
                    RationaleSummary = "Payment terms and due date require finance review.",
                    Confidence = 0.88m,
                    RecommendedAction = "request_human_approval",
                    CurrentWorkflowStatus = "awaiting_approval"
                },
                WorkflowHistory =
                [
                    new FinanceInvoiceWorkflowHistoryItemResponse
                    {
                        EventId = "tool-1",
                        EventType = "Tool event",
                        ActorOrSourceDisplayName = "System",
                        OccurredAtUtc = new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc),
                        RelatedAuditId = auditId
                    },
                    new FinanceInvoiceWorkflowHistoryItemResponse
                    {
                        EventId = "approval-1",
                        EventType = "Approval",
                        ActorOrSourceDisplayName = "User finance-approver",
                        OccurredAtUtc = new DateTime(2026, 4, 16, 8, 45, 0, DateTimeKind.Utc),
                        RelatedApprovalId = approvalId
                    }
                ],
                Actions = new FinanceInvoiceReviewActionAvailabilityResponse()
            }));

        Assert.Contains("Recommendation details", cut.Markup);
        Assert.Contains("Classification", cut.Markup);
        Assert.Contains("Overdue invoice", cut.Markup);
        Assert.Contains("Workflow history", cut.Markup);
        Assert.Contains("Tool event", cut.Markup);
        Assert.Contains("Approval", cut.Markup);
        Assert.Contains($"/audit/{auditId:D}?companyId=4c5cfd22-87fd-4214-b579-fc9e7554ab72", cut.Markup);
        Assert.Contains($"/approvals?companyId=4c5cfd22-87fd-4214-b579-fc9e7554ab72&approvalId={approvalId:D}", cut.Markup);
    }

    [Fact]
    public void Invoice_review_detail_component_normalizes_raw_workflow_history_before_rendering()
    {
        using var context = new TestContext();

        var cut = context.RenderComponent<InvoiceReviewDetailContent>(parameters => parameters
            .Add(x => x.CompanyId, Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72"))
            .Add(x => x.BackToListHref, "/finance/reviews?companyId=1")
            .Add(x => x.Detail, new FinanceInvoiceReviewDetailResponse
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-7",
                SupplierName = "Graphic Design Institute",
                WorkflowHistory =
                [
                    new FinanceInvoiceWorkflowHistoryItemResponse
                    {
                        EventId = "approval-1",
                        EventType = "Approval",
                        ActorOrSourceDisplayName = "Finance Approver",
                        OccurredAtUtc = new DateTime(2026, 4, 16, 8, 0, 0, DateTimeKind.Utc)
                    },
                    new FinanceInvoiceWorkflowHistoryItemResponse
                    {
                        EventId = "tool-1",
                        EventType = "Tool event",
                        ActorOrSourceDisplayName = "System",
                        OccurredAtUtc = new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc)
                    },
                    new FinanceInvoiceWorkflowHistoryItemResponse
                    {
                        EventId = " approval-1 ",
                        EventType = "Approval",
                        ActorOrSourceDisplayName = "Finance Approver",
                        OccurredAtUtc = new DateTime(2026, 4, 16, 7, 0, 0, DateTimeKind.Utc)
                    },
                    new FinanceInvoiceWorkflowHistoryItemResponse
                    {
                        EventId = string.Empty,
                        EventType = "Task execution",
                        ActorOrSourceDisplayName = "System",
                        OccurredAtUtc = default
                    }
                ],
                Actions = new FinanceInvoiceReviewActionAvailabilityResponse()
            }));

        var items = cut.FindAll(".list-group-item");
        Assert.Equal(3, items.Count);
        Assert.Contains("Tool event", items[0].TextContent);
        Assert.Contains("Approval", items[1].TextContent);
        Assert.Contains("Task execution", items[2].TextContent);
        Assert.Contains("Unknown time", items[2].TextContent);
    }

    [Fact]
    public void Invoice_workflow_presentation_sorts_descending_deduplicates_by_event_id_and_preserves_blank_ids()
    {
        var newestAuditId = Guid.Parse("db84057d-2d39-4322-8f57-bf0687cc748e");
        var approvalId = Guid.Parse("b1143925-4938-4e74-a421-7b5cb8855ff8");

        var history = InvoiceWorkflowPresentation.NormalizeWorkflowHistory(
        [
            new FinanceInvoiceWorkflowHistoryItemResponse
            {
                EventId = " duplicate-event ",
                EventType = "Approval",
                ActorOrSourceDisplayName = "Finance Approver",
                OccurredAtUtc = new DateTime(2026, 4, 16, 8, 30, 0, DateTimeKind.Utc),
                RelatedApprovalId = approvalId
            },
            new FinanceInvoiceWorkflowHistoryItemResponse
            {
                EventId = "duplicate-event",
                EventType = "Approval",
                ActorOrSourceDisplayName = "Finance Approver",
                OccurredAtUtc = new DateTime(2026, 4, 16, 8, 0, 0, DateTimeKind.Utc),
                RelatedApprovalId = approvalId
            },
            new FinanceInvoiceWorkflowHistoryItemResponse
            {
                EventId = string.Empty,
                EventType = "Task execution",
                ActorOrSourceDisplayName = "System",
                OccurredAtUtc = default
            },
            new FinanceInvoiceWorkflowHistoryItemResponse
            {
                EventId = "tool-1",
                EventType = "Tool event",
                ActorOrSourceDisplayName = "System",
                OccurredAtUtc = new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc),
                RelatedAuditId = newestAuditId
            }
        ]);

        Assert.Equal(3, history.Count);
        Assert.Equal("tool-1", history[0].EventId);
        Assert.Equal("duplicate-event", history[1].EventId);
        Assert.Equal(string.Empty, history[2].EventId);
        Assert.Equal(newestAuditId, history[0].RelatedAuditId);
        Assert.Equal(approvalId, history[1].RelatedApprovalId);
    }

    [Fact]
    public void Invoice_workflow_presentation_prefers_more_complete_duplicate_when_timestamps_match()
    {
        var auditId = Guid.Parse("db84057d-2d39-4322-8f57-bf0687cc748e");

        var history = InvoiceWorkflowPresentation.NormalizeWorkflowHistory(
        [
            new FinanceInvoiceWorkflowHistoryItemResponse
            {
                EventId = "duplicate-event",
                EventType = "Review",
                OccurredAtUtc = new DateTime(2026, 4, 16, 8, 30, 0, DateTimeKind.Utc)
            },
            new FinanceInvoiceWorkflowHistoryItemResponse
            {
                EventId = "duplicate-event",
                EventType = "Review",
                ActorOrSourceDisplayName = "Finance analyst",
                OccurredAtUtc = new DateTime(2026, 4, 16, 8, 30, 0, DateTimeKind.Utc),
                RelatedAuditId = auditId
            }
        ]);

        var item = Assert.Single(history);
        Assert.Equal("duplicate-event", item.EventId);
        Assert.Equal("Review", item.EventType);
        Assert.Equal("Finance analyst", item.ActorOrSourceDisplayName);
        Assert.Equal(auditId, item.RelatedAuditId);
    }

    [Fact]
    public void Invoice_workflow_presentation_resolves_invoice_detail_recommendation_from_structured_fields_only()
    {
        var recommendation = InvoiceWorkflowPresentation.ResolveRecommendationDetails(new FinanceInvoiceDetailResponse
        {
            WorkflowContext = new FinanceInvoiceWorkflowContextResponse
            {
                Classification = "overdue_invoice",
                RiskLevel = "high",
                RecommendedAction = "request_human_approval",
                Rationale = "Payment terms and due date require finance review.",
                Confidence = 0.88m,
                ReviewTaskStatus = "awaiting_approval"
            }
        });

        Assert.NotNull(recommendation);
        Assert.Equal("overdue_invoice", recommendation!.Classification);
        Assert.Equal("high", recommendation.Risk);
        Assert.Equal("Payment terms and due date require finance review.", recommendation.RationaleSummary);
        Assert.Equal("request_human_approval", recommendation.RecommendedAction);
        Assert.Equal("awaiting_approval", recommendation.CurrentWorkflowStatus);
        Assert.Equal(0.88m, recommendation.Confidence);
    }

    [Fact]
    public void Invoice_review_detail_component_renders_non_blocking_empty_states_for_recommendation_and_history()
    {
        using var context = new TestContext();

        var cut = context.RenderComponent<InvoiceReviewDetailContent>(parameters => parameters
            .Add(x => x.BackToListHref, "/finance/reviews?companyId=1")
            .Add(x => x.Detail, new FinanceInvoiceReviewDetailResponse
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-6",
                SupplierName = "Northwind",
                Actions = new FinanceInvoiceReviewActionAvailabilityResponse()
            }));

        Assert.Contains("No recommendation details available yet.", cut.Markup);
        Assert.Contains("No workflow history available yet.", cut.Markup);
        Assert.Contains("INV-6", cut.Markup);
    }

    [Fact]
    public void Invoice_review_detail_component_omits_optional_navigation_links_when_related_records_are_unavailable()
    {
        using var context = new TestContext();

        var cut = context.RenderComponent<InvoiceReviewDetailContent>(parameters => parameters
            .Add(x => x.Detail, new FinanceInvoiceReviewDetailResponse
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-3",
                SupplierName = "Adventure Works",
                RecommendationSummary = "Awaiting review context.",
                RecommendedAction = "pending_review",
                SourceInvoiceId = Guid.Empty,
                Actions = new FinanceInvoiceReviewActionAvailabilityResponse()
            })
            .Add(x => x.BackToListHref, "/finance/reviews?companyId=1"));

        Assert.DoesNotContain("Open source invoice", cut.Markup);
        Assert.DoesNotContain("Open related approval", cut.Markup);
    }

    [Fact]
    public void Invoice_review_detail_component_disables_visible_actions_while_submitting()
    {
        using var context = new TestContext();

        var cut = context.RenderComponent<InvoiceReviewDetailContent>(parameters => parameters
            .Add(x => x.IsSubmitting, true)
            .Add(x => x.Detail, new FinanceInvoiceReviewDetailResponse
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-4",
                SupplierName = "Northwind",
                RecommendationSummary = "Escalate for finance approval.",
                RecommendedAction = "request_human_approval",
                SourceInvoiceId = Guid.NewGuid(),
                Actions = new FinanceInvoiceReviewActionAvailabilityResponse
                {
                    IsActionable = true,
                    CanApprove = true,
                    CanReject = true,
                    CanSendForFollowUp = true
                }
            })
            .Add(x => x.BackToListHref, "/finance/reviews?companyId=1")
            .Add(x => x.SourceInvoiceHref, "/finance/invoices/4?companyId=1"));

        Assert.All(cut.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void Invoice_reviews_page_initializes_filters_from_query_parameters_and_requests_matching_data()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        using var harness = CreateInvoiceReviewsPageHarness(companyId, new[]
        {
            new FinanceInvoiceReviewListItemResponse
            {
                Id = Guid.Parse("89d7fe3e-3f44-43cf-b383-8f9b4f24cf4e"),
                InvoiceNumber = "INV-24051",
                SupplierName = "Contoso Supplies",
                Amount = 15420.50m,
                Currency = "USD",
                Status = "pending_approval",
                RiskLevel = "high",
                RecommendationStatus = "awaiting_approval",
                RecommendationOutcome = "request_human_approval",
                Confidence = 0.91m,
                LastUpdatedUtc = new DateTime(2026, 4, 16, 8, 30, 0, DateTimeKind.Utc)
            }
        });

        harness.Navigation.NavigateTo($"http://localhost/finance/reviews?companyId={companyId:D}&status=pending_approval&supplier=Contoso%20Supplies&riskLevel=high&outcome=request_human_approval");

        var cut = harness.Context.RenderComponent<InvoiceReviewsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.Status, "pending_approval")
            .Add(x => x.Supplier, "Contoso Supplies")
            .Add(x => x.RiskLevel, "high")
            .Add(x => x.Outcome, "request_human_approval"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("INV-24051", cut.Markup);
            Assert.Contains("status=pending_approval", harness.LastFinanceRequestQuery);
            Assert.Contains("supplier=Contoso%20Supplies", harness.LastFinanceRequestQuery);
            Assert.Contains("riskLevel=high", harness.LastFinanceRequestQuery);
            Assert.Contains("outcome=request_human_approval", harness.LastFinanceRequestQuery);
        });
    }

    [Fact]
    public void Invoice_reviews_page_updates_query_string_when_filters_change()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        using var harness = CreateInvoiceReviewsPageHarness(companyId, Array.Empty<FinanceInvoiceReviewListItemResponse>());

        harness.Navigation.NavigateTo($"http://localhost/finance/reviews?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<InvoiceReviewsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() => Assert.Contains("No invoice reviews matched the active filters", cut.Markup));

        cut.Find("#invoice-review-status").Change("pending_approval");
        cut.WaitForAssertion(() => Assert.Contains($"companyId={companyId:D}", harness.Navigation.Uri));
        cut.WaitForAssertion(() => Assert.Contains("status=pending_approval", harness.Navigation.Uri));

        cut.Find("#invoice-review-supplier").Change("Northwind");
        cut.WaitForAssertion(() => Assert.Contains("supplier=Northwind", harness.Navigation.Uri));
    }

    private static InvoiceReviewsPageHarness CreateInvoiceReviewsPageHarness(
        Guid companyId,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> reviews)
    {
        var context = new TestContext();
        var financeRequests = new List<Uri>();

        context.Services.AddSingleton(new FinanceAccessResolver());
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(CreateCurrentUserContext(companyId)),
                _ => CreateNotFoundResponse()
            };
        })) { BaseAddress = new Uri("http://localhost/") });
        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/finance/reviews")
            {
                financeRequests.Add(request.RequestUri);
                return CreateJsonResponse(reviews);
            }

            return CreateNotFoundResponse();
        })) { BaseAddress = new Uri("http://localhost/") });

        return new InvoiceReviewsPageHarness(
            context,
            context.Services.GetRequiredService<FakeNavigationManager>(),
            financeRequests);
    }

    private static CurrentUserContextViewModel CreateCurrentUserContext(Guid companyId) =>
        new()
        {
            Memberships =
            [
                new CompanyMembershipViewModel
                {
                    MembershipId = Guid.NewGuid(),
                    CompanyId = companyId,
                    CompanyName = "Contoso Finance",
                    MembershipRole = "owner",
                    Status = "active"
                }
            ],
            ActiveCompany = new ResolvedCompanyContextViewModel
            {
                MembershipId = Guid.NewGuid(),
                CompanyId = companyId,
                CompanyName = "Contoso Finance",
                MembershipRole = "owner",
                Status = "active"
            }
        };

    private static InvoiceReviewDetailPageHarness CreateInvoiceReviewDetailPageHarness(
        Guid companyId,
        string membershipRole,
        FinanceInvoiceReviewDetailResponse detail)
    {
        var context = new TestContext();

        context.Services.AddSingleton(new FinanceAccessResolver());
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(CreateCurrentUserContext(companyId, membershipRole)),
                _ => CreateNotFoundResponse()
            };
        })) { BaseAddress = new Uri("http://localhost/") });
        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/finance/reviews/{detail.Id:D}")
            {
                return CreateJsonResponse(detail);
            }

            return CreateNotFoundResponse();
        })) { BaseAddress = new Uri("http://localhost/") });

        return new InvoiceReviewDetailPageHarness(
            context,
            context.Services.GetRequiredService<FakeNavigationManager>());
    }

    private static FinanceInvoiceReviewDetailResponse CreateReviewDetail(Guid invoiceId, bool isActionable) =>
        new()
        {
            Id = invoiceId,
            InvoiceNumber = "INV-24051",
            SupplierName = "Contoso Supplies",
            Amount = 15420.50m,
            Currency = "USD",
            Status = isActionable ? "pending_approval" : "open",
            RiskLevel = "high",
            RecommendationStatus = isActionable ? "awaiting_approval" : "completed",
            RecommendationSummary = "Finance review summary.",
            RecommendedAction = isActionable ? "request_human_approval" : "no_action",
            Confidence = 0.91m,
            LastUpdatedUtc = new DateTime(2026, 4, 16, 8, 30, 0, DateTimeKind.Utc),
            SourceInvoiceId = invoiceId,
            Actions = new FinanceInvoiceReviewActionAvailabilityResponse { IsActionable = isActionable, CanApprove = isActionable, CanReject = isActionable, CanSendForFollowUp = isActionable }
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

    private sealed class InvoiceReviewsPageHarness : IDisposable
    {
        public InvoiceReviewsPageHarness(TestContext context, FakeNavigationManager navigation, IReadOnlyList<Uri> financeRequests)
        {
            Context = context;
            Navigation = navigation;
            FinanceRequests = financeRequests;
        }

        public TestContext Context { get; }
        public FakeNavigationManager Navigation { get; }
        public IReadOnlyList<Uri> FinanceRequests { get; }
        public string LastFinanceRequestQuery => FinanceRequests.LastOrDefault()?.Query ?? string.Empty;

        public void Dispose()
        {
            Context.Dispose();
        }
    }

    private sealed class InvoiceReviewDetailPageHarness : IDisposable
    {
        public InvoiceReviewDetailPageHarness(TestContext context, FakeNavigationManager navigation)
        {
            Context = context;
            Navigation = navigation;
        }

        public TestContext Context { get; }
        public FakeNavigationManager Navigation { get; }

        public void Dispose()
        {
            Context.Dispose();
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private static CurrentUserContextViewModel CreateCurrentUserContext(Guid companyId, string membershipRole) =>
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

    private static CurrentUserContextViewModel CreateCurrentUserContext(Guid companyId) =>
        CreateCurrentUserContext(companyId, "owner");
}