"""Verify desired/applied/observed settings, optimistic conflicts and old admin authentication during apply."""
import json
import pathlib
import uuid
from desktop_operations import instance_id, login, run_action
from desktop_access import request

csrf = login()
instance = json.loads(request('/api/v1/instances/' + instance_id)[1])
rules = dict(instance['desired'])
rules.update(name='panel literal $ # " quote (name)', description='description $ # (literal)', maxPlayers=5)
headers = {'X-CSRF-Token': csrf, 'If-Match': str(instance['revision'])}
status, raw, _ = request('/api/v1/instances/' + instance_id + '/settings', 'PATCH', rules, headers)
assert status == 200, ('settings-draft', status)
updated = json.loads(raw)
assert updated['desired'] != updated['applied']
assert request('/api/v1/instances/' + instance_id + '/settings', 'PATCH', rules, headers)[0] == 412
# Synthetic secrets stay in the private test volume, never console or repository.
password = uuid.uuid4().hex + '$#"(literal)'
pathlib.Path('/test/private/changed-game-admin').write_text(password)
headers['If-Match'] = str(updated['revision'])
status, _, _ = request('/api/v1/instances/' + instance_id + '/secrets', 'PATCH',
    {'administratorPassword': password, 'gamePassword': uuid.uuid4().hex + '$#(literal)'}, headers)
assert status == 200, ('secrets-draft', status)
assert 'administratorPassword' not in json.loads(request('/api/v1/instances/' + instance_id)[1])
run_action('apply-config')
observed = json.loads(request('/api/v1/instances/' + instance_id + '/settings')[1])
assert observed['desired'] == observed['applied']
assert observed['observed']['name'] == rules['name']
assert observed['observed']['maxPlayers'] == 5
print('PASS literal settings, password rotation, desired/applied/observed and stale revision conflict', flush=True)
