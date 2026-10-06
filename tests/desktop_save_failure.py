"""A real paused game causes SaveTimeout without implicitly stopping it or another instance."""
import json
import pathlib
import subprocess
from desktop_operations import instance_id, login, request, run_action

login()
instance = json.loads(request('/api/v1/instances/'+instance_id)[1])
selected = instance['project'] + '-' + instance['service'] + '-1'
clone_id = pathlib.Path('/test/private/clone-instance').read_text()
clone = json.loads(request('/api/v1/instances/'+clone_id)[1])
other = clone['project'] + '-' + clone['service'] + '-1'
def inspect(name): return json.loads(subprocess.check_output(['docker','inspect',name]))[0]
baseline = inspect(other)['State']['StartedAt']
subprocess.check_call(['docker','pause',selected],stdout=subprocess.DEVNULL)
try:
    task = run_action('stop', expected_state='Failed')
    assert task['safeCode'] == 'SaveTimeout'
    current = inspect(selected)
    assert current['State']['Running'] and current['State']['Paused']
    assert current['HostConfig']['RestartPolicy']['Name'] == 'unless-stopped'
    assert inspect(other)['State']['StartedAt'] == baseline and inspect(other)['State']['Running']
finally:
    subprocess.check_call(['docker','unpause',selected],stdout=subprocess.DEVNULL)
run_action('save')
print('PASS SaveTimeout is explicit, no implicit stop/hold, second real instance unchanged, save recovers after unpause',flush=True)
