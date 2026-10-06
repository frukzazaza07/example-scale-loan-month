#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; source .env; set +a
python3 - <<'PY' | kubectl apply -f -
import os,json,urllib.parse
keys=['AUTH_SECRET','BANK_SECRET','ADMIN_PASSWORD','OBSERVER_PASSWORD','POSTGRES_ADMIN_PASSWORD','WEBHOOK_DB_PASSWORD','LOAN_DB_PASSWORD','NOTIFICATION_DB_PASSWORD','SIMULATOR_DB_PASSWORD','RABBITMQ_USER','RABBITMQ_PASSWORD']
v={k:os.environ[k] for k in keys}
v['RABBITMQ_URL']='amqp://'+urllib.parse.quote(v['RABBITMQ_USER'],safe='')+':'+urllib.parse.quote(v['RABBITMQ_PASSWORD'],safe='')+'@rabbitmq.lms-demo.svc.cluster.local:5672'
for name in ['webhook','loan','notification','simulator']:
 v['DB_URL_'+name.upper()]=f"Host=postgres;Database={name};Username={name};Password={v[name.upper()+'_DB_PASSWORD']};Maximum Pool Size=5"
v['SEED_CONNECTION']='postgresql://loan:'+urllib.parse.quote(v['LOAN_DB_PASSWORD'],safe='')+'@postgres:5432/loan'
print(json.dumps(dict(apiVersion='v1',kind='Secret',metadata=dict(name='lms-secrets',namespace='lms-demo'),type='Opaque',stringData=v)))
PY


