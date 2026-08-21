# Accounting administration reference prompts

These prompts were written before implementation and generated with the built-in OpenAI image model. The screenshots are design references only and are not shipped as UI assets.

## Accounting setup

Use case: `ui-mockup`

Create a shippable Virtual Company SaaS screen for a micro-company's native accounting setup. Use the existing calm Finance workspace: light `#F7F9FC` background, white 16px cards, soft borders and shadows, Inter typography, blue `#2563EB` actions, restrained status colors, fixed left app sidebar, Finance secondary navigation, and Laura the Finance Manager visible in a compact right-side guidance card. Use a five-step progress rail (Basics, Policy, Accounts, Periods, Review), central grouped setup cards for internal ledger, base currency, fiscal-year start, country-neutral or validated policy pack, chart template, tax behavior, control accounts, and voucher series, plus a clear amber notice that country-neutral mode supports basic bookkeeping but not country-specific tax or statutory compliance. Include validation, helper text, a compact preview, and Back/Continue actions. Do not show provider/OAuth language, storage keys, tenant identifiers, enum values, mock transactions, or futuristic styling.

Generated reference: `accounting-setup-reference.png`.

## Chart of accounts

Use case: `ui-mockup`

Create a shippable Virtual Company Finance page titled “Chart of accounts”. Keep the established app shell and Finance navigation. Use a 62/38 list-detail layout: a white filter card with search, class, and status filters; a dense accessible accounts table with Code, Account, Class, Status, Tax default, and Reporting columns; protected control-account badges and clear posted-history notices; and a selected-account detail card with plain-English role, active dates, posting status, reporting placement, and permitted Rename/Deactivate actions. Include Add account, empty/loading/error affordances, and a compact Laura risk/compliance note. Small viewports collapse into a usable list followed by detail. Do not show a delete action for protected or history-linked accounts, internal role keys, storage tokens, tenant identifiers, provider concepts, or mock transactions.

Generated reference: `chart-of-accounts-reference.png`.

## Fiscal periods

Use case: `ui-mockup`

Create a shippable Virtual Company Finance page titled “Fiscal periods”. Reuse the Accounting subnavigation and established visual system. Use a header action “Create fiscal year”, a summary row for current fiscal year, open periods, closed periods, and reporting locks, followed by a 60/40 list-detail layout with monthly periods grouped by fiscal year and friendly Open, Closed, and Reporting locked badges. The selected-period panel shows dates, posting availability, close/lock history, validation state, and a plain-English note that closing, locking, and reopening are handled from period close. Include a compact creation form that validates overlaps and gaps, plus loading, empty, conflict, and unauthorized affordances and Laura's next-step guidance. Do not show raw enums, tenant identifiers, provider concepts, mock transactions, or close/lock/reopen mutation buttons on this screen.

Generated reference: `fiscal-periods-reference.png`.
