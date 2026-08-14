# Multi-stage build cho EmailService (consumer-only, không có Application/Domain layer).

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["services/EmailService/src/EmailService.Api/EmailService.Api.csproj", "services/EmailService/src/EmailService.Api/"]
COPY ["services/EmailService/src/EmailService.Infrastructure/EmailService.Infrastructure.csproj", "services/EmailService/src/EmailService.Infrastructure/"]
COPY ["shared/src/SharedContracts/SharedContracts.csproj", "shared/src/SharedContracts/"]
COPY ["shared/src/SharedInfrastructure/SharedInfrastructure.csproj", "shared/src/SharedInfrastructure/"]
COPY ["shared/src/SharedKernels/SharedKernels.csproj", "shared/src/SharedKernels/"]

RUN dotnet restore "services/EmailService/src/EmailService.Api/EmailService.Api.csproj"

COPY services/EmailService services/EmailService
COPY shared shared
WORKDIR /src/services/EmailService/src/EmailService.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build --chown=10001:10001 /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

USER 10001:10001

ENTRYPOINT ["dotnet", "EmailService.Api.dll"]
