"""Read-only registration of the explicitly failed Desktop VM-bind template fixture."""
import hashlib
import json
import pathlib
import sqlite3
import subprocess
import desktop_operations as operations
from desktop_operations import login, request, run_action
from desktop_mutations import recover

assert json.loads(pathlib.Path('/test/private/panel.json').read_text())['desktopValidation']
with sqlite3.connect('/test/state/panel.db') as database:
    records = [json.loads(row[0]) for row in database.execute('select document from instances')]
    candidates = [record for record in records if record['owned'] and not record.get('worldGuid') and not record.get('purgedUtc')]
    assert len(candidates) == 1, 'requires the isolated initial VM-bind failure fixture'
    original = candidates[0]
    row = database.execute("select id from tasks where instanceId=? and state='NeedsAttention'", (original['id'],)).fetchone()
root = pathlib.Path(original['root'])
assert root.is_relative_to('/test/instances')
operations.instance_id = original['id']
login()
task = json.loads(request('/api/v1/tasks/' + row[0])[1])
assert task['phase'] == 'InstallAndStart'
assert recover(task, 'acknowledge-safe')['state'] == 'Failed'
container = subprocess.check_output(['docker', 'ps', '-aq', '--filter', 'label=com.docker.compose.project=' + original['project']]).decode().strip()
before = json.loads(subprocess.check_output(['docker', 'inspect', container]))[0]
assert not before['State']['Running']
def hashes():
    return {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in ['compose.yaml', 'settings.env', 'secrets.env']}
baseline = hashes()
run_action('unmanage')
csrf = login()
status, raw, _ = request('/api/v1/discovery/register', 'POST', {'containerId': before['Id']}, {'X-CSRF-Token': csrf})
assert status == 200
registered = json.loads(raw)
assert not registered['configurationKnown'] and not registered['owned'] and not registered['writable']
assert registered['resourceBudgetKnown'] and registered['desired']['memoryMiB'] == 4096
assert request('/api/v1/instances/' + registered['id'] + '/settings', 'PATCH', registered['desired'], {'X-CSRF-Token': csrf, 'If-Match': str(registered['revision'])})[0] == 409
assert request('/api/v1/instances/' + registered['id'] + '/previews', 'POST', {'kind': 'adopt', 'arguments': {'externalSchedulesDisabled': True}}, {'X-CSRF-Token': csrf})[0] == 409
after = json.loads(subprocess.check_output(['docker', 'inspect', container]))[0]
assert after['Id'] == before['Id'] and after['State']['StartedAt'] == before['State']['StartedAt'] and not after['State']['Running']
assert hashes() == baseline
print('PASS unsupported existing template stays read-only; actual 4 GiB resource budget retained; no file/container mutation', flush=True)
