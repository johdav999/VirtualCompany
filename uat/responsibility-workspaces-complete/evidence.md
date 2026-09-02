# Responsibility-driven workspaces completion evidence

### FLOW-RESP-001 — Configure responsibility ownership and presets

Revision: `3ed44b3` plus working tree; environment: local automated substitute; role: Company owner / Ordinary member.

Preconditions: Isolated SQLite integration host with explicit company memberships, agents, and responsibility assignments.

Steps:
1. Exercise micro and medium preset preview/apply, fill-missing, explicit replacement, tenant filtering, mutation authorization, validation, audit, onboarding replay, and rendered Settings states.
2. Verify the EF Core snapshot against the current model.

Expected: Responsibility state is relational, tenant-scoped, idempotent, auditable, and editable only when backend policy allows it.

Observed: Focused API and Web suites passed; `dotnet ef migrations has-pending-model-changes` reported no model changes.

Artifacts: `docs/design/references/responsibility-assignments-settings-reference.png`; `uat/responsibility-settings-phase5/issue-ledger.md`.

Result: pass using the documented automated substitute; live browser interaction remains covered by FLOW-RESP-005 below.

### FLOW-RESP-002 — Review Today as owner and functional manager

Revision: `3ed44b3` plus working tree; environment: local automated substitute; roles: Company owner / Functional manager / Ordinary member.

Steps:
1. Request Today under owner, manager, unconfigured-member, inactive-member, and cross-company contexts.
2. Render owner, Sales, and Finance variants plus loading, empty, partial, stale, unauthorized, and responsive states.
3. Verify normalized agent states, deduplication, visibility reasons, canonical links, and lens/company query preservation.

Expected: One stable shell adapts to authorized responsibility lenses without cross-feature leakage or request-time agent execution.

Observed: API integration and bUnit assertions passed for all listed variants.

Artifacts: `docs/design/references/responsibility-driven-today-workspace-desktop-verified.png`; `docs/design/references/responsibility-driven-today-workspace-mobile-verified.png`.

Result: pass.

### FLOW-RESP-003 — Request a company review

Revision: `3ed44b3` plus working tree; environment: local automated substitute; role: Company owner / Ordinary member.

Steps:
1. Request the operating review twice while an equivalent request is active.
2. Exercise authorized, forbidden, cross-company, paused-operation, and exhausted-budget cases.
3. Render queued/running/completed/blocked/failed presentation states.

Expected: The API returns one durable idempotent request, audits the outcome, starts no forbidden work, and exposes a safe actionable state.

Observed: Focused API and rendered-component tests passed.

Result: pass.

### FLOW-RESP-004 — Review an explicit reporting month

Revision: `3ed44b3` plus working tree; environment: local automated substitute; role: Company owner / Functional manager.

Steps:
1. Request an explicit month under owner and Sales/Marketing responsibility lenses.
2. Exercise timezone/DST and year transitions, current/comparison filtering, missing authoritative metrics, contributor failure, priority ordering, and Today/Monthly cache isolation.
3. Render period switching, month controls, result cards, priorities, feature sections, decisions, agent outcomes, and all honest fallback states.

Expected: Monthly is a separate period-aware read model with authorized feature-owned sources and no invented values.

Observed: Focused API and Web tests passed, including partial contributor failure and unavailable Marketing outcomes.

Artifacts: `docs/design/references/responsibility-driven-monthly-workspace-reference.png`; `uat/responsibility-monthly-phase6/issue-ledger.md`.

Result: pass using the documented automated substitute.

### FLOW-RESP-005 — Live authenticated desktop/mobile pass

Revision: `3ed44b3` plus working tree; environment: local; roles: Company owner / Functional manager.

Preconditions: Web listener on `localhost:5062` and SQL Server listener on `localhost:1433`.

Expected: Exercise Settings, Today, Review now, and Monthly at desktop and mobile widths with keyboard/focus checks and real navigation follow-through.

Observed: Neither required port has a listener, so the authenticated application cannot launch in the configured local environment.

Result: blocked by local runtime availability.

