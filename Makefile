# =====================================================================
# Solar Battery Maintenance — Backend Makefile
# =====================================================================
# Usage:
#   make help                       # liệt kê tất cả target
#   make build                      # build toàn solution
#   make test                       # chạy test toàn solution
#   make run SVC=BatteryService     # chạy 1 service
#   make migration-add SVC=BatteryService NAME=AddBatteryStatus
#   make docker-up                  # khởi động stack docker-compose
# =====================================================================

SHELL := /bin/bash
.DEFAULT_GOAL := help

SLN          := SolarBatteryMaintainance.slnx
SERVICES_DIR := services
COMPOSE      := docker compose
ENV_FILE     := .env.Docker

# Danh sách services (dùng cho run-all, build-all)
SERVICES := ApiGateway AuthService BatteryService TicketService EmailService SmsService FileStorageService

# Service mặc định cho các lệnh cần SVC=...
SVC ?=
NAME ?=

# Đường dẫn project chuẩn cho 1 service
SVC_API   = $(SERVICES_DIR)/$(SVC)/src/$(SVC).Api
SVC_INFRA = $(SERVICES_DIR)/$(SVC)/src/$(SVC).Infrastructure

# ---------------------------------------------------------------------
# Help
# ---------------------------------------------------------------------
.PHONY: help
help: ## Hiển thị danh sách target
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
		| awk 'BEGIN{FS=":.*?## "}{printf "  \033[36m%-26s\033[0m %s\n", $$1, $$2}'

# ---------------------------------------------------------------------
# .NET — solution-wide
# ---------------------------------------------------------------------
.PHONY: restore build clean rebuild format
restore: ## dotnet restore toàn solution
	dotnet restore $(SLN)

build: ## dotnet build toàn solution (Debug)
	dotnet build $(SLN) --no-restore

rebuild: clean restore build ## clean + restore + build

clean: ## dotnet clean + xoá bin/obj
	dotnet clean $(SLN)
	find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

format: ## dotnet format toàn solution
	dotnet format $(SLN)

# ---------------------------------------------------------------------
# Test
# ---------------------------------------------------------------------
.PHONY: test test-coverage test-svc
test: ## Chạy tất cả test
	dotnet test $(SLN) --no-build --verbosity minimal

test-coverage: ## Test + coverage (XPlat Code Coverage)
	dotnet test $(SLN) --collect:"XPlat Code Coverage" --verbosity minimal

test-svc: _require-svc ## Test 1 service (SVC=BatteryService)
	dotnet test $(SERVICES_DIR)/$(SVC)/$(SVC).slnx --verbosity minimal

# ---------------------------------------------------------------------
# Run service (dotnet run)
# ---------------------------------------------------------------------
.PHONY: run watch run-all run-all-stop
run: _require-svc ## Chạy 1 service (SVC=BatteryService)
	dotnet run --project $(SVC_API)

watch: _require-svc ## dotnet watch run (SVC=BatteryService)
	dotnet watch --project $(SVC_API) run

# Đường dẫn project cho run-all: ApiGateway nằm tại src/ApiGateway, còn lại tại src/<Name>.Api
define _svc_project
$(if $(filter ApiGateway,$(1)),$(SERVICES_DIR)/$(1)/src/$(1),$(SERVICES_DIR)/$(1)/src/$(1).Api)
endef

run-all: ## Chạy song song tất cả services + ApiGateway (logs/run-all/*.log, Ctrl+C để dừng)
	@mkdir -p logs/run-all
	@echo "Building solution trước khi chạy..."
	@dotnet build $(SLN) --verbosity minimal
	@echo "Khởi động: $(SERVICES)"
	@pids=""; \
	trap 'echo; echo "Dừng tất cả services..."; kill $$pids 2>/dev/null; wait 2>/dev/null; exit 0' INT TERM; \
	for svc in $(SERVICES); do \
		if [ "$$svc" = "ApiGateway" ]; then proj=$(SERVICES_DIR)/$$svc/src/$$svc; \
		else proj=$(SERVICES_DIR)/$$svc/src/$$svc.Api; fi; \
		log="logs/run-all/$$svc.log"; \
		if grep -q '"https"' "$$proj/Properties/launchSettings.json" 2>/dev/null; then \
			profile="https"; \
		else \
			profile="http"; \
			echo "  ! $$svc không có profile https → fallback http"; \
		fi; \
		echo "  → $$svc  ($$profile, log: $$log)"; \
		dotnet run --no-build --project $$proj --launch-profile $$profile > "$$log" 2>&1 & \
		pids="$$pids $$!"; \
	done; \
	echo "PIDs:$$pids"; \
	echo "Tail logs: tail -f logs/run-all/*.log"; \
	echo "Đợi ApiGateway sẵn sàng..."; \
	gw_log="logs/run-all/ApiGateway.log"; \
	url=""; \
	for i in $$(seq 1 60); do \
		if [ -f "$$gw_log" ]; then \
			url=$$(grep -Eo 'Now listening on: https://[^[:space:]]+' "$$gw_log" | head -1 | sed 's/Now listening on: //'); \
			[ -n "$$url" ] && break; \
		fi; \
		sleep 1; \
	done; \
	if [ -z "$$url" ]; then url="https://localhost:5001"; fi; \
	printf '\n\033[1;32m==================================================\n'; \
	printf '  ApiGateway Swagger:  %s/swagger\n' "$$url"; \
	printf '==================================================\033[0m\n\n'; \
	wait

run-all-stop: ## Kill mọi tiến trình dotnet đang chạy services (best-effort)
	@pkill -f "dotnet run --project $(SERVICES_DIR)/" && echo "Đã kill các dotnet run." || echo "Không có tiến trình nào để kill."

# ---------------------------------------------------------------------
# EF Core migrations
# ---------------------------------------------------------------------
.PHONY: migration-add migration-update migration-remove migration-list migration-rollback-test
migration-add: _require-svc _require-name ## Thêm migration (SVC=... NAME=...)
	cd $(SVC_API) && dotnet ef migrations add $(NAME) -p ../$(SVC).Infrastructure -s .

migration-update: _require-svc ## Apply migrations lên DB (SVC=...)
	cd $(SVC_API) && dotnet ef database update -p ../$(SVC).Infrastructure -s .

migration-remove: _require-svc ## Xoá migration cuối (SVC=...)
	cd $(SVC_API) && dotnet ef migrations remove -p ../$(SVC).Infrastructure -s .

migration-list: _require-svc ## Liệt kê migrations (SVC=...)
	cd $(SVC_API) && dotnet ef migrations list -p ../$(SVC).Infrastructure -s .

migration-rollback-test: _require-svc _require-name ## Test rollback về NAME rồi apply lại
	cd $(SVC_API) && dotnet ef database update $(NAME) -p ../$(SVC).Infrastructure -s . \
		&& dotnet ef database update -p ../$(SVC).Infrastructure -s .

# ---------------------------------------------------------------------
# Docker compose
# ---------------------------------------------------------------------
.PHONY: docker-up docker-down docker-build docker-restart docker-logs docker-ps docker-clean
docker-up: ## docker compose up -d (build nếu cần)
	$(COMPOSE) --env-file $(ENV_FILE) up -d --build

docker-down: ## docker compose down
	$(COMPOSE) down

docker-build: ## docker compose build (no cache nếu NOCACHE=1)
	$(COMPOSE) --env-file $(ENV_FILE) build $(if $(NOCACHE),--no-cache,)

docker-restart: ## Restart 1 service trong stack (SVC=...)
	$(COMPOSE) restart $(SVC)

docker-logs: ## Logs follow (SVC=... để filter; bỏ trống = all)
	$(COMPOSE) logs -f --tail=200 $(SVC)

docker-ps: ## Liệt kê container
	$(COMPOSE) ps

docker-clean: ## down + xoá volumes (DESTRUCTIVE — xác nhận trước)
	@read -p "Xoá toàn bộ volumes (postgres, rabbit, minio, ...)? [y/N] " ans && [ "$$ans" = "y" ]
	$(COMPOSE) down -v

# ---------------------------------------------------------------------
# Helpers (internal)
# ---------------------------------------------------------------------
.PHONY: _require-svc _require-name
_require-svc:
	@if [ -z "$(SVC)" ]; then \
		echo "ERROR: thiếu SVC=... (vd: SVC=BatteryService)"; \
		echo "Available: $(SERVICES)"; \
		exit 1; \
	fi
	@if [ ! -d "$(SERVICES_DIR)/$(SVC)" ]; then \
		echo "ERROR: service '$(SVC)' không tồn tại tại $(SERVICES_DIR)/$(SVC)"; \
		exit 1; \
	fi

_require-name:
	@if [ -z "$(NAME)" ]; then \
		echo "ERROR: thiếu NAME=... (vd: NAME=AddBatteryStatus)"; \
		exit 1; \
	fi
