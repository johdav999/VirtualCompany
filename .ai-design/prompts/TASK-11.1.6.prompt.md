# Goal
Implement TASK-11.1.6 for **ST-501 Direct chat with named agents** so the system **never exposes raw model reasoning / chain-of-thought** in direct agent chat responses or related artifacts, and instead returns **concise rationale summaries where relevant**.

This change must align with the architecture guidance that:
- rationale summaries are a first-class persisted business artifact,
- explanations must be concise and operational,
- raw reasoning must not be surfaced to users,
- auditability should preserve summaries and source references, not hidden internal deliberation.

# Scope
In scope:
- Direct agent chat response shaping for ST-501.
- Orchestration output contracts used by chat so they support:
  - user-facing response text,
  - optional concise rationale summary,
  - no raw reasoning field exposed to API/UI/mobile consumers.
- Persistence updates where chat/task/audit artifacts currently store or derive explanation content.
- API and application-layer mapping to ensure only safe explanation fields are returned.
- Guardrails/tests preventing accidental serialization of raw reasoning fields.
- Prompt/orchestration instructions that explicitly request concise rationale summaries instead of chain-of-thought.

Out of scope unless required by existing shared code paths:
- Broad redesign of all explainability/audit screens outside the direct chat flow.
- New UX beyond minimally rendering an existing or newly added rationale summary field.
- Changes to approval, workflow, or dashboard features except where they share the same response contract and would otherwise leak reasoning.
- Provider-specific hidden reasoning features unless already present in the codebase; if present, ensure they remain internal and are not persisted or returned.

# Files to touch
Inspect the solution and update the relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `src/VirtualCompany.Mobile`
- tests in corresponding test projects if present

Likely file categories to touch:
- Chat/direct-agent application handlers/services
- AI orchestration service contracts and result models
- Prompt builder / response normalization components
- API DTOs and endpoint response models
- Web/mobile view models/components that render chat messages
- Audit/task persistence mapping if rationale is stored there
- Unit/integration tests around chat/orchestration serialization

Search for terms like:
- `reasoning`
- `chain`
- `rationale`
- `summary`
- `chat`
- `conversation`
- `message`
- `orchestration`
- `response`
- `explanation`
- `audit`
- `task`

# Implementation plan
1. **Discover current direct chat flow end-to-end**
   - Trace the ST-501 path from UI/mobile/API into application/orchestration and back.
   - Identify:
     - request/response DTOs,
     - orchestration result objects,
     - message persistence models,
     - any task/audit creation side effects,
     - any fields currently named `reasoning`, `thoughts`, `analysis`, `scratchpad`, or similar.
   - Confirm whether raw reasoning is currently:
     - requested from the model,
     - stored in DB,
     - logged,
     - serialized to API clients,
     - rendered in web/mobile.

2. **Define a safe response contract**
   - Standardize on a contract that exposes only:
     - final user-facing response content,
     - optional `rationaleSummary` or equivalent concise explanation field,
     - optional source references if already supported.
   - Remove or stop mapping any raw reasoning fields from outward-facing DTOs.
   - If internal orchestration models currently contain raw reasoning-like fields, either:
     - eliminate them entirely, or
     - keep them internal-only and mark them non-persisted/non-serialized, preferring elimination if feasible.

3. **Update orchestration instructions**
   - Modify prompt-building or response-format instructions so the model is told:
     - do not reveal internal reasoning,
     - provide a brief rationale summary only when useful,
     - keep rationale concise, operational, and user-safe.
   - Prefer structured output with separate fields such as:
     - `responseText`
     - `rationaleSummary`
   - Ensure the rationale summary is optional and short.

4. **Normalize model output**
   - In the orchestration layer, map provider/model output into a safe internal result.
   - If the provider returns extra analysis/reasoning metadata, do not propagate it.
   - If free-form output is used, parse/extract only approved fields.
   - Add a defensive sanitization step so only approved properties survive into application/API responses.

5. **Persist only approved explanation artifacts**
   - Where chat interactions create messages, tasks, or audit events:
     - persist `rationale_summary`-style content only,
     - do not persist raw reasoning text.
   - If an existing entity has a reasoning-like field used for business data, migrate usage to concise rationale summary semantics.
   - Keep alignment with architecture and backlog notes:
     - summaries are allowed,
     - raw chain-of-thought is not.

6. **Update API contracts**
   - Ensure direct chat endpoints return only safe fields.
   - Remove reasoning-like properties from response DTOs/OpenAPI-facing models if present.
   - Verify JSON serialization does not include internal-only properties.

7. **Update web/mobile consumers**
   - Adjust chat UI/view models to render:
     - main response body,
     - optional concise rationale summary where relevant.
   - Do not display hidden/internal explanation fields even if present in older payloads.
   - Keep UI changes minimal and consistent with current design.

8. **Add tests**
   - Unit tests for orchestration result mapping:
     - raw reasoning input is dropped,
     - rationale summary is preserved,
     - final response remains intact.
   - API/application tests:
     - chat response payload does not contain reasoning fields,
     - rationale summary appears only when expected.
   - Serialization tests if DTOs are used.
   - If there are persistence tests:
     - verify only rationale summary is stored in task/audit/message artifacts.

9. **Check logs and diagnostics**
   - Ensure technical logs do not accidentally write raw model reasoning payloads.
   - If request/response logging exists around LLM calls, redact or omit reasoning-bearing content.
   - Preserve correlation IDs and operational metadata.

10. **Document assumptions in code comments/PR notes**
   - Add concise comments where needed explaining:
     - raw reasoning must not be exposed,
     - rationale summaries are the approved explainability artifact.

# Validation steps
1. Build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify direct chat flow:
   - Open a direct conversation with a named agent from the web app.
   - Send a prompt likely to trigger explanation, e.g. asking “why” or requesting a recommendation.
   - Confirm the response includes:
     - normal answer text,
     - optionally a concise rationale summary,
     - no raw reasoning, step-by-step hidden deliberation, or chain-of-thought dump.

4. Inspect API response payloads:
   - Verify chat response JSON contains only approved fields.
   - Confirm no properties like `reasoning`, `analysis`, `thoughts`, `scratchpad`, or similar are returned.

5. Inspect persistence side effects:
   - If the chat creates/links a task or audit event, confirm stored explanation content is a concise rationale summary only.

6. Inspect logs during a chat request:
   - Confirm no raw reasoning content is emitted in application logs or exception traces.

7. Regression-check shared orchestration paths if reused by chat:
   - Ensure single-agent chat still works with persona, role brief, and scoped context intact.
   - Ensure task-linking behavior still functions.

# Risks and follow-ups
- **Risk: hidden leakage through shared DTOs**
  - The same orchestration result may be reused by chat, tasks, audit, and future explainability views. Be careful not to break consumers while removing unsafe fields.

- **Risk: provider payload logging**
  - Even if API/UI is safe, raw reasoning may still leak through debug logging or stored raw provider responses.

- **Risk: ambiguous existing “reasoning” semantics**
  - Some existing fields may actually be intended as user-safe summaries. Rename carefully only where necessary to avoid broad churn.

- **Risk: UI assumptions**
  - Web/mobile may expect a single text blob today. Keep backward-compatible rendering if possible while introducing optional rationale summary support.

- **Risk: over-sanitization**
  - Do not remove useful concise explanations entirely; the requirement is to replace raw reasoning with short rationale summaries where relevant.

Follow-ups to note if not completed here:
- Apply the same safe explanation contract consistently to ST-502/ST-503/ST-602 shared orchestration and audit surfaces.
- Add explicit OpenAPI/schema documentation stating that raw reasoning is never returned.
- Consider a centralized `SafeAiResponse` / `RationaleSummaryPolicy` abstraction if multiple features share this behavior.