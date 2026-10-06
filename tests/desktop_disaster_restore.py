"""Decrypt a real panel snapshot, quarantine copied tasks, boot isolated metadata without replay."""
import datetime
import json
import os
import pathlib
import shutil
import sqlite3
import subprocess
import time
import urllib.error
import uuid
import zipfile
import desktop_access as access
from desktop_operations import login, request

assert json.loads(pathlib.Path('/test/private/panel.json').read_text())['desktopValidation']
assert not pathlib.Path('/test/disaster-restored').exists(), 'fixture already exists'
login()
csrf = login()
password = pathlib.Path('/test/private/export-passphrase').read_text()
status, raw, _ = request('/api/v1/panel-backups/export-prepare', 'POST', {'passphrase': password}, {'X-CSRF-Token': csrf}, timeout=180)
assert status == 200
status, encrypted, _ = request(json.loads(raw)['downloadUrl'], timeout=180)
assert status == 200 and encrypted.startswith(b'PPBAK001')
pathlib.Path('/test/private/disaster-export.ppbak').write_bytes(encrypted)
environment = os.environ.copy()
environment['PANEL_EXPORT_PASSPHRASE'] = password
def command(*args, **kwargs):
    return subprocess.check_output(['docker', *args], **kwargs)
print(command('run', '--rm', '--label', 'com.palworldpanel.test=panel', '-e', 'PANEL_EXPORT_PASSPHRASE', '-v', 'palworldpanel-panel-validation:/test', 'palworldpanel:local', '--decrypt-export', '/test/private/disaster-export.ppbak', '/test/private/disaster-export.zip', env=environment).decode().strip(), flush=True)
directory = pathlib.Path('/test/private/disaster-extract')
directory.mkdir(mode=0o700)
with zipfile.ZipFile('/test/private/disaster-export.zip') as archive:
    for entry in archive.infolist():
        assert (directory / entry.filename).resolve().is_relative_to(directory.resolve())
    archive.extractall(directory)
manifest = json.loads((directory / 'disaster-manifest.json').read_text())
assert manifest['restoreRequiresTaskReview'] and not manifest['includesGameData']
for name in ['master-key', 'administrator.json', 'tls.pfx', 'tls-password']:
    assert (directory / 'private' / name).stat().st_size > 0
assert list((directory / 'session-keys').glob('*.xml'))
restored = pathlib.Path('/test/disaster-restored')
for name in ['state', 'private', 'instances', 'backups']:
    (restored / name).mkdir(mode=0o700, parents=True, exist_ok=True)
shutil.copy2(directory / 'panel.db', restored / 'state/panel.db')
shutil.copytree(directory / 'session-keys', restored / 'state/session-keys')
for path in (directory / 'private').iterdir():
    shutil.copy2(path, restored / 'private' / path.name)
    (restored / 'private' / path.name).chmod(0o600)
database_path = restored / 'state/panel.db'
identifiers = [pathlib.Path('/test/private/lifecycle-instance').read_text(), pathlib.Path('/test/private/clone-instance').read_text()]
with sqlite3.connect(database_path) as database:
    assert database.execute('pragma integrity_check').fetchone()[0] == 'ok'
    assert database.execute('pragma user_version').fetchone()[0] == 1
    for identifier, state in zip(identifiers, ['Queued', 'Running']):
        token = uuid.uuid4().hex
        database.execute('insert into tasks(id,instanceId,kind,state,phase,payload,user,idempotencyKey,requestHash,createdUtc) values(?,?,?,?,?,?,?,?,?,?)',
                         (token, identifier, 'stop', state, 'Stop', '{}', 'admin', token, token, datetime.datetime.now(datetime.timezone.utc).isoformat()))
print(command('run', '--rm', '--label', 'com.palworldpanel.test=panel', '-v', 'palworldpanel-panel-validation:/test', 'palworldpanel:local', '--quarantine-pending-tasks', str(database_path)).decode().strip(), flush=True)
with sqlite3.connect(database_path) as database:
    assert database.execute("select count(*) from tasks where state in ('Queued','Running')").fetchone()[0] == 0
    assert database.execute("select count(*) from tasks where safeCode='DatabaseRestored' and state='NeedsAttention'").fetchone()[0] == 2
options = json.loads((directory / 'panel-options.json').read_text())
options.update(port=18081, stateRoot=str(restored / 'state'), instanceRoots=[str(restored / 'instances')], backupRoot=str(restored / 'backups'),
               keyFile=str(restored / 'private/master-key'), administratorFile=str(restored / 'private/administrator.json'),
               certificateFile=str(restored / 'private/tls.pfx'), certificatePasswordFile=str(restored / 'private/tls-password'))
configuration = restored / 'private/panel.json'
configuration.write_text(json.dumps(options))
configuration.chmod(0o600)
def game_states():
    containers = command('ps', '-aq', '--filter', 'label=com.docker.compose.service=palworld').decode().split()
    return {item['Id']: (item['State']['StartedAt'], item['State']['Running']) for item in json.loads(command('inspect', *containers))} if containers else {}
before = game_states()
command('run', '-d', '--name', 'palworldpanel-validation-restored', '--label', 'com.palworldpanel.test=panel', '--network', 'host', '-v', 'palworldpanel-panel-validation:/test', '-v', '/var/run/docker.sock:/var/run/docker.sock', '-e', 'PANEL_CONFIG_FILE=' + str(configuration), 'palworldpanel:local')
access.base = 'https://172.30.88.1:18081'
for _ in range(60):
    try:
        if request('/')[0] == 200:
            break
    except (urllib.error.URLError, TimeoutError):
        pass
    time.sleep(1)
else:
    raise AssertionError('restored panel did not start')
login()
assert request('/api/v1/instances')[0] == 200
tasks = json.loads(request('/api/v1/tasks')[1])
assert not any(task['state'] in ['Queued', 'Running'] for task in tasks)
assert game_states() == before
assert not list((restored / 'instances').iterdir()), 'restored metadata must not generate replacement game directories'
print('PASS encrypted disaster archive integrity/keys; copied queued/running tasks quarantined; isolated panel boot; no game replay', flush=True)
