# Authoritative wire contracts

These capability files define transport data used by the API, Application, and
clients. They compile in `VirtualCompany.Shared`, even where an established
Application, API, or Domain namespace is retained for source compatibility.
Web's `WireContracts` aliases refer to these definitions; they contain no fields.

Use an existing capability file for related additions. New shared shapes should
use a Shared capability namespace. Shared has no project or package dependencies.
Use-case commands and queries remain in Application, controller request mapping
remains in API, and editor state and presentation projections remain in Web.
Controller-nested JSON payloads now use Shared capability namespaces. Framework
binding adapters such as multipart uploads remain in API. Document access-scope
fields and JSON encoding live here; the Domain subtype retains tenant validation.

Preserve JSON names and converters. Client convenience constructors support
object initializers; request records keep primary-constructor JSON binding to
preserve server defaults. Read-only collection contracts stay read-only;
editors replace collections when changing their contents.

The authority and schema tests live in `VirtualCompany.Web.Contract.Tests`.
Client serialization tests cover enum values, capability masks, custom JSON
names, and request defaults. The repository rules are in
[`architecture-rules.MD`](../../../docs/architecture-rules.MD).
