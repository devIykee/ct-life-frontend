"""Tripo3D text-to-model for CT Island hero props (World lane, W3; dev-time only, budget 200 credits).

Usage (repo root):
  python3 -I tools/world/tripo_gen.py balance
  python3 -I tools/world/tripo_gen.py make <id> "<prompt>" [face_limit]
Reads TRIPO_API_KEY from ../ct-life-backend/.env (never printed). Writes art/raw/tripo/<id>/<id>.glb plus a receipt
(task id, prompt, model version, credits consumed) and appends the spend to art/raw/tripo/ledger.json.
"""
import json
import os
import sys
import time
import urllib.request

API = 'https://api.tripo3d.ai/v2/openapi'
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
ENV = os.path.abspath(os.path.join(REPO, '..', 'ct-life-backend', '.env'))
RAW = os.environ.get('CTL_RAW') or os.path.join(REPO, 'art', 'raw')
OUT = os.path.join(RAW, 'tripo')
MODEL_VERSION = os.environ.get('TRIPO_MODEL_VERSION', 'v3.1-20260211')
BUDGET = 200


def key():
    for line in open(ENV):
        if line.startswith('TRIPO_API_KEY='):
            return line.split('=', 1)[1].strip().strip('"').strip("'")
    raise SystemExit('TRIPO_API_KEY missing from the backend .env')


def call(path, body=None, method=None):
    req = urllib.request.Request(API + path, data=json.dumps(body).encode() if body is not None else None,
                                 method=method or ('POST' if body is not None else 'GET'),
                                 headers={'Authorization': 'Bearer ' + key(), 'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=60) as r:
        doc = json.loads(r.read())
    if doc.get('code') != 0:
        raise SystemExit(f'tripo error {doc.get("code")}: {doc.get("message")}')
    return doc['data']


def ledger():
    p = os.path.join(OUT, 'ledger.json')
    return (json.load(open(p)) if os.path.exists(p) else {'spent': 0, 'tasks': []}), p


def make(mid, prompt, face_limit=12000):
    led, lp = ledger()
    if led['spent'] >= BUDGET:
        raise SystemExit(f'budget used: {led["spent"]} / {BUDGET} credits')
    task = call('/task', dict(type='text_to_model', model_version=MODEL_VERSION, prompt=prompt, texture=True, pbr=False,
                              texture_quality='standard', face_limit=int(face_limit)))['task_id']
    print(f'[tripo] {mid}: task {task}', flush=True)
    t0 = time.time()
    while True:
        d = call(f'/task/{task}')
        if d['status'] in ('success', 'failed', 'cancelled', 'banned', 'expired'):
            break
        if time.time() - t0 > 900:
            raise SystemExit('timeout')
        time.sleep(6)
    credits = d.get('consumed_credit')
    led['spent'] += credits or 0
    led['tasks'].append(dict(id=mid, task=task, prompt=prompt, status=d['status'], credits=credits, model_version=MODEL_VERSION,
                             at=time.strftime('%Y-%m-%d %H:%M')))
    os.makedirs(OUT, exist_ok=True)
    json.dump(led, open(lp, 'w'), indent=1)
    if d['status'] != 'success':
        raise SystemExit(f'[tripo] {mid}: {d["status"]}')
    url = (d.get('output') or {}).get('pbr_model') or (d.get('output') or {}).get('model')
    dst = os.path.join(OUT, mid)
    os.makedirs(dst, exist_ok=True)
    with urllib.request.urlopen(url, timeout=120) as r:
        open(os.path.join(dst, mid + '.glb'), 'wb').write(r.read())
    json.dump(dict(id=mid, task=task, prompt=prompt, credits=credits, model_version=MODEL_VERSION, source='Tripo3D text_to_model',
                   licence='Tripo3D paid API output (CT Life account)', at=time.strftime('%Y-%m-%d')),
              open(os.path.join(dst, 'receipt.json'), 'w'), indent=1)
    print(f'[tripo] {mid}: ok, {credits} credits (total {led["spent"]} / {BUDGET})', flush=True)


if __name__ == '__main__':
    if sys.argv[1] == 'balance':
        b = call('/user/balance')
        led, _ = ledger()
        print(f'[tripo] balance {b.get("balance")} (frozen {b.get("frozen")}); World spent {led["spent"]} / {BUDGET}')
    elif sys.argv[1] == 'make':
        make(sys.argv[2], sys.argv[3], *(sys.argv[4:5]))
