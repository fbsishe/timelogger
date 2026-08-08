# TimeLogger

A self-hosted Blazor Server application that aggregates time entries from **Tempo** (Jira) and **CSV/Excel files**, maps them to Timelog.com projects via configurable rules, and submits them automatically.

## Architecture

```
src/
  TimeLogger.Domain/          — entities, enums, domain types (no dependencies)
  TimeLogger.Application/     — interfaces, service contracts, mapping engine
  TimeLogger.Infrastructure/  — EF Core, Refit HTTP clients, Hangfire jobs, file parsing
  TimeLogger.Web/             — Blazor Server UI (MudBlazor)
tests/
  TimeLogger.Application.Tests/
  TimeLogger.Infrastructure.Tests/
  TimeLogger.Web.Tests/
```

Stack: **.NET 10**, **Blazor Server**, **SQL Server**, **Entity Framework Core**, **Hangfire**, **Refit**, **MudBlazor 8**, **Serilog**

See [PLAN.md](PLAN.md) for the full architecture and milestone plan.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server (local, Express, or [Docker](https://www.docker.com/products/docker-desktop/))
- A Timelog.com account with API access
- (Optional) Jira + Tempo API token for automated import

---

## Quick Start

### 1. Clone and restore

```bash
git clone https://github.com/fbsishe/timelogger
cd timelogger
dotnet restore
```

### 2. Start SQL Server (Docker)

```bash
docker-compose up sqlserver -d
```

### 3. Configure credentials

Use [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) for local dev:

```bash
dotnet user-secrets set "Timelog:ApiKey"  "your-timelog-api-key"  --project src/TimeLogger.Web
dotnet user-secrets set "Jira:ApiToken"   "your-jira-api-token"   --project src/TimeLogger.Web
dotnet user-secrets set "Tempo:ApiToken"  "your-tempo-token"       --project src/TimeLogger.Web
```

For production, set the full config in `appsettings.json` or as environment variables:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=TimeLogger;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Timelog": {
    "BaseUrl": "https://app.timelog.com",
    "Username": "your@email.com",
    "ApiKey": "your-timelog-api-key"
  },
  "Jira": {
    "BaseUrl": "https://yourcompany.atlassian.net",
    "UserEmail": "your@email.com",
    "ApiToken": "your-jira-api-token"
  },
  "Tempo": {
    "BaseUrl": "https://api.tempo.io/4"
  },
  "TimelogReporting": {
    "SiteCode": "from Timelog System Administration → Reporting API settings",
    "ApiId": "...",
    "ApiPassword": "..."
  },
  "Hangfire": {
    "DailyPullCron": "0 6 * * *"
  }
}
```

> **Note:** `TimelogReporting` is optional but strongly recommended — without it, existing
> registrations in Timelog can only be read for the user the REST API key belongs to
> (see [Timelog API notes](#timelog-api-notes)). If a credential contains `$`, escape it
> as `$$` in the docker-compose `.env` file, or compose's variable interpolation will
> silently truncate it.

### 4. Apply database migrations

```bash
dotnet ef database update \
  --project src/TimeLogger.Infrastructure \
  --startup-project src/TimeLogger.Web
```

### 5. Run

```bash
dotnet run --project src/TimeLogger.Web
```

Navigate to `https://localhost:5001` (or the port shown in the console).

---

## Key Features

| Feature | URL |
|---|---|
| Dashboard with unmapped entry count | `/` |
| Configurable mapping rules | `/mapping-rules` |
| All entries with status filter | `/entries` |
| Day review — side-by-side Timelogger vs Timelog per person/day (Manager/Admin: click a date in `/entries`) | `/entries` |
| Import history by period | `/import-history` |
| Import sources (Tempo, File Upload) | `/sources` |
| CSV / Excel file upload | `/upload` |
| Timelog.com project/task sync | `/timelog-sync` |
| Submission pipeline with audit trail | `/submission` |
| API/config settings view | `/settings` |
| Hangfire job dashboard | `/hangfire` |
| Health check | `/health` |

### Mapping Rules

Rules are evaluated in **priority order** (lowest number = highest priority). Each rule specifies:
- **Match field**: `ProjectKey`, `IssueKey`, `UserEmail`, `Description`, `Activity`, or `metadata.<key>`
- **Operator**: `Equals`, `Contains`, `StartsWith`, `Regex`
- **Match value**
- **Target**: Timelog project (required) + task (optional)

Use the **Test** button (flask icon) to preview which pending entries a rule would match before saving.

### File Import

Supported formats: `.csv`, `.xlsx`, `.xls`, `.xlsm`

Required columns (flexible aliases supported):

| Field | Accepted column names |
|---|---|
| Date | `date`, `workdate`, `work_date`, `day` |
| Hours | `hours`, `duration`, `time`, `timespent`, `h` |
| Email | `email`, `useremail`, `user_email`, `author` |

Optional: `description`, `projectkey`, `issuekey`, `activity`. Extra columns are stored as metadata and can be matched by rules using `metadata.<columnname>`.

### Background Jobs

Hangfire runs three recurring jobs (configured via `Hangfire:DailyPullCron`, default: daily at midnight):
- **timelog-sync** — syncs projects and tasks from Timelog.com
- **tempo-pull** — imports worklogs from Tempo for all enabled sources
- **timelog-submit** — submits all mapped entries to Timelog.com

Jobs can also be triggered manually from the UI.

---

## Timelog API notes

Hard-won facts about the Timelog APIs (tenant: `app2.timelog.com/relyits`) that shape how
this app reads and writes registrations. Timelog's own Swagger spec endpoint (`/v1/docs`)
returns HTTP 500, so most of this was established empirically.

### REST API (`/api/v1`, Bearer key — config `Timelog:*`)

- **`GET /time-tracking-item/get-by-date` is self-scoped**: it only returns registrations
  of the user the API key is issued to, even though the same key can *create/update/delete*
  registrations for anyone. An empty result for another employee means "not visible", not
  "not registered". The submission conflict check therefore treats this endpoint as
  authoritative only when the entry belongs to the key's own user (resolved via
  `GET /user/me`); otherwise it falls back to the local submission history — or, better,
  to the Reporting API below.
- **Pagination default is 10** on `get-by-date` — always pass `$pagesize=500` (baked into
  the Refit route) or busy days silently lose registrations.
- **`GET /timesheet-status/weekly?startDate=&endDate=&userId=`** works for *any* user and
  returns per-week `TimesheetStatus` (`Open`/`Closed`). A closed week rejects all writes
  with `422 { ErrorCode: 30012, "Date is closed by Timesheet" }` — the day review disables
  its fix/submit actions in that case.
- `TimeRegistrationApprovalStatus` on time-tracking items is an undocumented enum;
  observed values 6/7 on approved months (treat `>= 6` as locked). It does **not** match
  the documented Reporting-API ladder.
- `PUT /time-registration` identifies the registration by the GUID in the body (no id in
  the path); `DELETE /time-registration/{id}` takes the integer `TimeRegistrationID`.

### Reporting API (`/service.asmx`, SOAP/XML — config `TimelogReporting:*`)

- `GetWorkUnitsRawPaged` (plain form-POST works) returns **all employees'** registrations,
  including ones entered manually in Timelog — this is the only way to read other users'
  hours. Credentials (SiteCode + API ID + password) are separate from the REST key and are
  created in Timelog System Administration → Reporting API settings.
- Response work units include `UserID` (same id space as the REST API and
  `EmployeeMapping.TimelogUserId`), `TimeRegistrationGuid` (usable with the REST `PUT` for
  in-place fixes), `RegHours`, `Note`, and `ApprovedStatus` on the documented ladder
  (0 = open, 10 = timesheet closed, 20/30 = approved). Ignore `EmployeeID` — different id space.
- When configured, both the day review and the submission conflict check use this API
  (authoritative for every user); the REST `get-by-date` path remains as fallback.

### Secrets containing `$`

Both the shell (`source`) and docker-compose variable interpolation mangle values with `$`
in them. In the compose `.env`, escape `$` as `$$`; when testing with curl, pass secrets
via `--data-urlencode name@file` rather than shell variables. A truncated password shows
up as a plain `401` from Timelog with no further hint.

---

## Running Tests

```bash
dotnet test
```

234 tests across Domain, Application, Infrastructure, and Web (bUnit) layers
(xUnit + Moq + EF InMemory), plus Playwright E2E tests via `scripts/run-e2e.sh`.

## CI

GitHub Actions workflow (`.github/workflows/build-and-test.yml`) runs on every push/PR to `main`:
`dotnet restore` → `dotnet build` → `dotnet test` → test results published via `dorny/test-reporter`.

## Logs

Structured logs are written to:
- **Console**: `[HH:mm:ss LVL] SourceContext: Message`
- **`logs/timelogger-YYYYMMDD.txt`**: rolling daily, 7 days retained
