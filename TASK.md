# TASK.md — Month-End LMS Demo

## Purpose

Build a runnable interview demo of a Loan Management System handling a month-end bank webhook spike. Show reliable receipt, correct installment allocation, duplicate protection, failure recovery, reconciliation, and Kubernetes autoscaling.

Use [LMS-Demo-Build-Prompt.md](./LMS-Demo-Build-Prompt.md) as the detailed specification. Put both documents in the project root. This checklist is the execution plan; it does not replace the financial or reliability rules in the build prompt.

**Current state:** Runnable implementation and local verification completed. Compose and Kubernetes are running. See docs/validation.md for executed checks, measured load results and explicit limits; M9.13 remains partial because exhaustive peak resource/pending-pod history was not captured.

## Working rules

- Complete the milestones in dependency order. Keep each milestone runnable.
- Mark a task complete only after its behavior exists and its relevant check passes.
- Record implementation decisions, blockers, and validation evidence at the end of this file.
- Keep money in integer satang; expose monetary minor units as JSON strings.
- Keep financial updates inside the LoanService database transaction.
- Dashboard operational values must come from live services or explicitly display unavailable.
- Preserve original bank identities through delivery, retry, replay, and reconciliation.
- Use fictional customers, simulated money, and in-app notifications.
- Compose and Kubernetes are separate runtime modes. Only Kubernetes demonstrates autoscaling.
- Treat scaling settings as initial tuning values; report measured behavior separately.

## Milestones and completion gates

| Milestone | Depends on | Completion gate |
| --- | --- | --- |
| M0: Decisions and contracts | — | Financial policy, statuses, boundaries and event contract documented |
| M1: Repository and local infrastructure | M0 | Backend/frontend compile; databases and RabbitMQ start |
| M2: Loan financial core | M1 | Schedule and allocation tests pass; example balance is correct |
| M3: Durable webhook pipeline | M2 | Signed webhook leads to one atomic financial result |
| M4: First frontend flow | M3 | Loan and payment detail work with actual backend data |
| M5: Bank simulator | M4 | Configurable runs report intended and achieved load |
| M6: Failures and reconciliation | M3, M5 | Retry, replay and missing-payment recovery work safely |
| M7: Full dashboard and telemetry | M4, M6 | All seven screens display real state with freshness indicators |
| M8: Docker packaging | M7 | Complete demo runs from documented Compose commands |
| M9: Kubernetes autoscaling | M8 | Spike, queue-based scaling and recovery observed |
| M10: Interview readiness | M9 | Required checks pass and presentation is reproducible |

## M0 — Decisions and contracts

- [x] M0.01 Confirm C#/.NET 10, ASP.NET Core, EF Core/Npgsql, PostgreSQL and RabbitMQ.
- [x] M0.02 Confirm Vue 3, strict TypeScript, Vite and Composition API with `<script setup lang="ts">`.
- [x] M0.03 Document WebhookApi, LoanService API/worker modes, NotificationService, BankSimulator and DemoApi boundaries.
- [x] M0.04 Define service-owned databases, credentials and allowed HTTP/message interactions.
- [x] M0.05 Document the fictional monthly reducing-balance policy, rounding, eligible installments, partial payments and unapplied credit.
- [x] M0.06 Define distinct bank outcome, receipt, processing and notification statuses; document valid transitions and conflicting bank outcomes.
- [x] M0.07 Define versioned events with event ID, bank identity, transaction ID, run ID where applicable, timestamps, schema version, correlation ID and trace context.
- [x] M0.08 Specify HMAC headers/signing bytes, timestamp freshness, accepted responses and simulator retry rules.
- [x] M0.09 Define Admin/Observer access and audited operational actions.
- [x] M0.10 Document UTC timestamp storage, Asia/Bangkok business dates and the demo business date of 2026-10-31.

**Gate:** A developer can explain the payment flow and identify the database transaction owning each state change.

## M1 — Repository and local infrastructure

- [x] M1.01 Create a .NET solution, service projects, test projects and small shared contract/observability libraries.
- [x] M1.02 Create the Vue frontend with Router, typed API clients, linting and type checks.
- [x] M1.03 Add `deploy/compose`, `deploy/k8s`, `scripts` and `docs` directories.
- [x] M1.04 Pin compatible dependencies and commit package lockfiles.
- [x] M1.05 Add PostgreSQL and RabbitMQ Compose services with persistent volumes and health checks.
- [x] M1.06 Configure separate service databases/users and EF Core migrations.
- [x] M1.07 Add environment configuration validation and `.env.example` without real secrets.
- [x] M1.08 Add service health/readiness endpoints, structured logs and correlation IDs.
- [x] M1.09 Implement versioned REST APIs, OpenAPI, validation, pagination and ProblemDetails.
- [x] M1.10 Implement demo authentication and backend Admin/Observer authorization.
- [x] M1.11 Add a frontend-facing DemoApi gateway; aggregate service APIs without cross-service database access.

**Gate:** Services and frontend build; infrastructure starts; readiness and authorization are testable.

## M2 — Loan financial core

- [x] M2.01 Model loans, installments, bank payments, allocations, processing records, journals, postings and unapplied credit.
- [x] M2.02 Add monetary range constraints, scoped uniqueness and database migrations.
- [x] M2.03 Generate the monthly reducing-balance schedule using decimal rates and explicit satang rounding.
- [x] M2.04 Preserve month-end due dates, including February; adjust the final payment to clear scheduled principal.
- [x] M2.05 Implement scheduled interest recognition once per period with balanced accounting entries.
- [x] M2.06 Implement oldest-eligible-installment allocation: interest first, then principal.
- [x] M2.07 Keep contractual schedule values separate from actual outstanding amounts.
- [x] M2.08 Implement partial payment and unapplied-credit outcomes.
- [x] M2.09 Lock relevant loan/installment/processing records and commit allocation, journal, balance and deduplication together.
- [x] M2.10 Prevent floating-point calculations, negative outstanding amounts and edited posted journals.
- [x] M2.11 Track paidAt, receivedAt and appliedAt independently; document delayed-event review thresholds.
- [x] M2.12 Implement loan list/detail, installment, payment, allocation and journal read APIs.
- [x] M2.13 Seed deterministic fictional loans, including LOAN-1001: 100,000 THB, 12% annual rate, 9,000 THB installment.
- [x] M2.14 Provide seed modes for a quick demo and at least 1,000 loans.
- [x] M2.15 Test interest, rounding, final installment, month-end dates, eligibility, partial payment, excess credit and concurrent allocation.

**Gate:** The first 9,000 THB payment allocates 1,000 THB to interest and 8,000 THB to principal, leaving 92,000 THB actual principal. Journals balance.

## M3 — Durable webhook and messaging pipeline

- [x] M3.01 Implement `POST /webhooks/bank/payments` using the agreed contract.
- [x] M3.02 Verify HMAC against raw body bytes with constant-time comparison and fresh delivery timestamp.
- [x] M3.03 Validate bank identity, event identity, reference, positive amount, THB currency, status and timestamps.
- [x] M3.04 Persist receipt and outgoing event atomically before returning 200.
- [x] M3.05 Return a retryable response if persistence fails; keep broker availability outside the receipt transaction.
- [x] M3.06 Deduplicate bank/event ID; compare immutable fields and flag conflicting reuse.
- [x] M3.07 Implement safe outbox claiming, leases, recovery, retries and publication status queries.
- [x] M3.08 Publish persistent messages, require confirmation and handle unroutable messages.
- [x] M3.09 Configure durable queues and named routing contracts.
- [x] M3.10 Implement LoanService worker mode with bounded concurrency/prefetch and graceful shutdown.
- [x] M3.11 Enforce bank/transaction ID uniqueness for financial application even when event IDs differ.
- [x] M3.12 Atomically commit worker outcome, allocations, postings and result outbox event; acknowledge after commit.
- [x] M3.13 Preserve the previous outcome on redelivery after commit.
- [x] M3.14 Record bank FAILED events without money movement; flag conflicting outcomes without reversing success.
- [x] M3.15 Implement LoanService result-event publication.
- [x] M3.16 Implement NotificationService consumption, deduplication and in-app receipt queries.
- [x] M3.17 Propagate run/correlation/trace identity across HTTP and messaging.
- [x] M3.18 Test atomic receipt/outbox persistence and duplicate delivery/application against real dependencies.

**Gate:** One signed webhook completes the end-to-end payment flow. Sending it again produces no additional financial postings.

## M4 — First usable frontend flow

- [x] M4.01 Build the app shell, sidebar, authentication flow and permission-aware navigation.
- [x] M4.02 Add exact THB formatting and Asia/Bangkok date helpers.
- [x] M4.03 Build loan search/list and loan detail with installment schedule.
- [x] M4.04 Display actual principal, due interest, allocations, payment history and related journals.
- [x] M4.05 Build payments/webhooks list with separate bank and application status badges.
- [x] M4.06 Build payment detail with observed receipt, publication, processing, allocation and notification stages.
- [x] M4.07 Add typed `useLoans` and `usePayments` composables.
- [x] M4.08 Implement 2–5 second polling with cancellation, backoff and loading/empty/error/stale states.
- [x] M4.09 Validate monetary displays against backend amounts; do not recalculate business balances in the browser.

**Gate:** An interviewer can inspect the example loan and follow one actual payment from receipt through allocation.

## M5 — Configurable bank simulator

- [x] M5.01 Persist simulator runs, intended unique transactions, delivery attempts and settlement records.
- [x] M5.02 Implement seeded generation and paced requests/second with a bounded in-flight limit.
- [x] M5.03 Protect run ownership so restarts or multiple instances do not multiply load.
- [x] M5.04 Sign each attempt with a current delivery timestamp while preserving payment identity/body.
- [x] M5.05 Add target rate, duration, concurrency, random seed and fault-percentage controls.
- [x] M5.06 Add Normal, Month-End Spike, Duplicate Storm, Partial Payments, Same-Loan Concurrency, Missing Webhook and Dependency Recovery presets.
- [x] M5.07 Implement bounded retries for network errors, 429 and retryable 5xx; respect Retry-After.
- [x] M5.08 Record achieved rate, scheduling lag, timeouts and response breakdowns.
- [x] M5.09 Distinguish generated unique payments from duplicates, attempts, accepted receipts and applied results.
- [x] M5.10 Implement start/stop, progress, run history and results APIs.
- [x] M5.11 Build Simulator screen and `useSimulator` composable with configurable preset cards.
- [x] M5.12 Use a dataset/payment mix that keeps the intended test valid; clearly classify unapplied credit and review outcomes.

**Gate:** A 5 RPS baseline and configurable 100 RPS/120-second spike execute with actual measured results. A lower achieved rate is reported honestly.

## M6 — Failure handling, replay and reconciliation

- [x] M6.01 Classify transient infrastructure failures separately from permanent validation/business review outcomes.
- [x] M6.02 Add bounded backoff retries, followed by a durable failed-message path and queryable failure records.
- [x] M6.03 Preserve original IDs, payloads, attempts and reasons through retries.
- [x] M6.04 Add unmatched-reference review without endless automatic retries.
- [x] M6.05 Implement audited reference resolution as a separate mapping; retain the original received reference and payload.
- [x] M6.06 Implement Admin replay with safe state transitions; already-applied transactions return the existing result.
- [x] M6.07 Keep notification failures independent of financial application.
- [x] M6.08 Build Failures & Recovery screen with filters, detail, resolution and replay controls.
- [x] M6.09 Implement settlement-report comparison using transaction identities, amount totals, cutoff and grace period.
- [x] M6.10 Separate in-flight, missing receipt, unmatched reference and amount-conflict reconciliation results.
- [x] M6.11 Recover missing delivery through audited redelivery of the original bank transaction.
- [x] M6.12 Build Reconciliation screen with run/date selection, counts, amounts and recovery actions.
- [x] M6.13 Test crash before commit, crash after commit/before acknowledgement, broker outage, unknown reference, conflicting duplicates and missed delivery.
- [x] M6.14 Test Observer denial for simulator, replay, reference resolution and reconciliation recovery controls.

**Gate:** Outages recover without double posting; unmatched and missing payments can be resolved under their original identities.

## M7 — Complete dashboard and real observability

- [x] M7.01 Expose request rate, webhook acceptance latency, payment application latency and dependency health.
- [x] M7.02 Expose outbox backlog, retry/failure count, queue depth, oldest-message age and completed throughput.
- [x] M7.03 Use bounded-cardinality metrics; keep per-payment identities in logs/traces rather than metric labels.
- [x] M7.04 Configure Prometheus scraping and a useful Grafana dashboard.
- [x] M7.05 Implement DemoApi metrics aggregation with source and refresh timestamps.
- [x] M7.06 Build Overview cards: loans, actual principal, due amount, applied payments, processing and review counts.
- [x] M7.07 Show request rate, queue depth/age and replicas on aligned time axes.
- [x] M7.08 Implement Scaling & Observability screen with desired/current/ready replicas, HPA/KEDA data and pending pods.
- [x] M7.09 Label runtime mode; show unavailable/stale states for absent telemetry and autoscaling unavailable in Compose.
- [x] M7.10 Add `useMetrics`, safe Grafana/trace links and meaningful status/filter navigation.
- [x] M7.11 Complete responsive tables, keyboard access and consistent colors across all seven screens.
- [x] M7.12 Keep date/cutoff labels clear, including demo business date versus actual receipt/application date.

**Gate:** The dashboard explains the business outcome and the operational backlog using observed data.

## M8 — Complete Docker environment

- [x] M8.01 Add multi-stage service and frontend Dockerfiles.
- [x] M8.02 Package full Compose application with internal networking, persistent volumes and health checks.
- [x] M8.03 Use migration/seed jobs instead of uncoordinated startup migrations in every replica.
- [x] M8.04 Keep connection pools, consumer concurrency and resource use bounded.
- [x] M8.05 Add an optional observability profile for lower-resource computers.
- [x] M8.06 Provide PowerShell/Bash startup, build, seed and simulation scripts.
- [x] M8.07 Document readiness checks, URLs, demo credentials and nondestructive restart procedures.
- [x] M8.08 Label destructive reset commands and separate them from normal startup.
- [x] M8.09 Verify a clean documented startup and persistence after ordinary restart.

**Gate:** The complete demo starts from the repository using documented Docker commands on the supported environment.

## M9 — Kubernetes and autoscaling

- [x] M9.01 Add namespace `lms-demo`, Deployments, Services, ConfigMaps and Secret templates.
- [x] M9.02 Add persistent storage for PostgreSQL/RabbitMQ and migration/seed Jobs.
- [x] M9.03 Set readiness/liveness probes, resource requests/limits and termination grace periods.
- [x] M9.04 Deploy LoanService API separately from its payment worker.
- [x] M9.05 Add WebhookApi CPU HPA: minimum 2, maximum 6, initial target 60%.
- [x] M9.06 Add Loan worker KEDA ScaledObject: minimum 1, maximum 6, initial target 100 queued messages per replica.
- [x] M9.07 Add stabilization settings and verify that no independent HPA competes with KEDA.
- [x] M9.08 Check/install required Metrics Server, KEDA and Ingress controller without duplicating existing installations.
- [x] M9.09 Route simulator traffic through Kubernetes Service/Ingress so all receiver replicas are exercised.
- [x] M9.10 Provide least-privilege read-only telemetry RBAC for DemoApi; keep cluster credentials out of the frontend.
- [x] M9.11 Verify the total possible pool/concurrency load against local PostgreSQL capacity.
- [x] M9.12 Run baseline, spike, cooldown and failure recovery; capture actual metrics and pod counts.
- [ ] M9.13 Record queue drain time, latency, scaling delay, resource saturation and pending-pod conditions.
- [x] M9.14 Tune settings based on measurements. Do not add artificial production-path CPU work just to trigger scaling.
- [x] M9.15 Document that Docker Desktop adds pods within one machine and the local database/broker are not high availability.

**Gate:** Observe queue growth, worker scale-out, backlog drain and eventual scale-in. If capacity or telemetry blocks the demonstration, record and resolve the blocker before claiming this milestone complete.

## M10 — Final verification and interview presentation

- [x] M10.01 Run backend unit and integration suites using real PostgreSQL/RabbitMQ dependencies where required.
- [x] M10.02 Run frontend lint/type/build checks and a browser smoke test.
- [x] M10.03 Verify receipt/outbox atomicity and that publication occurs only after durable receipt.
- [x] M10.04 Verify same-event and same-bank-transaction duplicates create one financial result.
- [x] M10.05 Verify conflicting duplicate payloads are reviewed.
- [x] M10.06 Verify concurrent payments preserve allocation totals, balanced journals and actual principal.
- [x] M10.07 Verify bank failure never posts money or undoes successful application.
- [x] M10.08 Verify partial payment, excess credit and late-date rules match documented policy.
- [x] M10.09 Verify crash/outage recovery, unmatched replay and missing-webhook reconciliation.
- [x] M10.10 Verify Admin/Observer enforcement and secret-free logging.
- [x] M10.11 Verify business counts/amounts reconcile to unique payments, independently of delivery count.
- [x] M10.12 Verify all UI operational values have a source; inaccessible telemetry is visibly unavailable.
- [x] M10.13 Write README, architecture diagram, financial policy, event contracts and recovery runbook.
- [x] M10.14 Write a 10-minute interview script and a shorter fallback walkthrough.
- [x] M10.15 Record only tests/load/scaling checks actually executed; list remaining limitations explicitly.

**Gate:** Every required demonstration below can be repeated from a known seed/run state.

## Interview walkthrough

| Step | Action | Evidence to show |
| --- | --- | --- |
| 1 | Open LOAN-1001 and pay 9,000 THB | 1,000 interest, 8,000 principal, remaining principal 92,000 |
| 2 | Redeliver the same payment | Duplicate receipt; unchanged financial posting count |
| 3 | Start Month-End Spike | Measured request rate, queue backlog and worker replicas |
| 4 | Wait for recovery/cooldown | Backlog drains; application results settle; workers scale in |
| 5 | Delete a processing worker | Redelivery/recovery without additional financial entries |
| 6 | Stop and restore RabbitMQ | Durable receipts and pending outbox, followed by publication |
| 7 | Resolve/replay an unmatched payment | Audited mapping and successful allocation under original identity |
| 8 | Reconcile deliberately missing delivery | Difference found; original payment recovered once |

## Definition of done

- [x] Complete source, migrations, seeds, manifests, scripts and lockfiles are present.
- [x] All seven frontend screens work against actual service data.
- [x] Required financial and reliability checks pass.
- [x] Docker startup is documented and verified.
- [x] Kubernetes scaling is observed and its practical limits are documented.
- [x] Demo assumptions and exclusions match the build prompt.
- [x] The interview story is reproducible without manual database editing.
- [x] No milestone is marked complete based only on screenshots, mocks or unexecuted tests.

## Implementation decisions

| Date (UTC) | Decision | Reason / implication |
| --- | --- | --- |
| 2026-10-06 | .NET 10 services; Vue 3 strict TypeScript; four separately credentialed databases | Financial writes stay within LoanService; DemoApi aggregates HTTP without cross-database reads |
| 2026-10-06 | Integer satang and decimal schedule rates; month-end due dates | Exact money, fixed contractual schedule, separate actual principal |
| 2026-10-06 | Financial arrival means loan-lock acquisition order | Serialized allocation across replicas; no promise of global network ordering |
| 2026-10-06 | Confirmed persistent outbox, 30-second leases, manual ACK after commit | At-least-once transport with idempotent financial effects |
| 2026-10-06 | Fixed RabbitMQ node name and persistent volumes | Preserve the same broker store through replacement |
| 2026-10-06 | Five connections per process, PostgreSQL max_connections 200, prefetch eight | Bound peak replica pools and allow migration/rolling overlap |
| 2026-10-06 | Receiver CPU HPA and sole worker KEDA HPA, 120-second scale-in stabilization | Scale against observed CPU/backlog without competing worker controllers |
| 2026-10-06 | Readiness timeout increased to five seconds following load observations | Avoid one-second probe failures on the shared computer |
| 2026-10-06 | Explicit unavailable metrics, page-scoped status filters and bounded failure samples | Avoid invented data or misleading scope; limits are labelled |
| 2026-10-06 | Optional run IDs propagated as additive event context | Preserve existing recorded bodies and financial fingerprints |

## Validation evidence

Full results and limitations: [docs/validation.md](docs/validation.md). Compact measured load data: [docs/validation-results.json](docs/validation-results.json).

| Date (UTC) | Check | Environment | Result / evidence |
| --- | --- | --- | --- |
| 2026-10-06 | Solution build and finance tests | Local .NET 10.0.100 | Zero build warnings/errors; 12 unit tests pass |
| 2026-10-06 | Financial/messaging integration | Real Compose PostgreSQL/RabbitMQ/services | Atomic receipt and financial rollback, duplicates, concurrency, commit/ACK recovery, bounded retries/replay, unknown/missing recovery, notification deduplication and authorization pass; 19 integration tests passed in the final run |
| 2026-10-06 | Frontend lint/type/build/unit | Node / npm | Checks pass; two exact-money unit tests pass; nonfatal library chunk warning documented |
| 2026-10-06 | Browser smoke | Standalone Playwright Chromium, Docker and Kubernetes URLs | Loan schedule, specific new run/payment, unmatched visibility, payment journey, observed scaling and reconciliation pass |
| 2026-10-06 | Broker outage | Compose broker stopped/restored | Durable receipt while unavailable, publication after recovery, exactly one payment journal; artifacts/broker-recovery.json |
| 2026-10-06 | Complete build/start/restart | Docker Desktop Linux containers | Migrations/seed complete, services healthy; existing principal/payment data retained |
| 2026-10-06 | Month-end spike | Docker Desktop Kubernetes | 12,000 accepted, achieved 96.71 RPS, all 12,000 matched; receiver reached six and workers scaled out |
| 2026-10-06 | Spike with all worker pods deleted | Docker Desktop Kubernetes | 12,000 accepted, achieved 72.17 RPS; recorded queue peak 3,430; drain to zero; workers 1 to 6 to 1; receivers back to two |
| 2026-10-06 | Post-recovery ledger / reconciliation | Read-only LoanService database check and complete HTTP settlement report | 12,000 unique transactions, 12,000 payment journals, zero unbalanced journals, 10,800,000,000 satang matched |

## Remaining limits

There is no implementation blocker for the runnable local interview demo. M9.13 is partially verified: actual queue drain and scale-in times are recorded, but exhaustive peak CPU/memory saturation and pending-pod history were not captured. Current Kubernetes deployments are available. RabbitMQ oldest message age is explicitly unavailable; actual pending-outbox timestamps and observed queue depth remain available. Historical latency for the earlier spikes was not collected before histogram deployment. Payment status filtering is per displayed page; failure filtering is over loaded records. Linux host scripts and remote image-registry deployment have not been separately executed. See the validation report for the complete practical limits and reproducible commands.