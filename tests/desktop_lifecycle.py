"""Real isolated game lifecycle through the panel; never prints payload secrets."""
import json
import pathlib
import time
import uuid
from desktop_access import request

password = pathlib.Path('/test/private/test-password').read_text()
assert request('/api/v1/session', 'POST', {'userName': 'admin', 'password': password})[0] == 204
csrf = json.loads(request('/api/v1/session')[1])['csrfToken']
headers = {'X-CSRF-Token': csrf, 'Idempotency-Key': str(uuid.uuid4())}
rules = {'name': 'panel-lifecycle-validation', 'description': 'synthetic isolated validation',
         'maxPlayers': 4, 'deathPenalty': 'None', 'offlinePenalty': False,
         'deteriorationRate': 0, 'attackDamageRate': 1, 'cpu': 4, 'memoryMiB': 4096}
creation = {'rules': rules}
status, body, _ = request('/api/v1/creation-previews', 'POST', creation, headers)
assert status == 200
preview = json.loads(body)
assert not pathlib.Path(preview['plan']['root']).exists()
creation.update(plan=preview['plan'], confirmation=preview['token'], previewHash=preview['hash'])
status, body, _ = request('/api/v1/instances', 'POST', creation, headers)
assert status == 202, status
task = json.loads(body)
pathlib.Path('/test/private/lifecycle-instance').write_text(task['instanceId'])
print('CREATE accepted; instance isolation allocated', flush=True)
last_phase = None
for attempt in range(900):
    status, body, _ = request('/api/v1/tasks/' + task['id'])
    assert status == 200, status
    current = json.loads(body)
    if current['phase'] != last_phase:
        print('CREATE phase: ' + current['phase'], flush=True)
        last_phase = current['phase']
    if current['state'] not in ['Running', 'Queued']:
        assert current['state'] == 'Succeeded', (current['state'], current['safeCode'])
        break
    time.sleep(2)
else:
    raise AssertionError('Create did not finish within 30 minutes')
print('PASS create: game REST and world identity validated', flush=True)
