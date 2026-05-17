// =====================================================================
// Solar Battery — Full CI/CD Pipeline (VPS + k3s + Helm)
//
// CI  — chạy trên MỌI branch và PR (feature/*, dev, staging, main)
// CD  — chỉ chạy khi push vào branch `staging` hoặc `main`
//
// Credentials cần tạo 1 lần trên Jenkins UI:
//   ghcr-token   : Secret text  — GitHub PAT scope write:packages
//   github-token : Username/pass — GitHub username + PAT scope repo
//
// VPS cần cài thêm (1 lần — xem docs/jenkins-deploy-digitalocean.md):
//   dotnet-sdk-8.0  — để chạy dotnet format, build, test
//   trivy           — để chạy security scan
//   k3s             — lightweight Kubernetes
//   helm            — Kubernetes package manager
//   kubeconfig      — copy sang /var/lib/jenkins/.kube/config
// =====================================================================

pipeline {
  agent any

  options {
    timeout(time: 45, unit: 'MINUTES')
    buildDiscarder(logRotator(numToKeepStr: '10'))
    disableConcurrentBuilds()
    timestamps()
  }

  triggers {
    githubPush()
  }

  environment {
    REGISTRY   = 'ghcr.io/gsu26se55'
    SHA        = sh(script: 'git rev-parse --short HEAD', returnStdout: true).trim()
    ENV_FILE   = '/opt/solar/.env.prod'
    KUBECONFIG = '/var/lib/jenkins/.kube/config'
  }

  stages {

    // =================================================================
    // CI — chạy trên MỌI branch / PR
    // =================================================================

    stage('1. Format Check') {
      steps {
        sh '''
          dotnet restore SolarBatteryMaintainance.slnx
          dotnet format SolarBatteryMaintainance.slnx \
            --verify-no-changes --severity error --no-restore
        '''
      }
    }

    stage('2. Build') {
      steps {
        sh 'dotnet build SolarBatteryMaintainance.slnx -c Release --no-restore'
      }
    }

    stage('3. Unit Tests') {
      steps {
        sh '''
          dotnet test SolarBatteryMaintainance.slnx -c Release --no-build \
            --filter "FullyQualifiedName!~IntegrationTests" \
            --logger "trx;LogFileName=unit.trx" \
            --results-directory ./TestResults
        '''
      }
      post {
        always {
          junit allowEmptyResults: true, testResults: 'TestResults/*.trx'
        }
      }
    }

    stage('4. Project Rule Checks') {
      steps {
        script {
          // CHANGE_TARGET là target branch khi đây là PR build (vd: dev, main)
          // Khi push thẳng vào branch, fallback về dev để so sánh
          def baseRef = env.CHANGE_TARGET ?: 'dev'
          sh """
            git fetch origin ${baseRef}:refs/remotes/origin/${baseRef} 2>/dev/null || true

            DIFF=\$(git diff origin/${baseRef}...HEAD -- '*.cs' 2>/dev/null || git diff HEAD~1...HEAD -- '*.cs' 2>/dev/null || echo "")

            # Rule 1: UpdateAsync/DeleteAsync là void — KHÔNG được await
            if echo "\$DIFF" | grep -E '^\\+.*await\\s+\\w+(\\.\\w+)*\\.(UpdateAsync|DeleteAsync)\\s*\\('; then
              echo "FAIL: await UpdateAsync/DeleteAsync — 2 method nay void, khong await."
              exit 1
            fi
            echo "PASS: Khong co await tren void UpdateAsync/DeleteAsync"

            # Rule 2: GetAllAsync tra IQueryable — KHONG await
            if echo "\$DIFF" | grep -E '^\\+.*await\\s+\\w+(\\.\\w+)*\\.GetAllAsync\\s*\\('; then
              echo "FAIL: await GetAllAsync — method tra IQueryable khong phai Task."
              exit 1
            fi
            echo "PASS: Khong co await tren GetAllAsync"

            # Rule 3: Entity moi trong Domain/Entities phai extend AuditableEntity
            NEW_ENTITIES=\$(git diff origin/${baseRef}...HEAD --name-only --diff-filter=A 2>/dev/null | grep -E 'Domain/Entities/.*\\.cs\$' || true)
            FAILED=0
            for file in \$NEW_ENTITIES; do
              if [ -f "\$file" ] && ! grep -qE 'class\\s+\\w+\\s*:\\s*(\\w+\\s*,\\s*)*AuditableEntity' "\$file"; then
                if ! grep -qE '^(\\s*public\\s+)?(abstract|enum|interface)' "\$file"; then
                  echo "FAIL: \$file — entity moi phai extend AuditableEntity"
                  FAILED=1
                fi
              fi
            done
            [ \$FAILED -eq 0 ] && echo "PASS: Tat ca entity moi extend AuditableEntity" || exit 1
          """
        }
      }
    }

    stage('5. Security Scan (Trivy)') {
      steps {
        sh '''
          trivy fs \
            --severity CRITICAL \
            --exit-code 1 \
            --ignore-unfixed \
            --skip-dirs "**/bin,**/obj,.git" \
            .
        '''
      }
    }

    // Integration tests chỉ chạy trên PR (changeRequest()) — chậm, dùng Testcontainers
    stage('6. Integration Tests') {
      when { changeRequest() }
      steps {
        sh '''
          dotnet test SolarBatteryMaintainance.slnx -c Release --no-build \
            --filter "FullyQualifiedName~IntegrationTests" \
            --logger "trx;LogFileName=integration.trx" \
            --results-directory ./TestResults || true
        '''
      }
      post {
        always {
          junit allowEmptyResults: true, testResults: 'TestResults/*.trx'
        }
      }
    }

    // =================================================================
    // CD — chỉ chạy khi push vào staging hoặc main
    // =================================================================

    stage('7. Login GHCR') {
      when { anyOf { branch 'staging'; branch 'main' } }
      steps {
        withCredentials([string(credentialsId: 'GHCR_TOKEN', variable: 'GHCR_TOKEN')]) {
          sh 'echo "$GHCR_TOKEN" | docker login ghcr.io -u gsu26se55 --password-stdin'
        }
      }
    }

    stage('8. Build Docker Images') {
      when { anyOf { branch 'staging'; branch 'main' } }
      steps {
        script {
          def services = [
            [name: 'apigateway',         dockerfile: 'services/ApiGateway/src/ApiGateway/Dockerfile'],
            [name: 'authservice',        dockerfile: 'services/AuthService/src/AuthService.Api/Dockerfile'],
            [name: 'emailservice',       dockerfile: 'services/EmailService/src/EmailService.Api/Dockerfile'],
            [name: 'smsservice',         dockerfile: 'services/SmsService/src/SmsService.Api/Dockerfile'],
            [name: 'filestorageservice', dockerfile: 'services/FileStorageService/src/FileStorageService.Api/Dockerfile'],
            [name: 'batteryservice',     dockerfile: 'services/BatteryService/src/BatteryService.Api/Dockerfile'],
          ]
          services.each { svc ->
            sh """
              DOCKER_BUILDKIT=1 docker build \
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

    stage('9. Push to GHCR') {
      when { anyOf { branch 'staging'; branch 'main' } }
      steps {
        script {
          ['apigateway', 'authservice', 'emailservice', 'smsservice', 'filestorageservice', 'batteryservice'].each { svc ->
            sh "docker push ${REGISTRY}/${svc}:${SHA}"
            sh "docker push ${REGISTRY}/${svc}:${BRANCH_NAME}"
          }
        }
      }
    }

    stage('10. Deploy') {
      when { anyOf { branch 'staging'; branch 'main' } }
      steps {
        script {
          def namespace  = env.BRANCH_NAME == 'main' ? 'solar-production' : 'solar-staging'
          def valuesFile = env.BRANCH_NAME == 'main' ? 'values-prod.yaml'    : 'values-staging.yaml'
          sh """
            # Sync env file vào K8s Secret (idempotent)
            kubectl create secret generic solar-secrets \
              --namespace ${namespace} \
              --from-env-file=${ENV_FILE} \
              --dry-run=client -o yaml | kubectl apply -f -

            # Add helm repos (idempotent)
            helm repo add prometheus-community https://prometheus-community.github.io/helm-charts || true
            helm repo add grafana https://grafana.github.io/helm-charts || true
            helm repo update || true

            # Build helm chart dependencies nếu có subchart
            helm dependency build deploy/helm/solar-battery || true

            # Deploy — atomic: rollback tự động nếu pod không Ready trong timeout
            helm upgrade --install solar deploy/helm/solar-battery \
              --namespace ${namespace} \
              --create-namespace \
              -f deploy/helm/solar-battery/values.yaml \
              -f deploy/helm/solar-battery/${valuesFile} \
              --set global.imageTag=${SHA} \
              --atomic \
              --wait \
              --timeout 10m
          """
        }
      }
    }

    stage('11. Smoke Test') {
      when { anyOf { branch 'staging'; branch 'main' } }
      steps {
        retry(6) {
          sleep(time: 10, unit: 'SECONDS')
          sh 'curl -fsSk https://api.capstonegsu26se55.mooo.com/health || curl -fsSk https://api.capstonegsu26se55.mooo.com/swagger/index.html'
        }
      }
    }
  }

  post {
    success {
      echo "Pipeline success — ${env.BRANCH_NAME} — ${env.SHA}"
    }
    failure {
      echo "Pipeline FAILED — ${env.BRANCH_NAME} — build #${env.BUILD_NUMBER}"
      script {
        if (env.BRANCH_NAME in ['staging', 'main']) {
          def namespace = env.BRANCH_NAME == 'main' ? 'solar-production' : 'solar-staging'
          sh "helm rollback solar --namespace ${namespace} || true"
        }
      }
    }
    always {
      sh 'docker logout ghcr.io || true'
      cleanWs()
    }
  }
}
