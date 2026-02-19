# TimeLogger — Architecture & Implementation Plan

## Overview

**TimeLogger** is a .NET Blazor web application that aggregates time entries from third-party systems (Tempo, CSV/Excel) and submits them to [Timelog.com](https://timelog.com) via API. A flexible rule-based mapping engine bridges the gap between source entries and Timelog projects/tasks.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | Blazor Server (.NET 8+) |
| Backend API | ASP.NET Core Web API (.NET 8+) |
| Background Jobs | Hangfire (daily pull + submission) |
| Database | SQL Server + EF Core (Code-First) |
| ORM | Entity Framework Core 8 |
| File Parsing | EPPlus (Excel), CsvHelper (CSV) |
| HTTP Clients | Refit (typed API clients) |
| Testing | xUnit + Moq + Testcontainers (SQL Server) |
| CI/CD | GitHub Actions |

---

## Solution Structure

```
TimeLogger.sln
├── src/
│   ├── TimeLogger.Web/              # Blazor Server app (UI)
│   ├── TimeLogger.Application/      # Use cases, services, interfaces
│   ├── TimeLogger.Domain/           # Entities, value objects, enums
│   ├── TimeLogger.Infrastructure/   # EF Core, external API clients, file parsers
│   └── TimeLogger.Worker/           # Hangfire background jobs
└── tests/
    ├── TimeLogger.Application.Tests/
    ├── TimeLogger.Infrastructure.Tests/
    └── TimeLogger.Web.Tests/
```

---

## Core Domain Concepts

### Entities

- **ImportSource** — A configured connection to a third-party system (e.g. Tempo API key, polling schedule)
- **ImportedEntry** — A raw time entry as received from the source system (before mapping)
- **MappingRule** — A flexible rule that matches an imported entry and maps it to a Timelog project/task
- **TimelogProject** — Synced from Timelog API
- **TimelogTask** — Synced from Timelog API, child of a project
- **SubmittedEntry** — An entry that has been submitted to Timelog (audit trail)

### Mapping Rule Model

Rules are evaluated in priority order. Each rule has:

| Field | Purpose |
|---|---|
| `SourceSystem` | Which system this rule applies to (Tempo, CSV, etc.) |
| `MatchField` | Field to inspect (`ProjectKey`, `CustomField_XYZ`, `Tag`, `Activity`, etc.) |
| `MatchOperator` | `Equals`, `Contains`, `StartsWith`, `Regex` |
| `MatchValue` | The value to match against |
| `TimelogProjectId` | Target project in Timelog |
| `TimelogTaskId` | Target task in Timelog (optional — can map to project only) |
| `Priority` | Lower = evaluated first |

Example rules:
1. "If Jira project key equals `MOBILE` → Timelog project `App Development`"
2. "If Jira custom field `timelog_task` equals `42` → Timelog task `42`"
3. "If tag contains `internal` → Timelog project `Internal / Admin`"

---

## Data Flow

```
┌─────────────┐     API/File     ┌──────────────────┐
│  Tempo API  │ ──────────────► │                  │
│  CSV/Excel  │                  │  Import Pipeline │
│  (future)   │                  │                  │
└─────────────┘                  └────────┬─────────┘
                                          │ ImportedEntry (raw)
                                          ▼
                                 ┌──────────────────┐
                                 │  Mapping Engine  │
                                 │  (rule eval)     │
                                 └────────┬─────────┘
                          ┌───────────────┴───────────────┐
                          │                               │
                   Matched ▼                    Unmatched ▼
          ┌──────────────────────┐      ┌──────────────────────┐
          │   MappedEntry        │      │  Dashboard (UI)       │
          │   → Submit to        │      │  Manual mapping or    │
          │     Timelog API      │      │  rule creation        │
          └──────────────────────┘      └──────────────────────┘
```

---

## UI Pages

| Page | Purpose |
|---|---|
| **Dashboard** | Unmapped entries requiring attention |
| **Import History** | Log of all import runs, file uploads, errors |
| **Mapping Rules** | CRUD for mapping rules, with test/preview |
| **Sources** | Configure Tempo API connections |
| **Timelog Sync** | View synced projects/tasks, trigger manual sync |
| **Settings** | API keys, schedules, defaults |

---

## External API Integrations

### Tempo (Jira-based time tracker)
- REST API: `GET /worklogs` with date range filters
- Custom fields accessible on each worklog's linked Jira issue
- Auth: Bearer token

### Timelog.com
- REST API for projects, tasks, and hour registration
- Auth: API key
- Operations needed:
  - `GET /projects` + `GET /tasks` (for sync)
  - `POST /hours` (submit time entry)

---

## Background Jobs (Hangfire)

| Job | Schedule | Description |
|---|---|---|
| `PullTempoWorklogs` | Daily (configurable) | Fetches yesterday's worklogs from all active Tempo sources |
| `SubmitMappedEntries` | Daily after pull | Submits all newly mapped entries to Timelog |
| `SyncTimelogData` | Daily | Refreshes project/task list from Timelog |

---

## Testing Strategy

- **Unit tests**: Domain logic, mapping engine rule evaluation
- **Integration tests**: EF Core repositories (Testcontainers for SQL Server), API client parsing
- **Component tests**: Blazor UI components (bUnit)
- **End-to-end**: GitHub Actions workflow validates the full build + tests on every PR

---

## GitHub Actions CI Pipeline

```yaml
on: [push, pull_request]
jobs:
  build-and-test:
    - dotnet build
    - dotnet test (with Testcontainers SQL Server)
    - Report test results
```

---

## Milestones

| # | Milestone | Description |
|---|---|---|
| 1 | **Foundation** | Solution structure, domain model, EF Core, DB migrations |
| 2 | **Timelog Integration** | Timelog API client, project/task sync |
| 3 | **Tempo Import** | Tempo API client, daily job, raw entry storage |
| 4 | **Mapping Engine** | Rule model, evaluation engine, priority ordering |
| 5 | **Blazor UI** | Dashboard, mapping rules UI, import history |
| 6 | **File Import** | CSV and Excel upload + parsing |
| 7 | **Submission Pipeline** | Map + submit job, audit trail |
| 8 | **Testing & CI** | Full test suite, GitHub Actions |
| 9 | **Polish** | Error handling, logging, settings page, docs |
