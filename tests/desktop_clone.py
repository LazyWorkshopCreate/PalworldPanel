"""Configuration cloning creates a separate real world and independently encrypted secrets."""
import json
import pathlib
import sqlite3
import time
import uuid
import sys
import desktop_operations as operations
from desktop_operations import instance_id, login, request

csrf = login()
source = json.loads(request('/api/v1/instances/' + instance_id)[1])
rules = dict(source['desired'])
rules['name'] = 'panel clone validation'
headers = {'X-CSRF-Token':csrf,'Idempotency-Key':str(uuid.uuid4())}
if '--resume' in sys.argv:
    existing_id = pathlib.Path('/test/private/clone-instance').read_text()
    task = next(task for task in json.loads(request('/api/v1/tasks')[1]) if task['instanceId'] == existing_id and task['kind'] == 'create')
else:
    creation = {'rules': rules, 'cloneId': instance_id}
    status, raw, _ = request('/api/v1/creation-previews', 'POST', creation, headers)
    assert status == 200
    preview = json.loads(raw)
    assert not pathlib.Path(preview['plan']['root']).exists()
    creation.update(plan=preview['plan'], confirmation=preview['token'], previewHash=preview['hash'])
    status, raw, _ = request('/api/v1/instances','POST',creation,headers)
    assert status == 202, ('clone',status)
    task = json.loads(raw)
    assert request('/api/v1/instances','POST',creation,headers)[0] == 202
clone_id = task['instanceId']
pathlib.Path('/test/private/clone-instance').write_text(clone_id)
root = pathlib.Path('/test/instances') / clone_id
assert root != pathlib.Path('/test/instances') / instance_id
last = None
for _ in range(900):
    current = json.loads(request('/api/v1/tasks/' + task['id'])[1])
    if current['phase'] != last:
        print('clone phase: ' + current['phase'],flush=True)
        last = current['phase']
    if current['state'] not in ['Queued','Running']:
        assert current['state'] == 'Succeeded', (current['state'],current['safeCode'])
        break
    time.sleep(2)
else: raise AssertionError('clone startup timeout')
clone = json.loads(request('/api/v1/instances/'+clone_id)[1])
operations.instance_id = clone_id
operations.run_action('save')
assert clone['worldGuid'] != source['worldGuid']
assert clone['gamePort'] != source['gamePort']
assert clone['desired']['maxPlayers'] == source['desired']['maxPlayers']
assert request('/api/v1/instances/'+clone_id+'/backups')[1] == b'[]'
assert [path.name for path in (root/'data/Pal/Saved/SaveGames/0').iterdir()] == [clone['worldGuid']]
with sqlite3.connect('/test/state/panel.db') as connection:
    source_record = json.loads(connection.execute('select document from instances where id=?',(instance_id,)).fetchone()[0])
    clone_record = json.loads(connection.execute('select document from instances where id=?',(clone_id,)).fetchone()[0])
assert source_record['adminCipher'] != clone_record['adminCipher'] and source_record['gameCipher'] != clone_record['gameCipher']
print('PASS configuration clone: independent GUID, ports, directory, encrypted credentials and empty backup history',flush=True)
