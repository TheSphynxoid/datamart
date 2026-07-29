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

## Current feature in progress: Excel import

Component tree being built under `src/app/`:
- `Excelmap/excelmap.component.ts` — **parent/orchestrator** ("smart container") that owns
  the file + parsed data and drives upload/preview/send.
- `file-upload/` — child: acquire + validate a file (size + magic-byte pre-check), emit it up.
- Preview component (TBD) — child: receive parsed rows via `@Input`, render only.
- **Pattern:** lift shared state to the parent; children communicate via `@Output` (up) /
  `@Input` (down). `.xlsx` is a ZIP → magic bytes `50 4B 03 04`; reject `.xls`/`.xlsm`.

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

- **DevExtreme is half-wired:** packages installed and CSS referenced in `angular.json`
  (`dx.fluent.blue.light.css`), but **no DxModules imported in `app.module.ts`**. Add the
  specific module (e.g. `DxDataGridModule`) to `AppModule.imports` when first using a
  DevExtreme component.
- **`web-api-client.ts` is generated** — always edit backend endpoints then regenerate,
  never edit this file directly.
- npm install scripts are pinned in `package.json` under `allowScripts` (esbuild, lmdb,
  etc.). On Windows, esbuild's binary sometimes lands in a staging folder under
  `node_modules/@esbuild/` and needs manual renaming to `win32-x64` if a build fails with
  an esbuild resolution error.
- `Microsoft.OpenApi` must stay on the **2.x** line (pinned `2.11.0`) to match
  `Microsoft.AspNetCore.OpenApi` 10.0.10's source generator — do not bump to 3.x.
