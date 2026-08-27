#!/bin/sh
for p in \
  services/AuthService/tests/AuthService.UnitTests/AuthService.UnitTests.csproj \
  services/BatteryService/tests/BatteryService.UnitTests/BatteryService.UnitTests.csproj \
  services/TicketService/tests/TicketService.UnitTests/TicketService.UnitTests.csproj \
  services/NotificationService/tests/NotificationService.UnitTests/NotificationService.UnitTests.csproj \
  services/SmsService/tests/SmsService.UnitTests/SmsService.UnitTests.csproj \
  services/EmailService/tests/EmailService.UnitTests/EmailService.UnitTests.csproj \
  services/FileStorageService/tests/FileStorageService.UnitTests/FileStorageService.UnitTests.csproj \
  services/ApiGateway/tests/ApiGateway.UnitTests/ApiGateway.UnitTests.csproj \
  shared/tests/SharedInfrastructure.UnitTests/SharedInfrastructure.UnitTests.csproj
do
  name=$(basename "$p" .csproj)
  echo "########## $name ##########"
  dotnet test "$p" --collect:"XPlat Code Coverage" --results-directory /src/CoverageResults/$name || echo "COV_FAIL $name"
done
