"""Read-only preview and stale/tampered creation rejection before any instance allocation."""
import json
import pathlib
import uuid
from desktop_operations import instance_id, login, request

assert json.loads(pathlib.Path('/test/private/panel.json').read_text())['desktopValidation']
csrf = login()
source = json.loads(request('/api/v1/instances/' + instance_id)[1])
rules = dict(source['desired'])
rules['name'] = 'desktop preview only'
rules['memoryMiB'] = 512  # Preview only; keep this negative fixture usable after the real clone commits its budget.
headers = {'X-CSRF-Token': csrf, 'Idempotency-Key': str(uuid.uuid4())}
creation = {'rules': rules, 'cloneId': instance_id}
assert request('/api/v1/creation-previews', 'POST', {'rules': None}, headers)[0] == 400
assert request('/api/v1/instances', 'POST', creation, headers)[0] == 409
before = json.loads(request('/api/v1/instances')[1])
status, raw, _ = request('/api/v1/creation-previews', 'POST', creation, headers)
assert status == 200
preview = json.loads(raw)
root = pathlib.Path(preview['plan']['root'])
assert not root.exists() and preview['plan']['backupTime'] in ['05:00', '05:15']
assert len({preview['plan'][name] for name in ['gamePort', 'restPort', 'queryPort']}) == 3
creation.update(plan=preview['plan'], confirmation=preview['token'], previewHash=preview['hash'])
tampered = dict(creation, rules=dict(rules, name='changed after preview'))
assert request('/api/v1/instances', 'POST', tampered, headers)[0] == 409
# Same rules draft, newer source revision: metadata change only, no game/config writes.
assert request('/api/v1/instances/' + instance_id + '/settings', 'PATCH', source['desired'], {'X-CSRF-Token': csrf, 'If-Match': str(source['revision'])})[0] == 200
status, raw, _ = request('/api/v1/instances', 'POST', creation, headers)
assert status == 409 and json.loads(raw)['code'] == 'PreviewChanged'
assert not root.exists()
assert [item['id'] for item in json.loads(request('/api/v1/instances')[1])] == [item['id'] for item in before]
oversized = dict(rules, memoryMiB=1024 * 1024)
assert request('/api/v1/creation-previews', 'POST', {'rules': oversized}, headers)[0] == 409
print('PASS creation preview has no allocation; missing/tampered/stale source and overbudget rejected', flush=True)
