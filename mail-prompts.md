# Provider-Neutral Email Implementation Prompts

Execute these prompts in order. Every prompt must read and follow `production-implementation.md`, `/docs/architecture-rules.md`, and the repository `AGENTS.md`. For architecture-sensitive work, also read and follow `architecture-inst.md` if it exists; that file was not present when this pack was authored. For UI work, read and follow `ui-instructions.md` and `/docs/design.md`, including the mandatory screenshot-first workflow.

The target architecture is one standards-based IMAP transport, one standards-based SMTP transport, pluggable authentication strategies, and data-driven provider profiles. Provider profiles may contain endpoints, OAuth metadata, scopes, and capability defaults, but must not introduce a separate transport implementation for each email vendor. Preserve the existing Gmail and Microsoft 365 API integrations and all Finance, Sales, and Support behavior while the generic path is introduced.

## Prompt 1: Separate Mailbox Transport, Authentication, and Provider Configuration

### 1. Title and outcome

Introduce provider-neutral mailbox boundaries so Virtual Company can use the same IMAP and SMTP implementation with OAuth or application-password authentication across hosted-domain email services. The outcome is a stable application contract that no longer requires OAuth, reading, drafting, and sending to be implemented by one vendor-specific class.

### 2. Current context

- Read and follow `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, and `AGENTS.md`.
- `src/VirtualCompany.Application/Mailbox/MailboxConnectionContracts.cs` currently defines `IMailboxProviderClient`. It combines OAuth authorization, token exchange and refresh, account profile lookup, message listing, message retrieval, attachments, threads, drafts, and replies.
- `src/VirtualCompany.Infrastructure/Mailbox/MailboxProviderClients.cs` contains Gmail and Microsoft 365 implementations and `MailboxProviderRegistry`.
- Existing consumers include Finance mailbox ingestion, Sales email ingestion and sending, Support mailbox ingestion and reply delivery, manual inbox scanning, connected mailbox scanning, and startup credential refresh.
- `MailboxConnection` stores provider, purpose, encrypted credentials, token expiry, scopes, folders, status, and mailbox identity.
- Existing Gmail and Microsoft 365 flows are production behavior and must continue to work throughout the refactor.

### 3. Dependencies

None.

### 4. Implementation requirements

- Define focused Application-layer contracts for mailbox transport, mailbox authentication, provider/profile resolution, and provider capabilities. Keep protocol and provider payload types out of Domain.
- Model transport capabilities explicitly, including at minimum folder listing, incremental message reads, full message retrieval, attachments, thread correlation, draft creation, reply sending, and health testing. A missing capability must be represented and handled, not discovered through `NotSupportedException` during normal execution.
- Separate OAuth authorization lifecycle from authenticated mailbox operations. Authentication strategies must support OAuth 2.0 and application passwords without exposing either mechanism to Finance, Sales, or Support services.
- Introduce a normalized authenticated-session or credential lease contract that transports can consume without knowing how the credential was obtained.
- Define a provider configuration profile contract containing stable profile key, display metadata, endpoint defaults, supported authentication methods, required OAuth metadata, and capability defaults. Profiles are configuration, not business entities.
- Add compatibility adapters around the existing Gmail and Microsoft 365 clients so current routes, callbacks, scans, drafts, sends, token refresh, and tests remain operational.
- Move registrations into the owning Mailbox/Communication module registration in accordance with `/docs/architecture-rules.md`; do not grow the root composition class with capability-specific registrations.
- Document the new boundary and extension flow near the mailbox contracts.

### 5. Constraints and preservation rules

- Domain and Application must not depend on MailKit, SMTP, IMAP, provider SDKs, Infrastructure, API, or Web.
- Preserve all current API routes, stored provider values, OAuth state protection, authorization policies, company scoping, encrypted token handling, and existing Gmail/Microsoft 365 behavior.
- Do not add a generic service locator, raw string dispatch spread across consumers, or a second mailbox orchestration stack.
- Provider capability decisions must be deterministic and server-authoritative.
- Never expose credentials, tokens, provider secrets, or raw provider errors through contracts intended for UI display.

### 6. Acceptance criteria

- Given an existing Gmail or Microsoft 365 connection, when current mailbox workflows execute after the refactor, then behavior and persisted values are unchanged.
- Given a transport supports reading but not drafts, when capability resolution runs, then the result reports that limitation before an unsupported action is attempted.
- Given OAuth and application-password strategies, when a mailbox workflow requests an authenticated session, then the workflow is independent of the credential type.
- Given an unknown provider profile or unsupported capability, when resolved, then the system returns a stable, safe, actionable failure.

### 7. Verification

- Add contract and registry tests for capability resolution, authentication strategy selection, unknown profiles, and duplicate registrations.
- Run existing Gmail client, mailbox flow, callback, persistence, Finance mailbox, Sales ingestion, and Support delivery tests.
- Add architecture tests that prevent Application from referencing transport libraries and prevent feature modules from resolving vendor clients directly.
- Build Application, Infrastructure, API, and Web.

### 8. Definition of done

The new boundaries are used by production mailbox orchestration, Gmail and Microsoft 365 remain functional through compatibility adapters, capabilities are explicit, and there are no placeholder implementations, duplicate paths, silent fallbacks, or deferred in-scope TODOs.

## Prompt 2: Persist Generic Endpoint, Authentication, and Synchronization State

### 1. Title and outcome

Add a secure tenant-scoped persistence model for generic hosted-domain mailboxes, their IMAP/SMTP endpoints, authentication binding, capabilities, and per-folder synchronization cursors. The outcome supports Zoho and future hosted providers without storing provider-specific schemas in core entities.

### 2. Current context

- Complete Prompt 1 first.
- Read and follow `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, and `AGENTS.md`.
- `MailboxConnection` and its EF configuration currently persist provider, purpose, mailbox identity, encrypted OAuth credentials, scopes, configured folders, status, and timestamps.
- Provider storage values and SQL check constraints are owned by `MailboxProviderValues` and mailbox EF configurations.
- Existing migrations are under `src/VirtualCompany.Infrastructure/Persistence/Migrations`; EF Core migrations are the only schema authority.
- SQL Server is the production database. Local SQL Server and Docker SQL Server restore/startup paths must remain equivalent.

### 3. Dependencies

Prompt 1.

### 4. Implementation requirements

- Extend the mailbox aggregate or introduce narrowly owned tenant entities for a connection profile, endpoint configuration, authentication binding, and per-folder sync cursor. Prefer relational columns for queryable core state.
- Persist IMAP host, IMAP port, IMAP TLS mode, SMTP host, SMTP port, SMTP TLS mode, profile key, authentication type, authenticated identity, capability snapshot, and safe health status without storing plaintext secrets.
- Store credential references or encrypted credential envelopes separately from non-secret connection configuration. Preserve the existing company- and purpose-bound field-encryption semantics.
- Model authentication types with stable values such as OAuth 2.0 and application password. Do not use an unbounded JSON bag for authentication type, endpoint security, status, or cursor identity.
- Persist one folder cursor per connection and folder with folder identity, `UIDVALIDITY`, last processed UID, optional highest modification sequence, last successful sync, and reset/reconciliation state.
- Define uniqueness and indexes needed for connection assignment, folder cursor lookup, and idempotent message ingestion. Preserve the distinction between one physical mailbox connection and its Finance, Sales, or Support assignment.
- Add safe mutation methods and invariants to Domain entities. Validate lengths, ports, supported TLS modes, and required fields.
- Add an EF migration and model snapshot update. Preserve existing provider rows and upgrade them without requiring reauthentication merely because the schema changed.
- Update persistence projections and API contracts only through normalized Application models.
- Document the schema, secret boundaries, and cursor semantics.

### 5. Constraints and preservation rules

- Every new tenant-owned row must be company-scoped, covered by query filters where established, and protected by explicit authorization/application boundaries.
- Never persist plaintext mailbox passwords, application passwords, access tokens, refresh tokens, or OAuth client secrets.
- Never change or delete existing migration IDs. Do not use startup DDL, `EnsureCreated`, or database recreation as a migration strategy.
- Existing Gmail and Microsoft 365 connections must remain readable and operational.
- Keep the migration compatible with local SQL Server and Docker SQL Server backup restoration.

### 6. Acceptance criteria

- Given an existing Gmail or Microsoft 365 connection, when the migration is applied, then its provider, purpose, credentials, folders, and status remain intact.
- Given a generic mailbox connection, when persisted, then endpoints and capabilities are queryable while secrets are available only as encrypted values or secret references.
- Given two companies with similar mailbox addresses, when either company queries or updates its connection and cursors, then it cannot access the other company's records.
- Given an IMAP folder whose `UIDVALIDITY` changes, when cursor state is updated, then the connection records a bounded resynchronization requirement rather than continuing from an invalid UID.

### 7. Verification

- Add domain invariant, EF mapping, tenant-isolation, uniqueness, encryption-at-rest, and cursor transition tests.
- Generate and review the EF migration and snapshot; verify no unrelated model churn.
- Apply the migration to a restored local SQL Server database and to the Docker SQL Server flow described by repository scripts.
- Run migration validation and existing mailbox persistence tests.
- Build Domain, Application, Infrastructure, and API.

### 8. Definition of done

The production schema stores generic mailbox configuration and synchronization state securely, upgrades existing connections without data loss, passes tenant and migration verification, and includes no plaintext secrets, ad hoc DDL, mock records, or deferred schema work.

## Prompt 3: Implement the Secure Generic IMAP and SMTP Transport

### 1. Title and outcome

Implement one production-grade IMAP/SMTP transport used by all standards-compatible providers. The transport must read folders and messages, retrieve attachments, create drafts where standards allow, send mail, and test connectivity without containing vendor-specific branches.

### 2. Current context

- Complete Prompts 1 and 2 first.
- Read and follow `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, and `AGENTS.md`.
- Existing Gmail and Microsoft 365 clients use HTTP APIs. They remain valid adapters.
- Generic mailbox contracts and persisted endpoint/capability models now exist from Prompts 1 and 2.
- Finance, Sales, and Support consume normalized mailbox contracts and must not depend on MailKit types.

### 3. Dependencies

Prompts 1 and 2. Add the approved MailKit/MimeKit packages through normal package management; do not hand-roll IMAP, SMTP, MIME, SASL, or message parsing.

### 4. Implementation requirements

- Implement a single Infrastructure `MailKitMailboxTransport` for all generic profiles.
- Support IMAP folder discovery, capability negotiation, incremental UID-based listing, normalized envelope/header projection, full MIME body retrieval, attachments, and thread correlation using standards headers such as `Message-ID`, `In-Reply-To`, and `References`.
- Support SMTP submission with normalized recipients, HTML/plain alternatives, reply headers, attachments, and a stable caller-supplied `Message-ID`.
- Support draft creation only when the server exposes a suitable Drafts folder and IMAP APPEND behavior. Report capability unavailability explicitly when it does not.
- Enforce TLS 1.2 or newer. Prefer implicit TLS for IMAP 993 and SMTP 465; permit SMTP 587 only with mandatory STARTTLS. Never permit authentication before TLS, cleartext fallback, certificate bypass, or hostname mismatch.
- Implement bounded connection, command, download, upload, and idle timeouts. Enforce message and attachment count/size limits before materializing untrusted content.
- Normalize protocol exceptions into stable transient, permanent-authentication, permanent-configuration, throttling, ambiguous-send, and provider-unavailable categories with safe user explanations.
- Detect capabilities during connection testing and persist the normalized snapshot through the application boundary.
- Ensure connection objects are not shared across tenants or concurrent operations. Dispose sockets and streams deterministically.
- Add structured logs and metrics that exclude credentials, message bodies, attachment content, and sensitive headers.

### 5. Constraints and preservation rules

- Follow RFC 8314 for TLS email access/submission, RFC 9051 cursor semantics, RFC 4954 SMTP authentication, and RFC 7628 OAuth SASL where applicable.
- Do not add vendor-name conditionals to the transport. Differences belong in validated profiles or authentication strategies.
- Do not disable certificate validation in Development or tests. Use trusted test certificates or controlled test doubles.
- Treat email bodies and attachments as untrusted content and preserve the existing extraction, malware scanning, and grounding boundaries.
- SMTP submission must not be called directly from controllers or UI event handlers.

### 6. Acceptance criteria

- Given any standards-compatible profile with valid credentials, when the connection is tested, then IMAP and SMTP TLS, authentication, folders, and capabilities are verified independently.
- Given a valid cursor, when new messages arrive, then only UIDs after the cursor are returned and normalized without vendor-specific behavior.
- Given an invalid certificate, plaintext-only endpoint, TLS downgrade, or authentication before TLS, when connecting, then the operation fails closed with a safe explanation.
- Given SMTP accepts a message, when send completes, then the stable `Message-ID` and provider response are returned without exposing credentials.

### 7. Verification

- Add protocol integration tests against disposable standards-compatible IMAP and SMTP test servers, including TLS certificates controlled by the test.
- Cover implicit TLS, mandatory STARTTLS, rejected cleartext, invalid certificate, authentication failure, timeout, folder discovery, UID pagination, `UIDVALIDITY` changes, MIME alternatives, attachments, Unicode headers, draft capability, successful send, transient SMTP errors, permanent rejection, and ambiguous disconnect.
- Add tests proving two provider profiles use the same transport implementation.
- Run existing Gmail/Microsoft 365 and mailbox workflow tests to verify no regression.
- Build Application and Infrastructure with warnings from new code treated as errors.

### 8. Definition of done

The generic transport performs real standards-based operations through MailKit/MimeKit, fails closed on insecure connections, returns normalized contracts, is covered by protocol-level tests, and contains no vendor-specific branches, fake production paths, secret logging, or unhandled protocol states.

## Prompt 4: Add Pluggable OAuth and Application-Password Authentication

### 1. Title and outcome

Implement secure authentication strategies for generic mailboxes so providers can use standards-based OAuth SASL only when the provider documents protocol support, and application-specific passwords as a controlled alternative. Add Zoho EU as the first data-driven application-password profile without creating a Zoho transport class.

### 2. Current context

- Complete Prompts 1-3 first.
- Read and follow `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, and `AGENTS.md`.
- Existing OAuth state protection, callback routing, encrypted token storage, provider configuration, startup refresh, and reconnect behavior support Gmail and Microsoft 365.
- The generic transport supports SASL authentication through the contracts from Prompt 1.
- Zoho documents OAuth for its REST APIs, while its third-party IMAP/SMTP setup documents application-specific passwords. Do not infer SASL OAuth support for IMAP/SMTP from REST API OAuth documentation.

### 3. Dependencies

Prompts 1, 2, and 3. OAuth client registration is required only for a trusted provider profile whose official documentation confirms IMAP/SMTP SASL OAuth support. Zoho EU requires an application-specific password when account policy requires it.

### 4. Implementation requirements

- Implement OAuth 2.0 authorization-code and refresh strategies that produce SASL `OAUTHBEARER` or `XOAUTH2` credentials for IMAP/SMTP according to server capabilities and validated profile configuration.
- Reuse the existing protected OAuth state and callback security model. State must bind company, user, mailbox purpose, profile, return URI, nonce, and expiration, and must be single-use.
- Implement application-password authentication as a separate strategy. Label it accurately; do not request or encourage storage of the user's normal account password.
- Extend credential persistence to store encrypted refresh tokens, access tokens, token expiry, and encrypted application passwords under company- and connection-specific purposes.
- Introduce a validated provider-profile registry loaded from trusted application configuration. Profiles must include stable key, region, endpoint defaults, supported authentication mechanisms, OAuth endpoints, client registration reference, scopes, and capability defaults.
- Add a `zoho-eu` profile using secure regional endpoints and application-password authentication. Use the generic MailKit transport; do not add `ZohoMailboxProviderClient`.
- Handle providers that do not support standard OAuth discovery through administrator-managed profile configuration. Do not allow tenants to supply arbitrary OAuth token endpoints without server-side trust policy.
- Refresh tokens before expiry and at startup through the existing lifecycle service. Missing keys, revoked grants, invalid refresh tokens, and removed OAuth application configuration must transition to an actionable reconnect state.
- Revoke provider tokens where supported when disconnecting, then remove or cryptographically erase stored credential material.
- Add configuration documentation for centralized production OAuth application registration and local secret injection.

### 5. Constraints and preservation rules

- OAuth client secrets belong in server configuration or a secret vault, not tenant database rows, source control, browser storage, agent profiles, or logs.
- Custom endpoint configuration must be restricted to trusted administrator profiles and protected against SSRF and credential exfiltration.
- Never silently fall back from OAuth to password authentication.
- Existing Gmail and Microsoft 365 callbacks, tokens, and reconnect behavior must remain compatible.
- Use least-privilege scopes for configured capabilities. Sending scope must not be requested for a read-only mailbox.

### 6. Acceptance criteria

- Given the Zoho EU profile and a valid application-specific password, when a Support mailbox connects, then the actual authenticated address is stored and the generic transport can authenticate without a Zoho-specific class.
- Given an expired access token and valid refresh token, when startup restoration runs, then access is refreshed without interactive sign-in.
- Given a revoked or undecryptable credential, when restoration or use occurs, then only that connection becomes reconnect-required and no secret appears in logs or responses.
- Given an application-password profile, when the user supplies an app password over an authorized protected request, then it is encrypted before persistence and never returned.
- Given an untrusted OAuth or mail endpoint, when configuration is submitted, then it is rejected before any outbound connection.

### 7. Verification

- Add OAuth state, callback, replay, tenant, return-URI, scope, refresh, revocation, missing-key, and secret-redaction tests.
- Add authentication strategy tests for `OAUTHBEARER`, `XOAUTH2`, application password, unavailable mechanism, and forbidden fallback.
- Add profile and application-password validation tests for Zoho EU and malicious custom endpoints.
- Run mailbox startup refresh and current Gmail/Microsoft 365 callback tests.
- Perform a credential-gated Zoho sandbox/manual validation without making CI depend on external credentials.

### 8. Definition of done

Generic OAuth is available only for explicitly trusted profiles with documented protocol support, application-password authentication is lifecycle-managed, Zoho EU works through profile data and the shared transport, existing providers remain intact, and there are no embedded secrets, insecure fallbacks, replay gaps, or provider-specific transport forks.

## Prompt 5: Implement Idempotent Generic Inbound Mail Synchronization

### 1. Title and outcome

Integrate generic IMAP mailboxes into Finance, Sales, and Support ingestion with durable cursoring, idempotency, bounded background execution, and safe handling of untrusted content. Repeated polling must never duplicate business records or agent work.

### 2. Current context

- Complete Prompts 1-4 first.
- Read and follow `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, and `AGENTS.md`.
- Existing mailbox scanning normalizes provider messages and feeds Finance bill detection, Sales email ingestion, and Support routing/ingestion.
- Existing snapshots and hashes provide parts of the deduplication model.
- Background jobs must create company execution scopes and preserve tenant isolation.
- Generic folder cursors from Prompt 2 and the transport from Prompt 3 are available.

### 3. Dependencies

Prompts 1-4.

### 4. Implementation requirements

- Add one generic inbound synchronization orchestrator that uses normalized mailbox contracts and dispatches messages to the existing purpose-owned Finance, Sales, or Support workflows.
- Select configured folders and persist `(connection, folder, UIDVALIDITY, UID)` cursor progress transactionally with normalized message snapshots.
- Detect `UIDVALIDITY` changes and enter a bounded reconciliation/rescan flow. Do not continue from invalid cursor state or blindly replay the entire mailbox.
- Fetch metadata and headers first. Download bodies and attachments only when required by routing or configured policy and within size/count limits.
- Derive stable idempotency identities from company, connection, folder, UID validity, UID, and immutable message identifiers. Preserve secondary deduplication by `Message-ID` and content hashes.
- Guarantee repeated polling and duplicate job delivery cannot create duplicate Support cases, Sales links/leads, Finance bills, tasks, agent executions, attachments, or audits.
- Use distributed or database-backed per-connection/folder claims with bounded leases. Recover abandoned claims safely.
- Classify transient network/throttling failures for retry with bounded backoff. Stop permanent authentication/configuration failures and surface reconnect/configuration actions.
- Persist scan attempts, counts, cursor movement, safe failure summaries, correlation IDs, and source references without persisting unnecessary sensitive content.
- Preserve Finance, Sales, and Support authorization, approval, grounding, and routing policies.

### 5. Constraints and preservation rules

- Do not put feature-specific business decisions in the generic synchronization layer.
- Never trust sender names, addresses, MIME types, filenames, HTML, or attachment text. Preserve malware scanning and content sanitization boundaries.
- Do not advance a cursor past messages whose required durable snapshot failed to persist.
- Do not hold a database transaction open across long network downloads.
- Keep each company and connection isolated in queries, locks, metrics, and jobs.

### 6. Acceptance criteria

- Given a Zoho or custom IMAP Support mailbox, when a new customer email arrives, then exactly one normalized message and one purpose-appropriate Support ingestion are created.
- Given the same sync job runs twice, when no new UID exists, then no duplicate business record, attachment, task, or agent execution is created.
- Given a crash after message fetch but before cursor advancement, when the job retries, then durable idempotency prevents duplication and the cursor eventually advances.
- Given `UIDVALIDITY` changes, when the next poll runs, then reconciliation is visible and bounded rather than silently losing or duplicating mail.
- Given one company's mailbox identifiers match another company's identifiers, when both synchronize, then their records remain isolated.

### 7. Verification

- Add integration tests for first sync, incremental sync, duplicate delivery, concurrent workers, abandoned claim, crash boundaries, UID gaps, `UIDVALIDITY` reset, moved/deleted messages, large attachments, malformed MIME, and tenant isolation.
- Verify Finance, Sales, and Support routing separately using the same generic transport contract.
- Run existing mailbox ingestion and business workflow tests.
- Test local SQL Server concurrency behavior; do not rely only on SQLite semantics for locks and indexes.
- Build and run affected background worker/API tests.

### 8. Definition of done

Generic inbound synchronization is durable, cursor-correct, idempotent, tenant-isolated, and integrated with all three functions using existing business policies, with no duplicate side effects, unbounded rescans, silent skips, or deferred recovery behavior.

## Prompt 6: Implement Reliable Generic Draft and SMTP Delivery

### 1. Title and outcome

Enable provider-neutral drafts and outbound email for Sales and Support through existing approval and outbox boundaries. SMTP submission must be retry-aware, idempotent where possible, and safe when delivery outcome is ambiguous.

### 2. Current context

- Complete Prompts 1-5 first.
- Read and follow `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, and `AGENTS.md`.
- Approved Support replies are queued as `support.reply.delivery_requested` and dispatched by `SupportReplyDeliveryDispatcher`; request handlers must not bypass that path.
- Sales sending has policy and approval behavior that must remain authoritative.
- Existing Gmail and Microsoft 365 clients can create drafts and send replies through provider APIs.
- The generic transport can append drafts where supported and submit mail through SMTP.

### 3. Dependencies

Prompts 1-5.

### 4. Implementation requirements

- Route generic mailbox draft and send operations through the same Application commands, policies, approvals, and durable outbox workflows as existing providers.
- Generate and persist a stable RFC-compliant `Message-ID` before submission. Include correct `In-Reply-To` and `References` headers for replies.
- Persist outbound intent, content hash/version, recipients, connection, purpose, approval reference, idempotency key, attempt state, retry state, and provider/SMTP response metadata before the external action.
- Classify SMTP responses into success, retryable transient, permanent recipient/content rejection, authentication/configuration failure, throttling, and ambiguous outcome.
- Never blindly resend after a timeout or disconnect that may have occurred after server acceptance. Enter an operator-visible reconciliation state and attempt bounded Sent-folder/header reconciliation where available.
- Use bounded exponential backoff and per-company/per-mailbox rate limits. Ensure duplicate outbox delivery cannot produce parallel sends for the same intent.
- Implement draft behavior through IMAP only when supported. If draft creation is unavailable, return a stable capability explanation and preserve the reviewable draft inside Virtual Company.
- Sanitize headers against injection, normalize addresses, constrain attachments, and prevent spoofed From addresses not authorized for the authenticated mailbox.
- Persist safe audit evidence for approval, execution, retries, reconciliation, and final outcome without storing or logging secrets.

### 5. Constraints and preservation rules

- No controller, Blazor component, agent tool, or synchronous request handler may send directly over SMTP.
- Existing Support and Sales approval/autonomy policies remain authoritative and must be rechecked immediately before sending.
- Do not claim exactly-once SMTP delivery; explicitly model ambiguity and reconciliation.
- Do not silently change From identity to make a failed send pass.
- Finance mailbox ingestion must remain read-focused unless a separately approved Finance outbound use case exists.

### 6. Acceptance criteria

- Given an approved Support reply using a generic mailbox, when the dispatcher runs, then one durable outbound intent is submitted through SMTP with stable reply headers and audited outcome.
- Given the same outbox message is delivered concurrently, when claims are acquired, then only one SMTP attempt proceeds.
- Given a definitive temporary SMTP failure, when retry policy applies, then a bounded retry is scheduled without losing the original idempotency identity.
- Given the connection drops after message submission may have succeeded, when outcome is unknown, then the item enters reconciliation and is not automatically resent.
- Given a From address not authorized by the connected mailbox, when sending is requested, then policy rejects it before SMTP submission.

### 7. Verification

- Add integration tests with a controllable SMTP server for success, duplicate dispatch, 4xx retry, 5xx rejection, authentication failure, throttling, disconnect before DATA, disconnect after DATA, and Sent-folder reconciliation.
- Add authorization and approval tests for Sales and Support.
- Add header-injection, oversized attachment, unauthorized From, cross-company connection, and secret-redaction tests.
- Run existing Support reply delivery and Sales outbound tests for Gmail/Microsoft 365 regression.
- Build Infrastructure and API and exercise worker recovery across process restart.

### 8. Definition of done

Generic outbound email uses production outbox and approval controls, handles definitive and ambiguous outcomes safely, preserves current provider behavior, and contains no direct-send bypass, blind retry, spoofed identity path, mock delivery, or unhandled intermediate state.

## Prompt 7: Build the Hosted-Domain Mailbox Connection Experience

### 1. Title and outcome

Extend Agent Management with a production-ready connection experience for Zoho and other hosted-domain mailboxes. Users can select Finance, Sales, or Support, choose a known secure profile or custom IMAP/SMTP, authenticate, test access, understand capabilities, and save safely.

### 2. Current context

- Complete Prompts 1-6 first.
- Read and follow `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, `ui-instructions.md`, `/docs/design.md`, and `AGENTS.md`.
- `src/VirtualCompany.Web/Pages/TeamMailboxConnection.razor` currently provides a purpose-specific Gmail/Microsoft 365 connection flow.
- `src/VirtualCompany.Web/Pages/Agents.razor` links mailbox actions into that route.
- English and Swedish Agent resource files exist and must maintain key and placeholder parity.
- The backend now exposes normalized profile, endpoint, authentication, connection-test, capability, status, reconnect, and disconnect contracts.

### 3. Dependencies

Prompts 1-6. Production OAuth profiles require externally supplied client registrations and secrets.

### 4. Implementation requirements

- Before editing UI, write an explicit image-generation prompt and generate a reference screenshot under `/docs/design/references/hosted-mailbox-connection-reference.png`. Implement against it; do not ship the screenshot.
- Extend the existing connection route rather than creating a competing onboarding flow.
- Let the user choose function, then choose a known profile such as Zoho Mail or `Other hosted email`.
- For known profiles, prefill trusted endpoints and explain the available authentication methods. Do not expose OAuth client secrets to ordinary company users.
- For custom profiles, collect email address, IMAP host/port/TLS mode, SMTP host/port/TLS mode, username, and either OAuth profile selection or application password. Default to secure ports and do not offer insecure/no-TLS choices.
- Add separate `Test incoming mail` and `Test sending` actions that return plain-language results and discovered capabilities. Testing must be authorized, rate-limited, audited, and must not persist credentials unless the user explicitly saves.
- Display whether reading, attachments, folders, drafts, replies, and sending are available. Explain missing capabilities in plain language with a direct corrective action.
- Clearly label application-password fallback and instruct users not to enter their normal account password. Use password controls with no value rehydration.
- Show OAuth redirect/consent state, reconnect requirement, current authenticated address, purpose assignment, last successful check, and safe failures.
- Add loading, validation, authorization, provider unavailable, certificate, endpoint, authentication, partial capability, reconnect, disconnect, and success states.
- Localize all visible text in complete English and Swedish resources with parity tests.
- Ensure desktop and mobile layouts do not overlap, clip long addresses, expose raw enum values, or nest cards unnecessarily.

### 5. Constraints and preservation rules

- UI selection grants no access by itself; backend authorization, endpoint trust, connection tests, and secret handling remain authoritative.
- Never return saved passwords, tokens, or client secrets to Web. A saved secret field must render empty with a separate replace action.
- Do not put provider configuration back on Company Onboarding or duplicate Agent access assignment behavior.
- Preserve existing Gmail and Microsoft 365 connection paths and localized behavior.
- Follow `/docs/design.md`; use existing tokens/components and plain business language.

### 6. Acceptance criteria

- Given `hello@prosa-app.com` hosted by Zoho EU, when the user selects Support and Zoho Mail, authenticates, tests, and saves, then the connected address and capabilities appear under the Support function.
- Given a custom provider with secure IMAP and SMTP plus an app password, when both tests pass, then the user can save without exposing the password afterward.
- Given an invalid certificate, private/internal endpoint, plaintext configuration, or failed authentication, when tested, then saving is blocked with a plain corrective explanation.
- Given Swedish UI culture, when every connection state renders, then all owned text is Swedish and long labels remain readable on mobile.

### 7. Verification

- Add component and API-client tests for profile selection, purpose preservation, custom validation, secret non-rehydration, test results, capabilities, OAuth return, reconnect, disconnect, authorization, and localization.
- Add English/Swedish resource key and placeholder parity tests.
- Build Web and API.
- Browser-test all important states at desktop and mobile widths. Verify no overlap, horizontal page overflow, raw machine values, or inaccessible controls.
- Capture implementation screenshots and compare them with the generated reference.

### 8. Definition of done

Users can complete real Zoho and custom secure mailbox connections from Agent Management, understand exactly what works or needs attention, and manage credentials without exposure; the UI is localized, responsive, production-ready, and contains no dead controls, mock results, or onboarding redirects.

## Prompt 8: Harden Operations, Lifecycle, and Extensibility

### 1. Title and outcome

Complete the provider-neutral email capability with secure endpoint governance, startup restoration, health monitoring, rotation and revocation, observability, recovery runbooks, and a tested profile-extension process. The outcome is safe operation across many tenants and hosted email providers without vendor-specific code proliferation.

### 2. Current context

- Complete Prompts 1-7 first.
- Read and follow `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, and `AGENTS.md`.
- `MailboxConnectionStartupRefreshBackgroundService` restores and refreshes persisted mailbox credentials and marks unreadable credentials for reconnection.
- Background execution, outbox, audit, correlation, health, and company execution-scope infrastructure already exist.
- Generic profiles can introduce customer-supplied network destinations, which creates SSRF, DNS rebinding, credential exfiltration, certificate, and resource-exhaustion risks.

### 3. Dependencies

Prompts 1-7.

### 4. Implementation requirements

- Add a central outbound mail-endpoint policy that validates host syntax, DNS resolution, allowed ports, TLS mode, and resolved addresses before every initial/test connection and after DNS changes. Block loopback, unspecified, link-local, multicast, private infrastructure ranges, and cloud metadata targets unless an explicit deployment-level allowlist permits a controlled private mail server.
- Defend against DNS rebinding by validating all resolved addresses and binding the validated destination consistently for the connection attempt where the networking library permits it. Revalidate periodically and when endpoint configuration changes.
- Add bounded global, company, connection, and destination concurrency/rate limits for tests, scans, token refreshes, and sends.
- Extend startup restoration to all authentication types. OAuth tokens refresh when needed; application-password connections perform bounded health checks without logging or rewriting secrets. One broken connection must not stop restoration of others.
- Implement explicit credential replace, rotate, revoke, disconnect, and cryptographic-erasure workflows with audit evidence.
- Add scheduled health checks and status transitions for healthy, attention required, reconnect required, invalid certificate, configuration blocked, provider unavailable, throttled, and disconnected states. Keep user-facing explanations safe and actionable.
- Add metrics for connection attempts, TLS/auth failures, sync lag, cursor resets, messages scanned, duplicates prevented, outbound attempts, retries, ambiguous outcomes, and reconnect requirements. Use low-cardinality labels and never include addresses or secrets as metric dimensions.
- Add safe structured logs, correlation IDs, traces, and operator diagnostics that identify company and connection IDs without message content or credentials.
- Add a validated profile schema and documented review process so a new hosted provider is normally added through configuration and tests, not a new transport class.
- Write operator and customer runbooks covering Zoho setup, custom IMAP/SMTP setup, OAuth registration, app passwords, DNS/TLS failures, token key loss, reconnect, rate limits, cursor reconciliation, ambiguous sends, credential rotation, local SQL Server, and Docker SQL Server.
- Add security and architecture documentation describing residual risks and why primary mailbox passwords, plaintext TLS, certificate bypass, and arbitrary internal endpoints are prohibited.

### 5. Constraints and preservation rules

- Do not weaken endpoint policy for local development. Use explicit test profiles and trusted local certificates.
- Do not automatically retry permanent authentication, certificate, policy, or recipient failures.
- Do not log or metric-label email addresses, usernames, OAuth codes, tokens, passwords, message subjects/bodies, attachment names/content, or provider payloads.
- Preserve tenant isolation, purpose assignment, approval boundaries, idempotency, and current provider integrations.
- Provider profiles may customize standards-compliant configuration but must not execute code or override security invariants.

### 6. Acceptance criteria

- Given a malicious custom endpoint resolving to loopback, private infrastructure, link-local, or metadata IP space, when tested or saved, then no connection is attempted and an actionable policy error is returned.
- Given one tenant has an invalid credential while others are healthy, when startup restoration and health checks run, then only the affected connection changes state.
- Given a provider changes DNS to a blocked address, when revalidation occurs, then subsequent connections are blocked without leaking credentials.
- Given a credential is replaced or a mailbox disconnected, when the operation completes, then old credential material is unusable and the audit records a safe outcome.
- Given a new standards-compatible provider profile, when configuration and conformance tests pass, then it operates through the existing generic transport with no provider-specific class.

### 7. Verification

- Add security tests for IPv4/IPv6 loopback, private/link-local/mapped addresses, DNS rebinding, disallowed ports, TLS downgrade, invalid certificates, endpoint changes, secret redaction, OAuth replay, and cross-tenant access.
- Add load/concurrency tests for connection limits, scan claims, rate limits, startup batches, and outbound workers.
- Add lifecycle tests for rotation, revocation, disconnect, startup restoration, partial failures, key loss, health transitions, and recovery.
- Run the complete mailbox, Finance ingestion, Sales email, Support ingestion/delivery, outbox, authorization, and migration suites.
- Verify local SQL Server and Docker SQL Server restore, migration, startup, and background execution.
- Run a final architecture dependency check and ensure no provider-specific generic transport forks exist.

### 8. Definition of done

The provider-neutral email subsystem is secure against untrusted endpoints, observable without sensitive leakage, recoverable across failures and restarts, extensible through reviewed profiles, verified on local and Docker SQL Server, and has complete operator/customer documentation with no unresolved production TODOs or silent failure modes.
