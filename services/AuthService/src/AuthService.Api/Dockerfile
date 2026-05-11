# Multi-stage build cho AuthService.
# Build context PHẢI là repo root để truy cập shared/ projects.
# Lệnh build: docker build -f services/AuthService/src/AuthService.Api/Dockerfile -t auth-service .
# Hoặc: docker compose build authservice (đã set context: . trong docker-compose.yml)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj trước để tận dụng Docker layer cache cho dotnet restore
COPY ["services/AuthService/src/AuthService.Api/AuthService.Api.csproj", "services/AuthService/src/AuthService.Api/"]
COPY ["services/AuthService/src/AuthService.Application/AuthService.Application.csproj", "services/AuthService/src/AuthService.Application/"]
COPY ["services/AuthService/src/AuthService.Domain/AuthService.Domain.csproj", "services/AuthService/src/AuthService.Domain/"]
COPY ["services/AuthService/src/AuthService.Infrastructure/AuthService.Infrastructure.csproj", "services/AuthService/src/AuthService.Infrastructure/"]
COPY ["shared/src/SharedContracts/SharedContracts.csproj", "shared/src/SharedContracts/"]
COPY ["shared/src/SharedInfrastructure/SharedInfrastructure.csproj", "shared/src/SharedInfrastructure/"]
COPY ["shared/src/SharedKernels/SharedKernels.csproj", "shared/src/SharedKernels/"]

RUN dotnet restore "services/AuthService/src/AuthService.Api/AuthService.Api.csproj"

# Copy toàn bộ source rồi publish
COPY services/AuthService services/AuthService
COPY shared shared
WORKDIR /src/services/AuthService/src/AuthService.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "AuthService.Api.dll"]
