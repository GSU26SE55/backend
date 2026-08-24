# Jenkins production controller job

Jenkins runs on its own VPS; it is not installed into the backend K3s cluster.

- Repository CI entrypoint: root `Jenkinsfile` (Multibranch Pipeline).
- Centrally managed production job: `production.Jenkinsfile.example` pasted into the Jenkins UI job `solar-backend-production` after review.
- The production job deploys over pinned SSH host keys as user `deploy` and uses the shared lock `solar-platform-prod`.
- The production job pauses after building, scanning, pushing, signing and packaging the release. An operator must move the four production DNS records to R4 and confirm the cutover before the deploy stage can start.

See `../../PRODUCTION_DEPLOYMENT_BACKEND_IOT.md` for credentials, executor tools and the complete bootstrap order.
