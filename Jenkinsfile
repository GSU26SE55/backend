// =====================================================================
// Solar Battery - CI/CD Pipeline (Jenkins + VPS + k3s + Helm)
//
// CI runs on dev, staging, main and pull requests.
// CD runs only when pushing to staging.
//
// Jenkins credential required:
//   GHCR_TOKEN: Secret text, GitHub PAT with write:packages
//
// VPS tools required:
//   dotnet-sdk-9.0, docker, trivy, k3s/kubectl, helm, curl
// =====================================================================

pipeline {
  agent any

  options {
    timeout(time: 120, unit: 'MINUTES')
    buildDiscarder(logRotator(numToKeepStr: '10'))
    disableConcurrentBuilds()
    timestamps()
  }

  triggers {
    githubPush()
  }

  environment {
    REGISTRY = 'ghcr.io/gsu26se55'
    SHA = sh(script: 'git rev-parse --short HEAD', returnStdout: true).trim()
    ENV_FILE = '/opt/solar/.env.prod'
    KUBECONFIG = '/var/lib/jenkins/.kube/config'
    NUGET_PACKAGES = '/var/lib/jenkins/.nuget/packages'
    TRIVY_CACHE_DIR = '/var/lib/jenkins/.cache/trivy'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    DOCKER_BUILDKIT = '1'
    COMPOSE_DOCKER_CLI_BUILD = '1'
  }

  stages {
    // =================================================================
    // CI
    // =================================================================

    stage('0. CI Preflight') {
      when { anyOf { branch 'dev'; branch 'staging'; branch 'main'; changeRequest() } }
      steps {
        sh '''
          set -eu

          for tool in git dotnet trivy; do
            command -v "$tool" >/dev/null 2>&1 || {
              echo "FAIL: missing required CI tool: $tool"
              exit 1
            }
          done

          mkdir -p "$NUGET_PACKAGES" "$TRIVY_CACHE_DIR"

          DOTNET_VERSION="$(dotnet --version)"
          DOTNET_MAJOR="${DOTNET_VERSION%%.*}"
          if [ "$DOTNET_MAJOR" -lt 9 ]; then
            echo "FAIL: SolarBatteryMaintainance.slnx requires .NET SDK 9.x or newer on Jenkins. Current: ${DOTNET_VERSION}"
            exit 1
          fi

          echo "CI preflight OK. dotnet=${DOTNET_VERSION}"
        '''
      }
    }

    stage('1. Restore') {
      when { anyOf { branch 'dev'; branch 'staging'; branch 'main'; changeRequest() } }
      steps {
        sh 'dotnet restore SolarBatteryMaintainance.slnx --packages "$NUGET_PACKAGES"'
      }
    }

    stage('2. Format Check') {
      when { anyOf { branch 'dev'; branch 'staging'; branch 'main'; changeRequest() } }
      steps {
        sh '''
          dotnet format SolarBatteryMaintainance.slnx \
            --verify-no-changes --severity error --no-restore
        '''
      }
    }

    stage('3. Build') {
      when { anyOf { branch 'dev'; branch 'staging'; branch 'main'; changeRequest() } }
      steps {
        sh 'dotnet build SolarBatteryMaintainance.slnx -c Release --no-restore'
      }
    }

    stage('4. Unit Tests') {
      when { anyOf { branch 'dev'; branch 'staging'; branch 'main'; changeRequest() } }
      steps {
        sh '''
          dotnet test SolarBatteryMaintainance.slnx -c Release --no-build \
            --filter "FullyQualifiedName!~IntegrationTests" \
            --logger "trx" \
            --results-directory ./TestResults
        '''
      }
      post {
        always {
          archiveArtifacts allowEmptyArchive: true, artifacts: 'TestResults/*.trx'
        }
      }
    }

    stage('5. Project Rule Checks') {
      when { anyOf { branch 'dev'; branch 'staging'; branch 'main'; changeRequest() } }
      steps {
        script {
          def baseRef = env.CHANGE_TARGET ?: 'dev'
                    sh """
            set -eu
            git fetch origin ${baseRef}:refs/remotes/origin/${baseRef} 2>/dev/null || true

            DIFF=\$(git diff origin/${baseRef}...HEAD -- '*.cs' 2>/dev/null || git diff HEAD~1...HEAD -- '*.cs' 2>/dev/null || echo "")

            if echo "\$DIFF" | grep -E '^\\+.*await\\s+\\w+(\\.\\w+)*\\.(UpdateAsync|DeleteAsync)\\s*\\('; then
              echo "FAIL: UpdateAsync/DeleteAsync are void in this repo. Do not await them."
              exit 1
            fi
            echo "PASS: no await on void UpdateAsync/DeleteAsync"

            if echo "\$DIFF" | grep -E '^\\+.*await\\s+\\w+(\\.\\w+)*\\.GetAllAsync\\s*\\('; then
              echo "FAIL: GetAllAsync returns IQueryable in this repo. Do not await it."
              exit 1
            fi
            echo "PASS: no await on GetAllAsync"

            NEW_ENTITIES=\$(git diff origin/${baseRef}...HEAD --name-only --diff-filter=A 2>/dev/null | grep -E 'Domain/Entities/.*\\.cs\$' || true)
            FAILED=0
            for file in \$NEW_ENTITIES; do
              if [ -f "\$file" ] && ! grep -qE 'class\\s+\\w+\\s*:\\s*(\\w+\\s*,\\s*)*AuditableEntity' "\$file"; then
                if ! grep -qE '^(\\s*public\\s+)?(abstract|enum|interface)' "\$file"; then
                  echo "FAIL: \$file must extend AuditableEntity"
                  FAILED=1
                fi
              fi
            done
            [ \$FAILED -eq 0 ] && echo "PASS: new domain entities extend AuditableEntity" || exit 1
          """
        }
      }
    }

    stage('6. Security Scan') {
      when { anyOf { branch 'dev'; branch 'staging'; branch 'main'; changeRequest() } }
      steps {
        sh '''
          trivy fs \
            --cache-dir "$TRIVY_CACHE_DIR" \
            --quiet \
            --scanners vuln \
            --severity CRITICAL \
            --exit-code 1 \
            --ignore-unfixed \
            --skip-dirs .git \
            --skip-dirs TestResults \
            .
        '''
      }
    }

    stage('7. Integration Tests') {
      when { anyOf { branch 'staging'; branch 'main'; changeRequest() } }
      steps {
        sh '''
          command -v docker >/dev/null 2>&1 || {
            echo "FAIL: integration tests require Docker/Testcontainers."
            exit 1
          }
          docker info >/dev/null 2>&1 || {
            echo "FAIL: Docker daemon is not reachable for integration tests."
            exit 1
          }

          dotnet test SolarBatteryMaintainance.slnx -c Release --no-build \
            --filter "FullyQualifiedName~IntegrationTests" \
            --logger "trx" \
            --results-directory ./TestResults
        '''
      }
      post {
        always {
          archiveArtifacts allowEmptyArchive: true, artifacts: 'TestResults/*.trx'
        }
      }
    }

    // =================================================================
    // CD
    // =================================================================

    stage('8. CD Preflight') {
      when { branch 'staging' }
      steps {
        sh '''
          set -eu

          for tool in docker kubectl helm curl; do
            command -v "$tool" >/dev/null 2>&1 || {
              echo "FAIL: missing required CD tool: $tool"
              exit 1
            }
          done

          docker info >/dev/null 2>&1 || {
            echo "FAIL: Docker daemon is not reachable by Jenkins."
            exit 1
          }

          kubectl version --client >/dev/null
          helm version >/dev/null

          [ -r "$ENV_FILE" ] || {
            echo "FAIL: runtime env file does not exist or is not readable: $ENV_FILE"
            exit 1
          }

          REQUIRED_KEYS="
          POSTGRES_PASSWORD
          RabbitMQ__Password
          JwtSettings__SecretKey
          JwtSettings__Issuer
          JwtSettings__Audience
          ObjectStorage__AccessKey
          ObjectStorage__SecretKey
          MailJet__ApiKey
          MailJet__ApiSecret
          GoogleOAuth__ClientId
          GoogleOAuth__ClientSecret
          ADMIN_PASSWORD
          "

          for key in $REQUIRED_KEYS; do
            if ! grep -q "^${key}=" "$ENV_FILE"; then
              echo "FAIL: missing required key in $ENV_FILE: ${key}"
              exit 1
            fi
          done

          FREE_KB="$(df -Pk "$WORKSPACE" | awk 'NR==2 {print $4}')"
          if [ "$FREE_KB" -lt 8388608 ]; then
            echo "FAIL: less than 8GiB free disk is available for Docker build cache."
            exit 1
          fi

          echo "CD preflight OK. Secrets were checked by key name only."
        '''
      }
    }

    stage('9. Login GHCR') {
      when { branch 'staging' }
      steps {
        withCredentials([string(credentialsId: 'GHCR_TOKEN', variable: 'GHCR_TOKEN')]) {
          sh 'echo "$GHCR_TOKEN" | docker login ghcr.io -u gsu26se55 --password-stdin'
        }
      }
    }

    stage('10. Build Docker Images') {
      when { branch 'staging' }
      steps {
        script {
          def services = [
            [name: 'apigateway',         dockerfile: 'services/ApiGateway/src/ApiGateway/Dockerfile'],
            [name: 'authservice',        dockerfile: 'services/AuthService/src/AuthService.Api/Dockerfile'],
            [name: 'emailservice',       dockerfile: 'services/EmailService/src/EmailService.Api/Dockerfile'],
            [name: 'smsservice',         dockerfile: 'services/SmsService/src/SmsService.Api/Dockerfile'],
            [name: 'filestorageservice', dockerfile: 'services/FileStorageService/src/FileStorageService.Api/Dockerfile'],
            [name: 'batteryservice',     dockerfile: 'services/BatteryService/src/BatteryService.Api/Dockerfile'],
            [name: 'ticketservice',      dockerfile: 'services/TicketService/src/TicketService.Api/Dockerfile'],
            [name: 'notificationservice',dockerfile: 'services/NotificationService/src/NotificationService.Api/Dockerfile'],
          ]

          services.each { svc ->
            sh """
              docker pull ${REGISTRY}/${svc.name}:${BRANCH_NAME} || true
              docker build \
                -f ${svc.dockerfile} \
                -t ${REGISTRY}/${svc.name}:${SHA} \
                -t ${REGISTRY}/${svc.name}:${BRANCH_NAME} \
                --build-arg BUILDKIT_INLINE_CACHE=1 \
                --cache-from ${REGISTRY}/${svc.name}:${BRANCH_NAME} \
                .
            """
          }
        }
      }
    }

    stage('11. Push to GHCR') {
      when { branch 'staging' }
      steps {
        script {
          ['apigateway', 'authservice', 'emailservice', 'smsservice', 'filestorageservice', 'batteryservice', 'ticketservice', 'notificationservice'].each { svc ->
            sh "docker push ${REGISTRY}/${svc}:${SHA}"
            sh "docker push ${REGISTRY}/${svc}:${BRANCH_NAME}"
          }
        }
      }
    }

    stage('12. Helm Validate') {
      when { branch 'staging' }
      steps {
        sh '''
          set -eu
          helm repo add prometheus-community https://prometheus-community.github.io/helm-charts || true
          helm repo add grafana https://grafana.github.io/helm-charts || true
          helm repo update
          helm dependency build deploy/helm/solar-battery

          helm lint deploy/helm/solar-battery \
            -f deploy/helm/solar-battery/values.yaml \
            -f deploy/helm/solar-battery/values-staging.yaml \
            -f deploy/helm/solar-battery/values-vps-small.yaml \
            --set-string global.imageTag="$SHA"

          helm template solar deploy/helm/solar-battery \
            --namespace solar-staging \
            -f deploy/helm/solar-battery/values.yaml \
            -f deploy/helm/solar-battery/values-staging.yaml \
            -f deploy/helm/solar-battery/values-vps-small.yaml \
            --set-string global.imageTag="$SHA" \
            >/tmp/solar-helm-rendered.yaml
        '''
      }
    }

    stage('13. Deploy') {
      when { branch 'staging' }
      steps {
        script {
          def namespace = 'solar-staging'

          def dumpDeployDiagnostics = {
            sh """
              set +e
              print_job_diagnostics() {
                job_name="\$1"
                echo "--- describe job/\$job_name ---"
                kubectl describe "job/\$job_name" --namespace ${namespace} || true
                for pod in \$(kubectl get pods --namespace ${namespace} -l "job-name=\$job_name" -o name 2>/dev/null); do
                  echo "--- logs \$pod for job/\$job_name ---"
                  kubectl logs "\$pod" --namespace ${namespace} --all-containers --timestamps --tail=200 --request-timeout=15s || true
                done
              }

              echo '=== Helm status ==='
              helm status solar --namespace ${namespace} || true

              echo '=== Kubernetes resources ==='
              kubectl get pods,pvc,jobs --namespace ${namespace} -o wide || true

              echo '=== Recent events ==='
              kubectl get events --namespace ${namespace} --sort-by=.lastTimestamp 2>/dev/null | tail -120 || true

              echo '=== Describe non-ready pods ==='
              for pod in \$(kubectl get pods --namespace ${namespace} --no-headers 2>/dev/null | awk '{split(\$2,a,"/"); if (a[1] != a[2] || \$3 != "Running") print "pod/"\$1}'); do
                echo "--- describe \$pod ---"
                kubectl describe "\$pod" --namespace ${namespace} || true
              done

              echo '=== Job logs ==='
              for job in \$(kubectl get jobs --namespace ${namespace} -o name 2>/dev/null | sed 's#^.*/##'); do
                print_job_diagnostics "\$job"
              done

              echo '=== Infra pod logs ==='
              for pod in postgres-0 rabbitmq-0 minio-0; do
                echo "--- logs pod/\$pod ---"
                kubectl logs "pod/\$pod" --namespace ${namespace} --all-containers --tail=120 || true
              done
            """
          }

          try {
            withCredentials([string(credentialsId: 'GHCR_TOKEN', variable: 'GHCR_TOKEN')]) {
              sh """
                set -eu
                kubectl create namespace ${namespace} --dry-run=client -o yaml | kubectl apply -f -

                kubectl create secret generic solar-secrets \
                  --namespace ${namespace} \
                  --from-env-file=${ENV_FILE} \
                  --dry-run=client -o yaml | kubectl apply -f -

                kubectl create secret docker-registry ghcr-pull \
                  --namespace ${namespace} \
                  --docker-server=ghcr.io \
                  --docker-username=gsu26se55 \
                  --docker-password=\${GHCR_TOKEN} \
                  --dry-run=client -o yaml | kubectl apply -f -
              """
            }

            sh """
              set -eu
              print_active_job_logs() {
                echo "--- active/failed job logs ---"
                for job in \$(kubectl get jobs --namespace ${namespace} --no-headers 2>/dev/null | awk '\$2 != "Complete" {print \$1}'); do
                  for pod in \$(kubectl get pods --namespace ${namespace} -l "job-name=\$job" -o name 2>/dev/null); do
                    echo "--- logs \$pod for job/\$job ---"
                    kubectl logs "\$pod" --namespace ${namespace} --all-containers --timestamps --tail=80 --request-timeout=10s || true
                  done
                done
              }

              print_deploy_progress() {
                label="\$1"
                echo "=== \${label} progress \$(date -Iseconds) ==="
                kubectl get pods,pvc,jobs --namespace ${namespace} -o wide || true
                echo "--- recent events ---"
                kubectl get events --namespace ${namespace} --sort-by=.lastTimestamp 2>/dev/null | tail -40 || true
                print_active_job_logs
              }

              start_deploy_watcher() {
                label="\$1"
                (
                  while true; do
                    print_deploy_progress "\$label"
                    sleep 30
                  done
                ) &
                WATCHER_PID=\$!
              }

              stop_deploy_watcher() {
                if [ -n "\${WATCHER_PID:-}" ]; then
                  kill "\$WATCHER_PID" 2>/dev/null || true
                  wait "\$WATCHER_PID" 2>/dev/null || true
                  unset WATCHER_PID
                fi
              }

              trap 'stop_deploy_watcher' EXIT

              echo '=== Phase 1: deploy infra ==='
              start_deploy_watcher 'Phase 1 infra'
              helm upgrade --install solar deploy/helm/solar-battery \
                --namespace ${namespace} \
                --create-namespace \
                -f deploy/helm/solar-battery/values.yaml \
                -f deploy/helm/solar-battery/values-staging.yaml \
                -f deploy/helm/solar-battery/values-vps-small.yaml \
                --set-string global.imageTag=${SHA} \
                --set services.apigateway.enabled=false \
                --set services.authservice.enabled=false \
                --set services.emailservice.enabled=false \
                --set services.smsservice.enabled=false \
                --set services.filestorageservice.enabled=false \
                --set services.batteryservice.enabled=false \
                --set services.ticketservice.enabled=false \
                --set services.notificationservice.enabled=false \
                --wait --wait-for-jobs --timeout 60m
              stop_deploy_watcher

              start_deploy_watcher 'Postgres database init job'
              kubectl wait job/postgres-database-init-${SHA} \
                --for=condition=Complete \
                --namespace ${namespace} \
                --timeout=10m
              stop_deploy_watcher

              start_deploy_watcher 'MinIO init job'
              kubectl wait job/minio-init-${SHA} \
                --for=condition=Complete \
                --namespace ${namespace} \
                --timeout=10m
              stop_deploy_watcher

              echo '=== Phase 2: deploy application services ==='
              start_deploy_watcher 'Phase 2 application services'
              helm upgrade --install solar deploy/helm/solar-battery \
                --namespace ${namespace} \
                -f deploy/helm/solar-battery/values.yaml \
                -f deploy/helm/solar-battery/values-staging.yaml \
                -f deploy/helm/solar-battery/values-vps-small.yaml \
                --set-string global.imageTag=${SHA} \
                --atomic --wait --wait-for-jobs --timeout 60m
              stop_deploy_watcher
              trap - EXIT
            """
          } catch (err) {
            echo 'Deploy failed - collecting Kubernetes diagnostics'
            dumpDeployDiagnostics()
            throw err
          }
        }
      }
    }

    stage('14. Smoke Test') {
      when { branch 'staging' }
      steps {
        sh './ci/scripts/smoke-test.sh https://api.capstonegsu26se55.mooo.com'
      }
    }
  }

  post {
    success {
      echo "Pipeline success - ${env.BRANCH_NAME} - ${env.SHA}"
    }
    failure {
      echo "Pipeline FAILED - ${env.BRANCH_NAME} - build #${env.BUILD_NUMBER}"
      script {
        if (env.BRANCH_NAME == 'staging') {
          sh '''
            if helm history solar --namespace solar-staging >/dev/null 2>&1; then
              helm rollback solar --namespace solar-staging || true
            fi
          '''
        }
      }
    }
    always {
      sh 'docker logout ghcr.io || true'
      cleanWs()
    }
  }
}
