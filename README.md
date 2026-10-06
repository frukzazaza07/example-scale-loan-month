# Month-End LMS

A runnable interview demo with fictional customers and simulated THB. Five .NET 10 services, four separately owned PostgreSQL databases, durable RabbitMQ messaging, and a Vue 3 operations dashboard demonstrate month-end installment processing.

## Run with Docker Desktop (Windows) or Docker Engine (Linux)

On Windows with Docker Desktop and Kubernetes already running and Metrics Server/KEDA/Ingress already installed, start **both Compose and Kubernetes**, including observability, with one command from the project root:

```powershell
.\start.cmd
```

The wrapper runs `scripts/start.ps1 -Observability`: Compose builds/starts first, then Kubernetes manifests/secrets are applied and migration/seed jobs, deployments and KEDA readiness are awaited. It creates `.env` only when missing, preserves existing data/volumes/PVCs, and does not install Docker, create a cluster or install Kubernetes dependencies. URLs are Compose `http://localhost:8080`, Kubernetes `http://localhost:30080`, Grafana `http://localhost:3000`, Prometheus `http://localhost:9090` and Jaeger `http://localhost:16686`. Grafana and Jaeger continue running in Compose; the Kubernetes metrics collector runs in `lms-demo`.

For Compose alone, use `scripts/start.ps1 -ComposeOnly -Observability`. On Linux, `scripts/start.sh` remains the Compose-only entry point.

Prerequisites: Linux containers, Docker Compose v2, at least 8 GB available RAM (12–16 GB recommended for Kubernetes plus observability), and available localhost ports 8080, 5100–5102, 5432 and 5672. Source development additionally requires .NET 10 SDK and Node 24.7+ / npm. The workspace has a project-local .NET SDK under `.tools/dotnet`; it is ignored and not required for Docker.

```powershell
Copy-Item .env.example .env
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/start.ps1 -ComposeOnly
# Include Prometheus, Grafana and Jaeger:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/start.ps1 -ComposeOnly -Observability
```

```bash
bash scripts/start.sh --observability
```

Open **http://localhost:8080**. Choose Admin / `demo-admin-password`, or Observer / `demo-observer-password` from `.env.example`. These are local demo credentials; `.env` supplies independently configurable passwords/signing secrets. Compose binds published ports to loopback. Migration jobs complete before seeding and dependent application startup. Seeding creates 1,000 loans and is idempotent.

Grafana: http://localhost:3000 (admin / your `ADMIN_PASSWORD`). RabbitMQ management: http://localhost:15672 (credentials in `.env`). Traces: http://localhost:16686. Prometheus: http://localhost:9090. Optional URLs report unavailable when the observability profile is absent.

```powershell
docker compose --env-file .env -f deploy/compose/compose.yaml ps
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/simulate.ps1 -Rps 1 -Seconds 1 -LoanReference LOAN-1001
```

On an unpaid LOAN-1001, this yields **1,000 THB interest + 8,000 THB principal, with 92,000 THB actual principal remaining**. Running another *distinct* payment after that installment is paid creates unapplied credit. Redeliver the original transaction to demonstrate deduplication.

## Repository

| Path | Responsibility |
| --- | --- |
| `backend/WebhookApi` | Raw HMAC validation, durable receipt and payment outbox |
| `backend/LoanService` | Financial database, loan reads, API/worker modes, allocation, journals, audited resolution/replay |
| `backend/NotificationService` | Deduplicated in-app notification records |
| `backend/BankSimulator` | Persisted runs/transactions/settlement, paced delivery, retry attempts and recovery audit |
| `backend/DemoApi` | Authentication, authorized gateway, HTTP aggregation and safe telemetry |
| `backend/Shared` | Versioned events, auth/observability and messaging infrastructure; no loan domain entities |
| `frontend` | Vue Composition API, strict TypeScript, Router, Pinia, Element Plus, ECharts |
| `tests` | Financial units and real PostgreSQL/RabbitMQ integration checks |
| `deploy/compose`, `deploy/k8s` | Local runtime, migration/seed jobs, observability, RBAC and autoscaling |
| `docs` | Policy, contracts, architecture, interview walkthrough and validation evidence |

Backend packages and frontend packages are pinned with committed lockfiles. .NET base images are pinned by digest; other images use explicit version tags. `dotnet restore --locked-mode` and `npm ci` reproduce dependency resolution.

## Source development and tests

```powershell
# Start just dependencies; stop source services before running the complete Compose app.
docker compose --env-file .env -f deploy/compose/compose.yaml up -d postgres rabbitmq
.tools/dotnet/dotnet.exe build MonthEndLms.slnx -p:RestoreLockedMode=true
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev.ps1 -Migrate
cd frontend
npm.cmd ci
npm.cmd run dev
```

Host services use 5100–5104; Vite proxies `/api` to DemoApi on 5100. `scripts/dev.ps1 -Stop` stops only recorded processes whose start times still match. `npm.cmd` avoids Windows PowerShell execution-policy restrictions on npm.ps1. On Linux, use normal `dotnet` / `npm` commands or the complete Compose environment.

```powershell
.tools/dotnet/dotnet.exe test tests/Finance.Tests --no-build
# Requires the running services plus real Compose PostgreSQL/RabbitMQ:
.tools/dotnet/dotnet.exe test tests/Integration.Tests --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-broker-recovery.ps1
cd frontend
npm.cmd run lint
npm.cmd run test
npm.cmd run build
npx.cmd playwright install chromium
npm.cmd run smoke
```

Integration tests create additional `TEST-*` fictional loans; they never reset or edit existing financial postings. Their defaults match `.env.example`; set the corresponding environment values when using changed credentials. Browser tests start two small simulator runs, so run them when no other simulation is active. `SMOKE_URL` overrides the default Docker dashboard URL; set `SMOKE_MODE=Kubernetes` alongside it for the Kubernetes assertions. Keep test data visible or use a fresh, explicitly reset demo environment before an interview.

Regenerate migrations with `dotnet tool restore` then `dotnet ef migrations add <name> --project backend/<service>` with `AUTH_SECRET`, `BANK_SECRET`, and `DATABASE_URL` set. Apply migrations through the one-shot jobs. The shared hosting library disables build-time ASP.NET route analyzers because SDK 10.0.100 crashes on its generic readiness helper; service projects retain analyzers and warnings as errors.

## Kubernetes demo

Enable Docker Desktop Kubernetes. The checked-in prerequisites target Kubernetes 1.31: Metrics Server 0.8.0, KEDA 2.18.1, and ingress-nginx 1.13.3. Scripts inspect existing installations before installing; adjust the Ingress class when using an existing different controller. The Docker Desktop flag permits its local kubelet certificate for Metrics Server; omit it on a cluster with trusted kubelet certificates.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/k8s-prerequisites.ps1 -DockerDesktop
# Build the lms-*:local images first; Docker Desktop shares its local image store.
docker compose --env-file .env -f deploy/compose/compose.yaml build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/k8s-start.ps1
kubectl -n lms-demo get jobs,pods
kubectl -n lms-demo apply -f deploy/k8s/observability.yaml
```

Linux equivalents: `bash scripts/k8s-prerequisites.sh`, then `bash scripts/k8s-start.sh`. A remote cluster requires pushing/tagging the images in `apps.yaml` and `jobs.yaml` to a reachable registry. Dashboard: **http://localhost:30080**, or **http://lms.localhost** via Ingress. BankSimulator delivers through `http://webhook-api:8080`, the receiver Service, so traffic can reach all replicas. Port-forwarding the dashboard for remote access does not change the simulator's receiver target.

The receiver HPA has 2–6 replicas at 60% CPU; KEDA controls the worker's sole HPA, 1–6 replicas at 100 queued messages per replica. Up/down stabilization is 10–15 / 120 seconds. These are tuning defaults. The pool limit is 5 connections per process and consumer concurrency is one per worker with prefetch 8; the maximum steady application pool budget is approximately 80 connections. The bundled PostgreSQL allows 200 sessions, covering this budget plus migration jobs and rolling-update overlap. PostgreSQL/RabbitMQ are single instances and Docker Desktop pods share one computer's CPU/RAM.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/observe-scaling.ps1 -StartSpike
kubectl -n lms-demo get hpa,scaledobject,pods -w
kubectl -n lms-demo top pods
```

The gateway service account can only read deployments, pods, HPAs and ScaledObjects in `lms-demo`. It cannot read Secrets or mutate the cluster. Browser responses exclude pod specs and cluster credentials. Kubernetes Prometheus uses namespace-scoped pod discovery; optional Grafana/Jaeger links can point to the Compose viewers or your own viewers.

## Recovery and persistence

```powershell
# Stop and restore the local broker; durable receipt remains available.
docker compose --env-file .env -f deploy/compose/compose.yaml stop rabbitmq
docker compose --env-file .env -f deploy/compose/compose.yaml start rabbitmq
# Normal shutdown preserves volumes.
docker compose --env-file .env -f deploy/compose/compose.yaml --profile observability down
# Delete a demo worker pod; its Deployment replaces it and RabbitMQ redelivers unacked work.
kubectl -n lms-demo delete pod -l app=loan-worker
```

Failures & Recovery can map an unmatched original reference to an existing loan, then replay with the original bank identity. Reconciliation finds missing deliveries after a grace period and audits re-delivery of the persisted original body. Applied transactions replay to the previous result.

**Destructive reset — erases all demo data:**

```powershell
docker compose --env-file .env -f deploy/compose/compose.yaml --profile observability down -v
# Separate Kubernetes reset, including its namespace PVCs:
kubectl delete namespace lms-demo
```

See [validation](docs/validation.md) for checks actually executed and limitations, [architecture](docs/architecture.md), [financial policy](docs/financial-policy.md), [event contracts](docs/event-contracts.md), and the [10-minute interview script](docs/interview.md).

