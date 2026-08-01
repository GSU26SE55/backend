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
#   make ci                         # chạy full CI local trước khi push
#   make ci-fast                    # CI nhanh (bỏ format + trivy)
#   make ci-full                    # CI + integration tests (cần Docker)
# =====================================================================

SHELL := /bin/bash
.DEFAULT_GOAL := help

SLN          := SolarBatteryMaintainance.slnx
SERVICES_DIR := services
COMPOSE      := docker compose
ENV_FILE     := .env.Docker

# Danh sách services (dùng cho run-all, build-all)
SERVICES := ApiGateway AuthService BatteryService TicketService NotificationService EmailService SmsService FileStorageService

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

test-perf: ## Chạy riêng test Category=Performance (YÊU CẦU máy rảnh — xem ghi chú)
	@printf '\n\033[1;34m[+] Performance tests (tuần tự, 1 assembly/lượt)\033[0m\n'
	@printf 'Các test này đo tài nguyên máy nên bị loại khỏi ci-test/ci-integration.\n'
	@printf '⚠️  Chạy trên máy RẢNH. Đo 2026-07-31: máy rảnh p99 ~6ms, pass 3/3 lần;\n'
	@printf '    chạy song song cả solution p99 = 389ms (ngưỡng 50ms) => đỏ giả.\n'
	@printf '    Đợi 10s cho tiến trình/container của lệnh trước teardown xong...\n'
	@sleep 10
	dotnet test services/AuthService/tests/AuthService.UnitTests/AuthService.UnitTests.csproj \
		--no-build --filter "Category=Performance" --verbosity minimal
	@docker info >/dev/null 2>&1 || { echo "SKIP: AuditThroughputChaosTests cần Docker."; exit 0; }
	@sleep 5
	dotnet test services/AuditAggregatorService/tests/AuditAggregatorService.IntegrationTests/AuditAggregatorService.IntegrationTests.csproj \
		--no-build --filter "Category=Performance" --verbosity minimal

# ---------------------------------------------------------------------
# CI — chạy local trước khi push (mirror Jenkinsfile stage 0-7)
# ---------------------------------------------------------------------
# Biến điều khiển:
#   BASE_REF=origin/main     # ref so sánh cho rule-checks (default: origin/dev)
#   SKIP_TRIVY=1             # bỏ qua security scan
#   REQUIRE_TRIVY=1          # ép preflight fail nếu chưa cài trivy
.PHONY: ci ci-fast ci-full ci-preflight ci-format ci-build ci-test ci-rules ci-trivy ci-integration

BASE_REF ?= origin/dev

ci: ci-preflight ci-format ci-build ci-test ci-rules ci-trivy ## Full local CI (preflight → format → build → unit test → rules → trivy)
	@printf '\n\033[1;32m========== CI PASS ==========\033[0m\n'

ci-fast: ci-preflight ci-build ci-test ci-rules ## CI nhanh (bỏ format + trivy) — cho vòng lặp dev
	@printf '\n\033[1;32m========== CI-FAST PASS ==========\033[0m\n'

ci-full: ci ci-integration ## CI + integration tests (cần Docker)
	@printf '\n\033[1;32m========== CI-FULL PASS ==========\033[0m\n'

ci-preflight: ## [stage 0] Check tool versions (git, dotnet ≥ 9, trivy optional)
	@printf '\n\033[1;34m[1/6] Preflight\033[0m\n'
	@./ci/scripts/ci-preflight.sh

ci-format: ## [stage 2] dotnet format --verify-no-changes
	@printf '\n\033[1;34m[2/6] Format check\033[0m\n'
	dotnet format $(SLN) --verify-no-changes --severity error

ci-build: ## [stage 1+3] dotnet restore + build Release
	@printf '\n\033[1;34m[3/6] Restore + Build (Release)\033[0m\n'
	dotnet restore $(SLN)
	dotnet build $(SLN) -c Release --no-restore

ci-test: ## [stage 4] Unit tests (exclude IntegrationTests + Performance)
	@printf '\n\033[1;34m[4/6] Unit tests\033[0m\n'
	@mkdir -p TestResults
	# Performance tests (PermissionResolverPerfTests) skip trong CI vì multi-project parallel
	# saturate CPU → p99 spike vượt 50ms threshold (test pass isolated với p99~6ms). Run local:
	#   dotnet test services/AuthService/tests/AuthService.UnitTests --filter "Category=Performance"
	dotnet test $(SLN) -c Release --no-build \
		--filter "FullyQualifiedName!~IntegrationTests&Category!=Performance" \
		--logger "trx" \
		--results-directory ./TestResults

ci-rules: ## [stage 5] Project rule checks (await void / AuditableEntity / audit conventions #AUDIT-04) — diff vs BASE_REF
	@printf '\n\033[1;34m[5/6] Project rule checks (BASE_REF=$(BASE_REF))\033[0m\n'
	@BASE_REF=$(BASE_REF) ./ci/scripts/rule-checks.sh

ci-trivy: ## [stage 6] Trivy fs scan (CRITICAL, ignore-unfixed) — SKIP_TRIVY=1 để bỏ qua
	@printf '\n\033[1;34m[6/6] Security scan (trivy)\033[0m\n'
	@./ci/scripts/trivy-scan.sh

ci-integration: ## [stage 7] Integration tests (cần Docker daemon)
	@printf '\n\033[1;34m[+] Integration tests\033[0m\n'
	@docker info >/dev/null 2>&1 || { echo "FAIL: Docker daemon không chạy."; exit 1; }
	@mkdir -p TestResults
	# AuditThroughputChaosTests skip: đo throughput/kết nối đồng thời, đỏ giả khi chạy song song
	# cả solution (428 ev/s vs 1000; Npgsql read-timeout khi mở 3200 connection). Run: make test-perf
	dotnet test $(SLN) -c Release --no-build \
		--filter "FullyQualifiedName~IntegrationTests&Category!=Performance" \
		--logger "trx" \
		--results-directory ./TestResults

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
# Monitoring
# ---------------------------------------------------------------------
ALERT_RULES_SRC  := monitoring/prometheus/alert-rules.yml
ALERT_RULES_HELM := deploy/helm/solar-battery/files/alert-rules.yml

.PHONY: sync-alert-rules check-alert-rules

sync-alert-rules: ## Đồng bộ alert rules sang chart Helm (chạy sau khi sửa alert-rules.yml)
	@cp $(ALERT_RULES_SRC) $(ALERT_RULES_HELM)
	@echo "Đã đồng bộ $(ALERT_RULES_SRC) -> $(ALERT_RULES_HELM) ($$(grep -c '^\s*- alert:' $(ALERT_RULES_SRC)) alert)."

check-alert-rules: ## Kiểm 2 bản alert rules còn khớp nhau (CI cũng chạy luật này)
	@if diff -q $(ALERT_RULES_SRC) $(ALERT_RULES_HELM) >/dev/null 2>&1; then \
		echo "PASS: alert rules đồng bộ."; \
	else \
		echo "FAIL: $(ALERT_RULES_SRC) và $(ALERT_RULES_HELM) đã lệch nhau."; \
		diff $(ALERT_RULES_SRC) $(ALERT_RULES_HELM) || true; \
		echo "Chạy: make sync-alert-rules"; \
		exit 1; \
	fi

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
