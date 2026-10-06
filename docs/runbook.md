# Recovery runbook

Before recovery, inspect receipt publication separately from allocation. A receipt HTTP 200 is durable acceptance. Unknown references are not infrastructure retries. Record the original bank transaction ID; replay never invents a replacement payment.

| Situation | Action | Evidence |
| --- | --- | --- |
| RabbitMQ unavailable | Restore the same broker/node/volume; publishers recover leased outbox rows | PublishedAt becomes non-null, queue drains, application appears |
| Loan worker crashes | Let Deployment/Compose replace it; unacked delivery recovers | Same processing identity, one payment journal |
| Unknown reference | Admin resolve to an existing loan then replay | OriginalReference retained; audited mapping and status transition |
| Infrastructure retry exhaustion | Fix dependency then Admin replay failed-envelope record | Same payload and ID; previous applied result wins |
| Missing receipt | Reconcile selected run past grace, redeliver original | One matched count/amount, irrespective of attempts |
| Conflicting immutable fields | Review source identity; preserve posted outcome | Conflict record; no silent replacement |
| Excess payment | Inspect UnappliedCredit and liability posting | Future principal untouched; separate review |
| KEDA unknown metrics | Check operator, external metrics API and namespace-qualified broker DNS | ScaledObject Ready and HPA ScalingActive conditions |
| Dashboard metrics unavailable | Check dependency readiness and Prometheus; inspect freshness | Explicit unavailable/null rather than invented counters |

RabbitMQ uses a fixed node name/hostname (`rabbit@rabbitmq`) so its persistent Mnesia store survives container/pod replacement. Changing node names creates a different broker store and must not be used for normal recovery. Use normal stop/start or Deployment replacement, and preserve volumes/PVCs.

Useful diagnostics:

```powershell
docker compose --env-file .env -f deploy/compose/compose.yaml logs --tail 100 webhook loan-worker
kubectl -n lms-demo describe hpa keda-hpa-loan-worker
kubectl -n lms-demo describe scaledobject loan-worker
kubectl -n keda logs deployment/keda-operator --tail=100
kubectl -n lms-demo get events --sort-by=.metadata.creationTimestamp
```

For exact commit/ACK crash injection in an isolated demo, set `CRASH_AFTER_COMMIT=true` on a loan worker, deliver one payment, then remove the setting before restarting. The process deliberately terminates after financial commit and before acknowledgement. Do not leave it enabled during normal simulations.
