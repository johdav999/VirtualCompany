# Goal
Implement backlog task **TASK-12.4.5** for **ST-604 Mobile companion for approvals, alerts, and quick chat** by making the product direction explicit in code/docs/UI: **responsive web may satisfy some early mobile usage, but the intended long-term companion experience remains the .NET MAUI app**.

This task should **not** introduce mobile-specific business logic. It should align the solution with the architecture and story notes by:
- preserving **web-first** as the primary experience,
- clarifying **mobile-companion scope**,
- ensuring any current mobile entry points, docs, or placeholders reflect that **responsive web is an interim bridge**, not the final mobile strategy,
- keeping backend/API reuse central for both web and MAUI.

Because no explicit acceptance criteria were provided for this task, derive implementation from:
- ST-604 acceptance criteria,
- the architecture principle **“Web-first, mobile-companion”**,
- the story note: **“Responsive web may cover some early mobile needs, but MAUI app remains the target companion.”**

# Scope
In scope:
- Update product-facing and developer-facing wording where this task is represented.
- Add or refine a lightweight implementation marker in the web/mobile surfaces that communicates:
  - responsive web can support early mobile access,
  - MAUI remains the target companion app for approvals, alerts, briefings, and quick chat.
- Ensure the mobile project and shared contracts are positioned for **API reuse** and **limited companion scope**.
- If there is an existing roadmap, feature flag, placeholder page, or shell screen for mobile, align it with this task.
- Add tests where practical for any changed view model, API contract text, or configuration behavior.

Out of scope:
- Building the full MAUI mobile app experience.
- Adding new backend business workflows or mobile-only endpoints.
- Implementing offline sync, push notifications, or full mobile parity.
- Reworking unrelated dashboard, approval, or chat features beyond what is needed to express this task.

# Files to touch
Inspect first, then modify only what is necessary. Likely candidates:

- `README.md`
- `src/VirtualCompany.Web/**/*`
- `src/VirtualCompany.Mobile/**/*`
- `src/VirtualCompany.Shared/**/*`
- `src/VirtualCompany.Api/**/*` only if an existing endpoint/contract description or metadata needs alignment
- `src/VirtualCompany.Application/**/*` only if an existing DTO/view model needs wording updates
- `tests/VirtualCompany.Api.Tests/**/*`
- Any existing docs or backlog/task mapping files you find under `docs/**/*`

Prefer touching:
- mobile/web landing or placeholder screens,
- shared constants/view models for feature messaging,
- documentation that describes platform strategy.

Avoid broad refactors.

# Implementation plan
1. **Discover current representation of mobile strategy**
   - Search the repo for:
     - `mobile`
     - `MAUI`
     - `responsive`
     - `approval`
     - `briefing`
     - `quick chat`
     - `companion`
     - `ST-604`
   - Identify whether this task is best implemented through:
     - docs,
     - a web/mobile placeholder screen,
     - shared feature metadata,
     - or a combination.

2. **Align wording with architecture and backlog**
   - Update any existing product copy so it consistently states:
     - the web app is the primary command center,
     - responsive web may cover early mobile needs,
     - the MAUI app is the intended focused companion,
     - companion scope is limited to approvals, alerts, briefings, quick chat, and lightweight follow-up.
   - Do not imply full mobile parity.

3. **Implement a minimal but real product artifact**
   - If the web app has a mobile-related page, card, roadmap item, or empty state, update it to reflect the above positioning.
   - If the MAUI app has a shell/home/placeholder screen, ensure it describes the intended companion scope clearly.
   - If neither exists, add the smallest appropriate artifact, such as:
     - a shared feature descriptor,
     - a roadmap/status component,
     - or a placeholder mobile companion screen in the MAUI app.
   - Keep the implementation production-safe and non-disruptive.

4. **Preserve backend reuse**
   - Confirm no mobile-specific business logic is introduced.
   - If any comments, service registrations, or docs suggest separate mobile logic, revise them toward:
     - shared backend APIs,
     - shared contracts where appropriate,
     - thin client behavior.

5. **Keep scope intentionally limited**
   - Ensure any UI text or comments reinforce that mobile is for:
     - alerts,
     - approvals,
     - daily briefing view,
     - direct agent chat,
     - quick company status / task follow-up.
   - Avoid suggesting admin setup, workflow design, or full cockpit parity on mobile.

6. **Add tests or verification hooks**
   - If you introduce shared constants/view models, add unit tests for expected values or rendering inputs where practical.
   - If changes are purely UI copy, keep tests lightweight and only where the project already has patterns for them.
   - Do not create brittle snapshot tests unless the repo already uses them.

7. **Document the task outcome**
   - Add a concise note in relevant docs or code comments explaining:
     - responsive web is an interim support path,
     - MAUI remains the target companion app,
     - backend APIs are shared.

# Validation steps
Run the minimum relevant validation for the touched projects, then broaden if needed:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If web files changed:
   - ensure the web project builds cleanly and any changed page/component renders without compile errors.

4. If mobile files changed:
   - ensure `src/VirtualCompany.Mobile` compiles in the current solution context.
   - Do not add platform-specific requirements unless already present.

5. Manual verification:
   - Confirm updated wording consistently communicates:
     - responsive web can cover early mobile needs,
     - MAUI remains the target companion,
     - mobile scope is limited and focused.
   - Confirm no new mobile-only backend logic or endpoints were introduced.

# Risks and follow-ups
Risks:
- The task may be interpreted too narrowly as docs-only; prefer a small but tangible implementation artifact if possible.
- The repo may not yet contain enough mobile/web UI scaffolding; in that case, keep the change minimal and additive.
- Overstating mobile capability could conflict with the architecture; keep wording conservative and explicit.

Follow-ups:
- Full ST-604 implementation should later cover:
  - sign-in,
  - company selection,
  - alert list,
  - approval actions,
  - daily briefing view,
  - direct agent chat,
  - quick status/task follow-up.
- Future work may add:
  - concise mobile DTOs,
  - intermittent connectivity handling,
  - notification delivery,
  - approval inbox optimization,
  - mobile-specific UX polish in MAUI.
- If not already present, a later task should define explicit acceptance criteria for the interim responsive-web experience versus the target MAUI companion.