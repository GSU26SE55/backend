# Multi-stage build cho BatteryService.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["services/BatteryService/src/BatteryService.Api/BatteryService.Api.csproj", "services/BatteryService/src/BatteryService.Api/"]
COPY ["services/BatteryService/src/BatteryService.Application/BatteryService.Application.csproj", "services/BatteryService/src/BatteryService.Application/"]
COPY ["services/BatteryService/src/BatteryService.Domain/BatteryService.Domain.csproj", "services/BatteryService/src/BatteryService.Domain/"]
COPY ["services/BatteryService/src/BatteryService.Infrastructure/BatteryService.Infrastructure.csproj", "services/BatteryService/src/BatteryService.Infrastructure/"]
COPY ["shared/src/SharedContracts/SharedContracts.csproj", "shared/src/SharedContracts/"]
COPY ["shared/src/SharedInfrastructure/SharedInfrastructure.csproj", "shared/src/SharedInfrastructure/"]
COPY ["shared/src/SharedKernels/SharedKernels.csproj", "shared/src/SharedKernels/"]

RUN dotnet restore "services/BatteryService/src/BatteryService.Api/BatteryService.Api.csproj"

COPY services/BatteryService services/BatteryService
COPY shared shared
WORKDIR /src/services/BatteryService/src/BatteryService.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
# GH-verify-sensor-grpc — gRPC server (HTTP/2) nội bộ solar-net.
EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "BatteryService.Api.dll"]
