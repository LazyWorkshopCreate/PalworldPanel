"""Desktop synthetic age fixture proves final purge keeps the independent final backup."""
import datetime
import hashlib
import json
import pathlib
import sqlite3
import subprocess
import sys
import desktop_operations as operations
from desktop_operations import login, request, run_action

assert json.loads(pathlib.Path('/test/private/panel.json').read_text())['desktopValidation']
original_id = operations.instance_id
login()
original = json.loads(request('/api/v1/instances/' + original_id)[1])
container = subprocess.check_output(['docker', 'ps', '-q', '--filter', 'label=com.docker.compose.project=' + original['project']]).decode().strip()
before = json.loads(subprocess.check_output(['docker', 'inspect', container]))[0]
operations.instance_id = pathlib.Path('/test/private/clone-instance').read_text()
clone = json.loads(request('/api/v1/instances/' + operations.instance_id)[1])
assert clone['owned']
if '--resume' not in sys.argv:
    assert clone['writable'] and not clone['purgedUtc']
    task = run_action('purge')
    with sqlite3.connect('/test/state/panel.db') as database:
        document = json.loads(database.execute('select document from instances where id=?', (operations.instance_id,)).fetchone()[0])
        root = pathlib.Path(document['root'])
        assert root == pathlib.Path('/test/instances/.quarantine') / operations.instance_id
        marker = root / 'quarantine.json'
        record = json.loads(marker.read_text())
        assert record['id'] == operations.instance_id and record['backupId'] == task['recoveryPoint']
        # No host clock alteration; only this synthetic instance's retention timestamps are aged.
        past = (datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(days=8)).isoformat()
        document['quarantinedUtc'] = record['utc'] = past
        marker.write_text(json.dumps(record))
        database.execute('update instances set document=? where id=?', (json.dumps(document), operations.instance_id))
    run_action('finalize-purge')
else:
    assert clone['purgedUtc'], 'resume requires an already finalized synthetic clone'
    with sqlite3.connect('/test/state/panel.db') as database:
        document = json.loads(database.execute('select document from instances where id=?', (operations.instance_id,)).fetchone()[0])
        point, started = database.execute("select recoveryPoint,createdUtc from tasks where instanceId=? and kind='purge' and state='Succeeded' order by createdUtc desc limit 1", (operations.instance_id,)).fetchone()
    record = {'backupId': point}
    root = pathlib.Path(document['root'])
    assert datetime.datetime.fromisoformat(before['State']['StartedAt'].replace('Z', '+00:00')) < datetime.datetime.fromisoformat(started.replace('Z', '+00:00'))
assert not root.exists()
with sqlite3.connect('/test/state/panel.db') as database:
    current = json.loads(database.execute('select document from instances where id=?', (operations.instance_id,)).fetchone()[0])
    backup = json.loads(database.execute('select document from backups where id=?', (record['backupId'],)).fetchone()[0])
assert current['purgedUtc'] and backup['protected']
path = pathlib.Path(backup['path'])
assert path.is_relative_to('/test/backups') and path.is_dir()
assert hashlib.sha256((path / 'manifest.json').read_bytes()).hexdigest().lower() == backup['sha256'].lower()
after = json.loads(subprocess.check_output(['docker', 'inspect', container]))[0]
assert after['Id'] == before['Id'] and after['State']['StartedAt'] == before['State']['StartedAt'] and after['State']['Running']
csrf = login()
password = pathlib.Path('/test/private/export-passphrase').read_text()
status, raw, _ = request('/api/v1/instances/' + operations.instance_id + '/backups/' + backup['id'] + '/export-prepare', 'POST', {'passphrase': password}, {'X-CSRF-Token': csrf}, timeout=180)
assert status == 200
assert request(json.loads(raw)['downloadUrl'], timeout=180)[0] == 200
assert not root.exists(), 'export must not recreate a purged root'
print('PASS synthetic eight-day fixture final purge; independent protected backup export; other game unchanged', flush=True)
