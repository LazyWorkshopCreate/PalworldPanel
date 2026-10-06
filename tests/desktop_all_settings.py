"""Full-parameter HTTP draft checks in a separate synthetic database; no game tasks."""
import hashlib
import http.cookiejar
import json
import pathlib
import shutil
import sqlite3
import sys
import urllib.error
import urllib.request

root = pathlib.Path('/test/settings-integration')
if sys.argv[1] == 'prepare':
    assert not root.exists()
    root.mkdir(mode=0o700)
    for name in ['state', 'instances', 'backups']:
        (root / name).mkdir(mode=0o700)
    options = json.loads(pathlib.Path('/test/private/panel.json').read_text())
    options.update(port=18082, desktopAllowHttp=True, stateRoot=str(root / 'state'),
                   instanceRoots=[str(root / 'instances')], backupRoot=str(root / 'backups'))
    (root / 'options.json').write_text(json.dumps(options))
    (root / 'options.json').chmod(0o600)
    source = sqlite3.connect('/test/state/panel.db')
    destination = sqlite3.connect(root / 'state/panel.db')
    source.backup(destination)
    instance = json.loads(source.execute('select document from instances limit 1').fetchone()[0])
    (root / 'original-hash').write_text(hashlib.sha256(json.dumps(instance, sort_keys=True).encode()).hexdigest())
    for table in ['instances', 'tasks', 'backups', 'audit', 'taskEvents']:
        destination.execute('delete from ' + table)
    instance_root = root / 'instances' / instance['id']
    instance_root.mkdir(mode=0o700)
    for name, value in [('compose.yaml', '{}'), ('settings.env', 'DISABLE_GENERATE_SETTINGS=false\n'), ('secrets.env', '')]:
        (instance_root / name).write_text(value)
    instance.update(root=str(instance_root), writable=True, sourceHash=hashlib.sha256(
        b'{}\0DISABLE_GENERATE_SETTINGS=false\n\0\0').hexdigest().upper(), backupTime='23:59',
        worldGuid=None, restPort=65533, project='pp-settings-integration', desiredPower='stopped')
    instance['desired']['additional'] = None
    destination.execute('insert into instances values(?,?)', (instance['id'], json.dumps(instance)))
    destination.commit()
    destination.close()
    source.close()
    print('PASS separate synthetic settings fixture prepared; original instance unchanged')
elif sys.argv[1] == 'run':
    base = 'http://172.30.88.1:18082'
    jar = http.cookiejar.CookieJar()
    client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    def request(path, method='GET', body=None, headers=None):
        req = urllib.request.Request(base + path, method=method,
            data=json.dumps(body).encode() if body is not None else None,
            headers={'Origin': base, 'Content-Type': 'application/json', **(headers or {})})
        try:
            with client.open(req, timeout=15) as response: return response.status, response.read()
        except urllib.error.HTTPError as error: return error.code, error.read()
    assert request('/api/v1/session', 'POST', {'userName': 'admin',
        'password': pathlib.Path('/test/private/test-password').read_text()})[0] == 204
    csrf = json.loads(request('/api/v1/session')[1])['csrfToken']
    instance = json.loads(request('/api/v1/instances')[1])[0]
    path = '/api/v1/instances/' + instance['id'] + '/settings'
    rules = instance['desired']
    rules['additional'] = {'ExpRate': '2.5', 'CrossplayPlatforms': 'Steam,Xbox',
        'bEnableVoiceChat': 'true', 'DenyTechnologyList': 'PALBOX,RepairBench', 'FishingDifficultyRate': '0.5'}
    headers = {'X-CSRF-Token': csrf, 'If-Match': str(instance['revision'])}
    for invalid in [{'RESTAPIEnabled': 'false'}, {'AdminPassword': 'synthetic-only'},
                    {'ExpRate': 'NaN'}, {'CrossplayPlatforms': '(Steam,Unknown)'}]:
        assert request(path, 'PATCH', {**rules, 'additional': invalid}, headers)[0] == 400
    status, body = request(path, 'PATCH', rules, headers)
    assert status == 200, status
    updated = json.loads(body)
    assert updated['desired']['additional'] == rules['additional']
    assert request(path, 'PATCH', rules, headers)[0] == 412
    assert request(path)[0] == 200
    assert json.loads(request(path)[1])['desired']['additional'] == rules['additional']
    assert not json.loads(request('/api/v1/tasks')[1])
    original = sqlite3.connect('/test/state/panel.db')
    document = json.loads(original.execute('select document from instances where id=?', (instance['id'],)).fetchone()[0])
    assert hashlib.sha256(json.dumps(document, sort_keys=True).encode()).hexdigest() == (root / 'original-hash').read_text()
    assert not list((root / 'instances').glob('*/data'))
    print('PASS extended draft persistence, types/lists, secret/managed-field rejection, revision conflict; no game or original-instance changes')
elif sys.argv[1] == 'cleanup':
    assert root.resolve() == pathlib.Path('/test/settings-integration')
    shutil.rmtree(root)
    print('PASS synthetic settings fixture removed')
