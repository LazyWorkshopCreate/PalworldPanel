"""Anonymous dashboard, redaction and server-side authorization checks; no credentials."""
import json
import pathlib
import ssl
import sys
import urllib.error
import urllib.request

options = json.loads(pathlib.Path('/test/private/panel.json').read_text())
base = ('http' if options.get('desktopAllowHttp') else 'https') + '://172.30.88.1:18080'
def request(path, method='GET'):
    req = urllib.request.Request(base + path, method=method, headers={'Origin': base, 'Content-Type': 'application/json'},
        data=b'{}' if method != 'GET' else None)
    try:
        with urllib.request.urlopen(req, context=ssl._create_unverified_context(), timeout=20) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error: return error.code, error.read()
if len(sys.argv) > 1 and sys.argv[1] == 'denied':
    assert request('/api/v1/dashboard')[0] == 403
    print('PASS anonymous dashboard denied outside exact IP allowlist')
    sys.exit(0)
status, body = request('/api/v1/dashboard')
assert status == 200
data = json.loads(body)
assert set(data) == {'host', 'instances', 'updatedUtc'}
assert set(data['host']) == {'usedMemoryBytes', 'availableMemoryBytes', 'cpuPercent', 'updatedUtc', 'totalMemoryBytes', 'cpuCount'}
for instance in data['instances']:
    assert set(instance) == {'id', 'name', 'status'}
    assert set(instance['status']) == {'container', 'players', 'fps', 'cpu', 'memoryBytes', 'updatedUtc', 'stale'}
for path in ['/instances', '/tasks', '/audit', '/backups', '/discovery', '/host', '/instances/synthetic/settings', '/instances/synthetic/logs', '/instances/synthetic/events', '/downloads/synthetic']:
    assert request('/api/v1' + path)[0] == 401, path
for path, method in [('/creation-previews', 'POST'), ('/instances/synthetic/actions', 'POST'), ('/panel-backups/export', 'POST'), ('/instances/synthetic/settings', 'PATCH'), ('/tasks/synthetic/cancel', 'POST')]:
    assert request('/api/v1' + path, method)[0] == 401, path
print('PASS anonymous dashboard 200 with exact public fields; management reads and writes 401; no tasks submitted')
