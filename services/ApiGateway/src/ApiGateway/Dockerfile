# Multi-stage build cho ApiGateway (YARP reverse proxy).
# HTTP only — production đặt reverse proxy (nginx/Caddy/Traefik) phía trước cho HTTPS.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["services/ApiGateway/src/ApiGateway/ApiGateway.csproj", "services/ApiGateway/src/ApiGateway/"]
COPY ["shared/src/SharedContracts/SharedContracts.csproj", "shared/src/SharedContracts/"]
COPY ["shared/src/SharedInfrastructure/SharedInfrastructure.csproj", "shared/src/SharedInfrastructure/"]
COPY ["shared/src/SharedKernels/SharedKernels.csproj", "shared/src/SharedKernels/"]

RUN dotnet restore "services/ApiGateway/src/ApiGateway/ApiGateway.csproj"

COPY services/ApiGateway services/ApiGateway
COPY shared shared
WORKDIR /src/services/ApiGateway/src/ApiGateway
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "ApiGateway.dll"]
