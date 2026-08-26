#!/bin/sh
mv /src/global.json /tmp/gj.bak 2>/dev/null
for p in \
  services/AuthService/tests/AuthService.IntegrationTests/AuthService.IntegrationTests.csproj \
  services/BatteryService/tests/BatteryService.IntegrationTests/BatteryService.IntegrationTests.csproj \
  services/TicketService/tests/TicketService.IntegrationTests/TicketService.IntegrationTests.csproj \
  services/NotificationService/tests/NotificationService.IntegrationTests/NotificationService.IntegrationTests.csproj \
  services/SmsService/tests/SmsService.IntegrationTests/SmsService.IntegrationTests.csproj \
  services/EmailService/tests/EmailService.IntegrationTests/EmailService.IntegrationTests.csproj \
  services/FileStorageService/tests/FileStorageService.IntegrationTests/FileStorageService.IntegrationTests.csproj \
  services/AuditAggregatorService/tests/AuditAggregatorService.IntegrationTests/AuditAggregatorService.IntegrationTests.csproj \
  shared/tests/SharedInfrastructure.IntegrationTests/SharedInfrastructure.IntegrationTests.csproj
do
  name=$(basename "$p" .csproj)
  echo "########## $name ##########"
  dotnet test "$p" --logger "trx;LogFileName=$name.trx" --results-directory /src/IntegrationResults || echo "EXIT_NONZERO $name"
done
mv /tmp/gj.bak /src/global.json 2>/dev/null
