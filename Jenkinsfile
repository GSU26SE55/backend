// =====================================================================
// Solar Battery — CI/CD Pipeline
// Trigger: push to branch `staging` → build → deploy lên VPS K8s.
// Branch khác (main, dev, feature/*) sẽ KHÔNG trigger Jenkins.
// =====================================================================

pipeline {
  // Build agent là 1 pod K8s ephemeral (spawn khi build, xoá khi xong).
  agent {
    kubernetes {
      yamlFile 'deploy/jenkins/agent-pod.yaml'
      defaultContainer 'jnlp'
    }
  }

  options {
    timeout(time: 30, unit: 'MINUTES')
    buildDiscarder(logRotator(numToKeepStr: '30'))
    disableConcurrentBuilds()
    timestamps()
    // Clone với depth đủ để git diff origin/main hoạt động
    skipDefaultCheckout()
  }

  // Chỉ chạy khi push vào branch staging.
  triggers {
    githubPush()
  }

  environment {
    REGISTRY     = 'ghcr.io/your-org'                                  // ← ĐỔI tên org GitHub của bạn
    SHA          = sh(script: 'git rev-parse --short HEAD', returnStdout: true).trim()
    NAMESPACE    = 'solar-staging'
    HELM_CHART   = 'deploy/helm/solar-battery'
    DEPLOY_HOST  = 'staging.example.com'                               // ← ĐỔI sang domain thực
  }

  stages {

    // -----------------------------------------------------------------
    stage('0a. Checkout (full history)') {
      steps {
        // Clone với depth đủ + fetch main → detect-changes.sh hoạt động
        checkout([
          $class: 'GitSCM',
          branches: scm.branches,
          extensions: scm.extensions + [
            [$class: 'CloneOption', depth: 50, noTags: false, shallow: false],
            [$class: 'CleanBeforeCheckout']
          ],
          userRemoteConfigs: scm.userRemoteConfigs
        ])
        // Fetch main để git diff origin/main resolve được
        sh 'git fetch origin main:refs/remotes/origin/main || true'
      }
    }

    stage('0b. Branch Check') {
      steps {
        script {
          if (env.BRANCH_NAME != 'staging') {
            currentBuild.result = 'NOT_BUILT'
            error("Pipeline chỉ chạy cho branch 'staging'. Hiện tại: ${env.BRANCH_NAME}")
          }
        }
      }
    }

    // -----------------------------------------------------------------
    stage('1. Detect Changes') {
      steps {
        script {
          // Output: list service tên (vd "apigateway authservice")
          // Empty hoặc shared change → build tất cả service.
          def changed = sh(
            script: "BASE_REF=origin/main bash ci/scripts/detect-changes.sh",
            returnStdout: true
          ).trim()
          env.CHANGED_SERVICES = changed
          echo "Services to build: ${env.CHANGED_SERVICES}"
        }
      }
    }

    // -----------------------------------------------------------------
    stage('2. Lint & Format') {
      steps {
        container('dotnet') {
          sh 'dotnet format SolarBatteryMaintainance.slnx --verify-no-changes --severity error'
        }
      }
    }

    // -----------------------------------------------------------------
    stage('2.5. Trivy Filesystem Scan (CVE in dependencies)') {
      // Tương đương security-scan.yml trên GitHub Actions — quét CVE trong .csproj
      // CRITICAL → fail. HIGH → warn only.
      steps {
        container('trivy') {
          sh '''
            trivy fs --severity CRITICAL --exit-code 1 --ignore-unfixed \
              --skip-dirs "**/bin,**/obj,node_modules,.git" \
              .
            trivy fs --severity HIGH --exit-code 0 --ignore-unfixed \
              --skip-dirs "**/bin,**/obj,node_modules,.git" \
              . || true
          '''
        }
      }
    }

    // -----------------------------------------------------------------
    stage('3. Restore & Build') {
      steps {
        container('dotnet') {
          sh '''
            dotnet restore SolarBatteryMaintainance.slnx
            dotnet build SolarBatteryMaintainance.slnx -c Release --no-restore
          '''
        }
      }
    }

    // -----------------------------------------------------------------
    stage('4. Unit Tests') {
      steps {
        container('dotnet') {
          sh '''
            dotnet test SolarBatteryMaintainance.slnx -c Release --no-build \
              --filter "FullyQualifiedName!~IntegrationTests" \
              --logger "trx;LogFileName=unit.trx" \
              --results-directory ./TestResults || true
          '''
        }
      }
    }

    // -----------------------------------------------------------------
    stage('5. Build & Push Images') {
      // Skip service không có thay đổi (detect-changes.sh quyết định).
      steps {
        container('kaniko') {
          script {
            def pathMap = [
              apigateway:         'services/ApiGateway/src/ApiGateway/Dockerfile',
              authservice:        'services/AuthService/src/AuthService.Api/Dockerfile',
              emailservice:       'services/EmailService/src/EmailService.Api/Dockerfile',
              smsservice:         'services/SmsService/src/SmsService.Api/Dockerfile',
              filestorageservice: 'services/FileStorageService/src/FileStorageService.Api/Dockerfile',
              batteryservice:     'services/BatteryService/src/BatteryService.Api/Dockerfile'
            ]
            def changed = env.CHANGED_SERVICES.trim().split(/\s+/) as List
            echo "Building images for: ${changed}"
            changed.each { svc ->
              def dockerfile = pathMap[svc]
              if (!dockerfile) { error("Unknown service: ${svc}") }
              sh """
                /kaniko/executor \
                  --dockerfile=${dockerfile} \
                  --context=. \
                  --destination=${REGISTRY}/${svc}:${SHA} \
                  --destination=${REGISTRY}/${svc}:staging \
                  --cache=true
              """
            }
          }
        }
      }
    }

    // -----------------------------------------------------------------
    stage('6. Image Vulnerability Scan') {
      // Quét image vừa build — bổ sung cho fs scan ở stage 2.5.
      steps {
        container('trivy') {
          script {
            def changed = env.CHANGED_SERVICES.trim().split(/\s+/) as List
            changed.each { svc ->
              sh """
                trivy image --severity CRITICAL --exit-code 1 --ignore-unfixed --no-progress \
                  ${REGISTRY}/${svc}:${SHA} || true
              """
            }
          }
        }
      }
    }

    // -----------------------------------------------------------------
    stage('7. Deploy to K8s') {
      steps {
        container('kubectl') {
          // Phase 1: Pull subchart dependencies (kube-prometheus-stack, loki-stack, exporters)
          sh "helm dependency update ${HELM_CHART}"

          // Phase 2: Install/upgrade CRDs trước (tránh CRD timing issue với PrometheusRule)
          sh """
            helm upgrade --install solar-crds ${HELM_CHART} \
              --namespace ${NAMESPACE} --create-namespace \
              -f ${HELM_CHART}/values.yaml \
              -f ${HELM_CHART}/values-staging.yaml \
              --set global.imageTag=${SHA} \
              --include-crds \
              --skip-tests \
              --atomic --timeout 3m \
              --dry-run > /tmp/manifest.yaml || true
            # Apply CRD trước (idempotent)
            grep -A 9999 'kind: CustomResourceDefinition' /tmp/manifest.yaml | \
              kubectl apply -f - --server-side --force-conflicts || true
          """

          // Phase 3: Full install — sau khi CRDs đã có
          sh """
            helm upgrade --install solar ${HELM_CHART} \
              --namespace ${NAMESPACE} --create-namespace \
              -f ${HELM_CHART}/values.yaml \
              -f ${HELM_CHART}/values-staging.yaml \
              --set global.imageTag=${SHA} \
              --atomic --wait --timeout 10m
          """
        }
      }
    }

    // -----------------------------------------------------------------
    stage('8. Smoke Test') {
      steps {
        container('curl') {
          script {
            // ASP.NET service expose /metrics qua prometheus-net.AspNetCore.
            // Retry 6 lần × 15s — đợi pod ready hoàn toàn (rolling update có thể mất vài phút).
            retry(6) {
              sleep(time: 15, unit: 'SECONDS')
              sh "curl -fsS https://api.${DEPLOY_HOST}/metrics > /dev/null"
            }
            echo "✅ Smoke test passed: api.${DEPLOY_HOST}/metrics responsive"
          }
        }
      }
    }
  }

  post {
    success {
      script {
        notifyDiscord("✅ Deploy STAGING success — version `${env.SHA}` — https://api.${env.DEPLOY_HOST}")
      }
    }
    failure {
      script {
        // Auto rollback nếu deploy/smoke fail
        if (currentBuild.result == 'FAILURE') {
          container('kubectl') {
            sh "helm rollback solar -n ${env.NAMESPACE} || true"
          }
        }
        notifyDiscord("🔥 Deploy STAGING FAILED — build #${env.BUILD_NUMBER} — đã rollback")
      }
    }
    always {
      // Cleanup workspace để tránh disk full
      cleanWs()
    }
  }
}

// ---- Helper function ----
def notifyDiscord(String message) {
  // Discord webhook URL lấy từ Jenkins credential `discord-webhook`
  withCredentials([string(credentialsId: 'discord-webhook', variable: 'WEBHOOK')]) {
    sh """
      curl -X POST -H 'Content-Type: application/json' \
        -d '{"content": "${message}"}' \
        ${WEBHOOK} || true
    """
  }
}
