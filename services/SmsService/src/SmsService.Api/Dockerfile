# Multi-stage build cho SmsService — SMS Gateway Hub (Sprint SMS).
# Build context PHẢI là repo root để truy cập shared/ projects.
# Lệnh build: docker build -f services/SmsService/src/SmsService.Api/Dockerfile -t sms-service .
# Hoặc: docker compose build smsservice (đã set context: . trong docker-compose.yml)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj trước để tận dụng Docker layer cache cho dotnet restore.
# 4 SmsService projects (Domain → Application → Infrastructure → Api) + 3 shared.
COPY ["services/SmsService/src/SmsService.Api/SmsService.Api.csproj", "services/SmsService/src/SmsService.Api/"]
COPY ["services/SmsService/src/SmsService.Application/SmsService.Application.csproj", "services/SmsService/src/SmsService.Application/"]
COPY ["services/SmsService/src/SmsService.Domain/SmsService.Domain.csproj", "services/SmsService/src/SmsService.Domain/"]
COPY ["services/SmsService/src/SmsService.Infrastructure/SmsService.Infrastructure.csproj", "services/SmsService/src/SmsService.Infrastructure/"]
COPY ["shared/src/SharedContracts/SharedContracts.csproj", "shared/src/SharedContracts/"]
COPY ["shared/src/SharedInfrastructure/SharedInfrastructure.csproj", "shared/src/SharedInfrastructure/"]
COPY ["shared/src/SharedKernels/SharedKernels.csproj", "shared/src/SharedKernels/"]

RUN dotnet restore "services/SmsService/src/SmsService.Api/SmsService.Api.csproj"

COPY services/SmsService services/SmsService
COPY shared shared
WORKDIR /src/services/SmsService/src/SmsService.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build --chown=10001:10001 /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

USER 10001:10001

ENTRYPOINT ["dotnet", "SmsService.Api.dll"]
