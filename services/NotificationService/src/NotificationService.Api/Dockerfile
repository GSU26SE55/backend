# Multi-stage build cho NotificationService.
# Build context PHẢI là repo root để truy cập shared/ projects.
# Lệnh build: docker build -f services/NotificationService/src/NotificationService.Api/Dockerfile -t notification-service .
# Hoặc: docker compose build notificationservice

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["services/NotificationService/src/NotificationService.Api/NotificationService.Api.csproj", "services/NotificationService/src/NotificationService.Api/"]
COPY ["services/NotificationService/src/NotificationService.Application/NotificationService.Application.csproj", "services/NotificationService/src/NotificationService.Application/"]
COPY ["services/NotificationService/src/NotificationService.Domain/NotificationService.Domain.csproj", "services/NotificationService/src/NotificationService.Domain/"]
COPY ["services/NotificationService/src/NotificationService.Infrastructure/NotificationService.Infrastructure.csproj", "services/NotificationService/src/NotificationService.Infrastructure/"]
COPY ["shared/src/SharedContracts/SharedContracts.csproj", "shared/src/SharedContracts/"]
COPY ["shared/src/SharedInfrastructure/SharedInfrastructure.csproj", "shared/src/SharedInfrastructure/"]
COPY ["shared/src/SharedKernels/SharedKernels.csproj", "shared/src/SharedKernels/"]

RUN dotnet restore "services/NotificationService/src/NotificationService.Api/NotificationService.Api.csproj"

COPY services/NotificationService services/NotificationService
COPY shared shared
WORKDIR /src/services/NotificationService/src/NotificationService.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build --chown=10001:10001 /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

USER 10001:10001

ENTRYPOINT ["dotnet", "NotificationService.Api.dll"]
