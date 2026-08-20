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
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build --chown=10001:10001 /app/publish .

# Thư mục khoá Data Protection phải tồn tại SẴN trong ảnh và thuộc về đúng UID chạy ứng dụng.
# Docker chỉ sao chép quyền sở hữu từ ảnh sang volume có tên vào LẦN GẮN ĐẦU TIÊN, và chỉ khi
# đường dẫn đó đã có trong ảnh. Thiếu dòng này thì Docker tự tạo thư mục thuộc root, còn tiến
# trình chạy bằng UID 10001 không ghi được — service chết ngay lúc khởi động với thông báo
# "Cannot write to '/app/keys'", và triệu chứng là container lặp vô hạn.
RUN mkdir -p /app/keys && chown -R 10001:10001 /app/keys

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

USER 10001:10001

ENTRYPOINT ["dotnet", "AuthService.Api.dll"]
