"""Verify quarantine guard/undo and read-only registration with unchanged files/container/world."""
import hashlib
import json
import pathlib
import subprocess
import time
import desktop_operations as operations
from desktop_operations import instance_id, login, request, run_action

def docker(*args):
    return json.loads(subprocess.check_output(['docker', *args]))

if __name__ == '__main__':
    login()
    original = json.loads(request('/api/v1/instances/' + instance_id)[1])
    root = pathlib.Path('/test/instances') / instance_id
    assert run_action('purge')['phase'] == 'Quarantined'
    assert not root.exists() and (root.parent / '.quarantine' / instance_id).is_dir()
    failed = run_action('finalize-purge', expected_state='Failed')
    assert failed['safeCode'] == 'QuarantineRetention'
    run_action('undo-quarantine')
    assert root.is_dir() and not (root.parent / '.quarantine' / instance_id).exists()
    run_action('start')
    print('PASS purge independent backup, seven-day guard, undo and original world restart', flush=True)
    # Baseline standard Docker Compose labels; no custom instance label is used for registration.
    container = 'pp-' + instance_id + '-palworld-1'
    before = docker('inspect', container)[0]
    with (root / 'settings.env').open('a') as output:
        output.write('# synthetic retained comment\nUNKNOWN_SYNTHETIC_OPTION=keep\n')
    def hashes():
        return {name:hashlib.sha256((root/name).read_bytes()).hexdigest() for name in ['compose.yaml','settings.env','secrets.env']}
    baseline = hashes()
    run_action('unmanage')
    current = docker('inspect', container)[0]
    assert current['Id'] == before['Id'] and current['State']['StartedAt'] == before['State']['StartedAt']
    assert current['State']['Running']
    # Build a fixture with only standard Compose identity, after verifying unmanage itself is read-only.
    compose_file = root / 'compose.yaml'
    compose = json.loads(compose_file.read_text())
    compose['services']['palworld'].pop('labels', None)
    compose_file.write_text(json.dumps(compose))
    subprocess.check_call(['docker','exec','palworldpanel-validation-panel','docker','compose','--project-name','pp-'+instance_id,
        '--file',str(compose_file),'--file',str(root/'transaction.override.json'),'up','--detach','--force-recreate','--pull','never','palworld'],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    current = docker('inspect', container)[0]
    assert 'com.palworldpanel.instance' not in current['Config']['Labels']
    baseline = hashes()
    time.sleep(15)
    csrf = login()
    status, raw, _ = request('/api/v1/discovery/register','POST',{'containerId':current['Id']},{'X-CSRF-Token':csrf})
    assert status == 200, ('registration',status)
    registered = json.loads(raw)
    assert not registered['writable'] and not registered['owned']
    assert hashes() == baseline, 'registration must not modify source files'
    assert registered['worldGuid'] == original['worldGuid'] and registered['configurationKnown']
    operations.instance_id = registered['id']
    pathlib.Path('/test/private/lifecycle-instance').write_text(registered['id'])
    task = run_action('adopt', {'externalSchedulesDisabled':True})
    assert task['state'] == 'Succeeded'
    contents = (root/'settings.env').read_text()
    assert '# synthetic retained comment' in contents and 'UNKNOWN_SYNTHETIC_OPTION=keep' in contents
    assert json.loads(request('/api/v1/instances/'+registered['id'])[1])['worldGuid'] == original['worldGuid']
    denied = run_action('purge', expected_state='Failed')
    assert denied['safeCode'] == 'LegacyPurgeDisabled'
    print('PASS unmanage preserves running container; read-only register preserves source/world; supported adoption preserves unknown keys; legacy purge denied', flush=True)
