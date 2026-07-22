# Localization Implementation Prompts

These prompts implement the strategy in `localisation.md`. Execute them in order. The source UI language is `en-GB`; the first complete translation is `sv-SE`. Every prompt must be implemented as production code, not as analysis or scaffolding.

## Prompt 1: Localization Foundation, User Preferences, and Culture Selection

### 1. Title and outcome

Implement the localization foundation and a persistent per-user language preference. A signed-in user can select an interface language independently of `Company.Language` and `CompanySettings.Locale`, and the selected culture is applied to a new Interactive Server circuit after a safe reload.

### 2. Current context

- Read and follow `localisation.md`, `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, `ui-instructions.md`, and `/docs/design.md` before editing.
- `src/VirtualCompany.Web/Program.cs` currently registers Interactive Server Razor Components but has no `AddLocalization`, `RequestLocalizationOptions`, or `UseRequestLocalization` setup.
- `src/VirtualCompany.Web/App.razor` hardcodes `<html lang="en">`.
- `VirtualCompany.Domain.Entities.User` in `TenantEntities.cs` stores identity fields but no UI or formatting preference.
- `ICurrentUserCompanyService`, `CompanyQueryService`, `AuthController`, `OnboardingApiClient`, and `CurrentUserContextViewModel` provide the current user context.
- There is no general user-profile settings page. Existing settings pages are capability-specific.
- `Company.Language` is the company/agent communication default and `CompanySettings.Locale` is company configuration; neither may become the authoritative UI language.

### 3. Dependencies

None.

### 4. Implementation requirements

- Add a supported-culture registry owned by the Web presentation layer with exactly `en-GB` and `sv-SE` initially, `en-GB` as the default, display names suitable for the language selector, and strict BCP 47 validation.
- Register .NET localization with `ResourcesPath = "Localization"`. Configure supported cultures and UI cultures, and place request-localization middleware before Razor components are mapped.
- Implement culture precedence as: persisted user UI preference, localization cookie, browser `Accept-Language`, then `en-GB`. Because the Web and API are separate hosts and Blazor uses Interactive Server circuits, implement a bounded bootstrap/synchronization flow that reads the authenticated user's preference from the existing API context, writes a validated localization cookie through a Web endpoint, and reloads only when required. Prevent redirect loops and validate return URLs as local.
- Persist user preferences independently of company data. Introduce a focused user-preference model supporting at least `UiCulture` and optional `FormattingCulture`; use one authoritative row per user and preserve room for timezone/accessibility preferences without using an unbounded JSON bag for the core culture values.
- Add the EF configuration, DbSet, migration, and model snapshot update. Define uniqueness, lengths, defaults, ownership, and update timestamps explicitly.
- Add authenticated application contracts and API endpoints to read and update only the current user's preferences. Controllers must remain transport-only. Validate cultures against an application-owned supported-culture policy, do not accept arbitrary locale strings, and return stable validation codes.
- Extend the current-user Web view model/API client only where needed to bootstrap culture without duplicating identity calls.
- Add a user/profile settings surface with an accessible language selector. Saving updates the API preference, applies the cookie, and reloads into the selected culture. Show explicit loading, saving, success, validation, unauthorized, and safe failure states.
- Set the document `lang` attribute from the active UI culture.
- Add audit evidence for preference changes without logging sensitive identity tokens.
- Add concise documentation describing the culture resolution lifecycle and how a new supported culture is registered.

### 5. Constraints and preservation rules

- UI culture is user-owned and global to the user; it is not tenant-owned and must not be overwritten when switching companies.
- Do not change `Company.Language` or `CompanySettings.Locale` semantics.
- Preserve authentication forwarding, development authentication, existing `api/auth/me` behavior, and company selection.
- Never trust a culture cookie or route value without allow-list validation. Prevent open redirects and CSRF on mutation endpoints.
- Store BCP 47 tags, not translated language names. Keep API values, enums, timestamps, logs, idempotency keys, and database statuses invariant.
- Follow the mandatory screenshot-first workflow for the new settings UI. Save the reference under `/docs/design/references/` and do not ship the screenshot as an application asset.
- The schema migration must work with both local SQL Server and Docker SQL Server. Preserve both restore/run paths and migration history.

### 6. Acceptance criteria

- Given a first-time anonymous or authenticated browser with Swedish preferred, when no saved preference or localization cookie exists, then the UI culture resolves to `sv-SE`.
- Given a saved `en-GB` preference and a Swedish browser/cookie, when the authenticated application initializes, then it converges to `en-GB` with at most one controlled reload.
- Given a user changes the selector to Swedish, when save succeeds, then the preference is persisted, the culture cookie is updated, a new circuit starts, and `<html lang="sv-SE">` is rendered.
- Given the same user switches companies or signs in on another browser, when the saved preference is resolved, then the same UI culture applies without changing either company's language.
- Given an unsupported culture, cross-user preference identifier, forged return URL, or unauthenticated update, when requested, then the server rejects it safely without persisting data or redirecting externally.

### 7. Verification

- Add domain and validator tests for supported/unsupported culture tags and preference updates.
- Add API integration tests for authentication, current-user ownership, read/update, CSRF/security behavior where applicable, and migration persistence.
- Add Web tests for provider precedence, cookie serialization, local return URL validation, bootstrap loop prevention, selector states, and dynamic `lang` output.
- Verify the migration from the current schema on SQL Server and verify equivalent local and Docker startup/restore flows.
- Build the affected Domain, Application, Infrastructure, API, and Web projects. Run browser checks at desktop and mobile widths for the profile settings surface.

### 8. Definition of done

The preference is stored and enforced through real authenticated endpoints, culture selection works across requests and circuits, the migration is production-ready, both supported cultures can be selected, and there are no placeholders, mock production data, silent failures, redirect loops, or deferred in-scope TODOs.

## Prompt 2: Shared UI, Navigation, Validation, and Status Localization

### 1. Title and outcome

Localize the application shell and shared presentation vocabulary so every feature can reuse stable semantic resources for navigation, common actions, validation, empty/error states, and status labels.

### 2. Current context

- Complete Prompt 1 first.
- Read and follow `localisation.md`, `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, `ui-instructions.md`, and `/docs/design.md`.
- `src/VirtualCompany.Web/Layout/NavMenu.razor`, `MainLayout.razor`, `App.razor`, authorization states, shared components, and many pages contain hardcoded English.
- API and database status values are currently presented in several places through direct strings, enum output, `ToTitleCase`, or feature-specific formatting helpers.
- No `.resx` files or strongly typed marker classes currently exist.

### 3. Dependencies

Prompt 1.

### 4. Implementation requirements

- Create the resource organization described in `localisation.md`: `Common`, `Navigation`, and `Validation` resources first, with complete neutral/source and `sv-SE` values. Use stable semantic keys, not English sentences as keys.
- Add feature-neutral marker classes and inject feature-specific `IStringLocalizer<T>` instances. Do not introduce a static service locator or one global catch-all resource containing every feature.
- Localize the app shell, navigation, page-not-found content, shared buttons, confirmation labels, loading states, authorization states, empty/error states, generic table/list vocabulary, and common date-relative labels that are actually user-visible.
- Implement one shared presentation mapping boundary for stable status/reason/action codes used across modules. The mapping must use exhaustive, testable code-to-resource-key definitions with a plain fallback for unknown forward-compatible values. Do not translate API wire values or persisted enums.
- Localize DataAnnotations/custom form validation presentation where it reaches users. Preserve invariant internal validator/error codes and map those codes to localized text in Web.
- Support resource placeholders and plural/count variants explicitly. Ensure placeholder indexes and types match between `en-GB` and `sv-SE`.
- Remove only the hardcoded strings covered by this shared scope. Record an inventory of remaining feature-owned strings to be handled by later prompts.

### 5. Constraints and preservation rules

- Preserve all routes, authorization policies, component behavior, status values, CSS classes, design tokens, and API contracts.
- Never localize logs, audit action codes, metric names, enum storage values, route segments, provider keys, or correlation/idempotency values.
- Avoid automatic title-casing of machine values. Unknown values must be rendered safely without exposing raw internal identifiers where an existing presentation mapper already supplies a label.
- Do not redesign the shell. UI changes must follow `/docs/design.md`; use screenshot-first only if the work becomes a significant redesign rather than a text migration.
- `en-GB` and `sv-SE` must have the same semantic key set and compatible placeholders.

### 6. Acceptance criteria

- Given `sv-SE`, when the shell and shared states render, then navigation, common commands, validation messages, empty states, and shared statuses are Swedish.
- Given `en-GB`, when the same surfaces render, then the existing business meaning and actions remain unchanged in polished English.
- Given an API status such as `pending_review`, when rendered, then the stored/wire value remains unchanged and only the visible label is localized.
- Given a missing resource or unknown future code, when rendered, then the UI shows a safe diagnosable fallback and does not crash or silently display an empty label.

### 7. Verification

- Add resource key-completeness and placeholder-parity tests for `Common`, `Navigation`, and `Validation`.
- Add component tests for both cultures covering navigation, shared actions, validation, known status mappings, unknown-code fallback, counts, and authorization/empty/error states.
- Run affected Web and API tests and a Web build.
- Browser-check desktop and mobile shell layouts in both cultures, including Swedish text expansion, focus order, and no clipping/overlap.

### 8. Definition of done

Shared visible vocabulary is resource-backed in both cultures, stable machine values remain invariant, all shared resource tests pass, and the remaining hardcoded-string inventory contains only clearly feature-owned work assigned to later prompts.

## Prompt 3: Agents UI Localization

### 1. Title and outcome

Localize the complete Agents experience, including roster, profile, brief, chat, operating settings, permissions, document states, and agent recommendations, without changing agent identities or orchestration behavior.

### 2. Current context

- Complete Prompts 1 and 2 first.
- Read and follow `localisation.md`, `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, `ui-instructions.md`, and `/docs/design.md`.
- Agent surfaces include `Pages/Agents.razor`, `AgentRoster.razor`, `AgentProfile.razor`, `AgentChat.razor`, contextual agent components, `AgentBriefDocumentStatusPresenter`, `AgentFinancePresentation`, `AgentApiClient`, and agent view models.
- Named agents and their configured role/persona are business data. UI chrome and status/explanation vocabulary are presentation text.
- Shared localization from Prompts 1-2 is available.

### 3. Dependencies

Prompts 1 and 2.

### 4. Implementation requirements

- Add complete `Agents` source and `sv-SE` resources organized by roster, profile, brief, permissions, autonomy, status, documents, chat, actions, and validation.
- Migrate hardcoded Agent page/component labels, headings, helper text, buttons, tabs, dialogs, loading/empty/error states, document-processing labels, and operator guidance to semantic keys.
- Localize stable agent status, health, autonomy, action, permission, source, and document ingestion/indexing codes at the Web presentation boundary. Preserve all domain/API values.
- Localize interpolated messages such as counts, agent names, filenames, progress, and timestamps using placeholders. Do not translate configured agent names, user-entered briefs, uploaded document titles, citations, or generated content.
- Ensure AI-generated or user-authored content is labelled and displayed as its original content language; UI localization must not silently translate or mutate it.
- Reuse shared resources from Prompt 2 instead of duplicating common actions.
- Update tests and any existing presentation helpers so they accept/use localizers without embedding culture-specific decisions in API clients.

### 5. Constraints and preservation rules

- Do not change agent orchestration, permissions, autonomy policy, tool availability, approvals, document ingestion, API routes, or company scoping.
- Agent names such as Laura, Alex, and Ben are proper names and remain unchanged unless they are configurable business data.
- No LLM call may be added merely to translate UI text.
- Do not expose enum names or internal workflow codes as localization fallbacks.
- Preserve the existing Agents design. Follow screenshot-first only for a significant layout redesign; localization alone is not permission to redesign.

### 6. Acceptance criteria

- Given either supported culture, when a user navigates every Agent route and state, then all owned UI chrome and operator messages render in that culture.
- Given a processing, failed, indexed, or unsupported document, when its state is shown, then the visible label/explanation is localized and the underlying status remains unchanged.
- Given agent-generated content in English while the UI is Swedish, when displayed, then the content remains intact and the surrounding controls are Swedish.
- Given long Swedish text, when rendered on desktop and mobile, then controls, tabs, badges, text areas, and dialogs do not clip or overlap.

### 7. Verification

- Add Agents resource completeness and placeholder tests.
- Add component/presenter tests for both cultures and every mapped agent/document status, including unknown fallbacks.
- Run existing agent management, roster/profile, communication-profile, brief document, document ingestion, and Web component tests.
- Build Web/API and browser-check roster, profile, brief, and chat at desktop/mobile widths in both cultures.

### 8. Definition of done

All Agent-owned presentation text is localized in `en-GB` and `sv-SE`, user and AI content is preserved, status mappings are exhaustive and tested, and agent behavior and contracts are unchanged.

## Prompt 4: Finance UI Localization

### 1. Title and outcome

Localize Finance pages and components so bills, invoices, payments, accounting, review, settings, simulations, and insights are understandable in English and Swedish while financial data remains invariant and auditable.

### 2. Current context

- Complete Prompts 1-3 first.
- Read and follow `localisation.md`, `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, `ui-instructions.md`, and `/docs/design.md`.
- Finance UI is split across `Pages/Finance`, `Components/Finance`, `FinanceApiClient.*`, finance presenters/view models, bill inbox/detail, approvals, anomalies, payments, transactions, reporting, integrations, and settings.
- Several Finance components currently format money and timestamps directly, sometimes with `InvariantCulture`. Prompt 7 will centralize formatting; this prompt must resource visible Finance vocabulary without inventing a competing formatter.
- Finance eligibility and policy explanations may arrive as stable codes plus user-visible details; authoritative decisions remain backend-owned.

### 3. Dependencies

Prompts 1, 2, and 3.

### 4. Implementation requirements

- Add complete `Finance` source and `sv-SE` resources grouped by overview, bills, invoices, payments, transactions, anomalies, reporting, integrations, settings, approvals, simulation, actions, and validation.
- Localize all Finance-owned headings, labels, commands, filters, tabs, table headings, empty/loading/error states, confirmation dialogs, helper text, warnings, and operator guidance.
- Map persisted/API finance statuses, eligibility reason codes, reconciliation states, payment states, invoice lifecycle states, integration connection states, and allowed actions to resources in Web. Preserve raw values in contracts and storage.
- Localize deterministic policy explanations only from stable reason codes and arguments where available. Do not reinterpret accounting eligibility in UI. Retain safe fallback behavior for legacy plain-English details until Prompt 8 migrates their API contracts.
- Use placeholders for supplier/customer names, document references, counts, dates, amounts, currencies, and agent names. Keep account codes, invoice numbers, VAT identifiers, provider references, and ISO currency codes unchanged.
- Reuse `Common`, `Validation`, and Agents resources where ownership is shared. Do not duplicate shared actions.
- Produce a precise list of direct formatting calls that Prompt 7 must replace, but do not leave visible labels hardcoded.

### 5. Constraints and preservation rules

- Do not alter calculations, posting rules, paid-expense eligibility, approvals, Fortnox/provider behavior, reconciliation, audit evidence, or finance workflows.
- Do not translate BAS account codes/names unless an authoritative localized chart-of-accounts source exists; never fabricate translated accounting data.
- Keep decimals, dates, currency codes, API payloads, exports, imports, and database values invariant. UI labels may be localized; financial facts may not be changed.
- Preserve all routes and existing visual hierarchy. Follow `/docs/design.md` and test Swedish text expansion.

### 6. Acceptance criteria

- Given `sv-SE`, when Finance overview, bill detail, invoice review, payment, anomaly, transaction, reporting, integration, and settings surfaces render, then all Finance-owned UI text is Swedish.
- Given a paid supplier bill with an ineligible expense-posting reason, when displayed, then the localized reason corresponds to the authoritative backend reason code and available actions are unchanged.
- Given finance identifiers and amounts, when culture changes, then identifiers and stored values remain identical; only labels and presentation formatting may differ.
- Given an unknown provider/status code, when displayed, then a safe localized fallback appears without exposing secrets or crashing.

### 7. Verification

- Add Finance resource key/placeholder tests and presenter/component tests for both cultures.
- Cover all known finance statuses, policy reason codes, integration states, and unknown fallback behavior.
- Run existing Finance page, presentation, API client tenant-context, policy, posting, approval, and integration tests.
- Browser-check representative overview/list/detail/settings pages in both cultures at desktop/mobile widths, including tables, badges, dialogs, and long warnings.

### 8. Definition of done

Finance-owned visible text is resource-backed and complete in both cultures, accounting behavior and data are unchanged, mappings are tested, and the formatting migration inventory for Prompt 7 is accurate.

## Prompt 5: Sales UI Localization

### 1. Title and outcome

Localize the Sales dashboard, leads, contacts, pipeline, deals, campaigns, and prospecting workflows while preserving sales records, source data, automation, and provider contracts.

### 2. Current context

- Complete Prompts 1-4 first.
- Read and follow `localisation.md`, `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, `ui-instructions.md`, and `/docs/design.md`.
- Sales surfaces are under `Pages/Sales`, `Components/Sales`, `SalesApiClient`, Sales presentation models, and shared agent panels.
- Several Sales Razor files contain dense inline English markup and direct `CultureInfo.CurrentCulture` formatting.
- Lead, deal, campaign, prospecting, suppression, consent, fit, email, and workflow statuses are stable business/API data and must not be translated in storage.

### 3. Dependencies

Prompts 1 through 4.

### 4. Implementation requirements

- Add complete `Sales` source and `sv-SE` resources grouped by dashboard, leads, contacts, pipeline, deals, campaigns, prospecting, sources, consent/suppression, actions, and validation.
- Migrate Sales-owned headings, metrics, tabs, filters, forms, buttons, table labels, dialogs, empty/loading/error states, and operator guidance to semantic keys.
- Map stable Sales status, score-band, fit-outcome, run-step, campaign, email, consent, suppression, and allowed-action codes to localized labels. Keep API and persisted values unchanged.
- Localize deterministic explanation templates with typed placeholders for names, counts, dates, values, and currencies. Preserve customer/company names, imported source values, campaign copy, email bodies, and user-authored fields.
- Reuse Common and Agents resources. Keep API clients transport-only; localization belongs in components/presenters.
- Identify direct date/number/money formatting calls for replacement by Prompt 7 without introducing duplicate formatter helpers.

### 5. Constraints and preservation rules

- Do not alter pipeline rules, lead scoring, ICP logic, source policies, suppression/consent enforcement, campaign approval, outbound delivery, idempotency, or tenant isolation.
- Do not translate customer content, company names, imported CSV/XLSX values, identifiers, source keys, or outbound copy automatically.
- Keep machine statuses and export formats invariant. A localized screen must still submit the same contract values.
- Preserve existing routes and design; verify text expansion instead of redesigning unrelated Sales workflows.

### 6. Acceptance criteria

- Given either culture, when each Sales route and major state renders, then all Sales-owned chrome and deterministic guidance use the selected culture.
- Given a translated option label, when a user filters or submits a form, then the API receives the unchanged stable value.
- Given customer-authored or imported content, when culture changes, then the content remains byte-for-byte semantically unchanged.
- Given Swedish text on dense prospecting and pipeline views, when rendered responsively, then controls and table content remain readable without overlap.

### 7. Verification

- Add Sales resource completeness/placeholder tests and code-to-label mapping tests for both cultures.
- Add component tests for filters/forms proving localized labels round-trip invariant values.
- Run existing Sales page, decision, lead-generation, campaign, consent/suppression, outbound, tenant-isolation, and API client tests.
- Browser-check dashboard, leads, pipeline, deal detail, campaigns, and prospecting views in both cultures and desktop/mobile widths.

### 8. Definition of done

Sales presentation text is complete in both cultures, business/source/customer data is untouched, all translated form options preserve stable values, and no new feature-owned hardcoded English remains.

## Prompt 6: Support UI Localization

### 1. Title and outcome

Localize the Support inbox, case detail, knowledge gaps, memory, SLA settings, drafting, approvals, and delivery states while preserving customer messages, grounding citations, safety decisions, and outbound content.

### 2. Current context

- Complete Prompts 1-5 first.
- Read and follow `localisation.md`, `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, `ui-instructions.md`, and `/docs/design.md`.
- Support surfaces include `Pages/Support`, Support view models/API clients, mailbox/inbox views, case messages/events, knowledge gaps, memory, refund actions, SLA settings, drafts, and delivery states.
- Support messages and drafts may be in a recipient language different from the operator UI culture.
- Grounding sources and citations are authoritative company content and must not be silently translated.

### 3. Dependencies

Prompts 1 through 5.

### 4. Implementation requirements

- Add complete `Support` source and `sv-SE` resources grouped by inbox, cases, messages, triage, drafting, grounding, citations, knowledge gaps, memory, SLA, refunds, delivery, approvals, actions, and validation.
- Migrate all Support-owned UI chrome, metrics, labels, tabs, filters, forms, commands, dialogs, empty/loading/error states, warnings, and operator guidance.
- Map case, priority, sentiment, SLA, draft, grounding, safety, approval, refund, mailbox, and delivery codes to localized labels in Web while preserving wire/storage values.
- Clearly distinguish localized UI text from customer messages, agent drafts, source excerpts, citations, and outbound replies. Show content language metadata when available; never translate or rewrite content merely because UI culture changed.
- Localize deterministic safety/review explanations from stable codes and arguments. Preserve review requirements and do not weaken policy based on localization.
- Reuse shared resources and record formatting calls for Prompt 7.

### 5. Constraints and preservation rules

- Preserve support grounding boundaries, source access, citations, knowledge-gap creation, safety policies, approval requirements, mailbox routing, idempotency, and delivery dispatch.
- Do not translate customer content, source documents, memory observations, generated drafts, outbound messages, or audit evidence automatically.
- Do not let UI culture determine reply language; Prompt 9 implements recipient-language resolution separately.
- Keep all routes, tenant authorization, API values, and statuses unchanged.

### 6. Acceptance criteria

- Given `sv-SE`, when Support pages render, then operator UI and deterministic status/explanation text are Swedish.
- Given an English customer message and draft in a Swedish UI, when viewed, then both contents remain unchanged and are visually distinguishable from Swedish controls.
- Given insufficient grounding or a safety block, when shown, then the localized explanation preserves the same mandatory review/action restrictions.
- Given localized SLA forms and filters, when submitted, then invariant API values and numeric thresholds are preserved.

### 7. Verification

- Add Support resource completeness/placeholder tests and status/reason mapping tests in both cultures.
- Add component tests proving customer/source/draft content is not transformed and localized form values round-trip correctly.
- Run support grounding, safety, mailbox, SLA, refund, drafting, delivery, tenant-isolation, and page tests.
- Browser-check inbox, case detail, knowledge gaps, memory, and SLA settings at desktop/mobile widths in both cultures.

### 8. Definition of done

Support-owned UI text is fully localized, customer and grounded content remains intact, policy restrictions are preserved, mappings are tested, and no UI-language logic controls reply language.

## Prompt 7: Centralized Culture-Aware Date, Number, and Money Formatting

### 1. Title and outcome

Introduce shared presentation formatters so dates, times, numbers, percentages, and money are displayed consistently using the user's formatting culture and the appropriate company timezone without changing stored or serialized values.

### 2. Current context

- Complete Prompts 1-6 first and use their formatting inventories.
- Read and follow `localisation.md`, `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, `ui-instructions.md`, and `/docs/design.md`.
- Web components currently mix `InvariantCulture`, `CurrentCulture`, `ToLocalTime()`, custom string interpolation, ISO currency prefixes/suffixes, and feature-specific helpers.
- Company onboarding stores a timezone and ISO currency. Prompt 1 stores user UI/formatting culture independently.

### 3. Dependencies

Prompts 1 through 6.

### 4. Implementation requirements

- Define focused Web presentation contracts such as `ILocalDateTimeFormatter`, `INumberFormatter`, and `IMoneyFormatter`, with one implementation each and clear typed inputs.
- Resolve formatting culture from the user's optional formatting preference, otherwise UI culture. Resolve timezone from an explicit user override if Prompt 1 implemented one, otherwise the active company's configured timezone, otherwise a documented UTC fallback. Do not use the server machine's local timezone.
- Format instants only after explicit UTC normalization and timezone conversion. Provide date-only, time-only, date-time, relative/empty, number, integer, decimal, percentage, and money variants needed by current UI.
- Format money using decimal amounts and ISO 4217 codes. Preserve the ISO code where symbols are ambiguous; define deterministic behavior for unknown codes. Never infer currency from UI culture when the business value already supplies a currency.
- Replace direct UI formatting identified in Prompts 4-6 and shared/Agents surfaces. Remove duplicated helpers only when their behavior is fully covered.
- Keep transport query serialization, imports/exports, provider payloads, calculations, logs, hashes, and idempotency values on invariant culture.
- Document formatting semantics and examples for `en-GB` and `sv-SE`.

### 5. Constraints and preservation rules

- Presentation formatters belong in Web and must not enter Domain calculations or API serialization.
- Never call `ToLocalTime()` for business display without the resolved timezone.
- Do not parse localized presentation strings back into business values except through explicit localized form binding/validation with typed models.
- Preserve decimal precision required by finance and do not change rounding rules owned by the backend.
- Keep exports and machine-readable values invariant unless a separate explicitly localized export feature exists.

### 6. Acceptance criteria

- Given the same UTC instant and Stockholm company timezone, when displayed under `en-GB` and `sv-SE`, then both represent the same instant with culture-appropriate text.
- Given `22000 SEK`, when displayed in each culture, then separators/symbol placement are appropriate and the underlying decimal/currency are unchanged.
- Given an unknown currency or timezone, when displayed, then a deterministic safe fallback is used and the UI does not crash.
- Given an API request, CSV export, provider payload, or idempotency key, when UI culture changes, then its serialized value is unchanged.

### 7. Verification

- Add unit tests for every formatter across `en-GB`, `sv-SE`, DST transitions, UTC fallback, negative/zero/large amounts, percentages, unknown currencies, and invalid timezones.
- Add regression tests proving invariant API query serialization, exports, and idempotency inputs.
- Add component tests for representative Finance, Sales, Support, Agents, dashboard, and activity views.
- Browser-check formatting and layout in both cultures. Build all affected projects.

### 8. Definition of done

User-visible formatting uses the centralized services, server-local time and ad hoc presentation formatting are removed from migrated UI, machine values remain invariant, and edge cases are covered by deterministic tests.

## Prompt 8: Stable API Error Codes and Localized Web Problem Presentation

### 1. Title and outcome

Replace reliance on English API error prose with stable error codes and structured arguments, then localize user-facing problem messages in Web while preserving diagnosable invariant logs and backward compatibility.

### 2. Current context

- Complete Prompts 1-7 first.
- Read and follow `localisation.md`, `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, `ui-instructions.md`, and `/docs/design.md`.
- `VirtualCompany.Api` has many controller-specific `ProblemDetails`, `ValidationProblemDetails`, `BadRequest`, `Conflict`, and exception mappings with hardcoded English titles/details.
- Some areas already expose stable codes in `ProblemDetails.Extensions`; others expose only English text.
- Web API clients have feature-specific safe error mapping and must preserve authentication/company/correlation behavior.

### 3. Dependencies

Prompts 1 through 7.

### 4. Implementation requirements

- Inventory user-visible API failures by shared, Agents, Finance, Sales, Support, document, mailbox, approval, and identity ownership. Migrate them incrementally without one giant controller refactor.
- Define a shared wire contract convention using RFC 7807 plus stable namespaced `code` and structured `arguments`. Keep HTTP status, title/detail fallback, trace/correlation information, and field validation associations.
- Add focused factories/mappers at the API transport boundary so controllers do not duplicate contract construction. Do not move business decisions into the factory.
- Update application exceptions/validation results to expose stable reason codes and typed-safe arguments where needed. Logs retain invariant diagnostic detail; public arguments must be allow-listed and free of secrets/provider payloads.
- Update Web transport/error presentation to map codes to `Validation`, `Common`, or feature resources and format arguments through the localizer/formatters. Preserve a safe fallback for old servers and unknown future codes.
- Migrate high-frequency user-facing paths first: authentication/context, documents, Agents, Finance bills/payments/approvals/integrations, Sales forms/campaigns, and Support drafting/delivery/refunds.
- Document the error-code naming/versioning policy and compatibility expectations.

### 5. Constraints and preservation rules

- Preserve route shapes, HTTP statuses, authorization semantics, field names, and successful response contracts.
- Never localize API logs or make API behavior depend on `Accept-Language`. API responses remain stable for all clients.
- Never expose exception stack traces, SQL/provider details, tokens, recipient content, or sensitive identifiers in arguments.
- Existing clients must retain usable English title/detail fallback during migration. Unknown codes must not crash Web.
- Keep tenant access failures indistinguishable where revealing resource existence would leak cross-company data.

### 6. Acceptance criteria

- Given a known validation/business failure, when API culture headers vary, then status, code, arguments, and invariant fallback remain stable.
- Given Web culture `sv-SE`, when the known problem is displayed, then the message is Swedish and uses the structured arguments safely.
- Given an unknown code or legacy problem without a code, when displayed, then Web shows a safe fallback with correlation guidance and does not expose internals.
- Given unauthorized cross-company access, when requested, then localization changes neither status nor information disclosure.

### 7. Verification

- Add API contract tests for representative errors in every module, code stability across culture headers, field validation, correlation metadata, and secret redaction.
- Add Web client/presenter tests for `en-GB`, `sv-SE`, placeholders, unknown codes, malformed arguments, and legacy fallback.
- Run authorization, tenant-isolation, global exception handling, module endpoint, and transport tests.
- Build API/Web and manually inspect browser error states in both cultures.

### 8. Definition of done

The targeted user-visible failures expose stable codes/arguments, Web localizes them without parsing English prose, compatibility and security behavior are tested, and no migrated path relies on exception text as a machine contract.

## Prompt 9: Recipient-Language Resolution for AI and Outbound Communication

### 1. Title and outcome

Implement deterministic recipient-language resolution for Finance, Sales, and Support communications so agents and templates use the recipient/conversation language rather than the operator's UI culture or unrelated company profile fields.

### 2. Current context

- Complete Prompts 1-8 first.
- Read and follow `localisation.md`, `agents-ai.md`, `production-implementation.md`, `architecture-inst.md` if present, `/docs/architecture-rules.md`, and relevant prompt/orchestration documentation.
- `StructuredPromptBuilder` currently appends `context.Company.Language`; `SingleAgentOrchestrationResolver` and `SingleAgentOrchestrationService` pass company language into shared orchestration.
- Support cases/messages, Sales contacts/campaigns/conversations, and Finance counterparties/documents may contain language evidence. The UI preference from Prompt 1 is not communication language.
- Shared AI orchestration is mandatory. Feature modules must not call LLM providers directly.

### 3. Dependencies

Prompts 1 through 8.

### 4. Implementation requirements

- Define an application-level `CommunicationLanguageResolution` contract containing a validated BCP 47 tag, resolution source, confidence/evidence, and whether human review is required.
- Implement the precedence from `localisation.md`: recipient's explicit known language; case/conversation/campaign language; company default communication language; `en-GB` fallback. Define deterministic tie/invalid/missing behavior.
- Add appropriate language fields only where authoritative recipient/conversation state lacks them. Use normalized BCP 47 tags, explicit update commands, tenant-scoped queries, EF migration/configuration when schema changes are needed, and audit meaningful changes.
- Pass the resolved language as structured shared orchestration context. Update prompt construction to state language and regional tone explicitly and to distinguish it from UI culture. Remove inference from unrelated profile fields or generated company descriptions.
- Update deterministic outbound email/notification template selection to use localized templates keyed by communication type and resolved language, with complete source/fallback behavior.
- For AI-generated communication, require the structured output to report the language used. Validate it where practical; low-confidence/conflicting language evidence must require review rather than silently sending.
- Preserve approval, safety, outbox/background delivery, idempotency, retry, and reconciliation boundaries for all external communication.
- Surface the resolved language, source, and review state to operators using the localized UI resources, without exposing hidden prompts.

### 5. Constraints and preservation rules

- UI culture must never control recipient communication language.
- Do not train a model or add automatic translation as a substitute for deterministic resolution.
- Do not infer language from names, geography alone, company compliance region, or a generated company brief.
- Recipient/customer language data is company-scoped. Enforce authorization and tenant isolation.
- No outbound side effect may bypass existing approval, safety, durable outbox/dispatcher, idempotency, or reconciliation paths.
- Database changes must support local and Docker SQL Server restoration/migration equally.

### 6. Acceptance criteria

- Given a recipient explicitly prefers `sv-SE` and the operator UI is `en-GB`, when an agent drafts a response, then orchestration and deterministic templates use `sv-SE`.
- Given no recipient preference but a Support conversation is Swedish, when language is resolved, then the conversation language wins over the company's English default.
- Given invalid/conflicting evidence, when resolving, then the system uses a documented fallback or requires review and records the reason; it never silently guesses from unrelated fields.
- Given a retry of approved outbound delivery, when processed, then language/template/version and idempotency identity remain stable and no duplicate is sent.

### 7. Verification

- Add resolver unit tests covering every precedence level, invalid tags, regional variants, ties, missing data, and review requirements.
- Add orchestration tests proving structured language reaches prompts/tools without UI culture leakage.
- Add Finance, Sales, and Support integration tests for tenant isolation, template fallback, approval, outbox idempotency, retries, and operator-visible evidence.
- Verify migrations on local and Docker SQL Server paths. Build all affected projects.

### 8. Definition of done

Recipient language is deterministic, structured, audited, and used by shared AI/template paths; UI culture remains separate; side-effect safeguards remain intact; and all three agent domains have tested end-to-end language behavior without mock production data.

## Prompt 10: Localization Quality Gates and New-Language Playbook

### 1. Title and outcome

Add automated localization quality gates and documentation so a new language can be added by registering a culture and translating matching resources without modifying page logic, API contracts, workflows, or database statuses.

### 2. Current context

- Complete Prompts 1-9 first.
- Read and follow `localisation.md`, `production-implementation.md`, `/docs/architecture-rules.md`, `ui-instructions.md`, and `/docs/design.md`.
- The solution now has `en-GB` and `sv-SE` resources, user culture selection, localized modules, centralized formatters, stable API error codes, and communication-language resolution.
- The repository has extensive `VirtualCompany.Api.Tests` coverage and feature-specific test projects but no localization-specific quality gate.

### 3. Dependencies

Prompts 1 through 9.

### 4. Implementation requirements

- Add automated tests that enumerate all resource families and enforce identical semantic keys between source and every supported culture, non-empty translations, compatible placeholders, valid format strings, and duplicate-key detection.
- Test fallback behavior for missing satellite resources, unsupported cultures, neutral/regional variants, and unknown stable status/error codes.
- Add a focused hardcoded user-visible text detector for Razor/C# presentation files. Use an explicit reviewed allow-list for proper names, identifiers, accessibility literals that cannot be resources, test fixtures, and content examples. Avoid a noisy regex that blocks normal code strings indiscriminately.
- Add tests ensuring supported-culture registry entries have all required resource families and selector metadata.
- Add browser smoke coverage for representative shell, Agents, Finance, Sales, and Support routes in `en-GB` and `sv-SE`, including desktop/mobile widths, text expansion, forms, validation, empty/error states, and no clipping/overlap.
- Add accessibility checks for language selector labels, focus order, live validation, and document `lang` changes.
- Document a concrete `de-DE` addition playbook: registry update, resource creation, translation workflow, key/placeholder tests, formatter checks, communication template coverage, browser QA, and release review. Do not add incomplete German resources as scaffolding.
- Wire deterministic localization checks into the repository's existing CI/build workflow where one exists; otherwise provide a normal test command and repository documentation without inventing an unrelated CI platform.

### 5. Constraints and preservation rules

- Quality gates must be deterministic, fast enough for normal CI, and independent of external translation or LLM services.
- Do not fail on invariant logs, API codes, route strings, provider keys, persisted values, user content, or test data that is intentionally language-neutral.
- Do not add a language unless all required resources/templates are complete.
- Existing unit/integration/browser suites remain authoritative; localization checks supplement rather than replace them.

### 6. Acceptance criteria

- Given a missing Swedish key, empty value, duplicate key, or placeholder mismatch, when localization tests run, then they fail with the resource family, culture, and key identified.
- Given a new supported culture without complete resources/templates, when tests run, then they fail before release.
- Given intentional invariant strings covered by the reviewed allow-list, when the hardcoded-text check runs, then it passes without hiding unrelated violations.
- Given all checks pass, when a developer follows the `de-DE` playbook, then no page, API status, workflow, or database enum requires modification solely to add the language.

### 7. Verification

- Deliberately mutate a resource fixture in tests to prove missing-key and placeholder gates fail, then restore it.
- Run the full localization test suite, affected Web/API test suites, and release builds.
- Run browser smoke checks for both cultures on desktop and mobile and retain normal test artifacts/screenshots according to repository practice.
- Run `git diff --check` and verify documentation commands on Windows PowerShell.

### 8. Definition of done

Localization regressions fail deterministically in normal test/CI workflows, both current cultures pass resource and browser checks, the new-language playbook is actionable, and no in-scope quality safeguard depends on manual memory, external AI, or unfinished TODOs.
