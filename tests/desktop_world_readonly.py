"""Opt-in real-game acceptance on the labelled Docker Desktop fixture only.
Run probe with --network host; run verify/backup on the allowlisted fixture client.
Credentials and downloads stay inside /test; never print raw API bodies or logs.
"""
import argparse
import base64
import hashlib
import http.cookiejar
import io
import json
import pathlib
import re
import sqlite3
import ssl
import time
import urllib.error
import urllib.request
import uuid
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('mode', choices=['probe', 'verify', 'verify-offline', 'stop', 'start', 'backup', 'browser', 'configure', 'restore-config', 'restore-policy'])
parser.add_argument('--instance', required=True)
args = parser.parse_args()
assert re.fullmatch('[a-f0-9]{32}', args.instance)
root = pathlib.Path('/test')
options = json.loads((root/'private/panel.json').read_text())
assert options.get('desktopValidation') and options['stateRoot'] == '/test/state'
assert options['instanceRoots'] == ['/test/instances'] and options['bindIp'] == '172.30.88.1'
evidence = root/'world-readonly-real-20261014'
assert (evidence/'before.db').exists(), 'First take an online backup of the isolated fixture database'
with sqlite3.connect('file:/test/state/panel.db?mode=ro', uri=True) as db:
    matches = [json.loads(r[0]) for r in db.execute('select document from instances where id=?', (args.instance,))]
assert len(matches) == 1
instance = matches[0]
assert instance['root'] == '/test/instances/' + args.instance and not instance.get('quarantinedUtc')
world = instance['worldGuid'].lower()
secrets = {}
for line in (pathlib.Path(instance['root'])/'secrets.env').read_text().splitlines():
    if '=' in line and not line.lstrip().startswith('#'):
        k,v = line.split('=',1)
        secrets[k] = re.sub(r'\\([\\"])', r'\1', v)
assert secrets.get('ADMIN_PASSWORD')
if args.mode == 'verify': assert secrets.get('SERVER_PASSWORD')
base = ('http' if options.get('desktopAllowHttp') else 'https') + '://172.30.88.1:18080'

def client():
    return urllib.request.build_opener(urllib.request.HTTPSHandler(context=ssl._create_unverified_context()), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
admin = client()
visitor = client()
def request(who, path, method='GET', body=None, headers=None, expected=200):
    h = {'Origin':base, 'Content-Type':'application/json'}
    h.update(headers or {})
    req = urllib.request.Request(base+path, data=json.dumps(body).encode() if body is not None else None, method=method, headers=h)
    try:
        with who.open(req, timeout=90) as r: code,raw,returned = r.status,r.read(),r.headers
    except urllib.error.HTTPError as e: code,raw,returned = e.code,e.read(),e.headers
    assert code == expected, (method, path, code, expected)
    return raw,returned

def login_admin():
    password = (root/'private/test-password').read_text()
    request(admin,'/api/v1/session','POST',{'userName':'admin','password':password}, expected=204)
    return json.loads(request(admin,'/api/v1/session')[0])['csrfToken']

if args.mode == 'probe':
    definitions = json.loads((evidence/'game-settings.json').read_text())
    allowed = {d['key'] for d in definitions if not d['secret'] and d['editable'] and d['key'] not in ('PublicIP','PublicPort','BanListURL')}
    def game(endpoint):
        url = 'http://127.0.0.1:' + str(instance['restPort']) + '/v1/api/' + endpoint
        auth = base64.b64encode(('admin:'+secrets['ADMIN_PASSWORD']).encode()).decode()
        with urllib.request.urlopen(urllib.request.Request(url, headers={'Authorization':'Basic '+auth}), timeout=15) as r:
            return json.load(r)
    info,settings,metrics = game('info'),game('settings'),game('metrics')
    assert info['worldguid'].lower()==world
    safe = {k:v for k,v in settings.items() if k in allowed}
    (evidence/'game-observed.json').write_text(json.dumps({'world':world,'version':info['version'],'settings':safe,'metrics':{k:metrics.get(k) for k in ('currentplayernum','serverfps')}}))
    print('PASS independent real REST info/settings/metrics; version='+info['version']+'; safeSettings='+str(len(safe))+'; arrayFields='+','.join(k for k,v in safe.items() if isinstance(v,list)),flush=True)
elif args.mode == 'browser':
    request(visitor,'/api/v1/world-view/session','POST',{'worldId':world,'password':secrets['SERVER_PASSWORD']})
    jar=next(h.cookiejar for h in visitor.handlers if isinstance(h,urllib.request.HTTPCookieProcessor))
    cookies=[{'name':c.name,'value':c.value,'domain':'127.0.0.1','path':c.path,'expires':c.expires,'httpOnly':True,'secure':False,'sameSite':'Strict'} for c in jar]
    assert len(cookies)==1
    (evidence/'browser-state.json').write_text(json.dumps({'cookies':cookies,'origins':[]}))
    print('PASS private browser visitor session prepared; no administrator session',flush=True)
elif args.mode in ('backup','configure','restore-config','stop','start'):

    csrf=login_admin()
    if args.mode in ('configure','restore-config'):
        if args.mode == 'configure':
            assert secrets['SERVER_PASSWORD']=='', 'This fixture expects an initially empty game password'
            request(visitor,'/api/v1/world-view/session','POST',{'worldId':world,'password':''},expected=401)
            new_password=uuid.uuid4().hex+uuid.uuid4().hex[:16]
            (evidence/'temporary-game-password').write_text(new_password)
        else:
            new_password=''
        request(admin,'/api/v1/instances/'+args.instance+'/secrets','PATCH',{'gamePassword':new_password},{'X-CSRF-Token':csrf,'If-Match':str(instance['revision'])})
        instance=json.loads(request(admin,'/api/v1/instances/'+args.instance)[0])
    kind=args.mode if args.mode in ('backup','stop','start') else 'apply-config'
    body={'kind':kind}
    headers={'X-CSRF-Token':csrf,'If-Match':str(instance['revision']),'Idempotency-Key':str(uuid.uuid4())}
    preview=json.loads(request(admin,'/api/v1/instances/'+args.instance+'/previews','POST',body,headers)[0])
    body.update(confirmation=preview['token'],previewHash=preview['hash'])
    task=json.loads(request(admin,'/api/v1/instances/'+args.instance+'/actions','POST',body,headers,202)[0])
    last=None
    for _ in range(300):
        current=json.loads(request(admin,'/api/v1/tasks/'+task['id'])[0])
        if current['phase']!=last: print(kind+' phase='+current['phase'],flush=True);last=current['phase']
        if current['state'] not in ('Queued','Running'):
            assert current['state']=='Succeeded', (current['state'],current.get('safeCode'))
            break
        time.sleep(2)
    else: raise AssertionError('Action exceeded ten minutes')
    if args.mode=='backup':
        points=json.loads(request(admin,'/api/v1/instances/'+args.instance+'/backups')[0])
        latest=max(points,key=lambda b:b['createdUtc'])
        (evidence/'selected-backup').write_text(latest['id'])
        print('PASS real-game save / stop / offline backup / restart; backupBytes='+str(latest['bytes']),flush=True)
    elif args.mode in ('stop','start'):
        print('PASS isolated real game '+args.mode,flush=True)
    else:
        print('PASS isolated game password '+('temporarily enabled' if args.mode=='configure' else 'restored to original empty state'),flush=True)
elif args.mode == 'restore-policy':
    policy=json.loads((evidence/'policy-before.json').read_text())
    csrf=login_admin()
    request(admin,'/api/v1/instances/'+args.instance+'/backup-policy','PATCH',{'time':policy['backupTime'],'retentionDays':policy['retentionDays']},{'X-CSRF-Token':csrf,'If-Match':str(instance['revision'])})
    print('PASS original isolated backup policy restored',flush=True)
else:
    expected=json.loads((evidence/'game-observed.json').read_text())
    path='/api/v1/world-view/'+world
    request(visitor,path,expected=401)
    request(visitor,'/api/v1/world-view/session','POST',{'worldId':world,'password':secrets['SERVER_PASSWORD']})
    detail=json.loads(request(visitor,path)[0])
    offline=args.mode=='verify-offline'
    if offline:
        assert detail['settingsUnavailable'] and detail['status']['container']=='exited'
        assert all(r['observed'] is None for r in detail['settings'])
        assert any(r['applied'] is not None for r in detail['settings'])
    else:
        assert detail['settingsUnavailable'] is None
        assert detail['status']['container']=='running' and not detail['status']['stale']
    assert detail['gameBuild']==expected['version']
    count=0
    for row in ([] if offline else detail['settings']):
        if row['key'] not in expected['settings']: continue
        actual = row['observed']; value=expected['settings'][row['key']]
        assert actual is not None, ('ObservedMissing',row['key'],type(value).__name__)
        if isinstance(value, list): assert actual=='('+','.join(str(v) for v in value)+')', ('ObservedArrayMismatch',row['key'])
        elif isinstance(value,bool): assert actual.lower()==str(value).lower(), ('ObservedBooleanMismatch',row['key'])
        elif isinstance(value,(int,float)): assert float(actual)==value, ('ObservedNumberMismatch',row['key'])
        else: assert actual==str(value), ('ObservedTextMismatch',row['key'])
        count+=1
    assert offline or count>=20
    logs=json.loads(request(visitor,path+'/logs')[0]); assert logs['text'] and logs['updatedUtc']
    for secret in secrets.values():
        assert secret not in json.dumps(detail) and secret not in logs['text'], 'SecretLeak'
    assert '/test/instances/' not in logs['text'] and '/home/steam/' not in logs['text']
    assert len(logs['text'])<=65536
    assert not re.search(r'\[[0-9;]+m',logs['text']), 'AnsiColourArtifact'
    assert 'world-view-redaction-probe' in logs['text'] and '[redacted]' in logs['text']
    for route in ('/api/v1/instances','/api/v1/tasks','/api/v1/audit'):
        request(visitor,route,expected=401)
    backup_id=(evidence/'selected-backup').read_text().strip()
    assert backup_id in {b['id'] for b in detail['backups']}
    raw,headers=request(visitor,path+'/backups/'+backup_id)
    assert headers['Content-Type'].startswith('application/zip')
    backup=root/'backups'/args.instance/backup_id
    manifest=json.loads((backup/'manifest.json').read_text())
    saved_prefix='Saved/SaveGames/0/'+instance['worldGuid']+'/'
    files={f['name']:f for f in manifest['files'] if f['name'].startswith(saved_prefix) and f['name'].lower().endswith('.sav')}
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        assert z.testzip() is None
        assert set(z.namelist())==set(files), 'ArchiveContentMismatch'
        assert any(n.endswith('/Level.sav') for n in z.namelist()) and any(n.endswith('/LevelMeta.sav') for n in z.namelist())
        for name in z.namelist():
            assert hashlib.sha256(z.read(name)).hexdigest().lower()==files[name]['sha256'].lower(), 'SaveHashMismatch'
    (evidence/'download.zip').write_bytes(raw)
    (evidence/('result-offline.json' if offline else 'result.json')).write_text(json.dumps({'settingsCompared':count,'logsCharacters':len(logs['text']),'downloadBytes':len(raw),'saveFiles':len(files),'zipSha256':hashlib.sha256(raw).hexdigest(),'version':expected['version']}))
    request(visitor,'/api/v1/world-view/session','DELETE',expected=204)
    request(visitor,path,expected=401)
    print('PASS real-world '+('offline' if offline else 'running')+' readonly settings='+str(count)+'; logs='+str(len(logs['text']))+' characters; ZIP='+str(len(files))+' saves, all hashes match offline backup; admin denied; logout revoked',flush=True)
