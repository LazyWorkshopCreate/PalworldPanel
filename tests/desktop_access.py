"""Isolated Docker Desktop HTTP checks; prints no credentials or response bodies."""
import http.cookiejar
import json
import pathlib
import ssl
import urllib.error
import urllib.request
import sys

options = json.loads(pathlib.Path('/test/private/panel.json').read_text())
base = ("http" if options.get("desktopAllowHttp") else "https") + "://172.30.88.1:18080"
cookies = http.cookiejar.CookieJar()
client = urllib.request.build_opener(urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),
                                   urllib.request.HTTPCookieProcessor(cookies))

def request(path, method="GET", body=None, headers=None, timeout=15):
    data = json.dumps(body).encode() if body is not None else None
    default = {"Origin": base, "Content-Type": "application/json"}
    default.update(headers or {})
    req = urllib.request.Request(base + path, data=data, method=method, headers=default)
    try:
        with client.open(req, timeout=timeout) as response:
            return response.status, response.read(), response.headers
    except urllib.error.HTTPError as error:
        return error.code, error.read(), error.headers

if len(sys.argv) > 1 and sys.argv[1] == "denied":
    for path in ["/", "/index.html", "/api/v1/session", "/api/v1/tasks", "/api/v1/instances", "/api/v1/instances/unknown/events", "/api/v1/downloads/unknown"]:
        assert request(path, headers={"X-Forwarded-For": "172.30.88.10"})[0] == 403, path
    print("PASS denied source: static, login, API; spoofed forwarding header ignored")
    sys.exit(0)

assert request("/")[0] == 200
assert request("/api/v1/instances")[0] == 401
assert request("/api/v1/instances/unknown/uploads", "POST", {}, {"Content-Type": "application/zip"})[0] == 401
assert request("/api/v1/session", "POST", {"userName": "admin", "password": "incorrect"}, {"Origin": "https://invalid.example"})[0] == 403
password = pathlib.Path("/test/private/test-password").read_text()
status, _, headers = request("/api/v1/session", "POST", {"userName": "admin", "password": password})
assert status == 204, status
cookie = headers.get("Set-Cookie", "")
assert all(value in cookie.lower() for value in ["httponly", "samesite=strict"])
assert ("; secure" in cookie.lower()) == (not options.get("desktopAllowHttp", False))
status, body, _ = request("/api/v1/session")
assert status == 200
csrf = json.loads(body)["csrfToken"]
assert request("/api/v1/instances")[0] == 200
assert request("/api/v1/host")[0] == 200
assert request("/api/v1/tasks")[0] == 200
assert request("/api/v1/discovery")[0] == 200
assert request("/api/v1/nonexistent")[0] == 404
assert request("/api/v1/session", "DELETE")[0] == 403
assert request("/api/v1/session", "DELETE", headers={"X-CSRF-Token": csrf})[0] == 204
assert request("/api/v1/instances")[0] == 401
print("PASS allowed source: login, cookie policy, read APIs, CSRF, origin, logout, API 404")
