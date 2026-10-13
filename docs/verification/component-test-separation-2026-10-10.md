# Component and integration test separation — 10 October 2026

This completes the component/integration boundary left after
[the initial test-project split](test-project-separation-2026-10-09.md).
Production code and behavior are unchanged.

## Ownership and dependency changes

- `FinancialStatementWorkspaceComponentTests` moved from Web.Contract.Tests to
  Web.Tests: all 13 cases and their assertions are preserved. The component
  fixtures now load fixed JSON rather than calling backend statement builders.
- Finance.Tests owns two new cases that build those same statement examples
  through the production layout, BAS catalog, and CSV formatter, then compare the
  complete reports against the shared JSON. The payloads were captured from the
  original fixture builder before the refactor. Both suites copy data into their
  own outputs; neither links executable test sources or references the other.
- Four `FinanceInsightPresentationTests` cases moved unchanged from Web.Tests
  to Finance.Tests because they exercise Application-owned calculations.
- Four `FinanceConversationRunContractTests` cases moved unchanged from Web.Tests
  to Web.Contract.Tests because they compare backend and Web contract shapes.
- The two backend/client compatibility cases in `SalesHumanRoomTests` moved to
  Web.Contract.Tests with their original identities and assertions. Component
  cases keep their existing assertions and now use Web DTOs for HTTP fixtures,
  with the same values as the former backend DTOs. Two additional contract cases
  verify backend media-token payloads through the host and guest Web clients.
- Web.Tests references Web only, with Shared obtained transitively. It has no
  Application, Domain, API, Infrastructure, Persistence, or TestSupport dependency.
  Web.Contract.Tests no longer references bUnit.
- Two Domain architecture guards enforce those transitive boundaries. The
  existing direct-reference guard was tightened to allow Web only.

Test namespaces are retained to preserve discovery identities; project ownership
is defined by the owning directory and dependency graph. The authoritative
[Test Architecture rules](../architecture-rules.MD#test-architecture) describe
the boundary. Shared fixture provenance and maintenance guidance are in
`tests/Fixtures/FinancialStatements/README.md`.

## Verification

| Check | Result |
| --- | --- |
| Focused statement/Sales component tests | 55 passed |
| Full Web.Tests suite | 1,115 passed, no skips |
| Focused Sales/Finance contract cases | 8 passed |
| Web.Contract.Tests hermetic suite | 1,174 passed, no skips |
| Focused Finance statement/presentation/fixture tests | 13 passed |
| Full Domain.Tests suite, including architecture guards | 51 passed |

API, Web, TestSupport, Finance.Tests, Domain.Tests, and Web.Contract.Tests built
successfully as part of these runs. Existing compiler/analyzer warnings remain.
Builds were sequenced after an initial concurrent build collided on Shared.dll.
A NuGet restore succeeded after retrying a denied network connection with
approved network access. `git diff --check` passes.

Before/after discovery comparison:

| Project | Before | After |
| --- | ---: | ---: |
| Web.Tests | 1,112 | 1,115 |
| Web.Contract.Tests | 1,179 | 1,174 |
| Finance.Tests | 586 | 592 |
| Domain.Tests | 49 | 51 |

All 2,926 original discovered cases remain accounted for; 23 moved project and
six new cases were added. No case has multiple project owners. One architecture
theory's displayed arguments changed because its allowed references were
tightened; this intentional change was normalized in the comparison. Long
VSTest theory names can have identical truncated displays, so comparison retains
their multiplicity within each project. The moved Finance suite files and every
statement component test body were also compared against HEAD and are unchanged.

Logs and `discovery-comparison.json` are under
`artifacts/test-matrix/component-test-separation`. TRX files are in the owning
projects' `TestResults` directories. No full Finance or API suite, external
SQL/provider lane, browser UAT, or deployment was run for this structural change.

## Running the affected boundaries

```powershell
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --filter "Category!=SqlServer&Category!=AccountingPerformance"
dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj --filter "FullyQualifiedName~FinancialStatementWorkspace|FullyQualifiedName~FinanceInsightPresentationTests"
dotnet test tests/VirtualCompany.Domain.Tests/VirtualCompany.Domain.Tests.csproj
```

Run builds sequentially because the projects share production output paths. The
existing `scripts/test-matrix.ps1` already includes all four owning projects.
