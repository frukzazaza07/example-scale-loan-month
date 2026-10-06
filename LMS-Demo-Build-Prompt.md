# Build prompt: Month-End LMS Webhook and Autoscaling Demo

Copy the instructions below into a coding agent with access to your project folder.

---

Act as a senior .NET engineer, Vue engineer, and distributed-systems architect. Build a complete runnable interview demo called "Month-End LMS". Implement the repository, frontend, backend, Docker environment, Kubernetes manifests, simulator, tests, and documentation. Do not stop at an architecture proposal, static mockups, or placeholder endpoints.

## Goal and story

At month-end, customers pay loan installments. A simulated bank sends many signed webhooks. The LMS durably receives them, processes installment payments asynchronously, and scales workers to drain the backlog. Demonstrate correct financial allocation, duplicate protection, concurrent processing, dependency outages, failed-message replay, and bank reconciliation.

Use simulated money and fictional customers only. Notifications are in-app records, not real emails or SMS. Run locally on Windows with Docker Desktop and on Linux. The simulator is a separate component representing an external bank. All dashboard data must come from running backend services; seeded business records are acceptable, invented operational metrics are not.

## Technology

- Backend: C#, .NET 10 LTS, ASP.NET Core, EF Core with Npgsql, PostgreSQL.
- Frontend: Vue 3, TypeScript strict mode, Vite, Composition API, and single-file components using <script setup lang="ts">. No Options API.
- Vue Router, Pinia where shared state is needed, typed API clients and reusable composables. Use a consistent component library and a chart library.
- RabbitMQ with durable queues, persistent messages, publisher confirms, manual consumer acknowledgement and bounded prefetch.
- Docker Compose for development; Kubernetes for the autoscaling demo.
- CPU HPA for webhook receivers; KEDA RabbitMQ queue scaling for loan workers. Do not put two competing HPAs on one deployment.
- Structured logs, OpenTelemetry trace propagation, Prometheus metrics and Grafana. A local trace viewer is optional if time/resources are limited.
- Pin compatible dependency versions and include lockfiles. Document prerequisites.

## Service boundaries and processes

1. WebhookApi owns a database containing webhook receipts and an outbox. It accepts signed bank events and exposes authenticated receipt/status queries.
2. LoanService owns loans, installment schedules, applied bank payments, payment allocations, journal entries, processing records, unapplied credit and an outbox. Support independent API and worker deployment modes from the same codebase so workers can scale independently. Financial updates stay within this service's database.
3. NotificationService owns in-app notifications and deduplication records.
4. BankSimulator owns run definitions, generated bank transactions, delivery attempts and a simulated settlement report. Provide a control API and background load generator.
5. DemoApi is a thin authenticated BFF/gateway for the Vue app. It aggregates service APIs and safe operational metrics. It does not calculate loan balances or join other services' database tables. It can expose Kubernetes read-only telemetry in Kubernetes mode, using a least-privilege service account; never send cluster credentials to the browser.

One PostgreSQL instance may host separate service databases and credentials for local resource efficiency. Each service may access only its own database. Share small event contracts and observability utilities, not shared domain entities or a generic repository framework.

Use HTTP for dashboard reads and simulator controls; use RabbitMQ for payment and notification events. Keep the frontend behind one origin where practical.

## Financial demo policy

Implement a clearly labelled fictional monthly reducing-balance loan product:
- THB only; store monetary values as integer satang in BIGINT.
- Encode monetary minor-unit values as strings in JSON; format with an exact frontend money helper.
- Use C# decimal for rate calculations; explicitly round period interest to the nearest satang using MidpointRounding.AwayFromZero. Reject invalid, nonpositive payment amounts and overflow.
- Seed a loan with opening principal 100,000 THB, annual rate 12%, monthly payment 9,000 THB and first due date 2026-10-31.
- Monthly scheduled interest = scheduled opening principal * annual rate / 12.
- Generate the complete schedule at origination, reducing scheduled principal each period and adjusting the final installment to clear the scheduled balance.
- Due dates remain at month-end, including February. Use Asia/Bangkok for business dates; store timestamps in UTC.
- For this seeded loan's first installment: interest 1,000 THB, principal 8,000 THB, resulting actual principal 92,000 THB after full payment.
- Allocate payments to unpaid due installments, oldest first; within each installment, interest before principal.
- Partial payments leave the installment partially paid.
- Amounts beyond eligible due installments become unapplied credit with a visible review state; do not silently apply early principal reductions.
- Contractual scheduled interest does not get recomputed on webhook retry, partial payment, or late delivery. This demo excludes daily accrual, late fees, refunds and early-payoff re-amortization.
- Record bank paidAt separately from receivedAt and appliedAt. Use a documented rule for eligibility: due date <= bank paidAt business date. If no installment is eligible, record unapplied credit.
- Delivery order must not overwrite previous allocation totals. Concurrent payments for one loan are serialized by row locks and deterministic allocation; arrival order is the allocation order for this simplified policy. Flag materially late bank timestamps for review and preserve the original timestamp.
- Never edit posted journals. Model each applied payment with a balanced journal: debit bank clearing/cash, credit loan principal receivable for principal, interest receivable for scheduled interest, and unapplied customer credit liability for any excess. Account for scheduled interest recognition with its own balanced journal before allocation. Reconcile journal amounts against allocations and balances.
- Show contractual scheduled balances separately from actual outstanding principal and outstanding due interest.

## Webhook contract and security

POST /webhooks/bank/payments receives:
{
  "eventId": "evt-001",
  "bankTransactionId": "bank-tx-001",
  "paymentReference": "LOAN-1001",
  "amountMinor": "900000",
  "currency": "THB",
  "status": "SUCCEEDED",
  "paidAt": "2026-10-31T16:59:00+07:00"
}

- Simulator signs deliveryTimestamp + "." + raw request body with HMAC-SHA256 using an environment-provided secret. Verify raw bytes and compare signatures in constant time.
- Timestamp freshness protects deliveries; on retry generate a fresh delivery timestamp/signature while preserving payment/event identity and body. Bank paidAt is an independent business timestamp.
- Scope deduplication by bank identifier. Unique bank/eventId rejects duplicate deliveries; unique bank/bankTransactionId in LoanService prevents duplicate financial application.
- Compare normalized immutable financial fields for reused identifiers. A conflicting duplicate goes to review; do not silently overwrite.
- Validate schema, currency, amount, status and required references. Reject invalid signatures/payloads without creating a financial event.
- Save valid receipt and outbox event in one local database transaction before returning 200. A 200 means durable receipt, not successful loan allocation.
- If receipt persistence fails, return 503 and simulator retries. A broker outage alone must not prevent durable receipt.
- A valid bank FAILED event is recorded without posting money. An out-of-order FAILED event must not undo an already applied success; flag conflicting outcomes.
- Do not log secrets, signatures, tokens or raw sensitive customer data.
- Provide simple local demo authentication with an authenticated Admin role for operational controls and Observer role for viewing. Implement backend authorization; hiding buttons is insufficient. Use environment secrets and provide .env.example.

## Reliable messaging and financial application

Every event contains eventId, bankTransactionId when applicable, occurredAt, schemaVersion, correlationId and trace context.

Outbox publisher:
- Claim pending rows safely across publisher instances with a lease/locking strategy.
- Publish persistently and use publisher confirms; handle unroutable messages as failures.
- Mark published only after confirmation; retry with exponential backoff/jitter.
- Expired claims can be recovered. Duplicate publication is expected.

Loan worker:
- Use a transaction and lock the loan, relevant installments and processing record.
- Atomically commit deduplication outcome, allocations, financial journal, balance changes and result outbox event.
- Acknowledge only after commit. A crash after commit but before acknowledgement must not create another posting.
- Treat business conditions such as unknown reference as visible unmatched/review outcomes, not infinite retries.
- Use bounded retry delays for transient infrastructure errors, e.g. 5s, 15s, 60s, then a failed-message queue and visible failure record.
- Preserve original identifiers during retry and replay. Never create a new payment merely to replay.
- Include graceful shutdown and bounded consumer concurrency.
- Admin replay is audited. Already applied payments return the previous result. Reviewed unmatched/failed processing can resume safely under the same financial identity.
- Notification consumers deduplicate atomically with notification creation.

Persist explicit statuses that separate bank outcome, webhook receipt, allocation processing and notification delivery. Do not label an acknowledged webhook as an applied payment. Expose pending age and human-readable reasons.

## Simulator requirements

Persist each run and its intended transactions before delivery. Store planned unique payments separately from attempt count, duplicates and retries.
- Seed at least 1,000 fictional loans with deterministic identifiers and consistent schedules. Provide quick seeding and an optional larger dataset.
- Configure requests/second, duration, concurrency cap, random seed, duplicate percentage, invalid-signature percentage, bank-failed percentage and unknown-reference percentage.
- Presets: Normal (5 RPS), Month-End Spike (100 RPS for 120 seconds), Duplicate Storm, Partial Payments, Same-Loan Concurrency, Missing Webhook and Dependency Recovery.
- These are configurable target loads, not throughput guarantees. Record achieved send rate, client-side scheduling lag, accepted/rejected responses, timeout count and payment completion results.
- Use paced scheduling rather than waiting for each response before generating the next request. Bound in-flight work and expose backpressure/scheduling lag.
- The load-generator process must not accidentally multiply the target load if restarted/scaled. Use single-run ownership and recovery for unfinished runs.
- Retry network errors, 429 and retryable 5xx with backoff, jitter and Retry-After support. Do not blindly retry invalid-signature or schema 4xx responses.
- Make duplicate deliveries preserve identity. Multiple distinct payments to one loan use different transaction IDs.
- Include start/stop, progress, run history, preset comparisons and a results report.
- Missing Webhook preset deliberately skips delivery for some successful bank transactions while retaining them in the settlement report.
- Use signed current delivery timestamps but a clearly displayed demo business date of 2026-10-31.

## Frontend screens: make the demo visually understandable

Build a polished desktop-first dashboard with a sidebar, clear typography, consistent status colors, responsive tables and accessible controls. Currency displays as THB, business dates use Asia/Bangkok, and technical timestamps can show UTC details. Default to English.

1. Overview
- Active loans, actual outstanding principal, due amount, applied payments today, processing count, review count.
- Request-rate chart, queue depth/oldest-message-age chart and worker-replica chart on an aligned time axis.
- Dependency health, data freshness, current simulator run, runtime mode (Compose/Kubernetes).
- Business aggregates must be labelled with date/cutoff and eventual-consistency state when appropriate.
- Clicking a card opens its filtered detail view.

2. Loans
- Search/filter table: customer, loan reference, principal, annual rate, due date, due amount and loan/repayment status.
- Loan detail: current principal, paid principal, paid interest, due interest, unapplied credit and next due date.
- Installment table: number, due date, scheduled opening principal, interest, principal, payment, amount paid and remaining due.
- Payment history, allocation breakdown and related journals.
- Include the 100,000 THB example so an interviewer can verify the first payment manually.

3. Payments and Webhooks
- Separate bank outcome from application status using distinct badges.
- Columns: bank transaction ID, loan reference, amount, paidAt, receivedAt, processing duration, attempt count and result.
- Detail panel: receipt, outbox publication, consumer attempt, allocation and notification stages, with timestamps and trace links.
- Show actual observed stages; mark missing/unavailable trace data explicitly.
- Filters for pending, applied, duplicate, unmatched, failed and bank-declined.
- Financial outcome and duplicates must reconcile to unique transaction identities.

4. Simulator
- Preset cards and custom controls for target RPS, duration, concurrency and fault percentages.
- Start/stop actions, achieved rate, attempted deliveries, unique payments, duplicates, accepted receipts and completed allocations.
- Charts and run results; clear warning if simulator CPU/concurrency limits prevent the configured rate.
- Never fake an animated counter while no run exists.

5. Failures and Recovery
- Failed-message list, unmatched payments, validation failures and conflicting duplicates.
- Show reason, attempts, original identity, last error and related loan.
- Admin can resolve an unmatched reference, replay eligible failed processing and see the audit trail.
- Show before/after statuses and prevent unsafe duplicate financial application.

6. Scaling and Observability
- Real desired/current/ready replicas, HPA metrics/targets, KEDA queue trigger, pending pods, queue depth, oldest age, throughput and acceptance/application latency.
- Graph current replica count beside workload and backlog.
- Display the telemetry source and refresh timestamp.
- In Compose mode show actual container/service telemetry available and label autoscaling "Unavailable in Compose". If Kubernetes/Prometheus is unavailable, show an unavailable state, never generated values.
- Link to Grafana and trace viewer using configurable URLs.
- Explain briefly that Docker Desktop pods share one computer's resources.

7. Reconciliation
- Compare successful simulated bank transactions with applied LMS payments for a selected run/date.
- Show matched, not-yet-applied, missing receipt, unmatched reference and amount-conflict counts with totals.
- Use a cutoff and processing grace period so in-flight payments are not incorrectly declared lost.
- Admin can recover a missing receipt through an audited re-delivery of the original bank transaction, preserving identity.
- Both amount and count comparisons must work independently of duplicate delivery count.

Poll authenticated dashboard APIs every 2-5 seconds with cancellation and backoff. Handle loading, empty, error and stale-data states. Use composables such as useLoans, usePayments, useSimulator and useMetrics. Do not use SignalR unless needed; polling is sufficient for the first version.

## APIs and repository

Implement versioned REST APIs with OpenAPI, validation, pagination, correlation IDs and ProblemDetails errors. Include service-local endpoints for loan reads, payment reads, receipt reads, simulator controls, notifications and audited replay; DemoApi exposes the frontend-facing aggregate routes.

Use a clear repository structure: backend service projects, tests, frontend, deploy/compose, deploy/k8s, scripts, docs. Avoid unnecessary abstractions and unexplained CQRS frameworks. Provide real EF migrations and seed commands. Financial operations must never depend on a cached balance.

## Docker and Kubernetes

- Multi-stage Dockerfiles; persistent PostgreSQL and RabbitMQ volumes; health checks; startup readiness and migration jobs.
- Docker Compose starts the entire demo with a documented command. Include optional observability profile if resources are limited.
- Kubernetes namespace lms-demo, Deployments, Services, ConfigMaps, Secrets templates, persistent storage, readiness/liveness probes, resource requests/limits and migration/seed Jobs.
- Webhook API HPA starts at 2 replicas, maximum 6, initial CPU target 60%. Loan worker KEDA ScaledObject starts at 1, maximum 6, with an initial queue-length target of 100 messages per replica. These are tuning defaults, not measured capacity.
- Include scaling stabilization and termination grace periods, and document how to tune them.
- Ensure the bank simulator targets the receiver through the Kubernetes Service/Ingress, not a port-forward tied to one pod, so receiver scaling is exercised.
- Metrics Server is required for CPU HPA; KEDA and an Ingress controller are prerequisites. Check for existing installations before applying installers.
- One local PostgreSQL/RabbitMQ instance is acceptable; document that it is not a high-availability deployment.
- Database pool size and per-worker concurrency must be bounded for the maximum replica count.
- Simulator/operational endpoints are local demo controls. Do not grant the app cluster-admin.
- Show Docker/Kubernetes recovery commands in README for stopping/restoring broker, deleting a worker pod and watching autoscaling. Destructive reset commands must be explicitly labelled.
- Include PowerShell and Bash scripts for build, seed, startup and sample simulations.

## Meaningful automated verification

Use unit tests for schedule generation, rounding, partial allocation and date rules. Use integration tests against real PostgreSQL/RabbitMQ containers for:
1. Receipt and outbox commit atomically.
2. Duplicate webhook gives one financial result.
3. Different events for one bank transaction do not double-apply.
4. Conflicting duplicate data is detected.
5. Concurrent distinct payments to one loan preserve balances and allocation totals.
6. Worker crash after commit/before acknowledgement does not duplicate journal entries.
7. Broker outage leaves durable pending outbox rows and recovery publishes them.
8. Unknown loan reference is visible and can be safely resolved/replayed.
9. Bank failure never posts money or reverses an applied success.
10. Partial payment and overpayment follow the stated policy.
11. Missing webhook is found by reconciliation and recovered once.
12. Observer cannot invoke simulator, replay or reference-resolution controls.

Add a browser smoke test covering loan detail, simulator start, payment completion and failure visibility. Report tests actually run and any infrastructure checks that could not run. Do not claim measured throughput or Kubernetes scaling unless observed.

## Interview acceptance and deliverables

Deliver working source, manifests, migrations, seed dataset, .env.example, dependency lockfiles, tests, architecture documentation, event contracts and a concise interview script.

The interview should demonstrate:
- Pay the first 9,000 THB installment: 1,000 interest, 8,000 principal, actual principal becomes 92,000.
- Send duplicate events: one financial posting.
- Run a month-end spike: real backlog and worker replicas change, then backlog drains and workers scale down.
- Crash a worker: processing recovers without duplicate accounting.
- Stop RabbitMQ: receipts stay saved and publish after recovery.
- Resolve/replay an unmatched payment.
- Recover a deliberately missing webhook through reconciliation.

Explain the tradeoffs: durable acceptance versus eventual allocation, at-least-once delivery plus idempotent financial effects, local transactional financial boundary, CPU versus queue-based scaling, and single-machine capacity limits.

Build the runnable vertical slice first, then complete reliability, UI, simulation and Kubernetes. Continue through implementation and validation. Keep assumptions and limitations documented, and do not silently substitute mock business logic or fake metrics.

