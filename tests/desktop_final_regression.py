"""Desktop-only current Config preservation and lowercase world import regression."""
import hashlib
import json
import pathlib
import sqlite3
import zipfile
import desktop_operations as operations
from desktop_operations import login, request, run_action
from desktop_mutations import recover, upload

assert json.loads(pathlib.Path('/test/private/panel.json').read_text())['desktopValidation']
identifier = operations.instance_id
with sqlite3.connect('/test/state/panel.db') as database:
    document = json.loads(database.execute('select document from instances where id=?', (identifier,)).fetchone()[0])
root = pathlib.Path(document['root'])
assert root.is_relative_to('/test/instances')
login()
original = json.loads(request('/api/v1/instances/' + identifier)[1])
records = json.loads(request('/api/v1/instances/' + identifier + '/backups')[1])
run_action('stop')
config = root / 'data/Pal/Saved/Config/LinuxServer/PalWorldSettings.ini'
with config.open('a') as output:
    output.write('\n; desktop config retention marker\n')
expected = hashlib.sha256(config.read_bytes()).hexdigest()
task = run_action('restore', {'backupId': records[0]['id']}, 'NeedsAttention')
assert hashlib.sha256(config.read_bytes()).hexdigest() == expected, 'restore must keep current target Config'
assert recover(task)['state'] == 'RolledBack'
archive = pathlib.Path('/test/private/lowercase-world.zip')
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as output:
    world = root / 'data/Pal/Saved/SaveGames/0' / original['worldGuid']
    for path in world.rglob('*'):
        if path.is_file():
            output.write(path, 'Saved/SaveGames/0/' + original['worldGuid'].lower() + '/' + path.relative_to(world).as_posix())
    output.writestr('Saved/Config/LinuxServer/PalWorldSettings.ini', 'ignored synthetic source config')
plan = upload(archive)
assert plan['worlds'] == [original['worldGuid'].lower()]
task = run_action('import', {'uploadId': plan['uploadId'], 'sha256': plan['sha256'], 'world': plan['worlds'][0]}, 'NeedsAttention')
assert task['safeCode'] == 'PlayerVerificationPending'
assert (root / 'data/Pal/Saved/SaveGames/0' / original['worldGuid']).is_dir()
assert hashlib.sha256(config.read_bytes()).hexdigest() == expected
recover(task, 'start-verification')
assert recover(task)['state'] == 'RolledBack'
run_action('start')
login()
host = json.loads(request('/api/v1/host')[1])
assert host['usedMemoryBytes'] > 0 and host['availableMemoryBytes'] > 0
assert host['systemSampleUtc'] and 0 <= host['cpuPercent'] <= 100
print('PASS current Config retained; lowercase import normalized; verification/rollback; real host sampling', flush=True)
