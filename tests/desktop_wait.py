"""Wait for actual HTTPS readiness; container Running alone is insufficient."""
import json
import pathlib
import ssl
import time
import urllib.error
import urllib.request

options = json.loads(pathlib.Path('/test/private/panel.json').read_text())
assert options['desktopValidation']
scheme = 'http' if options.get('desktopAllowHttp') else 'https'
url = scheme + '://' + options['bindIp'] + ':' + str(options['port']) + '/'
for _ in range(30):
    try:
        with urllib.request.urlopen(url, context=ssl._create_unverified_context(), timeout=2) as response:
            if response.status == 200:
                print('PASS isolated panel ' + scheme.upper() + ' ready', flush=True)
                break
    except (urllib.error.URLError, TimeoutError):
        pass
    time.sleep(1)
else:
    raise AssertionError('isolated panel HTTPS was not ready within the bounded wait')
