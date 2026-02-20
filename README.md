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
  "Hangfire": {
    "DailyPullCron": "0 6 * * *"
  }
}
```

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

## Running Tests

```bash
dotnet test
```

87 tests across Application and Infrastructure layers (xUnit + Moq + EF InMemory).

## CI

GitHub Actions workflow (`.github/workflows/build-and-test.yml`) runs on every push/PR to `main`:
`dotnet restore` → `dotnet build` → `dotnet test` → test results published via `dorny/test-reporter`.

## Logs

Structured logs are written to:
- **Console**: `[HH:mm:ss LVL] SourceContext: Message`
- **`logs/timelogger-YYYYMMDD.txt`**: rolling daily, 7 days retained
