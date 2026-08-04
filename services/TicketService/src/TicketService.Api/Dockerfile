# Multi-stage build cho TicketService.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["services/TicketService/src/TicketService.Api/TicketService.Api.csproj", "services/TicketService/src/TicketService.Api/"]
COPY ["services/TicketService/src/TicketService.Application/TicketService.Application.csproj", "services/TicketService/src/TicketService.Application/"]
COPY ["services/TicketService/src/TicketService.Domain/TicketService.Domain.csproj", "services/TicketService/src/TicketService.Domain/"]
COPY ["services/TicketService/src/TicketService.Infrastructure/TicketService.Infrastructure.csproj", "services/TicketService/src/TicketService.Infrastructure/"]
COPY ["shared/src/SharedContracts/SharedContracts.csproj", "shared/src/SharedContracts/"]
COPY ["shared/src/SharedInfrastructure/SharedInfrastructure.csproj", "shared/src/SharedInfrastructure/"]
COPY ["shared/src/SharedKernels/SharedKernels.csproj", "shared/src/SharedKernels/"]

RUN dotnet restore "services/TicketService/src/TicketService.Api/TicketService.Api.csproj"

COPY services/TicketService services/TicketService
COPY shared shared
WORKDIR /src/services/TicketService/src/TicketService.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# ffmpeg — chuẩn hóa voice message (.webm từ web) về m4a để iOS phát được (FFMpegCore gọi binary này)
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "TicketService.Api.dll"]
