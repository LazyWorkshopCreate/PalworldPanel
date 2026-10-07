"""Real isolated ZIP import and full installation upgrade rollback; never simulates player acceptance."""
import hashlib
import json
import pathlib
import urllib.request
import zipfile
from desktop_operations import instance_id, login, request, run_action
from desktop_access import base, client

def recover(task, resolution='rollback'):
    csrf = login()
    body = {'resolution': resolution}
    headers = {'X-CSRF-Token': csrf}
    status, raw, _ = request('/api/v1/tasks/' + task['id'] + '/recovery-preview', 'POST', body, headers)
    assert status == 200, (resolution, status)
    preview = json.loads(raw)
    body.update(confirmation=preview['token'], previewHash=preview['hash'])
    status, raw, _ = request('/api/v1/tasks/' + task['id'] + '/recover', 'POST', body, headers, timeout=600)
    assert status == 200, (resolution, status, json.loads(raw).get('code'))
    return json.loads(raw)

def upload(path):
    csrf = login()
    req = urllib.request.Request(base + '/api/v1/instances/' + instance_id + '/uploads',
        data=path.read_bytes(), method='POST', headers={'Origin': base, 'X-CSRF-Token': csrf, 'Content-Type':'application/zip'})
    with client.open(req, timeout=120) as response:
        assert response.status == 200
        return json.loads(response.read())

if __name__ == '__main__':
    login()
    original = json.loads(request('/api/v1/instances/' + instance_id)[1])
    run_action('stop')
    root = pathlib.Path('/test/instances') / instance_id
    config = root / 'data/Pal/Saved/Config/LinuxServer/PalWorldSettings.ini'
    config_hash = hashlib.sha256(config.read_bytes()).hexdigest()
    archive = pathlib.Path('/test/private/import-world.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as output:
        world_root = root / 'data/Pal/Saved/SaveGames/0' / original['worldGuid']
        for path in world_root.rglob('*'):
            if path.is_file(): output.write(path, 'Saved/SaveGames/0/' + original['worldGuid'] + '/' + str(path.relative_to(world_root)))
        output.writestr('Saved/Config/LinuxServer/PalWorldSettings.ini', 'source-secret-must-not-apply')
        other = 'F' * 32
        output.writestr('Saved/SaveGames/0/' + other + '/Level.sav', b'synthetic-unselected')
        output.writestr('Saved/SaveGames/0/' + other + '/LevelMeta.sav', b'synthetic-unselected-meta')
    plan = upload(archive)
    assert len(plan['worlds']) == 2 and 'Config' in plan['ignored']
    task = run_action('import', {'uploadId':plan['uploadId'], 'sha256':plan['sha256'], 'world':original['worldGuid']}, 'NeedsAttention')
    assert task['safeCode'] == 'PlayerVerificationPending'
    assert hashlib.sha256(config.read_bytes()).hexdigest() == config_hash
    assert json.loads(request('/api/v1/instances/' + instance_id)[1])['desiredPower'] == 'stopped'
    recover(task, 'start-verification')
    assert json.loads(request('/api/v1/tasks/' + task['id'])[1])['state'] == 'NeedsAttention'
    assert recover(task)['state'] == 'RolledBack'
    run_action('start')
    print('PASS multi-world ZIP selection, ignored source Config, stopped import, verification gate and rollback', flush=True)
    current = json.loads(request('/api/v1/instances/' + instance_id)[1])
    status, raw, _ = request('/api/v1/instances/' + instance_id + '/update-check', 'POST', {}, {'X-CSRF-Token': login()})
    assert status == 200, ('update-check', status)
    update = json.loads(raw)
    if update['status'] == 'available':
        task = run_action('upgrade', {'updateToken': update['updateToken']}, 'NeedsAttention')
        assert task['safeCode'] == 'PlayerVerificationPending'
        assert recover(task)['state'] == 'RolledBack'
    else:
        assert update['status'] == 'up-to-date' and update['updateToken'] is None
        print('PASS current installation does not create unnecessary upgrade', flush=True)
    if update['status'] == 'available':
        run_action('start')
    assert json.loads(request('/api/v1/instances/' + instance_id)[1])['worldGuid'] == original['worldGuid']
    print('PASS update check and available-target upgrade/rollback branch; no simulated player acceptance', flush=True)
