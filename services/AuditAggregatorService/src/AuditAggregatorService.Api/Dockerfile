# Multi-stage build cho AuditAggregatorService (Sprint audit #AUDIT-13).
# Build context PHẢI là repo root để truy cập shared/ projects.
# Lệnh build: docker compose build auditaggregatorservice (đã set context: . trong docker-compose.yml)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj trước để tận dụng Docker layer cache cho dotnet restore
COPY ["services/AuditAggregatorService/src/AuditAggregatorService.Api/AuditAggregatorService.Api.csproj", "services/AuditAggregatorService/src/AuditAggregatorService.Api/"]
COPY ["services/AuditAggregatorService/src/AuditAggregatorService.Application/AuditAggregatorService.Application.csproj", "services/AuditAggregatorService/src/AuditAggregatorService.Application/"]
COPY ["services/AuditAggregatorService/src/AuditAggregatorService.Domain/AuditAggregatorService.Domain.csproj", "services/AuditAggregatorService/src/AuditAggregatorService.Domain/"]
COPY ["services/AuditAggregatorService/src/AuditAggregatorService.Infrastructure/AuditAggregatorService.Infrastructure.csproj", "services/AuditAggregatorService/src/AuditAggregatorService.Infrastructure/"]
COPY ["shared/src/SharedContracts/SharedContracts.csproj", "shared/src/SharedContracts/"]
COPY ["shared/src/SharedInfrastructure/SharedInfrastructure.csproj", "shared/src/SharedInfrastructure/"]
COPY ["shared/src/SharedKernels/SharedKernels.csproj", "shared/src/SharedKernels/"]

RUN dotnet restore "services/AuditAggregatorService/src/AuditAggregatorService.Api/AuditAggregatorService.Api.csproj"

# Copy toàn bộ source rồi publish
COPY services/AuditAggregatorService services/AuditAggregatorService
COPY shared shared
WORKDIR /src/services/AuditAggregatorService/src/AuditAggregatorService.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "AuditAggregatorService.Api.dll"]
