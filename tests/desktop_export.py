"""Export bytes and token replay checks; plaintext passwords remain private."""
import json
import pathlib
import secrets
from desktop_operations import instance_id, login, request

csrf = login()
headers = {'X-CSRF-Token': csrf}
password = secrets.token_hex(24)
pathlib.Path('/test/private/export-passphrase').write_text(password)
records = json.loads(request('/api/v1/instances/' + instance_id + '/backups')[1])
for path, target in [('/api/v1/instances/' + instance_id + '/backups/' + records[0]['id'] + '/export-prepare', 'world-export.ppbak'),
                     ('/api/v1/panel-backups/export-prepare', 'panel-export.ppbak')]:
    status, raw, _ = request(path, 'POST', {'passphrase': password}, headers, timeout=180)
    assert status == 200, status
    url = json.loads(raw)['downloadUrl']
    assert url.startswith('/api/v1/downloads/')
    status, encrypted, _ = request(url, timeout=180)
    assert status == 200 and encrypted.startswith(b'PPBAK001')
    pathlib.Path('/test/private/' + target).write_bytes(encrypted)
    assert request(url)[0] == 404
    print('PASS authenticated single-use encrypted download: ' + target, flush=True)
