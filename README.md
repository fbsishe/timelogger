# TimeLogger

A .NET Blazor web application that aggregates time entries from third-party systems (Tempo, CSV/Excel) and submits them to [Timelog.com](https://timelog.com) via API.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for local SQL Server)

## Quick Start

### 1. Start SQL Server

```bash
docker-compose up sqlserver -d
```

### 2. Apply database migrations

```bash
dotnet ef database update \
  --project src/TimeLogger.Infrastructure \
  --startup-project src/TimeLogger.Web
```

### 3. Configure API keys

Copy `appsettings.json` and fill in credentials (use User Secrets for local dev):

```bash
cd src/TimeLogger.Web
dotnet user-secrets set "Timelog:ApiKey" "your-timelog-api-key"
dotnet user-secrets set "Tempo:ApiToken" "your-tempo-token"
```

### 4. Run the app

```bash
dotnet run --project src/TimeLogger.Web
```

## Solution Structure

```
src/
  TimeLogger.Domain/          # Entities, enums — no dependencies
  TimeLogger.Application/     # Use cases, service interfaces
  TimeLogger.Infrastructure/  # EF Core, API clients, file parsers
  TimeLogger.Web/             # Blazor Server UI
  TimeLogger.Worker/          # Hangfire background jobs
tests/
  TimeLogger.Application.Tests/
  TimeLogger.Infrastructure.Tests/
  TimeLogger.Web.Tests/
```

## Running Tests

```bash
dotnet test
```

## Architecture

See [PLAN.md](PLAN.md) for the full architecture and implementation plan.
