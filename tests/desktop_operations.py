"""Exercise panel actions against the isolated real-game instance."""
import json
import pathlib
import time
import uuid
import urllib.error
from desktop_access import request

def login():
    password = pathlib.Path('/test/private/test-password').read_text()
    assert request('/api/v1/session', 'POST', {'userName': 'admin', 'password': password})[0] == 204
    return json.loads(request('/api/v1/session')[1])['csrfToken']

instance_id = pathlib.Path('/test/private/lifecycle-instance').read_text()

def run_action(kind, arguments=None, expected_state='Succeeded'):
    csrf = login()
    instance = json.loads(request('/api/v1/instances/' + instance_id)[1])
    body = {'kind': kind}
    if arguments is not None:
        body['arguments'] = arguments
    headers = {'X-CSRF-Token': csrf, 'If-Match': str(instance['revision']), 'Idempotency-Key': str(uuid.uuid4())}
    status, raw, _ = request('/api/v1/instances/' + instance_id + '/previews', 'POST', body, headers)
    assert status == 200, (kind, status)
    preview = json.loads(raw)
    body.update(confirmation=preview['token'], previewHash=preview['hash'])
    if kind in ['purge', 'finalize-purge']:
        body['typedName'] = instance['name']
    status, raw, _ = request('/api/v1/instances/' + instance_id + '/actions', 'POST', body, headers)
    assert status == 202, (kind, status)
    task = json.loads(raw)
    pathlib.Path('/test/private/last-task-id').write_text(task['id'])
    status, repeated, _ = request('/api/v1/instances/' + instance_id + '/actions', 'POST', body, headers)
    assert status == 202 and json.loads(repeated)['id'] == task['id'], 'idempotency'
    last_phase = None
    for _ in range(300):
        try:
            current = json.loads(request('/api/v1/tasks/' + task['id'])[1])
        except (urllib.error.URLError, TimeoutError):
            time.sleep(2)
            continue
        if current['phase'] != last_phase:
            print(kind + ' phase: ' + current['phase'], flush=True)
            last_phase = current['phase']
        if current['state'] not in ['Queued', 'Running']:
            assert current['state'] == expected_state, (kind, current['state'], current['safeCode'])
            return current
        time.sleep(2)
    raise AssertionError(kind + ' did not complete within ten minutes')

if __name__ == '__main__':
    run_action('save')
    run_action('backup')
    backups = json.loads(request('/api/v1/instances/' + instance_id + '/backups')[1])
    assert len(backups) >= 1 and backups[0]['bytes'] > 0
    assert json.loads(request('/api/v1/instances/' + instance_id + '/observations')[1])['gameApi'] == 'healthy'
    run_action('stop')
    assert json.loads(request('/api/v1/instances/' + instance_id + '/observations')[1])['container'] == 'exited'
    run_action('backup')
    assert json.loads(request('/api/v1/instances/' + instance_id + '/observations')[1])['container'] == 'exited'
    run_action('start')
    assert json.loads(request('/api/v1/instances/' + instance_id + '/observations')[1])['gameApi'] == 'healthy'
    print('PASS real-game save, offline backup, stop, stopped backup, start and idempotency', flush=True)
