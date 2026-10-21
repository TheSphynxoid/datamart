# DataMart

DataMart is a controlled, auditable Excel-to-PostgreSQL import pipeline, built
as an internship deliverable for a banking and finance counseling company.
Every write to the database is preceded by explicit human verification and is
journaled at batch and row level.

## What it does

- A 5-step import wizard: pick the target entity, upload the workbook, map
  columns to database fields, review a transformed preview, verify and import.
- Reusable, named mapping configurations with per-field format options
  (date formats with auto-detection, decimal separators, custom booleans).
- An asynchronous pre-flight validation (dry run) that shares its parsing and
  rule-evaluation core with the import path, so a validated file imports
  identically.
- Streaming server-side import: ExcelDataReader forward-only parsing,
  PostgreSQL binary COPY in a single all-or-nothing transaction, background
  job queues separate from validation.
- A two-tier audit journal: per-batch record (status, row counters, checksum,
  operator) and per-row records with JSON snapshots of mapped values.
- Per-entity validation rules enforced identically in dry run and import.
- Hardened workbook upload: OOXML package inspection, size and entry caps,
  macro rejection, regenerated GUID file names stored outside the webroot.
- ASP.NET Core Identity, an SPA-safe antiforgery round trip, and access
  logging.

## Technology

Backend: .NET 10, ASP.NET Core minimal APIs, Clean Architecture with CQRS
(MediatR, FluentValidation, AutoMapper), EF Core 10 with Npgsql (PostgreSQL),
.NET Aspire orchestration, NSwag-generated API clients.

Frontend: Angular 21, DevExtreme, SheetJS for in-browser Excel preview, a web
worker for off-main-thread parsing.

Tests: xUnit, Shouldly, Moq (backend); Karma/Jasmine (frontend).

## Getting started

Prerequisites: .NET 10 SDK, Docker Desktop, Node.js 20 or later.

Run the whole system (API, database containers, orchestration dashboard):

```bash
dotnet run --project src/AppHost
```

The Aspire dashboard opens with the application URLs and logs.

For frontend development with live reload (backend from the previous step
still running):

```bash
cd src/Web/ClientApp
npm install
npm start
```

`npm start` regenerates the typed API clients from the OpenAPI document
first, then serves the SPA with `/api` and `/openapi` proxied to the backend.

## Tests

```bash
dotnet test
```

The functional suite starts a temporary PostgreSQL through Aspire, so Docker
must be running. Unit suites (Domain, Application) run without Docker.

Frontend specs:

```bash
cd src/Web/ClientApp
npm test
```

## Documentation

The `docs/` folder holds the French technical documentation and user guide,
rendered to PDF with Quarto and Typst:

```bash
quarto render docs
```

Sample workbooks with individually expected results live in
`docs/sample-data/`. Rendered PDFs land in `docs/_render/`.

## Repository layout

| Project | Role |
|---|---|
| `src/Domain` | Entities, value objects, enums, domain events. No external dependencies. |
| `src/Application` | CQRS use cases (MediatR), validators, DTOs, pipeline behaviors. |
| `src/Infrastructure` | EF Core context, Identity, database configurations, bulk importer, background services. |
| `src/Web` | Minimal API endpoints, SPA host, auth. Composition root. |
| `src/AppHost`, `src/ServiceDefaults`, `src/Shared` | Aspire orchestration, telemetry, shared constants. |
| `tests/` | Domain, Application unit suites; Application functional suite. |

## Branches

- `master` is the fixed V1 deliverable state, kept as the reference for the
  deliverable documentation and demo assets.
- `v2` is the active development line.

## Credit

Generated from the
[Clean.Architecture.Solution.Template](https://github.com/jasontaylordev/CleanArchitecture)
version 10.8.0.
