# Jenkins production controller job

Jenkins runs on its own VPS; it is not installed into the backend K3s cluster.

- Repository CI entrypoint: root `Jenkinsfile` (Multibranch Pipeline).
- Centrally managed production job: `production.Jenkinsfile.example` pasted into the Jenkins UI job `solar-backend-production` after review.
- The production job deploys over pinned SSH host keys as user `deploy` and uses the shared lock `solar-platform-prod`.

See `../../PRODUCTION_DEPLOYMENT_BACKEND_IOT.md` for credentials, executor tools and the complete bootstrap order.
