# Goal
Implement backlog task **TASK-7.4.3 — Unhandled exceptions are captured and mapped to safe API responses** for story **ST-104 Baseline platform observability and operational safeguards** in the existing **.NET modular monolith**.

The coding agent should add a production-ready global exception handling path for the ASP.NET Core API so that:
- unhandled exceptions are consistently captured,
- structured logs are emitted with correlation context,
- clients receive safe, non-levealing error responses,
- HTTP status codes are mapped predictably,
- the solution fits the platform’s multi-tenant and observability direction.

Because no task-specific acceptance criteria were provided, derive behavior from the story acceptance criteria and architecture/backlog context.

# Scope
In scope:
- Add or complete **global exception handling middleware/filter/pipeline** in `VirtualCompany.Api`.
- Map known exception categories to safe HTTP responses using **RFC 7807 ProblemDetails** or an equivalent consistent error contract already used by the API.
- Ensure **unexpected/unhandled exceptions** return a generic 500 response without stack traces or sensitive internals.
- Log exceptions in a structured way, including:
  - correlation/trace identifier,
  - tenant/company context when available,
  - request path/method,
  - exception type.
- Preserve compatibility with the existing architecture and coding style.
- Add/update tests covering exception-to-response mapping behavior.

Out of scope unless already partially implemented and required to complete this task:
- Full health check implementation.
- Full background worker retry framework.
- Broad logging platform changes outside what is needed for exception handling.
- Business audit events for technical exceptions unless there is already a clear pattern in the codebase.
- Large-scale refactors across application/domain layers.

Assumptions to validate in the codebase before implementing:
- The API project is ASP.NET Core and likely uses minimal hosting in `Program.cs`.
- There may already be partial support for `ProblemDetails`, custom exceptions, tenant context, or correlation IDs.
- Existing exception types may already exist in Application/Domain layers and should be reused rather than replaced.

# Files to touch
Inspect first, then modify only what is necessary. Likely files include:

- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Api/...` any existing middleware, filters, or exception handling classes
- `src/VirtualCompany.Api/...` any API error contract or ProblemDetails configuration
- `src/VirtualCompany.Application/...` existing custom exception types, if present
- `src/VirtualCompany.Domain/...` domain exception types, if present
- `src/VirtualCompany.Api.Tests/...` or nearest existing test project for API/integration tests
- `README.md` only if there is a concise API error handling section already documented and it needs updating

If no suitable location exists, create focused files such as:
- `src/VirtualCompany.Api/Middleware/GlobalExceptionHandlingMiddleware.cs`
- `src/VirtualCompany.Api/Extensions/ExceptionHandlingExtensions.cs`
- `src/VirtualCompany.Api/Errors/ApiProblemDetailsFactory.cs` or similar
- test files under the existing test project structure

Do not invent new projects unless absolutely necessary.

# Implementation plan
1. **Inspect current API startup and cross-cutting infrastructure**
   - Review `Program.cs` and any extension methods to find:
     - current middleware order,
     - logging setup,
     - ProblemDetails usage,
     - correlation ID handling,
     - tenant/company context resolution,
     - authentication/authorization pipeline.
   - Search for existing exception classes such as:
     - validation exceptions,
     - not found exceptions,
     - forbidden/authorization exceptions,
     - conflict exceptions,
     - domain rule exceptions.
   - Search for any existing middleware or filters that already handle exceptions to avoid duplication.

2. **Choose the app-wide exception handling mechanism that best fits the current codebase**
   - Prefer the standard ASP.NET Core global approach already aligned with the project:
     - `UseExceptionHandler(...)`,
     - custom middleware,
     - or `IExceptionHandler` if the target framework supports it and the project already uses modern patterns.
   - Keep the implementation centralized and easy to extend.

3. **Define exception-to-response mapping**
   - Reuse existing exception types if present.
   - If needed, support mappings like:
     - validation-related exception -> `400 Bad Request` or `422 Unprocessable Entity` depending on existing conventions,
     - not found exception -> `404 Not Found`,
     - forbidden/authorization exception -> `403 Forbidden`,
     - unauthenticated exception -> `401 Unauthorized` only if this is already surfaced through the auth pipeline rather than exception handling,
     - conflict/business rule conflict -> `409 Conflict`,
     - cancellation/timeouts if explicitly represented -> appropriate safe status if already conventional in the codebase,
     - all unknown exceptions -> `500 Internal Server Error`.
   - Do **not** expose stack traces, raw exception messages, connection strings, SQL, provider errors, or internal type details in responses.
   - For unknown exceptions, return a generic title/detail such as:
     - title: `An unexpected error occurred.`
     - include a trace/correlation identifier clients can report.
   - Prefer `ProblemDetails` with fields like:
     - `type`
     - `title`
     - `status`
     - `detail` (safe only)
     - `instance`
     - extension field for `traceId`
     - extension field for `correlationId` if the app distinguishes it

4. **Integrate tenant and correlation context into logging**
   - When logging exceptions, include:
     - request method/path,
     - trace identifier,
     - tenant/company identifier if available from request context, claims, route values, or tenant service,
     - authenticated user identifier if already safely available and consistent with current logging practices.
   - Use structured logging placeholders, not interpolated strings.
   - Ensure logs for 500-level failures are emitted at `Error` level.
   - For expected business exceptions, use the project’s existing logging convention; avoid noisy duplicate logs if controllers/handlers already log them.

5. **Ensure middleware ordering is correct**
   - Place exception handling early enough in the pipeline to catch downstream failures.
   - Do not break authentication, authorization, routing, or existing middleware.
   - If there is correlation ID middleware, ensure exception handling can access the generated identifier.
   - If there is status code pages middleware, ensure it does not conflict with exception responses.

6. **Implement safe response generation**
   - Return JSON consistently for API endpoints.
   - If the API already uses `application/problem+json`, preserve that.
   - Include trace/correlation identifiers in the response payload extensions for supportability.
   - For validation exceptions, include field-level errors only if the project already has a safe validation contract; otherwise keep the response minimal and consistent.

7. **Add tests**
   - Add focused tests that verify:
     - an unhandled exception from a test endpoint/controller/handler returns 500 with a safe payload,
     - the response does not contain stack trace or raw exception message for unknown exceptions,
     - known exception types map to expected status codes,
     - `traceId` or equivalent identifier is present,
     - content type is correct for ProblemDetails if applicable.
   - Prefer integration tests against the API pipeline if a test host pattern already exists.
   - If integration infrastructure is absent, add the smallest viable test coverage consistent with the repo.

8. **Keep implementation minimal and aligned**
   - Avoid introducing a large custom error framework.
   - Reuse ASP.NET Core primitives and existing project abstractions.
   - Keep naming and folder structure consistent with the solution.

9. **Document only if needed**
   - If the repo already documents API conventions, add a brief note about global exception handling and safe error responses.
   - Do not add verbose documentation if the project currently avoids it.

# Validation steps
Run and report the results of the relevant validation commands after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there is an API test project or runnable API host, verify behavior for:
   - a forced unhandled exception returns HTTP 500,
   - response body is safe and structured,
   - known mapped exceptions return expected status codes.

4. In the final implementation notes, summarize:
   - which exception types are mapped,
   - what response contract is returned,
   - where middleware/handler is registered,
   - what tests were added or updated.

# Risks and follow-ups
- **Unknown existing conventions:** The repo may already have partial error handling or a preferred error contract. Reuse it instead of creating a competing pattern.
- **Framework/version differences:** If the API targets a .NET version with `IExceptionHandler`, use it only if it fits the current style; otherwise prefer middleware.
- **Tenant context availability:** Tenant/company context may not always be resolved before an exception occurs. Log it when available, but do not fail if absent.
- **Validation exception shape:** Be careful not to break existing client expectations if validation responses already have a defined schema.
- **Duplicate logging:** Avoid logging the same exception multiple times across middleware, controllers, and handlers.
- **Security:** Never leak internal exception details in production responses.

Suggested follow-ups outside this task if not already implemented:
- standardize correlation ID middleware across API and background workers,
- add health checks for app/database/Redis/object storage,
- add retry/error classification patterns for background jobs,
- add a shared exception taxonomy in Application layer if the codebase currently lacks one.