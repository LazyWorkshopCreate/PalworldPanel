"""Opt-in native lifecycle acceptance. Credentials stay outside the repository."""
import argparse
import http.cookiejar
import json
import pathlib
import re
import time
import urllib.error
import urllib.request
import urllib.parse
import uuid
import hashlib
import zipfile
import secrets
import sqlite3
import datetime
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("command", choices=["inspect", "create", "action", "wait", "settings", "recover", "state", "probe", "observe", "backups", "storage-probe", "maintenance", "maintenance-tail", "maintenance-finish", "secrets", "adoption", "final-purge"])
parser.add_argument("--base", default="http://127.0.0.1:18082")
parser.add_argument("--credentials", required=True)
parser.add_argument("--id")
parser.add_argument("--kind")
parser.add_argument("--arguments", default="{}")
parser.add_argument("--name", default="acceptance-20261007")
parser.add_argument("--state-file")
parser.add_argument("--expected", default="Succeeded")
parser.add_argument("--fixture-root")
args = parser.parse_args()
assert urllib.parse.urlparse(args.base).hostname in ("127.0.0.1", "localhost"), "Local acceptance only"
assert args.name.startswith("acceptance-"), "Synthetic instance name required"
cookies = http.cookiejar.CookieJar()
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookies))

def request(path, method="GET", body=None, headers=None, expected=200):
    data = json.dumps(body).encode() if body is not None else None
    h = {"Origin": args.base, "Content-Type": "application/json"}
    h.update(headers or {})
    try:
        with client.open(urllib.request.Request(args.base + "/api/v1" + path, data=data, method=method, headers=h), timeout=600) as response:
            status, raw = response.status, response.read()
    except urllib.error.HTTPError as error:
        status, raw = error.code, error.read()
    value = json.loads(raw) if raw else None
    assert status == expected, (path, status, value.get("code") if isinstance(value, dict) else None)
    return value

password = re.search(r"(?m)^Password:\s*(.+)$", pathlib.Path(args.credentials).read_text()).group(1).strip()
request("/session", "POST", {"userName": "admin", "password": password}, expected=204)
csrf = request("/session")["csrfToken"]
headers = {"X-CSRF-Token": csrf, "Idempotency-Key": str(uuid.uuid4())}

def wait(task):
    phase = None
    for _ in range(900):
        current = request("/tasks/" + task)
        if current["phase"] != phase:
            print(json.dumps({k: current[k] for k in ("id", "instanceId", "kind", "state", "phase", "safeCode")}), flush=True)
            phase = current["phase"]
        if current["state"] not in ("Queued", "Running"):
            assert current["state"] == args.expected, (current["state"], current["safeCode"])
            return current
        time.sleep(2)
    raise AssertionError("Task timed out")

def run_action(kind, arguments=None, expected="Succeeded"):
    instance = request("/instances/" + args.id)
    assert instance["name"].startswith("acceptance-")
    request("/session", "POST", {"userName": "admin", "password": password}, expected=204)
    h = {"X-CSRF-Token": request("/session")["csrfToken"], "If-Match": str(instance["revision"]), "Idempotency-Key": str(uuid.uuid4())}
    body = {"kind": kind, "arguments": arguments or {}}
    preview = request("/instances/" + args.id + "/previews", "POST", body, h)
    body.update(confirmation=preview["token"], previewHash=preview["hash"])
    if kind in ("purge", "finalize-purge"):
        body["typedName"] = instance["name"]
    task = request("/instances/" + args.id + "/actions", "POST", body, h, 202)
    assert request("/instances/" + args.id + "/actions", "POST", body, h, 202)["id"] == task["id"]
    args.expected = expected
    result = wait(task["id"])
    print("PASS " + kind + ": " + result["state"], flush=True)
    return result

def rollback(task):
    h = {"X-CSRF-Token": request("/session")["csrfToken"]}
    body = {"resolution": "rollback"}
    preview = request("/tasks/" + task["id"] + "/recovery-preview", "POST", body, h)
    body.update(confirmation=preview["token"], previewHash=preview["hash"])
    result = request("/tasks/" + task["id"] + "/recover", "POST", body, h)
    assert result["state"] == "RolledBack"
    assert request("/instances/" + args.id)["desiredPower"] == "stopped"
    print("PASS paired rollback remains stopped", flush=True)

def reject_unverified_players(task):
    h = {"X-CSRF-Token": request("/session")["csrfToken"]}
    body = {"resolution": "confirm-players", "originalPlayersVerified": False}
    preview = request("/tasks/" + task["id"] + "/recovery-preview", "POST", body, h)
    body.update(confirmation=preview["token"], previewHash=preview["hash"])
    result = request("/tasks/" + task["id"] + "/recover", "POST", body, h, 409)
    assert result["code"] == "PlayerVerificationRequired"
    print("PASS real player confirmation cannot be bypassed", flush=True)

if args.command == "inspect":
    print(json.dumps({"instances": request("/instances"), "tasks": request("/tasks"), "host": request("/host")}, ensure_ascii=False))
elif args.command in ("maintenance", "maintenance-tail", "maintenance-finish"):
    root = pathlib.Path(args.fixture_root).resolve()
    assert root.name == args.id and root.parent.parent.name.startswith("lifecycle-") and request("/capabilities")["desktopValidation"]
    original = request("/instances/" + args.id)
    world = original["worldGuid"]
    if args.command == "maintenance":
        run_action("stop", {"force": True})
        assert request("/instances/" + args.id + "/observations")["container"] == "exited"
        run_action("start")
        run_action("backup")
        backups = request("/instances/" + args.id + "/backups")
        assert backups and backups[0]["bytes"] > 0
        task = run_action("restore", {"backupId": backups[0]["id"]}, "NeedsAttention")
        assert task["safeCode"] == "PlayerVerificationPending"
        rollback(task)
        run_action("start")
        run_action("stop")
        archive = root.parent.parent / "synthetic-import.zip"
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
            source = root / "data/Pal/Saved/SaveGames/0" / world
            for file in source.rglob("*"):
                if file.is_file():
                    z.write(file, "SaveGames/0/" + world + "/" + file.relative_to(source).as_posix())
            z.writestr("Config/LinuxServer/PalWorldSettings.ini", "ignored untrusted source configuration")
        config = root / "data/Pal/Saved/Config/LinuxServer/PalWorldSettings.ini"
        config_hash = hashlib.sha256(config.read_bytes()).hexdigest()
        upload_request = urllib.request.Request(args.base + "/api/v1/instances/" + args.id + "/uploads", data=archive.read_bytes(), method="POST", headers={"Origin": args.base, "X-CSRF-Token": request("/session")["csrfToken"], "Content-Type": "application/zip"})
        with client.open(upload_request, timeout=120) as response:
            upload = json.loads(response.read())
        task = run_action("import", {"uploadId": upload["uploadId"], "sha256": upload["sha256"], "world": world}, "NeedsAttention")
        assert task["safeCode"] == "PlayerVerificationPending" and hashlib.sha256(config.read_bytes()).hexdigest() == config_hash
        rollback(task)
        run_action("start")
    if args.command != "maintenance-finish":
        run_action("apply-config")
        update = request("/instances/" + args.id + "/update-check", "POST", {}, {"X-CSRF-Token": request("/session")["csrfToken"]})
        if update["status"] == "available":
            task = run_action("upgrade", {"updateToken": update["updateToken"]}, "NeedsAttention")
            assert task["safeCode"] == "PlayerVerificationPending"
            reject_unverified_players(task)
            rollback(task)
            run_action("start")
        else:
            assert update["status"] == "up-to-date" and update["updateToken"] is None
            print("PASS current installation does not create unnecessary upgrade", flush=True)
    run_action("retain-data")
    assert (root / "data/Pal/Saved").is_dir()
    run_action("start")
    run_action("purge")
    assert request("/instances/" + args.id)["quarantinedUtc"]
    task = run_action("finalize-purge", expected="Failed")
    assert task["safeCode"] == "QuarantineRetention"
    run_action("undo-quarantine")
    run_action("start")
    assert request("/instances/" + args.id)["worldGuid"] == world
    assert request("/instances/" + args.id + "/observations")["gameApi"] == "healthy"
    print("PASS retain-data, quarantine retention and undo; world preserved", flush=True)
elif args.command == "secrets":
    instance = request("/instances/" + args.id)
    assert instance["name"].startswith("acceptance-")
    admin, game_password = secrets.token_urlsafe(24), secrets.token_urlsafe(18)
    h = headers | {"If-Match": str(instance["revision"])}
    result = request("/instances/" + args.id + "/secrets", "PATCH", {"administratorPassword": admin, "gamePassword": game_password}, h)
    assert admin not in json.dumps(result) and game_password not in json.dumps(result)
    run_action("apply-config")
    assert request("/instances/" + args.id + "/observations")["gameApi"] == "healthy"
    instance = request("/instances/" + args.id)
    request("/instances/" + args.id + "/secrets", "PATCH", {"gamePassword": ""}, headers | {"X-CSRF-Token": request("/session")["csrfToken"], "If-Match": str(instance["revision"])})
    run_action("apply-config")
    assert request("/instances/" + args.id + "/observations")["gameApi"] == "healthy"
    print("PASS secret rotation, no password echo, old credentials save before switch, clear game password")
elif args.command == "adoption":
    original = request("/instances/" + args.id)
    assert original["name"].startswith("acceptance-")
    expected_world = original["worldGuid"] or request("/instances/" + args.id + "/observations")["worldGuid"]
    container = subprocess.check_output(["docker", "ps", "-q", "--filter", "label=com.docker.compose.project=" + original["project"]]).decode().strip()
    started = subprocess.check_output(["docker", "inspect", "--format", "{{.State.StartedAt}}", container]).decode()
    run_action("unmanage")
    assert subprocess.check_output(["docker", "inspect", "--format", "{{.State.StartedAt}}", container]).decode() == started
    candidates = request("/discovery")
    candidate = next(c for c in candidates if c["containerId"].startswith(container))
    assert candidate["root"] and not candidate["registered"]
    registered = request("/discovery/register", "POST", {"containerId": candidate["containerId"]}, {"X-CSRF-Token": request("/session")["csrfToken"]})
    assert not registered["writable"] and registered["worldGuid"] == expected_world and registered["configurationKnown"]
    args.id = registered["id"]
    result = request("/instances/" + args.id + "/settings", "PATCH", registered["desired"], headers | {"X-CSRF-Token": request("/session")["csrfToken"], "If-Match": str(registered["revision"])}, 409)
    assert result["code"] == "ReadOnlyInstance"
    run_action("adopt", {"externalSchedulesDisabled": True})
    assert request("/instances/" + args.id)["writable"]
    print("PASS unmanage preserves running container; read-only discovery and explicit adoption: " + args.id)
elif args.command == "final-purge":
    root = pathlib.Path(args.fixture_root).resolve()
    assert root.name == args.id and root.parent.parent.name.startswith("lifecycle-") and request("/capabilities")["desktopValidation"]
    task = run_action("purge")
    quarantined = root.parent / ".quarantine" / args.id
    state = root.parent.parent / "state/panel.db"
    with sqlite3.connect(state) as database:
        document = json.loads(database.execute("select document from instances where id=?", (args.id,)).fetchone()[0])
        assert pathlib.Path(document["root"]) == quarantined
        marker = quarantined / "quarantine.json"
        record = json.loads(marker.read_text())
        assert record["id"] == args.id and record["backupId"] == task["recoveryPoint"]
        past = (datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(days=8)).isoformat()
        document["quarantinedUtc"] = record["utc"] = past
        marker.write_text(json.dumps(record))
        database.execute("update instances set document=? where id=?", (json.dumps(document), args.id))
    run_action("finalize-purge")
    assert not quarantined.exists()
    backup = next(b for b in request("/instances/" + args.id + "/backups") if b["id"] == task["recoveryPoint"])
    assert backup["protected"]
    prepared = request("/instances/" + args.id + "/backups/" + backup["id"] + "/export-prepare", "POST", {"passphrase": secrets.token_urlsafe(24)}, {"X-CSRF-Token": request("/session")["csrfToken"]})
    with client.open(args.base + prepared["downloadUrl"], timeout=600) as response:
        assert response.status == 200 and len(response.read()) > 0
    assert not quarantined.exists(), "Export cannot recreate purged directory"
    print("PASS synthetic retention fixture final purge and protected encrypted export")
elif args.command == "storage-probe":
    before = request("/instances")
    before_tasks = request("/tasks")
    result = request("/creation-previews", "POST", {"rules": {"name": args.name, "cpu": 4, "memoryMiB": 4096}}, headers, 507)
    assert result["code"] == "InsufficientStorage"
    assert request("/instances") == before and request("/tasks") == before_tasks
    print("PASS storage preview rejects before allocating any instance or task")
elif args.command in ("observe", "backups"):
    print(json.dumps(request("/instances/" + args.id + ("/observations" if args.command == "observe" else "/backups"))))
elif args.command == "probe":
    instance = request("/instances/" + args.id)
    assert instance["name"].startswith("acceptance-")
    path = "/instances/" + args.id
    active = any(t["instanceId"] == args.id and t["state"] in ("Queued", "Running", "NeedsAttention") for t in request("/tasks"))
    result = request(path + "/settings", "PATCH", instance["desired"], {"X-CSRF-Token": csrf, "If-Match": "0"}, 409 if active else 412)
    assert result["code"] == ("TaskConflict" if active else "RevisionConflict")
    request(path + "/actions", "POST", {"kind": "start"}, {"X-CSRF-Token": csrf, "If-Match": str(instance["revision"])}, 409)
    request("/session", "DELETE", headers={"Origin": "https://invalid.example"}, expected=403)
    request("/session", "DELETE", expected=403)
    print("PASS task conflict/stale revision, missing preview, invalid Origin and missing CSRF rejected")
elif args.command == "state":
    assert args.state_file and pathlib.Path(args.state_file).is_absolute()
    state = {"cookies": [{"name": c.name, "value": c.value, "domain": c.domain, "path": c.path, "httpOnly": True, "secure": c.secure, "sameSite": "Strict", "expires": -1} for c in cookies], "origins": []}
    pathlib.Path(args.state_file).write_text(json.dumps(state))
    print("Authenticated browser state saved privately")
elif args.command == "create":
    rules = {"name": args.name, "maxPlayers": 4, "cpu": 4, "memoryMiB": 4096}
    body = {"rules": rules}
    if args.id:
        body["cloneId"] = args.id
    preview = request("/creation-previews", "POST", body, headers)
    body.update(plan=preview["plan"], confirmation=preview["token"], previewHash=preview["hash"])
    task = request("/instances", "POST", body, headers, 202)
    repeated = request("/instances", "POST", body, headers, 202)
    assert repeated["id"] == task["id"], "Create idempotency"
    print(json.dumps(task))
elif args.command == "wait":
    print(json.dumps(wait(args.id)))
elif args.command == "settings":
    instance = request("/instances/" + args.id)
    assert instance["name"].startswith("acceptance-"), "Only acceptance instances may be mutated"
    rules = instance["desired"] | json.loads(args.arguments)
    headers["If-Match"] = str(instance["revision"])
    print(json.dumps(request("/instances/" + args.id + "/settings", "PATCH", rules, headers)))
elif args.command == "action":
    instance = request("/instances/" + args.id)
    assert instance["name"].startswith("acceptance-"), "Only acceptance instances may be mutated"
    headers["If-Match"] = str(instance["revision"])
    body = {"kind": args.kind, "arguments": json.loads(args.arguments)}
    preview = request("/instances/" + args.id + "/previews", "POST", body, headers)
    body.update(confirmation=preview["token"], previewHash=preview["hash"])
    if args.kind in ("purge", "finalize-purge"):
        body["typedName"] = instance["name"]
    task = request("/instances/" + args.id + "/actions", "POST", body, headers, 202)
    repeated = request("/instances/" + args.id + "/actions", "POST", body, headers, 202)
    assert repeated["id"] == task["id"], "Action idempotency"
    print(json.dumps(task))
    print(json.dumps(wait(task["id"])))
elif args.command == "recover":
    task = request("/tasks/" + args.id)
    instance = request("/instances/" + task["instanceId"])
    assert instance["name"].startswith("acceptance-"), "Only acceptance instances may be recovered"
    body = {"resolution": args.kind}
    preview = request("/tasks/" + args.id + "/recovery-preview", "POST", body, headers)
    body.update(confirmation=preview["token"], previewHash=preview["hash"])
    print(json.dumps(request("/tasks/" + args.id + "/recover", "POST", body, headers)))
