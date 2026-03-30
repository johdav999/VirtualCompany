# Goal
Implement backlog task **TASK-9.1.6 — Virus scanning hook should be left in the pipeline design** for **ST-301 Company document ingestion and storage**.

The objective is **not** to build a full antivirus integration yet. The objective is to ensure the document ingestion pipeline is explicitly designed to support virus scanning as a first-class step, with clear extension points, statuses, and safe behavior so a real scanner can be plugged in later without redesigning the ingestion flow.

This work should preserve alignment with the architecture:
- ASP.NET Core modular monolith
- PostgreSQL for metadata
- Object storage for uploaded files
- Background-worker-driven ingestion pipeline
- Tenant-aware document ingestion in the Knowledge & Memory module

# Scope
In scope:
- Add a **virus scanning abstraction/hook** into the document ingestion pipeline.
- Ensure uploaded documents can pass through a **scan stage** before downstream processing.
- Add or update domain/application models so scan state is represented explicitly.
- Default current behavior to a **safe placeholder implementation** that does not perform real scanning but keeps the pipeline shape intact.
- Ensure the pipeline can distinguish between:
  - uploaded / pending scan
  - scan clean / ready for processing
  - scan failed / blocked / error
- Update comments, docs, and code structure so future implementation of a real scanner is straightforward.
- Keep all behavior tenant-aware and compatible with object storage + background processing.

Out of scope:
- Integrating a real antivirus engine or external malware scanning service.
- Building a UI for scan details beyond what is minimally required by existing ingestion status handling.
- Full end-to-end document parsing/chunking/embedding changes beyond what is needed to insert the scan hook.
- Broad refactors unrelated to document ingestion.

Assumptions:
- There is already some document upload and ingestion flow for ST-301, or at minimum a scaffold where this hook belongs.
- If the current implementation processes documents immediately after upload, change it so processing is gated behind the scan stage.
- If exact naming conventions already exist in the codebase, prefer those over inventing new ones.

# Files to touch
Inspect the existing implementation first, then update the most relevant files in these areas as needed:

- `README.md`
- `src/VirtualCompany.Domain/**`
  - knowledge/document entities
  - ingestion status enums/value objects
  - domain constants if present
- `src/VirtualCompany.Application/**`
  - document upload commands/handlers
  - ingestion orchestration services
  - background job contracts
  - DTOs/results for document status
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings/configurations
  - object storage integration points
  - background worker/job implementations
  - placeholder virus scanner implementation
- `src/VirtualCompany.Api/**`
  - API endpoints/controllers if upload/status contracts need adjustment
- `src/VirtualCompany.Web/**`
  - only if existing upload/status UI must reflect new pipeline states
- Tests in the corresponding test projects, if present

Likely concrete additions:
- A virus scanning interface, e.g. `IVirusScanner` or `IDocumentVirusScanService`
- A no-op / placeholder implementation, e.g. `NoOpVirusScanner`
- Status model updates for scan lifecycle
- Pipeline orchestration changes so processing waits for scan clearance
- Tests covering scan gating behavior

# Implementation plan
1. **Inspect the current document ingestion flow**
   - Find the upload entry point, metadata persistence, object storage write, and any background processing trigger.
   - Identify where `knowledge_documents.ingestion_status` is currently set and advanced.
   - Determine whether ingestion is synchronous, background-driven, or mixed.
   - Reuse existing module boundaries and naming patterns.

2. **Define explicit scan-stage semantics**
   - Introduce or extend document ingestion statuses to represent a virus scan stage.
   - Prefer a minimal, clear lifecycle such as:
     - `uploaded`
     - `pending_scan`
     - `scan_clean` or directly `queued_for_processing`
     - `scan_failed` / `blocked`
     - `processed`
     - `failed`
   - If the project already uses a single status field, extend it rather than adding unnecessary parallel state unless a separate scan status is clearly cleaner in the existing design.
   - Ensure unsupported/failed files can still surface actionable error states per ST-301.

3. **Add a scanning abstraction**
   - Create an application-facing interface for virus scanning.
   - The contract should be future-proof and return a structured result, not just a boolean.
   - Include enough information for future integrations, e.g.:
     - outcome/status
     - scanner name/version if available
     - reason/message
     - scanned timestamp
   - Keep the interface independent from HTTP/UI concerns.

4. **Implement a placeholder scanner**
   - Add a default infrastructure implementation that performs no real scan but preserves the pipeline step.
   - It should return a deterministic result such as “not scanned / assumed clean in development placeholder” or “clean via no-op scanner,” depending on how you model the hook.
   - Choose the option that best preserves safe future behavior while keeping current development unblocked.
   - Register it in DI so the pipeline always resolves a scanner implementation.

5. **Gate downstream processing behind the scan hook**
   - Update the ingestion orchestration so document parsing/chunking/processing does not begin until the scan step completes successfully.
   - If upload currently enqueues processing immediately, change it to enqueue a scan job first, then enqueue processing only on a clean result.
   - If there is a single worker pipeline, insert the scan step before processing and short-circuit on blocked/error outcomes.
   - Ensure blocked or failed scan results do not proceed to chunking/embedding.

6. **Persist scan-related state**
   - Update the document metadata model and persistence mapping as needed.
   - If the existing schema supports only `ingestion_status`, use that unless additional metadata is already idiomatic.
   - If appropriate, add metadata fields in `metadata_json` rather than over-modeling early.
   - Keep tenant ownership and access scope unchanged.

7. **Handle failure and blocked outcomes clearly**
   - Define behavior for:
     - scanner unavailable/error
     - suspicious/infected result
     - placeholder scanner result
   - Ensure the document ends in a visible non-processed state with actionable messaging.
   - Do not allow ambiguous states that could accidentally continue processing.

8. **Update API/application contracts if needed**
   - If document status is exposed via DTOs or API responses, include the new statuses or mapped user-facing states.
   - Keep backward compatibility where practical.
   - Avoid leaking internal implementation details beyond what clients need.

9. **Add tests**
   - Add or update tests to verify:
     - uploaded documents enter the scan stage
     - processing is not executed before a successful scan result
     - blocked/failed scan results prevent downstream processing
     - placeholder scanner is wired correctly
   - Prefer unit tests for orchestration logic and integration tests where existing patterns support them.

10. **Document the hook**
   - Update `README.md` or relevant module docs with a short note that the ingestion pipeline includes a virus scanning extension point and currently uses a placeholder implementation.
   - Mention where a real scanner should be integrated later.

Implementation guidance:
- Keep the design simple and incremental.
- Do not introduce a heavyweight workflow engine just for this task.
- Favor clean seams over speculative complexity.
- Preserve modular monolith boundaries:
  - Domain = statuses/rules
  - Application = orchestration/contracts
  - Infrastructure = scanner implementation/storage/job wiring

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify code paths:
   - Upload flow persists document metadata and object storage reference.
   - Newly uploaded document enters a scan-related state before processing.
   - Processing path is gated on scan success.
   - Scan failure/blocked path does not continue to downstream ingestion.

4. Verify DI and startup wiring:
   - Application resolves the virus scanner abstraction successfully.
   - Default placeholder implementation is registered in the current environment.

5. Verify persistence/model consistency:
   - Any new or changed statuses are handled consistently across domain, application, persistence, and API/UI mappings.
   - No unreachable or orphaned statuses remain.

6. If schema changes are required:
   - Add/update migrations using the project’s existing migration approach.
   - Confirm the app builds and tests pass after migration changes.

# Risks and follow-ups
- **Risk: status-model mismatch**
  - If the current code uses a narrow ingestion status enum/string, adding scan states may require updates across multiple layers.
  - Mitigation: trace all status consumers before changing names.

- **Risk: accidental behavior change**
  - If current ingestion assumes immediate processing, inserting a scan stage may break tests or timing assumptions.
  - Mitigation: update orchestration carefully and add explicit tests for sequencing.

- **Risk: overengineering**
  - This task only requires the hook, not a production malware platform.
  - Mitigation: keep the scanner contract small and the default implementation simple.

- **Risk: unclear failure semantics**
  - Placeholder behavior can be confusing if not documented.
  - Mitigation: make the result explicit in code/comments and ensure statuses are unambiguous.

Follow-ups for future backlog:
- Integrate a real malware scanning provider/service.
- Add richer scan metadata and audit events.
- Add admin/operator visibility for blocked documents.
- Add retry behavior for transient scanner failures.
- Add policy controls for quarantine, deletion, and notification on infected uploads.