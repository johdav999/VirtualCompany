
## Finance Seed Bootstrap

The finance seed generator can be invoked through the internal API with
`POST /internal/companies/{companyId}/finance/bootstrap/seed` and a body containing
`seedValue`, optional `seedAnchorUtc`, and optional `replaceExisting`. The same bootstrap path is
available from the API host CLI with `seed-finance --company-id <guid> --seed <integer> [--anchor-utc <datetime>] [--replace|--append]`.
# PostgreSQL Migration Archive

These files are the previous PostgreSQL/Npgsql migration source files preserved as historical reference during the SQL Server migration reset.

- They are no longer part of the active EF Core migration path.
- They were moved out of `src/VirtualCompany.Infrastructure/Persistence/Migrations` to keep future migration scaffolding clean for SQL Server and Azure SQL.
- The active migration strategy from this point is a fresh SQL Server baseline migration generated from the current model.

The archived files retain their original filenames with a `.txt` suffix so they are preserved in the repository without being compiled as active migration classes.
