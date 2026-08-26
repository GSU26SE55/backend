pipeline {
    agent none

    options {
        disableConcurrentBuilds()
        timestamps()
        timeout(time: 150, unit: 'MINUTES')
        buildDiscarder(
            logRotator(
                numToKeepStr: '30',
                artifactNumToKeepStr: '15'
            )
        )
        skipDefaultCheckout(true)
    }

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        NUGET_XMLDOC_MODE = 'skip'
        DOCKER_BUILDKIT = '1'
        FILESTORAGE_TEST_MINIO_IMAGE = 'minio/minio:RELEASE.2025-04-22T22-12-26Z'
        FILESTORAGE_TEST_MC_IMAGE = 'minio/mc:RELEASE.2025-04-16T18-13-26Z'
    }

    stages {
        stage('CI and security gates') {
            agent {
                label 'docker-linux'
            }

            stages {
                stage('Checkout') {
                    steps {
                        checkout scm

                        script {
                            env.GIT_SHA = sh(
                                script: 'git rev-parse HEAD',
                                returnStdout: true
                            ).trim()

                            if (!(env.GIT_SHA ==~ /[0-9a-f]{40}/)) {
                                error('Unable to resolve a full immutable Git SHA')
                            }
                        }

                        sh 'git diff --check'
                    }
                }

                stage('Executor preflight') {
                    steps {
                        sh '''
                            set -eu

                            for tool in \
                              git dotnet docker shellcheck trivy syft helm jq sha256sum base64
                            do
                              command -v "${tool}" >/dev/null 2>&1 || {
                                echo "Missing required Jenkins tool: ${tool}" >&2
                                exit 1
                              }
                            done

                            dotnet_major="$(dotnet --version | cut -d. -f1)"
                            test "${dotnet_major}" -ge 9 || {
                              echo 'The .slnx build requires .NET SDK 9 or newer' >&2
                              exit 1
                            }

                            docker info >/dev/null
                            docker buildx version >/dev/null
                            helm version >/dev/null
                        '''
                    }
                }

                stage('Backend build and tests') {
                    steps {
                        sh '''
                            set -eu

                            dotnet restore SolarBatteryMaintainance.slnx
                            dotnet format SolarBatteryMaintainance.slnx \
                              --verify-no-changes \
                              --severity error \
                              --no-restore
                            dotnet build SolarBatteryMaintainance.slnx \
                              --configuration Release \
                              --no-restore

                            # Make dependency acquisition explicit on clean Jenkins executors.
                            # Use the same immutable releases exercised by production.
                            docker pull "${FILESTORAGE_TEST_MINIO_IMAGE}"
                            docker pull "${FILESTORAGE_TEST_MC_IMAGE}"

                            dotnet test SolarBatteryMaintainance.slnx \
                              --configuration Release \
                              --no-build \
                              --filter 'Category!=Performance' \
                              --logger 'trx' \
                              --results-directory test-results
                        '''
                    }

                    post {
                        always {
                            // Jenkins' JUnit parser does not understand VSTest TRX files.
                            // Preserve the original evidence; dotnet test's exit code is the gate.
                            archiveArtifacts(
                                allowEmptyArchive: true,
                                artifacts: 'test-results/*.trx',
                                fingerprint: true
                            )
                        }
                    }
                }

                stage('Contracts and deployment configuration') {
                    steps {
                        sh '''
                            set -eu

                            shellcheck ci/scripts/*.sh deploy/scripts/*.sh

                            if git show-ref --verify --quiet refs/remotes/origin/dev; then
                              BASE_REF=origin/dev ./ci/scripts/rule-checks.sh
                            fi

                            ./ci/scripts/verify-deploy-registry-auth.sh

                            helm repo add prometheus-community \
                              https://prometheus-community.github.io/helm-charts \
                              --force-update
                            helm repo add grafana \
                              https://grafana.github.io/helm-charts \
                              --force-update
                            helm repo update
                            helm dependency build deploy/helm/solar-battery
                            helm lint deploy/helm/solar-battery \
                              -f deploy/helm/solar-battery/values.yaml \
                              -f deploy/helm/solar-battery/values-production.yaml \
                              -f deploy/helm/solar-battery/values-vps-small.yaml \
                              --set-string iot.mqttNodeIp=10.20.0.1

                            helm template solar deploy/helm/solar-battery \
                              --namespace solar-prod \
                              -f deploy/helm/solar-battery/values.yaml \
                              -f deploy/helm/solar-battery/values-production.yaml \
                              -f deploy/helm/solar-battery/values-vps-small.yaml \
                              --set-string iot.mqttNodeIp=10.20.0.1 \
                              > rendered-production.yaml

                            if grep -Eq \
                              'CHANGE_ME|Admin@123|capstonegsu26se55[.]mooo[.]com' \
                              rendered-production.yaml
                            then
                              echo 'Production Helm render contains a forbidden placeholder' >&2
                              exit 1
                            fi

                            for expected in \
                              'Cors__AllowedOrigins__0: "https://solars.io.vn"' \
                              'Frontend__WebBaseUrl: "https://solars.io.vn"' \
                              'GoogleOAuth__RedirectUri: "https://solars.io.vn/auth/google/callback"' \
                              'GoogleOAuth__AllowedRedirectUris__0: "https://solars.io.vn/auth/google/callback"' \
                              'Ai__GrpcAddress: "https://ai.solaris.io.vn"' \
                              'Ai__HttpBaseUrl: "https://ai.solaris.io.vn"' \
                              'TicketAi__AiGrpcAddress: "https://ai.solaris.io.vn"' \
                              'Battery__MaintenanceSchedule__Enabled: "true"' \
                              'Battery__MaintenanceSchedule__TimeZoneId: "Asia/Ho_Chi_Minh"' \
                              'Battery__MaintenanceSchedule__DefaultCycleMonths: "6"' \
                              'Battery__MaintenanceSchedule__LeadDays: "7"' \
                              'Battery__MaintenanceSchedule__PollIntervalSeconds: "60"' \
                              'Battery__MaintenanceSchedule__BatchSize: "100"' \
                              'Ticket__PeriodicMaintenance__Enabled: "true"' \
                              'Ticket__PeriodicMaintenance__TimeZoneId: "Asia/Ho_Chi_Minh"' \
                              'Ticket__PeriodicMaintenance__OverdueScheduleWindowDays: "7"' \
                              'Ticket__PeriodicMaintenance__ReminderTime: "08:00:00"' \
                              'Ticket__PeriodicMaintenance__PollIntervalSeconds: "60"' \
                              'Ticket__PeriodicMaintenance__BatchSize: "100"' \
                              'SlaBusinessHours__TimeZoneId: "Asia/Ho_Chi_Minh"' \
                              'SlaBusinessHours__Start: "07:00:00"' \
                              'SlaBusinessHours__End: "17:00:00"' \
                              'SlaBusinessHours__WorkingDays__0: "Sunday"' \
                              'SlaBusinessHours__WorkingDays__1: "Monday"' \
                              'SlaBusinessHours__WorkingDays__2: "Tuesday"' \
                              'SlaBusinessHours__WorkingDays__3: "Wednesday"' \
                              'SlaBusinessHours__WorkingDays__4: "Thursday"' \
                              'SlaBusinessHours__WorkingDays__5: "Friday"' \
                              'SlaBusinessHours__WorkingDays__6: "Saturday"'
                            do
                              grep -Fq "${expected}" rendered-production.yaml || {
                                echo "Missing production URL contract: ${expected}" >&2
                                exit 1
                              }
                            done

                            # This setting is scoped to EmailService's Deployment env list,
                            # not the shared ConfigMap rendered as key/value YAML.
                            if ! grep -F -A1 -- \
                              '- name: PartnerImport__ResetPasswordUrlBase' \
                              rendered-production.yaml | \
                              grep -Fq \
                                'value: "https://solars.io.vn/forgot-password"'
                            then
                              echo \
                                'Missing EmailService PartnerImport reset-password URL contract' \
                                >&2
                              exit 1
                            fi

                            if grep -Fq \
                              'GoogleOAuth__AllowedRedirectUris__1:' \
                              rendered-production.yaml
                            then
                              echo 'Production Google OAuth must use one canonical frontend redirect URI' >&2
                              exit 1
                            fi

                            if grep -E \
                              '(Ai__GrpcAddress|Ai__HttpBaseUrl|TicketAi__AiGrpcAddress):.*(/docs|/openapi[.]json)' \
                              rendered-production.yaml
                            then
                              echo 'AI application base URL must not point to documentation' >&2
                              exit 1
                            fi

                            ./ci/scripts/verify-alertmanager-native-discord.sh \
                              rendered-production.yaml

                            ./deploy/scripts/verify-monitoring-resource-policy.sh \
                              rendered-production.yaml

                            ./deploy/scripts/verify-postgres-backup-policy.sh \
                              rendered-production.yaml

                            ./deploy/scripts/verify-geoip-production.sh \
                              rendered-production.yaml \
                              /opt/solar-platform/geoip/GeoLite2-City.mmdb
                        '''
                    }
                }

                stage('Filesystem vulnerability and secret scan') {
                    steps {
                        sh '''
                            set -eu

                            if ! trivy fs \
                              --ignore-unfixed \
                              --exit-code 1 \
                              --severity HIGH,CRITICAL \
                              --scanners vuln,secret \
                              --format json \
                              --output trivy-fs.json \
                              .
                            then
                              echo 'Blocking filesystem findings:' >&2
                              jq -r '
                                (.Results // [])[] as $result |
                                (($result.Vulnerabilities // [])[] |
                                  ["VULNERABILITY", .Severity, .VulnerabilityID,
                                   .PkgName, (.InstalledVersion // ""),
                                   (.FixedVersion // ""), $result.Target]) ,
                                (($result.Secrets // [])[] |
                                  ["SECRET", .Severity, (.RuleID // ""),
                                   $result.Target, ((.StartLine // 0) | tostring)]) |
                                @tsv
                              ' trivy-fs.json >&2
                              exit 1
                            fi
                        '''
                    }

                    post {
                        always {
                            archiveArtifacts(
                                allowEmptyArchive: true,
                                artifacts: 'trivy-fs.json'
                            )
                        }
                    }
                }

                stage('Kubernetes security baseline') {
                    steps {
                        sh '''
                            set -eu

                            # Scan the dependency-complete manifest that will actually be
                            # packaged by the trusted production job. The checked baseline
                            # makes all known findings explicit and rejects any silent drift.
                            trivy config \
                              --severity HIGH,CRITICAL \
                              --format json \
                              --output trivy-k8s-misconfig.json \
                              rendered-production.yaml

                            ./ci/scripts/verify-trivy-k8s-baseline.sh \
                              trivy-k8s-misconfig.json \
                              ci/security/trivy-k8s-baseline.env
                        '''
                    }

                    post {
                        always {
                            archiveArtifacts(
                                allowEmptyArchive: true,
                                artifacts: 'trivy-k8s-misconfig.json'
                            )
                        }
                    }
                }

                stage('Build immutable service images') {
                    steps {
                        sh '''
                            set -eu

                            for specification in \
                              'apigateway|services/ApiGateway/src/ApiGateway/Dockerfile' \
                              'authservice|services/AuthService/src/AuthService.Api/Dockerfile' \
                              'emailservice|services/EmailService/src/EmailService.Api/Dockerfile' \
                              'smsservice|services/SmsService/src/SmsService.Api/Dockerfile' \
                              'filestorageservice|services/FileStorageService/src/FileStorageService.Api/Dockerfile' \
                              'batteryservice|services/BatteryService/src/BatteryService.Api/Dockerfile' \
                              'ticketservice|services/TicketService/src/TicketService.Api/Dockerfile' \
                              'notificationservice|services/NotificationService/src/NotificationService.Api/Dockerfile' \
                              'auditaggregatorservice|services/AuditAggregatorService/src/AuditAggregatorService.Api/Dockerfile'
                            do
                              service="${specification%%|*}"
                              dockerfile="${specification#*|}"
                              image="solar-backend-ci/${service}:${GIT_SHA}"
                              docker build \
                                --pull \
                                --file "${dockerfile}" \
                                --tag "${image}" \
                                .

                              test "$(docker image inspect "${image}" --format '{{.Config.User}}')" = '10001:10001'
                            done
                        '''
                    }
                }

                stage('Image security and SBOM') {
                    steps {
                        sh '''
                            set -eu

                            mkdir -p security-artifacts
                            for service in \
                              apigateway \
                              authservice \
                              emailservice \
                              smsservice \
                              filestorageservice \
                              batteryservice \
                              ticketservice \
                              notificationservice \
                              auditaggregatorservice
                            do
                              image="solar-backend-ci/${service}:${GIT_SHA}"
                              report="security-artifacts/trivy-${service}.json"

                              if ! trivy image \
                                --ignore-unfixed \
                                --exit-code 1 \
                                --severity HIGH,CRITICAL \
                                --format json \
                                --output "${report}" \
                                "${image}"
                              then
                                trivy convert \
                                  --format table \
                                  --severity HIGH,CRITICAL \
                                  "${report}"
                                exit 1
                              fi

                              syft "${image}" \
                                -o "cyclonedx-json=security-artifacts/sbom-${service}.cdx.json"
                            done
                        '''
                    }

                    post {
                        always {
                            archiveArtifacts(
                                allowEmptyArchive: true,
                                artifacts: 'security-artifacts/*.json'
                            )
                        }
                    }
                }
            }

            post {
                always {
                    sh '''
                        if [ -n "${GIT_SHA:-}" ]; then
                          for service in \
                            apigateway authservice emailservice smsservice \
                            filestorageservice batteryservice ticketservice \
                            notificationservice auditaggregatorservice
                          do
                            docker image rm \
                              "solar-backend-ci/${service}:${GIT_SHA}" \
                              >/dev/null 2>&1 || true
                          done
                        fi
                    '''
                    deleteDir()
                }
            }
        }

        // No docker-linux executor is held while the trusted job runs.
        stage('Request trusted production release') {
            when {
                allOf {
                    branch 'main'
                    expression { env.CHANGE_ID == null }
                }
            }

            steps {
                build(
                    job: 'solar-backend-production',
                    wait: true,
                    propagate: true,
                    parameters: [
                        string(name: 'GIT_SHA', value: env.GIT_SHA)
                    ]
                )
            }
        }
    }

    post {
        success {
            echo "Backend pipeline succeeded for ${env.GIT_SHA}"
        }
        failure {
            echo 'Backend pipeline failed; production was not changed or Helm rolled back'
        }
    }
}
