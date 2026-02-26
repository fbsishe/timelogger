# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for layer-cached restore
COPY TimeLogger.sln .
COPY src/TimeLogger.Domain/TimeLogger.Domain.csproj src/TimeLogger.Domain/
COPY src/TimeLogger.Application/TimeLogger.Application.csproj src/TimeLogger.Application/
COPY src/TimeLogger.Infrastructure/TimeLogger.Infrastructure.csproj src/TimeLogger.Infrastructure/
COPY src/TimeLogger.Worker/TimeLogger.Worker.csproj src/TimeLogger.Worker/
COPY src/TimeLogger.Web/TimeLogger.Web.csproj src/TimeLogger.Web/
COPY tests/ tests/

RUN dotnet restore src/TimeLogger.Web/TimeLogger.Web.csproj

# Copy source and publish
COPY src/ src/
RUN dotnet publish src/TimeLogger.Web/TimeLogger.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "TimeLogger.Web.dll"]
