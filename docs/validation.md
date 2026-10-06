# Executed validation

Validated on 2026-10-06 UTC / 2026-10-07 Asia/Bangkok, using Windows PowerShell, Docker Desktop Linux containers, and Docker Desktop Kubernetes 1.31.4 (8 CPUs, approximately 16 GB available to the VM). Business payment dates remain 2026-10-31. Compose and Kubernetes have independent persistent datasets.

## Build and automated checks

| Check | Executed result |
| --- | --- |
| .NET 10 solution build | Passed, zero warnings/errors |
| Financial unit tests | 12 passed |
| Real PostgreSQL/RabbitMQ integration tests | 19 passed, zero skipped |
| Vue strict TypeScript / production build | Passed |
| ESLint | Passed |
| Frontend exact-money unit tests | 2 passed |
| Playwright browser smoke | Passed against Compose and Kubernetes |
| npm dependency audit | Zero reported advisories after dependency updates |
| Multi-stage Docker build and complete Compose startup | Passed; migrations/seed jobs completed and all application health checks healthy |
| Ordinary container replacement | Existing loan/payment data retained; LOAN-1001 actual principal remained 92,000 THB |
| Kubernetes manifests / rollout | Applied successfully; migration/seed jobs completed, deployments available, HPA and KEDA reporting live data |
| Authenticated OpenAPI / readiness | DemoApi, WebhookApi and LoanService returned OpenAPI 3.1.1 documents and ready status |
| Optional observability endpoints | Grafana database health OK; Prometheus readiness and Jaeger UI returned HTTP 200 |
| Sampled service logs | Configured signing secrets and passwords absent from the last 100 log lines per service |
| Combined Windows startup (`start.cmd`) | Executed successfully on the existing Docker Desktop/Kubernetes setup; Compose, migration/seed jobs, all deployments and KEDA Ready checks passed; existing `.env` retained |

The integration suite verifies receipt/outbox atomicity, rollback on outbox failure, duplicate financial effects, distinct event IDs sharing one transaction, conflicting immutable fields/outcomes, concurrent allocation, balanced journals, commit-before-ACK redelivery using a real RabbitMQ connection, unknown-reference mapping/replay, bank failure, partial/excess payment, invalid signatures, atomic notification deduplication, database credential isolation, Observer denial, and missing-delivery reconciliation/recovery. Run-context propagation from receipt through financial result and the read-only run aggregate is also verified. Additional tests force financial journal insertion failure to verify complete rollback before commit, and exercise the actual 5/15/60-second retry queues through exhaustion followed by audited Admin replay. The final integration run took 2 minutes 30 seconds.

The browser smoke signs in, inspects the example loan and February due date, starts and waits for its specific new run/payment, produces an unmatched reference, inspects the payment journey, opens live scaling telemetry, and compares settlement. It checks for JavaScript page errors. The Kubernetes version additionally verifies deployment data. An early Kubernetes smoke exposed a test assumption about previously completed runs; the test now follows the newly returned run/transaction identities. The in-app Browser runtime had no available browser, so these checks used standalone Playwright Chromium. Screenshots were also visually inspected.

## Broker outage

`scripts/verify-broker-recovery.ps1` stopped the real Compose RabbitMQ broker, submitted a signed payment, verified HTTP 200 with a durable receipt and unpublished outbox, restored the same broker/node/volume, then verified publication and application with exactly one payment journal. The script restores the broker in `finally`.

Evidence: local `artifacts/broker-recovery.json`, financial status `Applied`, journal count `1`, checked at 16:32:35 UTC.

## Observed baseline and month-end autoscaling

The final 5 RPS / 10-second Kubernetes baseline (`8a9c6d32-4525-40b5-8dbd-fe922e8b890b`) attempted, accepted and applied all 50 unique transactions. Achieved rate was 4.69 RPS with 9.21 ms maximum scheduling lag and zero timeouts. These payments correctly became credit review against schedules already paid by the spike. Local artifacts: `artifacts/baseline-results.json` and `artifacts/baseline-application.json`.

Both spike runs configured **100 RPS for 120 seconds**, concurrency 100, 12,000 intended unique successful transactions of 900,000 satang each. The generator reports actual delivery rate over run elapsed time, including planning/startup and backpressure. These are local measurements, not throughput guarantees.

| Measurement | First spike | Worker recovery spike |
| --- | --- | --- |
| Run ID | `683b7382-cc1a-45c2-9c2f-9f9c53416199` | `25506c34-2bcd-4268-8d31-0c6e994aeb15` |
| Intended / attempted / accepted | 12,000 / 12,000 / 12,000 | 12,000 / 12,000 / 12,000 |
| Achieved send rate | 96.71 RPS | 72.17 RPS |
| Maximum scheduling lag | 167.27 ms | 42,617.67 ms |
| Rejected / timed out / retryable responses | 0 / 0 / 0 | 0 / 0 / 0 |
| Final reconciled count | 12,000 Matched | 12,000 Matched |
| Final reconciled amount | 10,800,000,000 satang | 10,800,000,000 satang |

Receiver CPU HPA grew to six replicas. KEDA increased workers from one to six. The first run reconciled fully at 16:44:50 UTC. The recovery run ran from 16:46:48 to 16:49:34 UTC while builds/tests also used the shared computer; the larger scheduling lag and lower achieved rate are retained in its report.

During the second backlog, all six loan-worker pods were deleted. The Deployment replaced them. Recorded queue depth peaked at **3,430 ready messages** at 16:49:50 UTC while worker readiness was absent. All six replacement workers were ready by the subsequent drain samples; ready and unacknowledged queue counts reached zero at **16:50:31 UTC**, approximately 57 seconds after delivery completion. Workers returned to one at **16:52:08 UTC**; receivers returned to two at **16:52:33 UTC**. At 16:53:53 UTC the complete settlement comparison showed all 12,000 matched transactions.

A direct read-only ledger check after recovery found **12,000 distinct bank transaction IDs, 12,000 payment journals, and zero unbalanced journals**. Repeated payments exceeding the one eligible monthly installment correctly became `AppliedWithCredit`; reconciliation includes this applied financial outcome, rather than treating customer credit as missing money.

Local raw artifacts: `artifacts/scaling-20261006-163712.jsonl`, `artifacts/scaling-20261006-164650.jsonl`, both `spike-results-*.json`, and both `spike-reconciliation*.json`. Compact results are retained in [validation-results.json](validation-results.json). Reproduce collection with `scripts/observe-scaling.ps1 -StartSpike`.

## Practical limits and remaining coverage

- RabbitMQ management does not expose oldest message age. The dashboard explicitly displays unavailable; the actual oldest pending-outbox timestamp is available separately. Historical latency before histogram instrumentation was deployed was not collected.
- Prometheus p95 acceptance/application latency comes from real histogram samples. Idle/reset series can produce NaN, displayed as unavailable. Charts accumulate observations while the page is open; reopening does not reconstruct historical browser samples.
- Payment status filters apply to the displayed page of 50 unique transactions, as labelled. Failure search filters loaded records, with review/conflict/audit sources bounded to their latest 100 records. Settlement counts and amount totals cover the complete run; its displayed actionable sample is limited to 500 rows.
- The financial serialization order is loan-lock acquisition order. Independent receiver replicas do not guarantee global network-arrival ordering; the financial policy documents this boundary.
- Docker Desktop shares CPU/RAM across all pods and concurrent Compose/build workloads. Readiness timeouts were increased to five seconds after load-related failures; PostgreSQL allows 200 sessions and service pools are capped at five. There is no artificial CPU work to induce scaling.
- The sample history contains occasional unavailable broker metrics under load. Missing samples are retained as unavailable. Peak pending-pod/resource-saturation history was not exhaustively captured; current deployment availability and actual HPA/queue/replica changes were checked.
- PostgreSQL and RabbitMQ are single persistent instances, not high availability. Linux container images were exercised on Docker Desktop; host Bash scripts were not separately executed on a Linux machine. Remote-cluster image push/deployment was not tested.
- Compose observability is optional; its Prometheus, Grafana and Jaeger services were started and checked healthy. Kubernetes Prometheus was exercised; Grafana/Jaeger viewer links require the optional Compose viewers or equivalent configured deployments. A locally signed demo session is intended for this fictional local application.
- Earlier simulations and integration loans remain visible. Use a disclosed existing run or the explicitly labelled destructive reset before presenting a fresh first-installment example.
- Vite reports nonfatal chunks over 500 KB for the UI/chart libraries. Build, type checking and browser execution pass.

The milestone checklist in [TASK.md](../TASK.md) preserves unchecked items where the complete requested telemetry/coverage was not observed.
