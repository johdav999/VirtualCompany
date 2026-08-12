# Calendar connections

Calendar authorization is independent from mailbox authorization.

## Connection model

- `ExternalAccountConnection` owns encrypted OAuth credentials for a Google or Microsoft 365 account.
- OAuth mailbox rows reference that external account and do not store new OAuth token ciphertext. Token columns retained on migrated mailbox rows are dormant rollback data and are not read or refreshed at runtime.
- `CalendarConnection` identifies the calendar used for availability, event creation, rescheduling, and cancellation.
- `MailboxConnection` identifies an inbox and its reply capabilities. One connection per company and purpose is the primary inbound scanner; other active connections remain available.
- `SalesMeetingInvitation.CalendarConnectionId` records the meeting host calendar.
- `SalesMeetingInvitation.ConfirmationMailboxConnectionId` records the mailbox used to confirm the meeting in the originating sales thread.

This permits combinations such as a Zoho sales inbox and a Gmail sales calendar.

## OAuth scopes

Mailbox and calendar flows use separate state protection and separate requested scopes.

- Google calendar: `calendar.events` and `calendar.events.freebusy`, plus identity scopes.
- Microsoft 365 calendar: `Calendars.ReadWrite`, plus identity and offline access scopes.
- Calendar authorization does not request Gmail or Microsoft Mail permissions.
- Mailbox authorization no longer requests calendar permissions.
- Google calendar-only authorization resolves account identity through OpenID Connect rather than the Gmail API.
- When the same external account is extended with another capability, incremental consent preserves its previously granted scopes. A new calendar-only account remains calendar-only.

## Thread confirmation

The confirmation dispatcher resolves the mailbox attached to the originating `SalesEmailLink`. It selects providers by capability, not by provider name.

- Gmail and Microsoft 365 declare their required reply scopes through `IMailboxProviderClient.ReplyRequiredScopes`.
- Standard hosted email, including Zoho IMAP/SMTP connections, uses `SendMessages` capability and the existing standard mailbox session.
- SMTP replies set a deterministic `Message-ID` and preserve the source `Message-ID` in `In-Reply-To` and `References`.
- A confirmation mailbox must support both `SendMessages` and `ThreadCorrelation`. Each invitation records whether the provider used native or header-based threading.
- Ambiguous SMTP submissions are reconciled in the sent folder before any retry.

## Database migration

`SeparateCalendarConnectionsFromMailboxes`:

- creates the external account and calendar connection tables;
- links existing Google and Microsoft mailbox rows to a shared external account;
- creates a primary calendar for existing calendar-capable accounts;
- remaps existing sales meeting invitations;
- backfills OAuth mailbox capabilities;
- selects one primary inbound mailbox per company and purpose without disconnecting the others.

The migration targets SQL Server and is compatible with the Docker SQL Server flow.

```powershell
& "$env:USERPROFILE\.dotnet\tools\dotnet-ef.exe" database update `
  --project src\VirtualCompany.Persistence.Migrations\VirtualCompany.Persistence.Migrations.csproj `
  --startup-project src\VirtualCompany.Api\VirtualCompany.Api.csproj `
  --context VirtualCompanyDbContext
```

## Mixed-provider test

1. Connect the Zoho or hosted mailbox as the Sales team mailbox.
2. Open `/settings/calendar-connections?companyId=<company-id>`.
3. Connect Google Calendar and grant calendar permissions.
4. Open a qualified sales lead and schedule a meeting using the Gmail calendar.
5. Approve the meeting invitation.
6. Verify that the calendar event is created in Google and the confirmation reply is sent through the originating Zoho thread.
