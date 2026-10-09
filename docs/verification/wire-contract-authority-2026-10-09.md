# Wire contract authority verification — 2026-10-09

## Delivered change

API and Web now consume the same transport definitions from
`src/VirtualCompany.Shared/Contracts`, grouped by capability. The client uses
1,006 compatibility aliases instead of independently declaring those payloads.
Shared has no project or package dependencies, and Web references Shared without
referencing API or Application implementations.

Established Application, API, and primitive Domain namespaces remain on moved
top-level definitions for source compatibility. Thirty-one former controller
nested JSON models now use Shared capability namespaces. Framework binding
adapters, such as multipart document uploads, remain in API. Client aggregation,
HTTP envelopes, routing context, and presentation state remain client-owned.

Twelve request mapping classes remain in API and translate shared transport
data to Application commands or inputs. Use-case services, commands, queries,
authorization, and persistence remain in their existing backend owners.

Connection enum values and converters are shared. The converter preserves API
snake-case values and accepts existing numeric values. The Support calendar's
specialized weekday converter retains its numeric format. Document access-scope
fields and JSON encoding have a shared data base and converter; the Domain
subtype retains tenant validation and delegates JSON encoding to that converter.

Client consumers now use actual API payloads where their previous copies had
drifted: finance permissions, task-policy fields, invoice-detail evidence,
simulation clock `currentUtc`, statement drilldown `selectedLine` and
`journalEntries`, and integration connection responses. Invoice payment context
continues to come from the list payload; the detail payload does not supply it.
Finance's monthly client aggregation is explicitly named a view model.

Empty former contract files were removed. Existing unrelated uncommitted work
was preserved. This change is uncommitted and has not been deployed.

## Compatibility checks

`WireContractAuthorityTests` verifies 1,106 captured pre-refactor backend
schemas: serialized property metadata, custom JSON attributes, and original
public constructor parameter names, types, and defaults. It also checks shared
assembly ownership, absence of former client declarations, project independence,
and absence of controller-to-command mappings on transport definitions.

Former nested CLR names are normalized to their new Shared names in the schema
comparison. The document access-scope property uses its shared data base;
its original schema hash remains intact through explicit CLR-name normalization,
with a separate JSON roundtrip and cross-company validation test.

Editor convenience constructors retain mutable fields and empty collection
defaults. Positional request records explicitly keep their original JSON
constructor, preserving server binding when fields are omitted. Additional
serialization tests cover provider/status values, capability masks, calendar
payloads, finance JSON names, and server defaults versus editor defaults.

## Final verification

| Check | Result |
| --- | --- |
| Web tests | 1,112 passed, no skips |
| Web/API contract tests | 1,179 passed, no skips |
| Focused API regressions | 171 passed, no skips |
| Finance module tests | 575 passed, 11 existing skips |
| Domain tests | 49 passed, no skips |
| API and Web compilation | Passed |
| Workspace UAT host build | Passed, zero errors |
| EF pending-model check | No changes since the last migration |
| `git diff --check` | Clean |

The focused API run covers finance route/composition boundaries, approval target
handlers, task policy, document ingestion/repository/publication, mailbox flow
and callbacks, standard mailbox infrastructure, finance integrations, reporting
period close, and Sales presentation inputs. The finance controller boundary
test retains the existing 395-route surface.

Builds and EF report existing analyzer/nullability/model warnings. The Finance
suite's SQL Server-dependent cases retain their existing skips; no new skips
were introduced. The EF command uses the design-time model factory and does not
apply migrations. No live provider operation, browser session, production
database migration, or deployment was used as verification.

Logs and schema-capture tooling are retained locally under the ignored
`artifacts/wire-contracts-refactor` directory. Checked-in schema and alias
fixtures live under `tests/VirtualCompany.Web.Contract.Tests/Fixtures`.
