# Events, API and statuses

## Bank HTTP contract

`POST /webhooks/bank/payments` is the anonymous signed bank endpoint. Bank scope is the single configured `demo-bank`; separate bank onboarding/secrets is outside this fictional demo. Headers:

- `X-Bank-Timestamp`: integer current Unix seconds; allowed clock skew is ±300 seconds.
- `X-Bank-Signature`: hex HMAC-SHA256 of UTF-8 delivery timestamp + literal `.` + **exact raw request-body bytes**.
- Optional `X-Correlation-ID`: at most 128 letters/digits, hyphens or underscores.

```json
{"eventId":"evt-001","bankTransactionId":"bank-tx-001","paymentReference":"LOAN-1001","amountMinor":"900000","currency":"THB","status":"SUCCEEDED","paidAt":"2026-10-31T16:59:00+07:00"}
```

Identity/reference length limit: 128. Body limit: 8 KiB. Amount must be a positive decimal-digit satang string within the documented bound. Status is SUCCEEDED or FAILED; currency THB. Signatures compare in constant time. Signed valid delivery: 200 with Received or Duplicate; conflicting event reuse: 409 plus review; invalid signature/freshness: 401; signed invalid contract: 400; body too large: 413; persistence unavailable: 503 and Retry-After 5. No rejected signature/schema creates a financial event. Signature failures are not persisted as receipt records; signed schema failures have a separate validation record.

Retries retain raw body, event ID, bank transaction ID and bank paidAt, but create fresh delivery timestamp/signature. Compare normalized transaction identity, reference, integer amount, currency, bank outcome and paidAt UTC when an event/transaction identity repeats.

## RabbitMQ schema version 1

`PaymentReceived`: eventId, bankTransactionId, bankId, occurredAt UTC, schemaVersion, correlationId, traceParent, normalized bank payment, receivedAt UTC and optional runId. Routing queue `lms.payments`.

`PaymentResult`: eventId (`result:<processing ID>:<status>`), bankTransactionId, bankId, occurredAt UTC, schemaVersion, correlationId, traceParent, paymentReference, status, amountMinor string and optional runId. Routing queue `lms.notifications`.

New simulator bodies include runId, propagated through both event envelopes and retained in their persisted JSON. It is additive schema-version-1 context; external one-off payments and older recorded bodies can omit it. It does not change normalized financial deduplication. LoanService's read-only run aggregate also recognizes the documented `tx-<run GUID without separators>-<six-digit sequence>` identity format for earlier simulator settlements. Simulator shows this observed application count separately from delivery/receipt counts.

Queues are durable. Messages are persistent, mandatory and confirmed. `attempt` is bounded retry metadata, not a new financial identity. `<queue>.retry.5000`, `.15000`, `.60000` dead-letter back to the original queue. `<queue>.failed` drains into a service-owned visible failed-envelope record. Replay publishes the original envelope/identity.

## Independent status dimensions

| Dimension | States / meaning |
| --- | --- |
| Bank outcome | SUCCEEDED / FAILED; original bank timestamp retained |
| Receipt | Received, Duplicate response, Conflict review |
| Publication | pending lease/backoff or confirmed publishedAt |
| Allocation | Pending (receipt without observed worker result), Applied, AppliedWithCredit, Unmatched, BankDeclined |
| Review | conflicting immutable fields/outcomes, unapplied credit, materially late bank timestamp |
| Notification | Created in-app record; unobserved stage explicitly pending/unavailable |
| Simulator | Running, Completed, Stopped; attempts vs unique planned payments separately persisted |
| Reconciliation | Matched, NotYetApplied, MissingReceipt, UnmatchedReference, AmountConflict, OutcomeConflict |

Bank failure never reverses a success. AppliedWithCredit is a completed posting with a separate visible credit review. Replay of an applied payment returns the earlier result. Replay of unmatched payment can proceed after an audited mapping. Reconciliation compares unique bank transactions, not attempt counts; cutoff excludes later applied timestamps and grace protects in-flight work. Count and satang totals cover the full selected run, while the actionable response limits displayed rows to 500.

## REST and authorization

Authenticated `/api/v1`: loan list/detail, payment list/detail, receipt queries, notification queries, summaries, failures, simulator presets/run history/results/settlement, and audit. Loan/receipt lists support bounded pagination. OpenAPI is authenticated at `/openapi/v1.json` on each service. DemoApi exposes matching aggregate/proxy routes, overview, metrics and reconciliation.

Admin only: start/stop simulation, resolve reference, replay payment/failed envelope, redeliver original bank transaction. Observer can view all dashboards. Authorization runs in both BFF and service endpoints. Sessions are eight-hour HMAC-signed bearer/cookies; the browser cookie is HttpOnly, SameSite Strict, and Secure on HTTPS. Mutations reject a cross-origin Origin. Original user sessions are forwarded for audit attribution. Health and Prometheus routes are intended for the local/internal network.

External reference documentation: [RabbitMQ .NET publisher confirms](https://www.rabbitmq.com/tutorials/tutorial-seven-dotnet), [RabbitMQ .NET client guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide), [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [KEDA RabbitMQ scaler](https://keda.sh/docs/2.18/scalers/rabbitmq-queue/).
