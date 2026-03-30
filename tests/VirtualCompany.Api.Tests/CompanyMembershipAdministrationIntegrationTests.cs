using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyMembershipAdministrationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CompanyMembershipAdministrationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InviteUser_creates_pending_invitation_membership_and_outbox_messages()
    {
        var seed = await SeedSingleMembershipAsync("owner", "owner@example.com", CompanyMembershipRole.Owner);

        using var client = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "teammate@example.com", MembershipRole = "manager" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(payload);
        Assert.Equal("teammate@example.com", payload!.Invitation.Email);
        Assert.Equal("manager", payload.Invitation.MembershipRole);
        Assert.Equal("pending", payload.Invitation.Status);
        Assert.False(payload.IsReinvite);
        Assert.False(string.IsNullOrWhiteSpace(payload.AcceptanceToken));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        Assert.True(await dbContext.CompanyInvitations.AnyAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Email == "teammate@example.com" &&
            x.Status == CompanyInvitationStatus.Pending));

        var membership = await dbContext.CompanyMemberships.SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.InvitedEmail == "teammate@example.com");

        Assert.Equal(CompanyMembershipRole.Manager, membership.Role);
        Assert.Equal(CompanyMembershipStatus.Pending, membership.Status);
        Assert.Null(membership.UserId);

        Assert.True(await dbContext.CompanyOutboxMessages.AnyAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Topic == "company.invitation.delivery_requested"));

        var deliveryOutbox = await dbContext.CompanyOutboxMessages.SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Topic == CompanyOutboxTopics.InvitationDeliveryRequested);

        Assert.True(await dbContext.CompanyOutboxMessages.AnyAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Topic == "company.invitation.created"));
        Assert.Equal(0, deliveryOutbox.AttemptCount);
        Assert.Null(deliveryOutbox.ProcessedUtc);
        Assert.False(string.IsNullOrWhiteSpace(deliveryOutbox.CorrelationId));
    }

    [Fact]
    public async Task Employee_cannot_invite_users()
    {
        var seed = await SeedSingleMembershipAsync("employee", "employee@example.com", CompanyMembershipRole.Employee);

        using var client = CreateAuthenticatedClient("employee", "employee@example.com", "Employee");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "teammate@example.com", MembershipRole = "employee" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_invite_users()
    {
        var seed = await SeedSingleMembershipAsync("admin", "admin@example.com", CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("admin", "admin@example.com", "Admin");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "manager@example.com", MembershipRole = "manager" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(payload);
        Assert.Equal("manager@example.com", payload!.Invitation.Email);
        Assert.False(payload.IsReinvite);
    }

    public static IEnumerable<object[]> SupportedRoleValues()
    {
        foreach (var role in CompanyMembershipRoleValues.All)
        {
            yield return [role.Value, role.Role];
        }
    }

    [Theory]
    [MemberData(nameof(SupportedRoleValues))]
    public async Task InviteUser_accepts_each_supported_role(string roleValue, CompanyMembershipRole expectedRole)
    {
        var seed = await SeedSingleMembershipAsync("owner", "owner@example.com", CompanyMembershipRole.Owner);

        using var client = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = $"{roleValue}@example.com", MembershipRole = roleValue });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(payload);
        Assert.Equal(roleValue, payload!.Invitation.MembershipRole);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var membership = await dbContext.CompanyMemberships.SingleAsync(x => x.CompanyId == seed.CompanyId && x.InvitedEmail == $"{roleValue}@example.com");
        Assert.Equal(expectedRole, membership.Role);
    }

    [Fact]
    public async Task AcceptInvitation_activates_membership_for_matching_authenticated_user()
    {
        var seed = await SeedSingleMembershipAsync("owner", "owner@example.com", CompanyMembershipRole.Owner);

        using var ownerClient = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var invitationResponse = await ownerClient.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "invitee@example.com", MembershipRole = "employee" });

        var invitation = await invitationResponse.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(invitation);

        Guid pendingMembershipId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            pendingMembershipId = (await dbContext.CompanyMemberships.SingleAsync(x => x.CompanyId == seed.CompanyId && x.InvitedEmail == "invitee@example.com")).Id;
        }

        using var inviteeClient = CreateAuthenticatedClient("invitee", "invitee@example.com", "Invitee");
        var acceptResponse = await inviteeClient.PostAsJsonAsync(
            "/api/invitations/accept",
            new { Token = invitation!.AcceptanceToken });

        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        var accepted = await acceptResponse.Content.ReadFromJsonAsync<AcceptInvitationResponse>();
        Assert.NotNull(accepted);
        Assert.Equal(seed.CompanyId, accepted!.CompanyId);
        Assert.Equal("employee", accepted.MembershipRole);
        Assert.Equal("active", accepted.Status);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var inviteeUser = await dbContext.Users.SingleAsync(x => x.Email == "invitee@example.com");
        var membership = await dbContext.CompanyMemberships.SingleAsync(x => x.Id == pendingMembershipId);
        var storedInvitation = await dbContext.CompanyInvitations.SingleAsync(x => x.CompanyId == seed.CompanyId && x.Email == "invitee@example.com");

        Assert.Equal(CompanyMembershipStatus.Active, membership.Status);
        Assert.Equal(CompanyMembershipRole.Employee, membership.Role);
        Assert.Equal(inviteeUser.Id, membership.UserId);
        Assert.Equal(CompanyInvitationStatus.Accepted, storedInvitation.Status);
        Assert.Equal(inviteeUser.Id, storedInvitation.AcceptedByUserId);
    }

    [Fact]
    public async Task RevokedInvitation_cannot_be_accepted()
    {
        var seed = await SeedSingleMembershipAsync("owner", "owner@example.com", CompanyMembershipRole.Owner);

        using var ownerClient = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var invitationResponse = await ownerClient.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "revoked@example.com", MembershipRole = "employee" });

        var invitation = await invitationResponse.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(invitation);

        var revokeResponse = await ownerClient.PostAsync(
            $"/api/companies/{seed.CompanyId}/invitations/{invitation!.Invitation.InvitationId}/revoke",
            content: null);

        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var membership = await dbContext.CompanyMemberships.SingleAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.InvitedEmail == "revoked@example.com");

            Assert.Equal(CompanyMembershipStatus.Revoked, membership.Status);
        }

        using var inviteeClient = CreateAuthenticatedClient("revoked", "revoked@example.com", "Revoked");
        var acceptResponse = await inviteeClient.PostAsJsonAsync(
            "/api/invitations/accept",
            new { Token = invitation.AcceptanceToken });

        Assert.Equal(HttpStatusCode.BadRequest, acceptResponse.StatusCode);
    }

    [Fact]
    public async Task OutboxProcessor_dispatches_pending_invitation_and_marks_delivery_metadata()
    {
        var sender = ResetInvitationSender();
        var seed = await SeedSingleMembershipAsync("owner", "owner@example.com", CompanyMembershipRole.Owner);

        using var ownerClient = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var invitationResponse = await ownerClient.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "dispatch@example.com", MembershipRole = "employee" });

        var invitationPayload = await invitationResponse.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(invitationPayload);

        using (var scope = _factory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            await processor.DispatchPendingAsync(CancellationToken.None);
        }

        using var assertionScope = _factory.Services.CreateScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var invitation = await dbContext.CompanyInvitations.SingleAsync(x => x.CompanyId == seed.CompanyId && x.Email == "dispatch@example.com");
        var deliveryMessage = await dbContext.CompanyOutboxMessages.SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Topic == CompanyOutboxTopics.InvitationDeliveryRequested);

        Assert.Equal(CompanyInvitationDeliveryStatus.Delivered, invitation.DeliveryStatus);
        Assert.NotNull(invitation.DeliveredUtc);
        Assert.NotNull(invitation.LastDeliveryAttemptUtc);
        Assert.Null(invitation.DeliveryError);
        Assert.NotNull(deliveryMessage.ProcessedUtc);
        Assert.Single(sender.Sent);
        Assert.Equal(invitationPayload!.AcceptanceToken, sender.Sent.Single().AcceptanceToken);
    }

    [Fact]
    public async Task OutboxProcessor_retries_failed_delivery_and_preserves_error_until_success()
    {
        var sender = ResetInvitationSender();
        sender.FailNext();

        var seed = await SeedSingleMembershipAsync("owner", "owner@example.com", CompanyMembershipRole.Owner);

        using var ownerClient = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        await ownerClient.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "idempotent@example.com", MembershipRole = "employee" });

        using (var firstScope = _factory.Services.CreateScope())
        {
            var processor = firstScope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            await processor.DispatchPendingAsync(CancellationToken.None);
        }

        using (var firstAssertionScope = _factory.Services.CreateScope())
        {
            var dbContext = firstAssertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var invitation = await dbContext.CompanyInvitations.SingleAsync(x => x.CompanyId == seed.CompanyId && x.Email == "retry@example.com");
            var deliveryMessage = await dbContext.CompanyOutboxMessages.SingleAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.Topic == CompanyOutboxTopics.InvitationDeliveryRequested);

            Assert.Equal(CompanyInvitationDeliveryStatus.Failed, invitation.DeliveryStatus);
            Assert.NotNull(invitation.DeliveryError);
            Assert.Equal(1, deliveryMessage.AttemptCount);
            Assert.Null(deliveryMessage.ProcessedUtc);
            Assert.NotNull(deliveryMessage.LastError);
            Assert.Equal(1, sender.AttemptCount);
            Assert.Empty(sender.Sent);
        }

        using (var secondScope = _factory.Services.CreateScope())
        {
            var processor = secondScope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            await processor.DispatchPendingAsync(CancellationToken.None);
        }

        using var secondAssertionScope = _factory.Services.CreateScope();
        var secondDbContext = secondAssertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var deliveredInvitation = await secondDbContext.CompanyInvitations.SingleAsync(x => x.CompanyId == seed.CompanyId && x.Email == "retry@example.com");
        var processedMessage = await secondDbContext.CompanyOutboxMessages.SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Topic == CompanyOutboxTopics.InvitationDeliveryRequested);

        Assert.Equal(CompanyInvitationDeliveryStatus.Delivered, deliveredInvitation.DeliveryStatus);
        Assert.Null(deliveredInvitation.DeliveryError);
        Assert.NotNull(deliveredInvitation.DeliveredUtc);
        Assert.NotNull(processedMessage.ProcessedUtc);
        Assert.Equal(1, processedMessage.AttemptCount);
        Assert.Equal(2, sender.AttemptCount);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task OutboxProcessor_skips_delivery_for_revoked_invitation()
    {
        var sender = ResetInvitationSender();
        var seed = await SeedSingleMembershipAsync("owner", "owner@example.com", CompanyMembershipRole.Owner);

        using var ownerClient = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var invitationResponse = await ownerClient.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "skip@example.com", MembershipRole = "employee" });

        var invitationPayload = await invitationResponse.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(invitationPayload);

        var revokeResponse = await ownerClient.PostAsync(
            $"/api/companies/{seed.CompanyId}/invitations/{invitationPayload!.Invitation.InvitationId}/revoke",
            content: null);

        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            await processor.DispatchPendingAsync(CancellationToken.None);
        }

        using var assertionScope = _factory.Services.CreateScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var invitation = await dbContext.CompanyInvitations.SingleAsync(x => x.CompanyId == seed.CompanyId && x.Email == "skip@example.com");
        var deliveryMessage = await dbContext.CompanyOutboxMessages.SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Topic == CompanyOutboxTopics.InvitationDeliveryRequested);

        Assert.Equal(CompanyInvitationDeliveryStatus.Skipped, invitation.DeliveryStatus);
        Assert.NotNull(invitation.DeliveryError);
        Assert.NotNull(deliveryMessage.ProcessedUtc);
        Assert.Equal(0, sender.AttemptCount);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Role_upgrade_is_reflected_in_subsequent_authorization_checks()
    {
        var seed = await SeedRoleChangeScenarioAsync();

        using var employeeScope = CreateAuthorizationCheckContext(
            seed.CompanyId,
            seed.EmployeeUserId,
            "employee",
            "employee@example.com",
            "Employee");

        var beforeResult = await employeeScope.AuthorizationService.AuthorizeAsync(
            employeeScope.Principal,
            resource: null,
            CompanyPolicies.CompanyOwnerOrAdmin);

        Assert.False(beforeResult.Succeeded);

        using var ownerScope = CreateAuthorizationCheckContext(
            seed.CompanyId,
            seed.OwnerUserId,
            "owner",
            "owner@example.com",
            "Owner");

        var membershipAdministrationService = ownerScope.Scope.ServiceProvider.GetRequiredService<ICompanyMembershipAdministrationService>();
        var changedMembership = await membershipAdministrationService.ChangeMembershipRoleAsync(
            seed.CompanyId,
            seed.EmployeeMembershipId,
            new ChangeCompanyMembershipRoleRequest(CompanyMembershipRole.Admin),
            CancellationToken.None);

        Assert.Equal(CompanyMembershipRole.Admin, changedMembership.MembershipRole);

        var afterResult = await employeeScope.AuthorizationService.AuthorizeAsync(
            employeeScope.Principal,
            resource: null,
            CompanyPolicies.CompanyOwnerOrAdmin);

        Assert.True(afterResult.Succeeded);
    }

    [Fact]
    public async Task Role_downgrade_is_reflected_in_subsequent_authorization_checks()
    {
        var seed = await SeedRoleChangeScenarioAsync(CompanyMembershipRole.Admin);

        using var employeeScope = CreateAuthorizationCheckContext(
            seed.CompanyId,
            seed.EmployeeUserId,
            "employee",
            "employee@example.com",
            "Employee");

        var beforeResult = await employeeScope.AuthorizationService.AuthorizeAsync(
            employeeScope.Principal,
            resource: null,
            CompanyPolicies.CompanyOwnerOrAdmin);

        Assert.True(beforeResult.Succeeded);

        using var ownerScope = CreateAuthorizationCheckContext(
            seed.CompanyId,
            seed.OwnerUserId,
            "owner",
            "owner@example.com",
            "Owner");

        var membershipAdministrationService = ownerScope.Scope.ServiceProvider.GetRequiredService<ICompanyMembershipAdministrationService>();
        var changedMembership = await membershipAdministrationService.ChangeMembershipRoleAsync(
            seed.CompanyId,
            seed.EmployeeMembershipId,
            new ChangeCompanyMembershipRoleRequest(CompanyMembershipRole.Employee),
            CancellationToken.None);

        Assert.Equal(CompanyMembershipRole.Employee, changedMembership.MembershipRole);

        var afterResult = await employeeScope.AuthorizationService.AuthorizeAsync(
            employeeScope.Principal,
            resource: null,
            CompanyPolicies.CompanyOwnerOrAdmin);

        Assert.False(afterResult.Succeeded);
    }

    [Theory]
    [MemberData(nameof(SupportedRoleValues))]
    public async Task ChangeMembershipRole_accepts_each_supported_role(string roleValue, CompanyMembershipRole expectedRole)
    {
        var seed = await SeedRoleChangeScenarioAsync();

        using var ownerClient = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var changeResponse = await ownerClient.PatchAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/memberships/{seed.EmployeeMembershipId}/role",
            new { Role = roleValue });

        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        var payload = await changeResponse.Content.ReadFromJsonAsync<CompanyMemberDirectoryEntryResponse>();
        Assert.NotNull(payload);
        Assert.Equal(roleValue, payload!.MembershipRole);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var membership = await dbContext.CompanyMemberships
            .AsNoTracking()
            .SingleAsync(x => x.Id == seed.EmployeeMembershipId);

        Assert.Equal(expectedRole, membership.Role);

        var membershipsResponse = await ownerClient.GetFromJsonAsync<List<CompanyMemberDirectoryEntryResponse>>(
            $"/api/companies/{seed.CompanyId}/memberships");
        Assert.Contains(membershipsResponse!, x => x.MembershipId == seed.EmployeeMembershipId && x.MembershipRole == roleValue);
    }

    [Fact]
    public async Task ChangeMembershipRole_does_not_mutate_membership_access_configuration_json()
    {
        var companyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var employeeMembershipId = Guid.NewGuid();
        const string membershipAccessConfigurationJson = """{"humanApprovalRouting":{"defaultApproverRole":"finance_approver"}}""";

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(ownerUserId, "owner@example.com", "Owner", "dev-header", "owner"),
                new User(employeeUserId, "employee@example.com", "Employee", "dev-header", "employee"));

            dbContext.Companies.Add(new Company(companyId, "Company A"));
            dbContext.CompanyMemberships.AddRange(
            new { Email = "pending@example.com", MembershipRole = "manager" });

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondPayload = await secondResponse.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(secondPayload);
        Assert.True(secondPayload!.IsReinvite);
        Assert.Equal(firstPayload!.Invitation.InvitationId, secondPayload.Invitation.InvitationId);
        Assert.Equal("manager", secondPayload.Invitation.MembershipRole);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var membership = await dbContext.CompanyMemberships.SingleAsync(x => x.Id == employeeMembershipId);
        Assert.Equal(CompanyMembershipRole.Admin, membership.Role);
        Assert.Equal(membershipAccessConfigurationJson, membership.MembershipAccessConfigurationJson);
    }

    }

    [Fact]
    public async Task InviteUser_reinvites_existing_pending_invitation_instead_of_creating_duplicate()
    {
        var seed = await SeedSingleMembershipAsync("owner", "owner@example.com", CompanyMembershipRole.Owner);

        using var client = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var firstResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "resend@example.com", MembershipRole = "employee" });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstPayload = await firstResponse.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(firstPayload);

        var secondResponse = await client.PostAsync(
            $"/api/companies/{seed.CompanyId}/invitations/{firstPayload!.Invitation.InvitationId}/resend",
            content: null);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var secondPayload = await secondResponse.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(secondPayload);
        Assert.True(secondPayload!.IsReinvite);
        Assert.Equal(firstPayload.Invitation.InvitationId, secondPayload.Invitation.InvitationId);
        Assert.NotEqual(firstPayload.AcceptanceToken, secondPayload.AcceptanceToken);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var invitation = await dbContext.CompanyInvitations.SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Email == "resend@example.com");

        Assert.Equal(CompanyInvitationStatus.Pending, invitation.Status);
        Assert.Equal(2, await dbContext.CompanyOutboxMessages.CountAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Topic == CompanyOutboxTopics.InvitationDeliveryRequested));
        Assert.True(await dbContext.CompanyOutboxMessages.AnyAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Topic == CompanyOutboxTopics.InvitationResent));
    }

    [Fact]
    public async Task ReinviteUser_rejects_revoked_invitation()
    {
        var seed = await SeedSingleMembershipAsync("owner", "owner@example.com", CompanyMembershipRole.Owner);

        using var ownerClient = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var invitationResponse = await ownerClient.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "revoked-resend@example.com", MembershipRole = "employee" });

        var invitation = await invitationResponse.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(invitation);

        var revokeResponse = await ownerClient.PostAsync(
            $"/api/companies/{seed.CompanyId}/invitations/{invitation!.Invitation.InvitationId}/revoke",
            content: null);

        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        var reinviteResponse = await ownerClient.PostAsync(
            $"/api/companies/{seed.CompanyId}/invitations/{invitation.Invitation.InvitationId}/resend",
            content: null);

        Assert.Equal(HttpStatusCode.BadRequest, reinviteResponse.StatusCode);
    }

    [Fact]
    public async Task RevokeInvitation_is_idempotent_for_already_revoked_invitation()
    {
        var seed = await SeedSingleMembershipAsync("owner", "owner@example.com", CompanyMembershipRole.Owner);

        using var ownerClient = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var invitationResponse = await ownerClient.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "revoke-twice@example.com", MembershipRole = "employee" });

        var invitation = await invitationResponse.Content.ReadFromJsonAsync<InvitationDeliveryResponse>();
        Assert.NotNull(invitation);

        var firstRevokeResponse = await ownerClient.PostAsync(
            $"/api/companies/{seed.CompanyId}/invitations/{invitation!.Invitation.InvitationId}/revoke",
            content: null);
        var secondRevokeResponse = await ownerClient.PostAsync(
            $"/api/companies/{seed.CompanyId}/invitations/{invitation.Invitation.InvitationId}/revoke",
            content: null);

        Assert.Equal(HttpStatusCode.OK, firstRevokeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondRevokeResponse.StatusCode);

        var revokedInvitation = await secondRevokeResponse.Content.ReadFromJsonAsync<InvitationResponse>();
        Assert.NotNull(revokedInvitation);
        Assert.Equal("revoked", revokedInvitation!.Status);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Equal(1, await dbContext.CompanyOutboxMessages.CountAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Topic == CompanyOutboxTopics.InvitationRevoked));
    }

    [Fact]
    public async Task InviteUser_rejects_existing_active_membership_for_same_company()
    {
        var seed = await SeedMembershipAdministrationScenarioAsync();

        using var client = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/invitations",
            new { Email = "member@example.com", MembershipRole = "employee" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private TestWebApplicationFactory.TestCompanyInvitationSender ResetInvitationSender()
    {
        var sender = _factory.InvitationSender;
        sender.Reset();
        return sender;
    }

    [Fact]
    public async Task Owner_cannot_invite_users_into_a_different_company()
    {
        var seed = await SeedCrossCompanyAdministrationScenarioAsync();

        using var client = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.OtherCompanyId}/invitations",
            new { Email = "cross-company@example.com", MembershipRole = "employee" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName, string? provider = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        if (!string.IsNullOrWhiteSpace(provider))
        {
            client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.ProviderHeader, provider);
        }

        return client;
    }

    private AuthorizationCheckContext CreateAuthorizationCheckContext(
        Guid companyId,
        Guid userId,
        string subject,
        string email,
        string displayName)
    {
        var scope = _factory.Services.CreateScope();
        var principal = CreateAuthenticatedPrincipal(userId, subject, email, displayName);
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = principal
        };

        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(companyId);

        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        return new AuthorizationCheckContext(scope, principal, authorizationService);
    }

    private static ClaimsPrincipal CreateAuthenticatedPrincipal(
        Guid userId,
        string subject,
        string email,
        string displayName)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(CurrentUserClaimTypes.UserId, userId.ToString()),
            new Claim(CurrentUserClaimTypes.AuthProvider, "dev-header"),
            new Claim(CurrentUserClaimTypes.AuthSubject, subject),
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, displayName)
        ], DevHeaderAuthenticationDefaults.Scheme);

        return new ClaimsPrincipal(identity);
    }

    private async Task<SingleMembershipSeed> SeedSingleMembershipAsync(string subject, string email, CompanyMembershipRole role)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, subject, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Company A"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                role,
                CompanyMembershipStatus.Active));

            return Task.CompletedTask;
        });

        return new SingleMembershipSeed(companyId, userId);
    }

    private async Task<RoleChangeSeed> SeedRoleChangeScenarioAsync(CompanyMembershipRole employeeRole = CompanyMembershipRole.Employee)
    {
        var companyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var employeeMembershipId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(ownerUserId, "owner@example.com", "Owner", "dev-header", "owner"),
                new User(employeeUserId, "employee@example.com", "Employee", "dev-header", "employee"));

            dbContext.Companies.Add(new Company(companyId, "Company A"));

            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(employeeMembershipId, companyId, employeeUserId, employeeRole, CompanyMembershipStatus.Active));

            return Task.CompletedTask;
        });

        return new RoleChangeSeed(companyId, ownerUserId, employeeUserId, employeeMembershipId);
    }

    private async Task<MembershipAdministrationSeed> SeedMembershipAdministrationScenarioAsync()
    {
        var companyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(ownerUserId, "owner@example.com", "Owner", "dev-header", "owner"),
                new User(memberUserId, "member@example.com", "Member", "dev-header", "member"));

            dbContext.Companies.Add(new Company(companyId, "Company A"));

            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, memberUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));

            return Task.CompletedTask;
        });

        return new MembershipAdministrationSeed(companyId);
    }

    private async Task<CrossCompanySeed> SeedCrossCompanyAdministrationScenarioAsync()
    {
        var ownerUserId = Guid.NewGuid();
        var ownedCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(ownerUserId, "owner@example.com", "Owner", "dev-header", "owner"));
            dbContext.Companies.AddRange(
                new Company(ownedCompanyId, "Owned Company"),
                new Company(otherCompanyId, "Other Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                ownedCompanyId,
                ownerUserId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));

            return Task.CompletedTask;
        });

        return new CrossCompanySeed(ownedCompanyId, otherCompanyId);
    }

    private sealed record SingleMembershipSeed(Guid CompanyId, Guid UserId);
    private sealed record RoleChangeSeed(Guid CompanyId, Guid OwnerUserId, Guid EmployeeUserId, Guid EmployeeMembershipId);
    private sealed record MembershipAdministrationSeed(Guid CompanyId);
    private sealed record CrossCompanySeed(Guid OwnedCompanyId, Guid OtherCompanyId);

    private sealed class InvitationDeliveryResponse
    {
        public InvitationResponse Invitation { get; set; } = new();
        public string AcceptanceToken { get; set; } = string.Empty;
        public bool IsReinvite { get; set; }
    }

    private sealed class InvitationResponse
    {
        public Guid InvitationId { get; set; }
        public Guid CompanyId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string MembershipRole { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid InvitedByUserId { get; set; }
        public Guid? AcceptedByUserId { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? LastSentUtc { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class CompanyMemberDirectoryEntryResponse
    {
        public Guid MembershipId { get; set; }
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string MembershipRole { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    private sealed class ValidationProblemResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AcceptInvitationResponse
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public Guid MembershipId { get; set; }
        public string MembershipRole { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    private sealed class AuthorizationCheckContext : IDisposable
    {
        public AuthorizationCheckContext(
            IServiceScope scope,
            ClaimsPrincipal principal,
            IAuthorizationService authorizationService)
        {
            Scope = scope;
            Principal = principal;
            AuthorizationService = authorizationService;
        }

        public IServiceScope Scope { get; }
        public ClaimsPrincipal Principal { get; }
        public IAuthorizationService AuthorizationService { get; }
        public void Dispose() => Scope.Dispose();
    }
}
