"""Exercise actual Beijing scheduler and durable one-per-day key against the isolated cloned game."""
import datetime
import json
import pathlib
import sqlite3
import time
from desktop_operations import login, request

csrf = login()
id = pathlib.Path('/test/private/clone-instance').read_text()
instance = json.loads(request('/api/v1/instances/'+id)[1])
now = datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8)))
time_string = (now - datetime.timedelta(minutes=1)).strftime('%H:%M')
headers = {'X-CSRF-Token':csrf,'If-Match':str(instance['revision'])}
status, _, _ = request('/api/v1/instances/'+id+'/backup-policy','PATCH',{'time':time_string,'retentionDays':14},headers)
assert status == 200
last = None
for _ in range(90):
    tasks = json.loads(request('/api/v1/tasks')[1])
    scheduled = [task for task in tasks if task['instanceId']==id and task['kind']=='backup']
    if scheduled:
        task = scheduled[0]
        if task['phase'] != last:
            print('scheduled backup phase: '+task['phase'],flush=True)
            last = task['phase']
        if task['state'] not in ['Queued','Running']:
            assert task['state']=='Succeeded', (task['state'],task['safeCode'])
            break
    time.sleep(2)
else: raise AssertionError('scheduler did not finish within three minutes')
with sqlite3.connect('/test/state/panel.db') as connection:
    assert connection.execute("select count(*) from tasks where instanceId=? and user='scheduler'",(id,)).fetchone()[0] == 1
current = json.loads(request('/api/v1/instances/'+id)[1])
headers['If-Match'] = str(current['revision'])
assert request('/api/v1/instances/'+id+'/backup-policy','PATCH',{'time':instance['backupTime'],'retentionDays':instance['retentionDays']},headers)[0] == 200
backups = json.loads(request('/api/v1/instances/'+id+'/backups')[1])
assert any(point['purpose']=='scheduled' and point['bytes']>0 for point in backups)
print('PASS Beijing scheduled offline backup, durable daily key, policy revision and restored default schedule',flush=True)
