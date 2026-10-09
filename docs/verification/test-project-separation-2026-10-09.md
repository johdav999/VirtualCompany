# Test project separation

Implemented 9 October 2026 in the existing checkout. The preceding finance insight
query/command and EF configuration refactors remain intact and uncommitted.

## Ownership and dependencies

| Project | Ownership |
| --- | --- |
| VirtualCompany.Domain.Tests | Nine pure entity suites; references Domain only. Includes test architecture guards. |
| VirtualCompany.Api.Tests | API composition, transport/authorization, and backend integration tests. |
| VirtualCompany.Finance.Tests | Finance policies/modules, EF mapping regressions, and Finance migration compatibility. |
| VirtualCompany.SalesSource.Tests | Sales source/provider tests and the Sales session suite that exercises internal Sales policies. |
| VirtualCompany.Web.Tests | Web components, presentation, navigation, and typed clients; references Web and Application only. |
| VirtualCompany.Web.Contract.Tests | Web/API wire contracts and the financial-statement component suite that composes backend report builders. |
| VirtualCompany.TestSupport | Non-test library: composed API hosts, common integration fixtures, and observability doubles. |
| VirtualCompany.Workspace.Uat | Disposable UAT adapter referencing TestSupport rather than the executable API test project. |

Relocated 70 existing source files. Namespaces and existing test identities were
preserved to keep filters compatible. Executable suites are no longer linked from
another test project's folder. SDK default source discovery replaces the Web and
contract compile whitelists and the API filename exclusions. Shared fixtures are
compiled once in TestSupport, with narrowly named friend assemblies for their
existing internal members.

The quarterly planning and business-evidence client helpers now belong to fixtures,
with the existing API test entry points forwarding to them. The accidentally
duplicated quarterly integration suite remains in API tests alone. The Sales module
grants its focused SalesSource test assembly access to existing internal policies.

Domain.Tests, TestSupport, and the previously omitted SalesSource and
SupportGrounding projects are registered in the solution. The hermetic test-matrix
lane includes Domain.Tests. Architecture rules document the ownership boundaries.

## Discovery and regression fixes

Compared test discovery before and after across API, Finance, Web, and contract
projects, including the resulting Domain and Sales projects:

- **No existing test identity was lost.**
- Five previously duplicated identities now have one owner.
- Default Web discovery restores ten cases omitted by the old compile whitelist.
- Five new architecture cases enforce dependency/source ownership boundaries.
- Two new HTTP serialization cases guard API-test JSON transport.
- The final Sales discovery includes 36 existing cases from a project not included
  in the original four-project discovery capture; these are not new tests.

Evidence: `artifacts/test-project-separation/discovery-comparison.json`, the
`before-*` / `after-*` discovery listings, `before-sources.json`,
`after-projects.json`, and the relocation/hash manifest `moves.json`.

Restored Web discovery exposed a missing `FinanceManager` label in the English and
Swedish Agents resources. Both keys now use the established translations. An older
Bills-page fixture also needed the current `AgentWorkApiClient` registration; its
existing assertions remain unchanged.

A broad API run exposed six requests using legacy media-SDK HTTP JSON overloads,
which cannot serialize System.Text.Json nodes. API-local test HTTP extensions now
select System.Text.Json explicitly. Their local scope avoids exporting competing
extension overloads to contract/UAT consumers. Production HTTP behavior is unchanged.

## Verification

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain, including architecture guards | 49 | 0 | 0 |
| Full Web unit/component/client suite | 1,104 | 0 | 0 |
| Web/API contract suite | 70 | 0 | 0 |
| Finance hermetic suite | 575 | 0 | 0 |
| SalesSource suite | 50 | 0 | 0 |
| Focused API shared-host/fixture/JSON/architecture/DI regressions | 97 | 0 | 0 |

**1,945 distinct focused cases passed.** The API JSON suites were subsequently
rebuilt and rerun after placing the serializer helper in its final API-local owner:
44 overlapping cases passed, including all six originally failing requests. Contract
tests were also rebuilt/rerun against the final support library: 70 passed.

TRX records are under `artifacts/test-project-separation/results`. Domain and Web
run without API/Infrastructure composition. Finance and contract runs exclude the
explicit SQL Server and accounting-performance categories. No external SQL/provider
lane was executed for this structural test-project refactor.

API, Web, TestSupport, all affected test projects, and Workspace.Uat build successfully.
The PowerShell test-matrix runner parses successfully, and `git diff --check` passes.
Builds used `-m:1 -p:UseSharedCompilation=false`; Sales restore assets were refreshed
because older assets pointed at an unavailable sandbox package cache.

### Full API suite limit

The broad API run was stopped after six minutes to release its output locks before
rebuilding the repaired harness. At interruption it had 633 passes and the six JSON
failures described above. An overlapping rebuild attempt failed on the running
testhost's locked binaries; subsequent checks were sequenced after stopping only
the owned testhost. The full 3,746-case API discovery was **not** executed to completion.
The focused 97-case and final 44-case regressions establish the affected boundaries;
they are not a claim that the complete API suite passed.

## Running the separated suites

Run a focused suite directly, for example:

```powershell
dotnet test tests/VirtualCompany.Domain.Tests/VirtualCompany.Domain.Tests.csproj -m:1 -p:UseSharedCompilation=false
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj -m:1 -p:UseSharedCompilation=false
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj -m:1 -p:UseSharedCompilation=false --filter "Category!=SqlServer&Category!=AccountingPerformance"
```

The complete hermetic matrix remains available through `scripts/test-matrix.ps1`.
TestSupport is a library and is not a separate executable test lane. No commits,
deployment, database schema change, or running application restart was performed.
