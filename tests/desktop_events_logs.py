"""Bounded SSE reconnect plus real Docker-log secret redaction in synthetic game only."""
import json
import pathlib
import subprocess
import time
import urllib.request
from desktop_operations import instance_id, login, request
from desktop_access import base, client

assert json.loads(pathlib.Path('/test/private/panel.json').read_text())['desktopValidation']
login()
path = '/api/v1/instances/' + instance_id + '/events'
assert request(path, headers={'Last-Event-ID': 'invalid'})[0] == 400
def first_event(cursor=None):
    headers = {'Origin': base}
    if cursor is not None:
        headers['Last-Event-ID'] = str(cursor)
    with client.open(urllib.request.Request(base + path, headers=headers), timeout=10) as response:
        assert response.headers['Content-Type'].startswith('text/event-stream')
        sequence = None
        for _ in range(10):
            line = response.readline(8192).decode()
            if line.startswith('id: '):
                sequence = int(line[4:])
            elif line.startswith('data: '):
                data = json.loads(line[6:])
                assert data['sequence'] == sequence
                return sequence
    raise AssertionError('bounded SSE event not received')
first = first_event()
assert first_event(first) > first
instance = json.loads(request('/api/v1/instances/' + instance_id)[1])
container = subprocess.check_output(['docker', 'ps', '-q', '--filter', 'label=com.docker.compose.project=' + instance['project']]).decode().strip()
assert container
configuration = json.loads(subprocess.check_output(['docker', 'inspect', container]))[0]
values = dict(entry.split('=', 1) for entry in configuration['Config']['Env'] if '=' in entry)
subprocess.check_call(['docker', 'exec', container, 'sh', '-c', 'printf "desktop log redaction fixture %s %s\\n" "$ADMIN_PASSWORD" "$SERVER_PASSWORD" >> /proc/1/fd/1'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
for _ in range(20):
    status, raw, _ = request('/api/v1/instances/' + instance_id + '/logs')
    assert status == 200
    text = json.loads(raw)['text']
    if 'desktop log redaction fixture' in text:
        break
    time.sleep(0.25)
assert 'desktop log redaction fixture' in text
assert all(values[name] not in text for name in ['ADMIN_PASSWORD', 'SERVER_PASSWORD'])
assert '[redacted]' in text.lower() or '[已隐藏]' in text
print('PASS bounded SSE cursor reconnect; real Docker log synthetic secrets redacted', flush=True)
