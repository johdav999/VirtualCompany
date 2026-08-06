# Architecture Overview

Virtual Company is a .NET 9 modular monolith. `VirtualCompany.Api` is the backend host, `VirtualCompany.Web` is the Blazor host, and business behavior is organized around Application contracts and capability-owned infrastructure assemblies.

## Dependency Direction

```text
Domain <- Application <- Persistence
                         ^
                         +-- Persistence.Migrations
                         +-- Infrastructure.Platform
                         +-- Infrastructure.Mailbox
                         +-- Infrastructure.Finance
                         +-- Infrastructure.Sales
                             +-- Sales and Marketing capabilities
                         +-- Infrastructure.Support
                         +-- Infrastructure.Operations

Infrastructure (facade) -> composes the infrastructure modules
Api -> Application + Infrastructure facade + Persistence.Migrations
Web -> Shared + HTTP API contracts
```

Capability assemblies may depend on Domain, Application, Persistence, and Platform where needed. They do not reference sibling capability implementations. Cross-capability work uses Application contracts, workflows, durable tasks, domain events, or the outbox.

## Persistence and Migrations

`VirtualCompany.Persistence` owns `VirtualCompanyDbContext`, entity configurations, and seed resources. `VirtualCompany.Persistence.Migrations` owns the complete SQL Server migration history, snapshot, and design-time factory. Platform runtime configuration explicitly selects that migrations assembly.

Local SQL Server and Docker SQL Server use the same model and migration history. Database-specific setup scripts may differ, but schema authority remains EF Core migrations.

## Composition

Each infrastructure project owns its registrations. The root Infrastructure project is a compatibility facade and composition entry point only. This preserves the existing API startup surface while allowing a change inside Finance, Sales, Support, Mailbox, or Operations to compile against a substantially smaller source set during focused development.

See `docs/architecture-rules.MD` for mandatory rules and `docs/build-performance.md` for reproducible build measurements.
