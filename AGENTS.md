# AGENTS.md

Project context for AI coding assistants. Read at the start of every session.

## Project purpose

**DataMart** is a tool for a **banking & finance counseling** company. Its job is to
**map Excel files into the database** through a controlled, auditable pipeline. Core
capabilities (being built incrementally):

- **Excel import** — upload `.xlsx`, preview/parse, send to server.
- **Mapping configuration** — loadable and savable mappings (Excel columns → DB fields).
- **Manual verification** — financial sector: a human must review before anything is
  committed. No silent/automatic writes to the DB.
- **Entity management** — select which entities are eligible for import.
- **Data validation** — client-side (UX) and server-side (the real security/defense).
- **Data import** — the final, verified write to the DB.

> Treat "finance sector" as a standing constraint: correctness, traceability, and
> human-in-the-loop matter more than convenience. Never auto-commit data.

## How to work with me (important)

I'm an **intern learning this codebase and Clean Architecture**. Adjust accordingly:

- **I write the code.** Teach me, guide me, review my code, point out bugs and explain
  *why*. Do not silently rewrite or hand me complete implementations I asked to learn.
- Ask clarifying/design questions and offer options with tradeoffs rather than guessing.
- **Delegate to you only:** boilerplate, tedious configuration, repetitive scaffolding,
  or things I explicitly hand off. When in doubt, ask first.
- Prefer small, reviewable steps. Verify after each change.
- When reviewing: explain the concept behind each issue so I learn, not just the fix.

## Tech stack

- **Backend:** .NET 10, ASP.NET Core Minimal APIs, **Clean Architecture**, EF Core
  (PostgreSQL via Npgsql), .NET Aspire orchestration.
- **Patterns:** CQRS with **MediatR**, **FluentValidation** (runs in a pipeline
  behavior), **AutoMapper**, ASP.NET Core Identity.
- **Frontend:** Angular 21 (NgModule style, `standalone: false`), **DevExtreme**
  (`devextreme-angular`, company-mandated UI library), SheetJS (`xlsx`) for in-browser
  Excel preview, NSwag-generated typed API clients.
- **Build config:** `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`,
  **`TreatWarningsAsErrors=true`** (code must compile clean, warnings are failures),
  central package management (`Directory.Packages.props`).

## Architecture

Layer dependencies point inward (inner layers know nothing about outer ones):

```
Domain ──► Application ──► Infrastructure ──► Web
```

| Project | Role |
|---|---|
| `src/Domain` | Entities, Value Objects, Enums, Domain Events. No external deps. |
| `src/Application` | CQRS Commands/Queries (MediatR handlers), Validators, DTOs, AutoMapper profiles, pipeline behaviors. Depends on Domain only. |
| `src/Infrastructure` | EF Core `ApplicationDbContext`, Identity, DB configurations, interceptors. Implements Application's interfaces (`IApplicationDbContext`). |
| `src/Web` | Minimal API endpoints (`IEndpointGroup`), SPA host, auth. The composition root (`Program.cs`). |
| `src/AppHost`, `src/ServiceDefaults`, `src/Shared` | Aspire orchestration, telemetry, shared constants. |

**Backend conventions:**
- A use case = a MediatR `IRequest` record + handler **in the same file**, under
  `src/Application/<Feature>/Commands/<Verb><Entity>/` or `.../Queries/...`.
- Validator: `<Command>Validator : AbstractValidator<>`, co-located; runs automatically
  via `ValidationBehaviour`.
- Endpoints: a class implementing `IEndpointGroup` in `src/Web/Endpoints/`. Auto-mounted
  at `/api/{ClassName}` by `MapEndpoints` reflection in `Program.cs` — **no manual registration.**
- DTOs carry a private nested `Mapping : Profile` class (AutoMapper auto-discovery).
- **No repositories** — handlers use `IApplicationDbContext` directly. **No EF migrations**
  — `EnsureCreatedAsync` + seed in `ApplicationDbContextInitialiser`.
- `IFormFile` stays in the Web layer; pass `Stream`/`byte[]` to Application commands.

**Frontend conventions:**
- Routes defined inline in `src/Web/ClientApp/src/app/app.module.ts` (`RouterModule.forRoot`).
- Components `standalone: false`, declared in `AppModule`.
- API clients are **generated** by NSwag into `src/app/web-api-client.ts`
  (from `wwwroot/openapi/v1.json`). **Never hand-edit** — run codegen instead.
- Components inject generated clients directly; hand-written services only for
  cross-cutting state (e.g. `AuthService`).
- New page = create component folder under `src/app/`, declare + add route in `app.module.ts`.
- **DevExtreme modules** currently imported in `AppModule`: DxDataGrid, DxForm, DxButton,
  DxSelectBox, DxTextBox, DxValidator, DxValidationSummary. Each UI module re-exports
  `DxTemplateModule` (the `*dxTemplate` directive) for custom item templates.

## Current feature: Excel import

5-step wizard in `src/app/Excelmap/excelmap.component.ts` (signal-based, progressive disclosure):
1. **Target entity** — DxSelectBox of importable entities + schema preview (fields, types, badges). ✅
2. **Upload file** — `file-upload/` child validates + emits file; web worker (`excel.worker.ts`) parses with SheetJS off main thread. ✅
3. **Map columns** — auto-match by name, duplicate prevention, format options (date/decimal/boolean), save/load mapping configurations. ✅
4. **Preview** — *(stub, not yet built)*
5. **Verify & import** — *(stub, not yet built)*

### Wizard architecture
- All state in Angular signals; derived state in `computed`.
- `confirmStep(index, commit)`: commit + advance; if step was already done, cascade-reset downstream.
- `statusOf(index)`: derived from data (locked/active/completed) — never stored.
- Draft vs committed pattern for entity picker (`draftEntity` → `selectedEntity` on confirm).
- Editing a completed step re-expands it; confirming re-runs cascade reset for downstream steps.

### Entity schema endpoint
- `ApplicationDbContext.GetEntityList()` returns `EntitySchemaDto[]` (table name, PK, properties with types/constraints).
- `IApplicationDbContext` hides `.Model`/`.Database`, so schema queries build DTOs inside the context.
- Entities filtered by `IImportable` interface + `[Exclude]` attribute (class or property level).
- `EntityPropertyDto.IsExcluded` flags `[Exclude]`-d properties (audit fields, computed columns).

### Format options (mapping step)
- Triggered by DB field CLR type: dates (`DateTime`/`DateOnly`), decimals (`Decimal`/`Double`), booleans.
- Date format auto-detection from sample data (unambiguous only — day > 12 disambiguates).
- Decimal separator (period/comma) + optional strip-symbols toggle ($, €, thousands separators).
- Boolean source mapping (custom true/false text values).
- Separate "format configuration" section below mapping table — clean separation of concerns.

### Web worker (`excel.worker.ts`)
- SheetJS parsing off main thread. ArrayBuffer transferred zero-copy (`postMessage(buf, [buf])`).
- Worker terminated + recreated per parse (discards stale results from rapid re-uploads).
- `sheetRows: 50` option stops parsing early for preview (never processes all 1M rows).

### Mapping configuration (save/load)
- `Mapping` entity (`Domain/Entities/Mapping.cs`) — stores named mapping configs with `ColumnMapping[]` (owned entities via `OwnsMany`).
- `MappingConfiguration` EF config (`Infrastructure/Data/Configurations/`).
- CRUD endpoints: `MappingEndpoints` (`/api/mappings`) — GET list (with `targetEntity`/`search` filters), GET by ID, POST create, PUT update, DELETE.
- `IEntitySchemaService` (singleton) — caches `GetEntityList()` result server-lifetime (avoids reflection on every validation).
- Frontend: "Load saved mapping" dropdown at top of step 3 + "Save as new mapping" form after confirm. Schema drift detected on load (fields no longer in entity are left unmapped).

### Component tree
- `Excelmap/excelmap.component.ts` — parent/orchestrator (owns all state, drives the wizard).
- `file-upload/` — child: acquire + validate file (size + magic-byte `50 4B 03 04`), emit via `@Output`.
- `excel.worker.ts` — SheetJS in web worker, exports `ExcelPreviewResult` interface.

**Pattern:** lift shared state to the parent; children communicate via `@Output` (up) / `@Input` (down).

## Security stance for file upload

Client-side validation is **UX only** (trivially bypassed). The server independently
enforces: magic-byte + OOXML-package verification, size cap, zip-bomb protection
(cap decompressed size / entry count), macro rejection, **streaming parse with row caps**
(ExcelDataReader, forward-only `reader.Read()` — avoid `AsDataSet()`), generated filename,
store outside webroot. Excel is the **only** input format for now.

## Commands

**Backend (from repo root):**
- Build: `dotnet build src/Web/Web.csproj`
- Run (via Aspire): `dotnet run --project src/AppHost` (orchestrates Web + DB)
- Tests: `dotnet test` (all), or `dotnet test tests/Application.UnitTests` (one suite)
- Restore after package changes: `dotnet restore`

**Frontend (from `src/Web/ClientApp`):**
- Install: `npm install`
- Serve (dev): `npm start` (auto-runs API codegen first via `prestart`)
- Build: `npm run build` (also runs codegen via `prebuild`)
- Regenerate API clients: `npm run generate-api`
- Unit tests: `npm test` (Karma/Jasmine, headless Chrome)

> Frontend dev proxies `/api` and `/openapi` to the backend (`proxy.conf.js`), reading the
> Aspire service address from env vars — so run the AppHost for local dev.

## Known incomplete / gotchas

- **Wizard steps 4–5 are stubs** — preview and verify/import are not yet built.
- **No server-side upload/import endpoint yet** — file parsing is client-side only (preview).
  Server-side import pipeline is the next major backend task (ExcelDataReader streaming +
  Npgsql COPY for bulk insert, single transaction, ImportBatch tracking).
- **SourceKey + ImportBatch columns decided but not built** — plan: add to `BaseAuditableEntity`
  for audit traceability (which DB record came from which Excel row / import session).
  Enables future UPSERT without redesigning the schema.
- **INSERT-only for MVP** — no UPSERT/update mode yet. Every import creates new records.
- **`web-api-client.ts` is generated** — always edit backend endpoints then regenerate,
  never edit this file directly.
- npm install scripts are pinned in `package.json` under `allowScripts` (esbuild, lmdb,
  etc.). On Windows, esbuild's binary sometimes lands in a staging folder under
  `node_modules/@esbuild/` and needs manual renaming to `win32-x64` if a build fails with
  an esbuild resolution error.
- `Microsoft.OpenApi` must stay on the **2.x** line (pinned `2.11.0`) to match
  `Microsoft.AspNetCore.OpenApi` 10.0.10's source generator — do not bump to 3.x.
