# Goal
Implement backlog task **TASK-32.1.4** by adding and updating documentation for the Fortnox integration so the codebase clearly explains:

- how to register and configure a real Fortnox developer app
- which configuration values are required by the application
- which OAuth scopes/endpoints/callback URLs are used
- how local development secrets should be stored with user-secrets
- how production secrets should be stored and rotated safely
- how token handling expectations map to the current implementation and acceptance criteria

The deliverable is documentation-first, but it must be grounded in the actual code/configuration paths in this repository. If any documented config keys, routes, or behaviors do not yet match the implementation, update the docs to reflect reality and explicitly note gaps/TODOs only when necessary. Do not invent unsupported behavior.

# Scope
In scope:

- Create or update:
  - `/docs/integrations/fortnox.md`
  - `/docs/runbooks/fortnox-integration.md`
- Document the Fortnox integration in the context of **US-32.1 Fortnox configuration, OAuth connect flow, and secure token lifecycle**
- Cover:
  - required configuration keys:
    - `FinanceIntegrations:Fortnox:ClientId`
    - `FinanceIntegrations:Fortnox:ClientSecret`
    - `FinanceIntegrations:Fortnox:RedirectUri`
    - `FinanceIntegrations:Fortnox:TokenUrl`
    - `FinanceIntegrations:Fortnox:ApiBaseUrl`
  - startup validation / fail-fast expectations when Fortnox is enabled
  - connect flow entry route:
    - `/finance/integrations/fortnox/connect`
  - callback route:
    - `/finance/integrations/fortnox/callback`
  - Fortnox authorization and token endpoint usage
  - callback validation expectations:
    - state
    - nonce
    - authenticated user
    - company scope
  - safe handling of invalid/expired authorization codes
  - encrypted-at-rest token storage expectations
  - prohibition on rendering tokens in UI or logging them
  - automatic refresh via `/oauth-v1/token`
  - reconnect/revoked behavior for invalid refresh tokens
  - local development setup with `dotnet user-secrets`
  - production secret storage and rotation procedures
  - operational troubleshooting guidance

Out of scope unless required to make docs accurate:

- implementing missing Fortnox OAuth code paths
- changing domain behavior unrelated to documentation
- broad refactors of integration architecture
- adding unrelated integrations documentation

If you discover code/doc mismatches that materially block accurate documentation, make the smallest necessary code/config/documentation adjustment and mention it in the final summary.

# Files to touch
Start by inspecting these likely locations, then update the most relevant files only:

- `docs/integrations/fortnox.md` *(create if missing)*
- `docs/runbooks/fortnox-integration.md` *(create if missing)*
- `README.md` *(only if there is an integrations docs index or setup section worth linking from)*
- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Api/appsettings*.json`
- `src/VirtualCompany.Api/**` for Fortnox options binding, startup validation, auth endpoints, and logging behavior
- `src/VirtualCompany.Application/**` for integration services / token lifecycle logic
- `src/VirtualCompany.Infrastructure/**` for token persistence, encryption-at-rest, HTTP clients, Fortnox adapter, and refresh logic
- `src/VirtualCompany.Web/**` for connect/callback routes or UI entry points
- `tests/VirtualCompany.Api.Tests/**`
- any existing docs under `docs/` that should cross-link to the new Fortnox docs

Prefer minimal, focused edits. If the docs are new, ensure they are internally consistent and reference actual namespaces/routes/config keys from the codebase.

# Implementation plan
1. **Inspect current Fortnox implementation**
   - Search the repo for:
     - `Fortnox`
     - `FinanceIntegrations`
     - `ClientId`
     - `RedirectUri`
     - `/finance/integrations/fortnox`
     - `/oauth-v1/token`
     - token encryption/storage logic
   - Identify:
     - actual options class names and config binding paths
     - whether Fortnox enablement is feature-flagged/config-driven
     - actual connect/callback endpoints and where they live
     - current token refresh behavior
     - current logging/redaction behavior
     - current startup validation behavior

2. **Map implementation to acceptance criteria**
   - Build a concise internal checklist of each acceptance criterion and whether the current code:
     - already satisfies it
     - partially satisfies it
     - does not yet satisfy it
   - Use this checklist to ensure the docs are truthful.
   - If there are small missing pieces required so the docs are not misleading, implement only the smallest necessary changes.

3. **Author `/docs/integrations/fortnox.md`**
   - Write this as the primary developer/setup guide.
   - Include sections such as:
     - Overview
     - Prerequisites
     - Fortnox developer app registration
     - Required application configuration
     - Required scopes/permissions
     - OAuth endpoints and callback URL
     - Local development setup with user-secrets
     - Connect flow overview
     - Callback validation and security expectations
     - Token storage and refresh behavior
     - Logging and secret-handling rules
     - Verification checklist
   - Include concrete examples for local config, but use placeholders only, never real secrets.
   - If exact Fortnox scope names are present in code/config, document them exactly. If not present, document only what the implementation actually requires and avoid fabricating scope names.

4. **Author `/docs/runbooks/fortnox-integration.md`**
   - Write this as the operational runbook.
   - Include sections such as:
     - Purpose and ownership
     - Required secrets and where they live
     - Secret rotation procedure
     - Redirect URI change procedure
     - Token refresh failure / reconnect handling
     - Safe troubleshooting steps
     - What must never be logged or exposed
     - Production rollout checklist
     - Incident response notes for revoked credentials or failed callbacks
   - Make the rotation procedure explicit:
     - prepare new secret in secret store
     - update Fortnox app config if needed
     - deploy/apply config
     - verify connect and refresh flows
     - revoke old secret
     - confirm no token leakage in logs/UI

5. **Cross-link docs**
   - Add links between:
     - integration guide and runbook
     - any existing docs index/README if appropriate
   - Keep links relative and repository-friendly.

6. **Align docs with actual config and routes**
   - Verify all documented keys, routes, and endpoint paths against code.
   - If startup validation exists, document where it happens.
   - If it does not exist yet but there is a clear options validator or startup path to add with minimal effort, add it and document it.
   - If token encryption-at-rest is implemented, document the mechanism at a high level without exposing sensitive internals.
   - If not implemented, do not claim it is; instead document the intended requirement and note the gap only if unavoidable.

7. **Add or update tests only if code changes are made**
   - If you add startup validation or route/config changes, add focused tests covering:
     - fail-fast on missing required config when Fortnox is enabled
     - safe error behavior for invalid callback/token failures if touched
   - Do not add tests for documentation-only changes.

8. **Quality pass**
   - Ensure docs are actionable, concise, and production-safe.
   - Remove any accidental secret values.
   - Ensure terminology is consistent:
     - Fortnox integration
     - connect flow
     - callback
     - token refresh
     - needs reconnect / revoked
   - Ensure markdown formatting is clean and readable.

# Validation steps
1. **Repository inspection**
   - Confirm the documented config keys, routes, and endpoint names match the codebase.
   - Confirm both target docs exist:
     - `docs/integrations/fortnox.md`
     - `docs/runbooks/fortnox-integration.md`

2. **Build/tests**
   - Run:
     - `dotnet build`
   - If code changes were made, also run:
     - `dotnet test`

3. **Documentation review checklist**
   - Verify `/docs/integrations/fortnox.md` includes:
     - app registration steps
     - required config keys
     - callback URL guidance
     - local user-secrets example
     - token handling/security notes
   - Verify `/docs/runbooks/fortnox-integration.md` includes:
     - production secret storage guidance
     - secret rotation steps
     - reconnect/revoked operational handling
     - troubleshooting guidance

4. **Acceptance-criteria traceability**
   - Ensure the docs explicitly cover:
     - fail-fast startup config requirements
     - connect route and callback route
     - callback validation expectations
     - safe handling of invalid/expired auth codes
     - encrypted token storage expectations
     - no token exposure in UI/logs
     - automatic refresh via `/oauth-v1/token`
     - invalid refresh token => needs reconnect/revoked behavior
     - local and production secret management

5. **Final output summary**
   - In your final response, include:
     - files changed
     - whether any code changes were required beyond docs
     - any gaps discovered between acceptance criteria and current implementation
     - commands run and results

# Risks and follow-ups
- **Risk: docs drift from implementation**
  - Mitigation: inspect code first and document only verified behavior.
- **Risk: Fortnox scopes/endpoints are not yet fully implemented**
  - Mitigation: do not invent unsupported details; document verified configuration and clearly note any implementation gap if necessary.
- **Risk: secret-handling docs accidentally encourage unsafe practices**
  - Mitigation: use placeholders only, prefer secret stores/user-secrets, and explicitly forbid committing secrets.
- **Risk: acceptance criteria imply behavior not yet present**
  - Mitigation: if a small targeted code fix can align behavior, make it; otherwise call out the gap clearly in the final summary.
- **Follow-up candidates**
  - add automated options validation tests if missing
  - add log redaction tests around Fortnox token exchange/refresh
  - add a docs index entry for finance integrations
  - add a production readiness checklist for all OAuth-based integrations