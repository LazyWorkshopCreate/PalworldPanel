"""Kill the isolated executor during an explicit upgrade; inspect durable hold and paired rollback."""
import json
import pathlib
import sqlite3
import subprocess
import threading
import time
import datetime
from desktop_operations import instance_id, login, request, run_action
from desktop_mutations import recover

def inspect(name):
    return json.loads(subprocess.check_output(['docker','inspect',name]))[0]

if __name__ == '__main__':
    login()
    original = json.loads(request('/api/v1/instances/' + instance_id)[1])
    other = inspect('palworldpanel-real-game-validation')
    crashed = threading.Event()
    def interrupt():
        deadline = time.monotonic() + 600
        while time.monotonic() < deadline:
            with sqlite3.connect('/test/state/panel.db') as connection:
                tasks = connection.execute("SELECT phase FROM tasks WHERE instanceId=? AND kind='upgrade' AND state='Running'", (instance_id,)).fetchall()
            if ('UpgradeInstallation',) in tasks:
                subprocess.check_call(['docker','kill','palworldpanel-validation-panel'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                time.sleep(2)
                subprocess.check_call(['docker','start','palworldpanel-validation-panel'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                crashed.set()
                return
            time.sleep(.2)
    pending = [task for task in json.loads(request('/api/v1/tasks')[1]) if task['instanceId'] == instance_id and task['kind'] == 'upgrade' and task['state'] == 'NeedsAttention' and task['safeCode'] == 'ExecutorInterrupted']
    if pending:
        task = pending[0]
    else:
        watcher = threading.Thread(target=interrupt, daemon=True)
        watcher.start()
        task = run_action('upgrade', {'mode':'image','image':original['image'],'expectedGameBuild':original['gameBuild']}, 'NeedsAttention')
        assert crashed.is_set()
    assert task['safeCode'] == 'ExecutorInterrupted'
    assert task['recoveryPoint']
    ids = subprocess.check_output(['docker','ps','-aq','--filter','label=com.docker.compose.project=pp-'+instance_id,
        '--filter','label=com.docker.compose.service=palworld']).decode().split()
    for id in ids:
        container = inspect(id)
        assert container['HostConfig']['RestartPolicy']['Name'] == 'no' and not container['State']['Running']
    unchanged = inspect('palworldpanel-real-game-validation')
    assert unchanged['State']['Running'] and unchanged['Id'] == other['Id'] and unchanged['State']['StartedAt'] == other['State']['StartedAt']
    assert datetime.datetime.fromisoformat(unchanged['State']['StartedAt'].replace('Z','+00:00')) < datetime.datetime.fromisoformat(task['createdUtc'])
    assert recover(task)['state'] == 'RolledBack'
    run_action('start')
    assert json.loads(request('/api/v1/instances/'+instance_id)[1])['worldGuid'] == original['worldGuid']
    print('PASS executor SIGKILL during upgrade: durable NeedsAttention, restart=no, selected instance stopped, other instance unchanged, paired rollback/start', flush=True)
