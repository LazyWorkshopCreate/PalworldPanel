"""Real-world restore stays pending; rollback restores its verified recovery point."""
import json
from desktop_operations import instance_id, login, request, run_action

login()
backups = json.loads(request('/api/v1/instances/' + instance_id + '/backups')[1])
task = run_action('restore', {'backupId': backups[0]['id']}, 'NeedsAttention')
assert task['safeCode'] == 'PlayerVerificationPending'
csrf = login()
body = {'resolution': 'confirm-players', 'originalPlayersVerified': False}
headers = {'X-CSRF-Token': csrf}
status, raw, _ = request('/api/v1/tasks/' + task['id'] + '/recovery-preview', 'POST', body, headers)
assert status == 200
preview = json.loads(raw)
body.update(confirmation=preview['token'], previewHash=preview['hash'])
assert request('/api/v1/tasks/' + task['id'] + '/recover', 'POST', body, headers)[0] == 409
print('PASS automated restore remains pending; false player confirmation rejected', flush=True)
body = {'resolution': 'rollback'}
status, raw, _ = request('/api/v1/tasks/' + task['id'] + '/recovery-preview', 'POST', body, headers)
assert status == 200
preview = json.loads(raw)
body.update(confirmation=preview['token'], previewHash=preview['hash'])
status, raw, _ = request('/api/v1/tasks/' + task['id'] + '/recover', 'POST', body, headers, timeout=180)
assert status == 200, status
assert json.loads(raw)['state'] == 'RolledBack'
assert json.loads(request('/api/v1/instances/' + instance_id)[1])['desiredPower'] == 'stopped'
run_action('start')
assert json.loads(request('/api/v1/instances/' + instance_id + '/observations')[1])['gameApi'] == 'healthy'
print('PASS rollback keeps stopped; explicit start validates original world again', flush=True)
