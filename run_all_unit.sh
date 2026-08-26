#!/bin/sh
mv /src/global.json /tmp/gj.bak 2>/dev/null
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
  dotnet test "$p" --logger "trx;LogFileName=$name.trx" --results-directory /src/TestResults || echo "EXIT_NONZERO $name"
done
mv /tmp/gj.bak /src/global.json 2>/dev/null
