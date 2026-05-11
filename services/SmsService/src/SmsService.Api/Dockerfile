# Multi-stage build cho SmsService (consumer-only).

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["services/SmsService/src/SmsService.Api/SmsService.Api.csproj", "services/SmsService/src/SmsService.Api/"]
COPY ["services/SmsService/src/SmsService.Infrastructure/SmsService.Infrastructure.csproj", "services/SmsService/src/SmsService.Infrastructure/"]
COPY ["shared/src/SharedContracts/SharedContracts.csproj", "shared/src/SharedContracts/"]
COPY ["shared/src/SharedInfrastructure/SharedInfrastructure.csproj", "shared/src/SharedInfrastructure/"]
COPY ["shared/src/SharedKernels/SharedKernels.csproj", "shared/src/SharedKernels/"]

RUN dotnet restore "services/SmsService/src/SmsService.Api/SmsService.Api.csproj"

COPY services/SmsService services/SmsService
COPY shared shared
WORKDIR /src/services/SmsService/src/SmsService.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "SmsService.Api.dll"]
