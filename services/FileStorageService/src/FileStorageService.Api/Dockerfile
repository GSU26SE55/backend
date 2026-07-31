# Multi-stage build cho FileStorageService.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["services/FileStorageService/src/FileStorageService.Api/FileStorageService.Api.csproj", "services/FileStorageService/src/FileStorageService.Api/"]
COPY ["services/FileStorageService/src/FileStorageService.Application/FileStorageService.Application.csproj", "services/FileStorageService/src/FileStorageService.Application/"]
COPY ["services/FileStorageService/src/FileStorageService.Infrastructure/FileStorageService.Infrastructure.csproj", "services/FileStorageService/src/FileStorageService.Infrastructure/"]
COPY ["shared/src/SharedContracts/SharedContracts.csproj", "shared/src/SharedContracts/"]
COPY ["shared/src/SharedInfrastructure/SharedInfrastructure.csproj", "shared/src/SharedInfrastructure/"]
COPY ["shared/src/SharedKernels/SharedKernels.csproj", "shared/src/SharedKernels/"]

RUN dotnet restore "services/FileStorageService/src/FileStorageService.Api/FileStorageService.Api.csproj"

COPY services/FileStorageService services/FileStorageService
COPY shared shared
WORKDIR /src/services/FileStorageService/src/FileStorageService.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "FileStorageService.Api.dll"]
