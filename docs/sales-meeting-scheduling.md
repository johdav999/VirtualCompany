# Sales Meeting Scheduling

Sales meeting scheduling is provider-neutral in the Sales application. A qualified or converted lead can be used to prepare an invitation against a connected Google Workspace or Microsoft 365 mailbox.

## Approval And Delivery

1. The user selects the meeting host, attendee, duration, time zone, and availability search range, then chooses a suggested slot and prepares the title, agenda, location, and online-meeting preference.
2. The application stores a `SalesMeetingInvitation` and creates a human approval request. Nothing is sent at this stage.
3. Approval queues a durable `sales.meeting_invitation.delivery_requested` outbox message with a stable idempotency key.
4. The outbox dispatcher verifies the approval again immediately before calling the provider.
5. Google Calendar sends the invitation and can create a Google Meet link. Microsoft Graph sends the invitation and can create a Teams link.
6. Provider identifiers and join links are stored on the invitation and shown on the lead detail page.
7. A separate durable outbox message replies in the original sales email thread with the confirmed date, time, time zone, and Meet or Teams link. Confirmation retries never recreate the calendar event.

Rejected invitations remain visible. Retryable failures are retried by the outbox. An uncertain provider response is marked `reconciliation_required` and is not retried blindly.

## Reschedule And Cancellation

A scheduled invitation exposes reschedule and cancellation actions on the lead detail page. Each action creates a durable `SalesMeetingChangeRequest` and a separate human approval request. Approval queues `sales.meeting_change.delivery_requested`; rejection leaves the provider calendar unchanged.

Rescheduling uses Google Calendar PATCH or Microsoft Graph PATCH against the stored external event ID. Cancellation uses DELETE against that same ID. The canonical invitation is updated only after the provider confirms success, so retries cannot silently create duplicate meetings. Ambiguous responses stop in `reconciliation_required` for operator review.

## OAuth Permissions

Google consent now requests calendar.events and calendar.events.freebusy. Microsoft 365 consent requests delegated Calendars.ReadWrite. Existing mailbox connections must be reconnected once so each user can explicitly grant these newly enabled permissions.

Mailbox Infrastructure owns OAuth tokens and refresh. Sales receives only a short-lived access-token lease and does not store or expose provider credentials.

## Database

The base schema is in `20260807144153_AddSalesMeetingInvitations`; lifecycle requests are added by `20260808064436_AddSalesMeetingChangeRequests`; durable email confirmation state is added by `20260808095815_AddSalesMeetingConfirmationDelivery`. The confirmation migration backfills a unique idempotency key for every existing invitation before adding its unique index. The same ordered migrations are used by local SQL Server and the Docker SQL Server container.

```powershell
$ef = Join-Path $env:USERPROFILE '.dotnet\tools\dotnet-ef.exe'
& $ef database update --project src\VirtualCompany.Persistence.Migrations\VirtualCompany.Persistence.Migrations.csproj --startup-project src\VirtualCompany.Api\VirtualCompany.Api.csproj --context VirtualCompanyDbContext
```

## Recovery

- `failed`: inspect the visible provider error, restore the connection if needed, and let retryable outbox work resume.
- `reconciliation_required`: check the organizer calendar for the stored meeting details before attempting another invitation.
- `calendar_authorization_required`: reconnect the provider and approve the required calendar permissions.
